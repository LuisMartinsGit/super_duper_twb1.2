// TerritoryClaimSystem.cs
// THE OWNERSHIP METER (docs/Design/Territory_Claims.md §2-§4, 2026-09-29).
//
// Once a second of lockstep time, every territory's meter moves by what is
// standing in it:
//
//   two hostile sides present   -> FROZEN (decay included)
//   only the holder present     -> fills by its weight, capped at 100
//   only a challenger present   -> drains by its weight; at 0 it becomes the
//                                  holder and starts filling
//   nobody present, no building -> decays by decayRate
//   nobody present, a building  -> HELD
//   locked                      -> pinned; challengers cannot drain it
//
// Weight = (summed population of the side's military units there) ^ exponent,
// x2 for the curse, whose units count only while standing (idle, or ordered
// to a point inside the territory) — a raid marching through claims nothing.
//
// Reaching 100 claims; falling to 0 from claimed LOSES the territory and
// collapses every building the loser had in it (Health -> 0; DeathSystem owns
// destruction).
//
// FORTRESSES ANCHOR THE EMPIRE (Territory_Claims.md §10). There is NO cap on
// how many territories a player holds (2026-10-04: "granted by how much you
// can defend, not a hard cap") — the meter above is the only thing that keeps
// ground. A player may START a claim only once aged up (Age 0 holds its start
// territory only), and only on ground that borders the ground connected to
// one of its Fortresses. Held ground that loses that connection wears down:
// every building of the holder there loses its full health over
// DisconnectSeconds (about four minutes).
//
// Every input is replicated simulation state and every step runs on the
// lockstep clock, so every peer moves every meter identically. The meter is
// stored as integer thousandths (TerritoryOwnership.MeterMax) and hashed.
//
// ROLLING CENSUS (2026-10-15 perf). The once-a-second pass used to walk every
// unit at once - a region lookup (polygon test) and seven EntityManager calls
// each, ~2,600 units: a 5-10 ms spike on one tick in thirty. Units are now
// counted in CensusSlots slices, one per tick across the second: a unit is
// sampled on the tick its Entity.Index falls due (Index % CensusSlots), so
// each living unit is counted exactly once per meter step, at a position at
// most a second old. Deterministic: Entity.Index is allocated identically on
// every peer (the assumption RunaiTradeSystem and LandmarkAgeUp already make)
// and the slot cursor is epoch-reset per match. The tag tests are per chunk.
// Buildings, which do not move, keep their region in a per-entity memo keyed
// on exact position and RegionMap.Version (identical answers), and the tick-0
// seed walks only Fortresses and curse nodes.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class TerritoryClaimSystem : SystemBase
    {
        private const float TickInterval = 1f;

        /// <summary>Sides: player factions 0..7, and the curse at 8
        /// (Faction.Border's own value).</summary>
        private const int Sides = 9;
        private const int CurseSide = 8;

        private SimCadence.Periodic _acc;
        private int _epoch = -1;

        private EntityQuery _unitQuery;
        private EntityQuery _buildingQuery;

        /// <summary>Seconds a building on ground cut off from every Fortress
        /// takes to wear down from full health (Territory_Claims.md §8).</summary>
        private const float DisconnectSeconds = 240f;

        private EntityQuery _fortressQuery;
        private EntityQuery _seedFortressQuery;
        private EntityQuery _seedNodeQuery;

        /// <summary>Slices of the rolling unit census - one per tick of the
        /// one-second meter step.</summary>
        private const int CensusSlots = 30;
        private int _censusSlot;
        private int[] _popAcc = System.Array.Empty<int>();

        // Building -> territory, valid while the building has not moved and
        // the region map is the same version. Rebuilt from the live building
        // set every pass, so the dead fall out.
        private struct RegionMemo { public float X, Z; public int Version, Region; }
        private System.Collections.Generic.Dictionary<Entity, RegionMemo> _regionMemo = new();
        private System.Collections.Generic.Dictionary<Entity, RegionMemo> _regionMemoNext = new();
        // Static so read-only displays (the debug board) can read the last
        // tick's answer; only this system writes them.
        private static readonly int[] _held = new int[Sides];
        /// <summary>Per player side: aged up as of the last tick — claims
        /// open at age-up (Territory_Claims.md §10).</summary>
        private static readonly bool[] _agedUp = new bool[Sides];
        /// <summary>[t * Sides + side] = 1 when t is held by side AND linked to
        /// one of its Fortresses through ground it holds.</summary>
        private static byte[] _connected = System.Array.Empty<byte>();

        /// <summary>Territories the faction holds or is mid-claim on, as of the last tick.</summary>
        public static int TerritoriesHeldBy(Faction f)
            => (int)f >= 0 && (int)f < CurseSide ? _held[(int)f] : 0;

        /// <summary>True when territory <paramref name="t"/> is held by
        /// <paramref name="f"/> and linked to one of its Fortresses.</summary>
        public static bool IsConnected(int t, Faction f)
        {
            int i = t * Sides + (int)f;
            return t >= 0 && (int)f >= 0 && (int)f < CurseSide && i < _connected.Length && _connected[i] != 0;
        }
        private readonly float[] _nextClaimNotice = new float[Sides];

        // Per-tick scratch, sized territories x sides.
        private int[] _pop = System.Array.Empty<int>();
        private byte[] _hasBuilding = System.Array.Empty<byte>();
        private byte[] _hasLock = System.Array.Empty<byte>();

        private static TerritoryOwnershipConfig _cfg;
        private static TerritoryOwnershipConfig Cfg =>
            _cfg != null ? _cfg
            : (_cfg = TheWaningBorder.Core.Settings.ComponentConfig.Require<TerritoryOwnershipConfig>());

        protected override void OnCreate()
        {
            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            _buildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            _fortressQuery = GetEntityQuery(
                ComponentType.ReadOnly<FortressTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.Exclude<UnderConstruction>());
            _seedFortressQuery = GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FortressTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            _seedNodeQuery = GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<SmallNodeTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
        }

        protected override void OnUpdate()
        {
            // No pre-match passes: the frame-driven warm-up before lockstep
            // takes over runs a machine-dependent number of times.
            var lockstep = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            if (lockstep != null && !lockstep.IsSimulationRunning) return;
            if (!RegionMap.Ready) return;

            // Scenarios and the sandbox are fixtures, not matches.
            if (GameSettings.IsSandbox || GameSettings.Mode == GameMode.Scenario) return;

            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                TerritoryOwnership.ResetMeter();
                _censusSlot = 0;
                System.Array.Clear(_popAcc, 0, _popAcc.Length);
                _regionMemo.Clear();
                _regionMemoNext.Clear();
            }
            if (!TerritoryOwnership.EnsureMeter()) return;

            // Nothing is owned until the map is on the ground: in single
            // player this group runs frame-driven from the moment the match
            // begins, before the Fortresses and curse nodes are spawned.
            if (!TheWaningBorder.Core.MatchLifecycle.MapPopulated) return;

            var em = EntityManager;
            // Every Fortress claims its home, every curse node its ground
            // (Territory_Claims.md §4, §6.4). Run on every pass, not once:
            // it only ever claims UNCLAIMED ground under a live Fortress or
            // curse node — which the build gate makes impossible after tick 0
            // except for exactly those two grants — so a late spawn (the
            // start-age promoter, a reseeded node) is picked up too.
            bool due = _acc.Due(SystemAPI.Time.DeltaTime, TickInterval);

            // The rolling census: this tick's slice, and on the meter tick
            // any slice a short period did not reach (never one twice).
            int cells = RegionMap.Count * Sides;
            if (_popAcc.Length != cells)
            {
                _popAcc = new int[cells];
                _censusSlot = 0;
            }
            if (_censusSlot < CensusSlots) SampleUnits(em, _censusSlot++);
            if (!due) return;
            while (_censusSlot < CensusSlots) SampleUnits(em, _censusSlot++);

            SeedFromStructures(em);
            Tick(em, TickInterval);
        }

        // ── Tick ────────────────────────────────────────────────────────

        private void Tick(EntityManager em, float dt)
        {
            int count = RegionMap.Count;
            int cells = count * Sides;
            if (_pop.Length != cells)
            {
                _pop = new int[cells];
                _hasBuilding = new byte[cells];
                _hasLock = new byte[cells];
            }
            System.Array.Clear(_hasBuilding, 0, cells);
            System.Array.Clear(_hasLock, 0, cells);

            // This second's census becomes the meter's input; the next one
            // starts empty.
            if (_popAcc.Length == cells) System.Array.Copy(_popAcc, _pop, cells);
            else System.Array.Clear(_pop, 0, cells);
            System.Array.Clear(_popAcc, 0, _popAcc.Length);
            _censusSlot = 0;

            GatherBuildings(em);
            ComputeReach(em, count);

            var cfg = Cfg;
            float rate = cfg.claimRate * dt * 1000f;   // thousandths per weight unit
            int decay = (int)math.round(cfg.decayRate * dt * 1000f);
            bool anyLoss = false;

            var weight = new float[Sides];
            for (int t = 0; t < count; t++)
            {
                int holder = TerritoryOwnership.HolderOf(t);
                int holderSide = SideOf(holder);
                int value = TerritoryOwnership.RawValueOf(t);
                bool claimed = TerritoryOwnership.IsClaimed(t);

                // Who is present, and at what weight. A side allied to the
                // holder (but not the holder) neither drains nor fills it —
                // allies never contest each other (Teams.md).
                int presentCount = 0;
                int firstPresent = -1;
                bool frozen = false;
                for (int s = 0; s < Sides; s++)
                {
                    weight[s] = 0f;
                    int pop = _pop[t * Sides + s];
                    if (pop <= 0) continue;
                    if (holderSide >= 0 && s != holderSide && !Hostile(s, holderSide)) continue;

                    float w = math.pow(pop, cfg.claimExponent);
                    if (s == CurseSide) w *= cfg.curseClaimMultiplier;
                    weight[s] = w;

                    if (presentCount > 0)
                        for (int o = 0; o < s; o++)
                            if (weight[o] > 0f && Hostile(o, s)) { frozen = true; break; }
                    presentCount++;
                    if (firstPresent < 0) firstPresent = s;
                }

                bool locked = claimed && holderSide >= 0 && _hasLock[t * Sides + holderSide] != 0;
                // A Sanctum holds itself (Territory_Claims.md § 11.4,
                // 2026-10-18): no building is needed to stop its decay.
                bool held = holderSide >= 0 && (_hasBuilding[t * Sides + holderSide] != 0
                                                || TerritoryResources.IsSanctum(t));
                bool lose = false;
                int loser = holder;
                int challenger = TerritoryOwnership.Natural;

                if (frozen)
                {
                    // Hostiles share the ground: nothing moves.
                }
                else if (presentCount == 0)
                {
                    if (!locked && !held && value > 0)
                    {
                        value -= decay;
                        if (value <= 0)
                        {
                            value = 0;
                            lose = claimed;
                            holder = TerritoryOwnership.Natural;
                            claimed = false;
                        }
                    }
                }
                else
                {
                    // One side, or several mutually allied ones. The holder
                    // fills if it is here; otherwise the heaviest challenger
                    // (lowest side on a tie) is the one taking the ground.
                    // Only a side ALLOWED to take this ground may gain on it
                    // (Territory_Claims.md §10): aged up and bordering its
                    // Fortress-connected ground. The holder
                    // filling its own ground is always allowed.
                    // §6.9.4: ground with ANY cursed cell in it gains for
                    // nobody — the holder's meter only holds.
                    if (TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(t))
                    {
                        TerritoryOwnership.SetMeter(t, holder, value, claimed, locked, false,
                            TerritoryOwnership.Natural);
                        continue;
                    }
                    int gainer = -1;
                    if (holderSide >= 0 && weight[holderSide] > 0f) gainer = holderSide;
                    else
                        for (int s = 0; s < Sides; s++)
                            if (weight[s] > 0f && MayTake(t, s)
                                && (gainer < 0 || weight[s] > weight[gainer])) gainer = s;
                    if (gainer < 0)
                    {
                        NoticeRefusedClaim(t, firstPresent);
                        TerritoryOwnership.SetMeter(t, holder, value, claimed,
                            claimed && holderSide >= 0 && _hasLock[t * Sides + holderSide] != 0,
                            false, TerritoryOwnership.Natural);
                        continue;
                    }

                    int step = (int)math.round(weight[gainer] * rate);
                    if (holderSide >= 0 && gainer != holderSide) challenger = HolderValue(gainer);

                    if (holderSide < 0)
                    {
                        holder = HolderValue(gainer);
                        value = math.min(TerritoryOwnership.MeterMax, value + step);
                    }
                    else if (gainer == holderSide)
                    {
                        value = math.min(TerritoryOwnership.MeterMax, value + step);
                    }
                    else if (!locked)
                    {
                        value -= step;
                        if (value <= 0)
                        {
                            lose = claimed;
                            holder = HolderValue(gainer);
                            value = 0;
                            claimed = false;
                        }
                    }
                }

                bool justClaimed = false;
                if (!claimed && holder != TerritoryOwnership.Natural && value >= TerritoryOwnership.MeterMax)
                {
                    claimed = true;
                    justClaimed = true;
                }

                // Lock is re-evaluated against the NEW holder, so a territory
                // taken this tick reads unlocked until its own lock stands.
                int newSide = SideOf(holder);
                bool nowLocked = claimed && newSide >= 0 && _hasLock[t * Sides + newSide] != 0;
                TerritoryOwnership.SetMeter(t, holder, value, claimed, nowLocked, frozen, challenger);

                if (lose)
                {
                    anyLoss = true;
                    CollapseBuildings(em, t, SideOf(loser));
                    Announce(t, loser, lost: true, by: holder);
                }
                if (justClaimed) Announce(t, holder, lost: false);
            }

            TerritoryOwnership.Publish();
            if (anyLoss) UnityEngine.Debug.Log("[TerritoryClaim] ownership lost — buildings collapsed.");

            WearDisconnected(em, count, dt);
        }

        // ── Fortress reach (Territory_Claims.md §8) ─────────────────────

        /// <summary>
        /// Per player side: whether it has aged up (claims open), how many
        /// territories it holds or is mid-claim on, and which of
        /// the territories it holds are linked to one of its Fortresses through
        /// ground it holds.
        /// </summary>
        private void ComputeReach(EntityManager em, int count)
        {
            int cells = count * Sides;
            if (_connected.Length != cells) _connected = new byte[cells];
            System.Array.Clear(_connected, 0, cells);
            System.Array.Clear(_held, 0, Sides);

            for (int t = 0; t < count; t++)
            {
                int side = SideOf(TerritoryOwnership.HolderOf(t));
                if (side >= 0 && side < CurseSide) _held[side]++;
            }

            var queue = new NativeList<int>(count, Allocator.Temp);
            using (var ents = _fortressQuery.ToEntityArray(Allocator.Temp))
            using (var facs = _fortressQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = _fortressQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var hps = _fortressQuery.ToComponentDataArray<Health>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (hps[i].Value <= 0) continue;
                    int side = (int)facs[i].Value;
                    if (side < 0 || side >= CurseSide) continue;
                    int t = RegionMap.NearestRegion(xfs[i].Position.x, xfs[i].Position.z);
                    if (t == RegionMap.None || !TerritoryOwnership.IsClaimed(t)
                        || SideOf(TerritoryOwnership.HolderOf(t)) != side) continue;
                    if (_connected[t * Sides + side] != 0) continue;
                    _connected[t * Sides + side] = 1;
                    queue.Add(t * Sides + side);
                }
            }

            // Flood out through ground the same side holds.
            for (int q = 0; q < queue.Length; q++)
            {
                int t = queue[q] / Sides, side = queue[q] % Sides;
                for (int o = 0; o < count; o++)
                {
                    if (_connected[o * Sides + side] != 0 || !RegionMap.AreAdjacent(t, o)) continue;
                    if (!TerritoryOwnership.IsClaimed(o) || SideOf(TerritoryOwnership.HolderOf(o)) != side) continue;
                    _connected[o * Sides + side] = 1;
                    queue.Add(o * Sides + side);
                }
            }
            queue.Dispose();

            for (int s = 0; s < CurseSide; s++)
                _agedUp[s] = AgedUp(em, (Faction)s);
        }

        private static bool AgedUp(EntityManager em, Faction faction)
            => FactionEconomy.TryGetBank(em, faction, out var bank)
               && em.HasComponent<FactionEra>(bank)
               && em.GetComponentData<FactionEra>(bank).Value >= 2;

        /// <summary>
        /// May <paramref name="side"/> START (or continue) taking territory
        /// <paramref name="t"/>? The curse always may. A player must have aged
        /// up (Age 0 holds its start territory only) and the ground must
        /// border — or hold — its Fortress-connected territory. There is no
        /// cap on how many it holds (Territory_Claims.md §10, 2026-10-04).
        /// </summary>
        private bool MayTake(int t, int side)
        {
            if (side == CurseSide) return true;
            if (side < 0 || side >= CurseSide) return false;
            bool alreadyMine = SideOf(TerritoryOwnership.HolderOf(t)) == side;
            if (!alreadyMine && !_agedUp[side]) return false;
            // A Sanctum may be claimed wherever it lies, connected or not
            // (Territory_Claims.md § 11.4, 2026-10-18).
            if (TerritoryResources.IsSanctum(t)) return true;
            return Borders(t, side);
        }

        private bool Borders(int t, int side)
        {
            if (_connected[t * Sides + side] != 0) return true;
            int count = RegionMap.Count;
            for (int o = 0; o < count; o++)
                if (_connected[o * Sides + side] != 0 && RegionMap.AreAdjacent(t, o)) return true;
            return false;
        }

        /// <summary>
        /// Ground a player holds but has lost the link to every Fortress on
        /// wears down: each of its buildings there loses its full health over
        /// <see cref="DisconnectSeconds"/>, so the lot is gone in about four
        /// minutes unless the link is restored.
        /// </summary>
        private void WearDisconnected(EntityManager em, int count, float dt)
        {
            bool any = false;
            for (int t = 0; t < count && !any; t++)
            {
                int side = SideOf(TerritoryOwnership.HolderOf(t));
                any = side >= 0 && side < CurseSide && TerritoryOwnership.IsClaimed(t)
                      && _connected[t * Sides + side] == 0 && !TerritoryResources.IsSanctum(t);
            }
            if (!any) return;

            using var ents = _buildingQuery.ToEntityArray(Allocator.Temp);
            using var facs = _buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                int side = (int)facs[i].Value;
                if (side < 0 || side >= CurseSide) continue;
                int t = BuildingRegion(ents[i], xfs[i].Position);
                if (t == RegionMap.None || !TerritoryOwnership.IsClaimed(t)) continue;
                if (SideOf(TerritoryOwnership.HolderOf(t)) != side) continue;
                if (_connected[t * Sides + side] != 0) continue;
                if (TerritoryResources.IsSanctum(t)) continue;   // a Sanctum needs no link (§ 11.4)
                var hp = em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0) continue;
                int wear = math.max(1, (int)math.ceil(hp.Max * dt / DisconnectSeconds));
                hp.Value = math.max(0, hp.Value - wear);
                em.SetComponentData(ents[i], hp);
            }
        }

        /// <summary>Tell the local player, at most every 20 s, why its army
        /// standing on ground is not claiming it. Presentation only.</summary>
        private void NoticeRefusedClaim(int t, int side)
        {
            int local = (int)GameSettings.LocalPlayerFaction;
            if (side != local || side < 0 || side >= CurseSide) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextClaimNotice[side]) return;
            _nextClaimNotice[side] = now + 20f;
            var L = (System.Func<string, string>)TheWaningBorder.Core.Localization.Loc.T;
            if (SideOf(TerritoryOwnership.HolderOf(t)) != side && !_agedUp[side])
                SimSignals.NotifyError(L("Age up to claim new ground — Age 0 holds your start territory only"));
            else
                SimSignals.NotifyError(L("Too far — you can only take ground that borders your Fortress's territories"));
        }

        // ── Inputs ──────────────────────────────────────────────────────

        /// <summary>
        /// One slice of the rolling census: every living, counting unit whose
        /// Entity.Index falls in <paramref name="slot"/> adds its population
        /// weight to the territory it stands in. Exactly the old GatherUnits
        /// rules (<see cref="Counts"/>, <see cref="PopOf"/>,
        /// <see cref="IsStanding"/>), with the archetype-level tests made once
        /// per chunk instead of once per unit.
        /// </summary>
        private void SampleUnits(EntityManager em, int slot)
        {
            var entH = GetEntityTypeHandle();
            var facH = GetComponentTypeHandle<FactionTag>(true);
            var xfH = GetComponentTypeHandle<LocalTransform>(true);
            var hpH = GetComponentTypeHandle<Health>(true);
            var tagH = GetComponentTypeHandle<UnitTag>(true);
            var caravanH = GetComponentTypeHandle<CaravanTag>(true);
            var traderH = GetComponentTypeHandle<RunaiTraderWarrior>(true);
            var escortH = GetComponentTypeHandle<RunaiEscort>(true);
            var summonH = GetComponentTypeHandle<TemporarySummon>(true);
            var conscriptH = GetComponentTypeHandle<ConscriptedTag>(true);
            var deathH = GetComponentTypeHandle<DeathAnimationState>(true);
            var popH = GetComponentTypeHandle<PopulationCost>(true);
            var godH = GetComponentTypeHandle<GodsplinterState>(true);
            var stingH = GetComponentTypeHandle<VeilstingerState>(true);

            _unitQuery.CompleteDependency();
            using var chunks = _unitQuery.ToArchetypeChunkArray(Allocator.Temp);
            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                // Counts(): never caravans, escorts, summons or conscripts.
                if (chunk.Has(ref caravanH) || chunk.Has(ref traderH) || chunk.Has(ref escortH)
                    || chunk.Has(ref summonH) || chunk.Has(ref conscriptH)) continue;

                var ents = chunk.GetNativeArray(entH);
                var facs = chunk.GetNativeArray(ref facH);
                var xfs = chunk.GetNativeArray(ref xfH);
                var hps = chunk.GetNativeArray(ref hpH);
                var tags = chunk.GetNativeArray(ref tagH);
                bool hasDeath = chunk.Has(ref deathH);
                bool hasPop = chunk.Has(ref popH);
                var pops = hasPop ? chunk.GetNativeArray(ref popH) : default;
                bool isGod = chunk.Has(ref godH), isSting = chunk.Has(ref stingH);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var e = ents[i];
                    if (e.Index % CensusSlots != slot) continue;
                    if (hps[i].Value <= 0) continue;
                    int side = (int)facs[i].Value;
                    if (side < 0 || side >= Sides) continue;
                    var cls = tags[i].Class;
                    if (cls == UnitClass.Economy || cls == UnitClass.Worker || cls == UnitClass.Scout) continue;
                    if (hasDeath && chunk.IsComponentEnabled(ref deathH, i)) continue;

                    var p = xfs[i].Position;
                    int t = RegionMap.RegionAt(p.x, p.z);
                    if (t == RegionMap.None) continue;

                    // §6.9.4 (2026-10-16): the curse owns no territory and
                    // adds nothing to any meter.
                    if (side == CurseSide) continue;

                    // PopOf(): the population cost, else the curse tier, else 1.
                    int pop = hasPop ? pops[i].Amount
                            : side == CurseSide ? (isGod ? 3 : isSting ? 2 : 1)
                            : 1;
                    _popAcc[t * Sides + side] += pop;
                }
            }
        }

        /// <summary>
        /// Military units only (Territory_Claims.md §2.1): no Workers, Scouts,
        /// caravans, conscripts, dying bodies or temporary summons.
        /// </summary>
        private static bool Counts(EntityManager em, Entity e, UnitClass cls)
        {
            if (cls == UnitClass.Economy || cls == UnitClass.Worker || cls == UnitClass.Scout) return false;
            if (em.HasComponent<CaravanTag>(e)) return false;
            // Runai trader-warriors are free escorts: they never claim (Age_1_Runai.md §3.4).
            if (em.HasComponent<RunaiTraderWarrior>(e) || em.HasComponent<RunaiEscort>(e)) return false;
            if (em.HasComponent<TemporarySummon>(e)) return false;
            if (em.HasComponent<ConscriptedTag>(e)) return false;
            if (em.HasComponent<DeathAnimationState>(e) && em.IsComponentEnabled<DeathAnimationState>(e))
                return false;
            return true;
        }

        /// <summary>
        /// Population weight of one unit. Curse units carry no
        /// PopulationCost, so they weigh by tier: crystalling 1,
        /// veilstinger 2, godsplinter 3.
        /// </summary>
        private static int PopOf(EntityManager em, Entity e, int side)
        {
            if (em.HasComponent<PopulationCost>(e))
                return em.GetComponentData<PopulationCost>(e).Amount;
            if (side == CurseSide)
            {
                if (em.HasComponent<GodsplinterState>(e)) return 3;
                if (em.HasComponent<VeilstingerState>(e)) return 2;
            }
            return 1;
        }

        /// <summary>
        /// Standing, not passing (Territory_Claims.md §6.2): a curse unit
        /// counts when it has no move order, or its order ends inside this
        /// territory.
        /// </summary>
        private static bool IsStanding(EntityManager em, Entity e, int t)
        {
            float3 dest;
            if (em.HasComponent<MoveCommand>(e) && em.IsComponentEnabled<MoveCommand>(e))
                dest = em.GetComponentData<MoveCommand>(e).Destination;
            else if (em.HasComponent<DesiredDestination>(e)
                     && em.GetComponentData<DesiredDestination>(e).Has != 0)
                dest = em.GetComponentData<DesiredDestination>(e).Position;
            else
                return true;
            return RegionMap.RegionAt(dest.x, dest.z) == t;
        }

        /// <summary>RegionMap.NearestRegion for a building, memoised on its
        /// exact position and the region map version - the same answer.</summary>
        private int BuildingRegion(Entity e, float3 p)
        {
            if (_regionMemo.TryGetValue(e, out var m) && m.X == p.x && m.Z == p.z && m.Version == RegionMap.Version)
                return m.Region;
            int t = RegionMap.NearestRegion(p.x, p.z);
            _regionMemo[e] = new RegionMemo { X = p.x, Z = p.z, Version = RegionMap.Version, Region = t };
            return t;
        }

        private void GatherBuildings(EntityManager em)
        {
            using var ents = _buildingQuery.ToEntityArray(Allocator.Temp);
            using var facs = _buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _buildingQuery.ToComponentDataArray<Health>(Allocator.Temp);

            // Re-key the memo on the live set: every building now standing
            // gets its region (cached or fresh); the destroyed fall out.
            _regionMemoNext.Clear();
            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                if (!(_regionMemo.TryGetValue(ents[i], out var m) && m.X == p.x && m.Z == p.z
                      && m.Version == RegionMap.Version))
                    m = new RegionMemo { X = p.x, Z = p.z, Version = RegionMap.Version,
                                         Region = RegionMap.NearestRegion(p.x, p.z) };
                _regionMemoNext[ents[i]] = m;
            }
            (_regionMemo, _regionMemoNext) = (_regionMemoNext, _regionMemo);

            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;   // foundations neither hold nor lock
                int side = (int)facs[i].Value;
                if (side < 0 || side >= Sides) continue;

                int t = BuildingRegion(e, xfs[i].Position);
                if (t == RegionMap.None) continue;

                _hasBuilding[t * Sides + side] = 1;
                if (IsLocking(em, e)) _hasLock[t * Sides + side] = 1;
            }
        }

        /// <summary>
        /// Structures that LOCK a territory (Territory_Claims.md §3): a
        /// building on a resource node, a Fortress, a curse node — and the
        /// Alanthor Trading Outpost, which stands beside its outcrop rather than
        /// on it but is that culture's node building
        /// (docs/Design/Veilstone_Economy.md §3.1).
        /// </summary>
        public static bool IsLocking(EntityManager em, Entity e)
            => em.HasComponent<FortressTag>(e)
            || em.HasComponent<GathererHutTag>(e)
            || em.HasComponent<MineTag>(e)
            || em.HasComponent<VeilstoneMineTag>(e)
            || em.HasComponent<TradingOutpostTag>(e)
            || em.HasComponent<RunaiSanctuaryTag>(e)   // the Runai node building (Age_1_Runai.md §5.1)
            || em.HasComponent<SmallNodeTag>(e);

        // ── Tick-0 seeding ──────────────────────────────────────────────

        private void SeedFromStructures(EntityManager em)
        {
            // Fortresses FIRST: a home is its player's before anything else
            // can stake it, whatever order the query walks in.
            SeedPass(em, fortresses: true);
            SeedPass(em, fortresses: false);
        }

        private void SeedPass(EntityManager em, bool fortresses)
        {
            // Only the structures this pass can match (was: every building,
            // tag-tested one by one). Same relative order: a narrower query
            // walks the same archetypes in the same creation order.
            var q = fortresses ? _seedFortressQuery : _seedNodeQuery;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (hps[i].Value <= 0) continue;                    // collapsing
                if (em.HasComponent<UnderConstruction>(e)) continue; // a foundation grants nothing
                int side = (int)facs[i].Value;
                if (side < 0 || side >= CurseSide) continue;   // §6.9.4: the curse claims nothing
                var p = xfs[i].Position;
                int t = RegionMap.NearestRegion(p.x, p.z);
                if (t == RegionMap.None) continue;
                if (TerritoryOwnership.IsClaimed(t)) continue;   // first structure wins
                if (TerritoryOwnership.RawValueOf(t) > 0
                    && TerritoryOwnership.HolderOf(t) != HolderValue(side)) continue;   // someone else is mid-claim
                TerritoryOwnership.ForceClaim(t, HolderValue(side));
            }
        }

        // ── Collapse ────────────────────────────────────────────────────

        /// <summary>
        /// The territory is lost: every building of the former holder in it
        /// collapses — walls, houses, towers and foundations alike
        /// (Territory_Claims.md §2.3). Health -> 0 is the whole contract;
        /// DeathSystem destroys.
        /// </summary>
        private void CollapseBuildings(EntityManager em, int t, int side)
        {
            if (side < 0) return;
            using var ents = _buildingQuery.ToEntityArray(Allocator.Temp);
            using var facs = _buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if ((int)facs[i].Value != side) continue;
                if (BuildingRegion(ents[i], xfs[i].Position) != t) continue;
                var hp = em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0) continue;
                hp.Value = 0;
                em.SetComponentData(ents[i], hp);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static int SideOf(int holder)
        {
            if (holder == TerritoryOwnership.Curse) return CurseSide;
            if (holder >= 0 && holder < CurseSide) return holder;
            return -1;
        }

        private static int HolderValue(int side)
            => side == CurseSide ? TerritoryOwnership.Curse : side;

        private static bool Hostile(int a, int b)
            => Alliances.AreHostile((Faction)a, (Faction)b);

        /// <summary>
        /// TERRITORY NOTICES (2026-10-11). The local player's own gains and
        /// losses are ANNOUNCEMENTS (held on screen longer) and a loss names
        /// who is taking the ground; any other player's claim or loss is a
        /// short notice. The curse's captures announce themselves
        /// (CurseTerritorySystem), so a curse side is skipped here. A
        /// spectator hears everything as plain notices. Presentation only —
        /// no sim state.
        /// </summary>
        private static void Announce(int t, int side, bool lost, int by = TerritoryOwnership.Natural)
        {
            if (side < 0 || side >= CurseSide) return;   // the curse and nobody
            var L = (System.Func<string, string>)TheWaningBorder.Core.Localization.Loc.T;
            string name = RegionMap.NameOf(t);
            if (string.IsNullOrEmpty(name)) name = L("a territory");
            var who = (Faction)side;
            bool mine = !GameSettings.IsSpectating && who == GameSettings.LocalPlayerFaction;

            if (mine)
            {
                if (!lost)
                    SimSignals.Announce(string.Format(L("You claimed {0}"), name));
                else if (by == TerritoryOwnership.Curse)
                    SimSignals.Announce(string.Format(L("The curse took {0} from you — its buildings collapse"), name));
                else if (by >= 0 && by < CurseSide)
                    SimSignals.Announce(string.Format(L("{0} took {1} from you — its buildings collapse"),
                        FactionColors.RichName((Faction)by), name));
                else
                    SimSignals.Announce(string.Format(L("You lost {0} — its buildings collapse"), name));
                return;
            }

            string text = lost
                ? string.Format(L("{0} lost {1}"), FactionColors.RichName(who), name)
                : string.Format(L("{0} claimed {1}"), FactionColors.RichName(who), name);
            SimSignals.Notify(text);
        }
    }
}
