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
// Every input is replicated simulation state and every step runs on the
// lockstep clock, so every peer moves every meter identically. The meter is
// stored as integer thousandths (TerritoryOwnership.MeterMax) and hashed.

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
            if (!_acc.Due(SystemAPI.Time.DeltaTime, TickInterval)) return;
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
            System.Array.Clear(_pop, 0, cells);
            System.Array.Clear(_hasBuilding, 0, cells);
            System.Array.Clear(_hasLock, 0, cells);

            GatherUnits(em);
            GatherBuildings(em);

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
                bool held = holderSide >= 0 && _hasBuilding[t * Sides + holderSide] != 0;
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
                    int gainer = holderSide >= 0 && weight[holderSide] > 0f ? holderSide : firstPresent;
                    for (int s = 0; s < Sides; s++)
                        if (weight[s] > weight[gainer]) gainer = s;
                    if (holderSide >= 0 && weight[holderSide] > 0f) gainer = holderSide;

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
                    Announce(t, loser, lost: true);
                }
                if (justClaimed) Announce(t, holder, lost: false);
            }

            TerritoryOwnership.Publish();
            if (anyLoss) UnityEngine.Debug.Log("[TerritoryClaim] ownership lost — buildings collapsed.");
        }

        // ── Inputs ──────────────────────────────────────────────────────

        private void GatherUnits(EntityManager em)
        {
            using var ents = _unitQuery.ToEntityArray(Allocator.Temp);
            using var facs = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _unitQuery.ToComponentDataArray<Health>(Allocator.Temp);
            using var classes = _unitQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                int side = (int)facs[i].Value;
                if (side < 0 || side >= Sides) continue;
                var e = ents[i];
                if (!Counts(em, e, classes[i].Class)) continue;

                var p = xfs[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None) continue;

                if (side == CurseSide && !IsStanding(em, e, t)) continue;

                _pop[t * Sides + side] += PopOf(em, e, side);
            }
        }

        /// <summary>
        /// Military units only (Territory_Claims.md §2.1): no Workers, Scouts,
        /// caravans, conscripts, dying bodies or temporary summons.
        /// </summary>
        private static bool Counts(EntityManager em, Entity e, UnitClass cls)
        {
            if (cls == UnitClass.Economy || cls == UnitClass.Miner || cls == UnitClass.Scout) return false;
            if (em.HasComponent<CaravanTag>(e)) return false;
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

        private void GatherBuildings(EntityManager em)
        {
            using var ents = _buildingQuery.ToEntityArray(Allocator.Temp);
            using var facs = _buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _buildingQuery.ToComponentDataArray<Health>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;   // foundations neither hold nor lock
                int side = (int)facs[i].Value;
                if (side < 0 || side >= Sides) continue;

                var p = xfs[i].Position;
                int t = RegionMap.NearestRegion(p.x, p.z);
                if (t == RegionMap.None) continue;

                _hasBuilding[t * Sides + side] = 1;
                if (IsLocking(em, e)) _hasLock[t * Sides + side] = 1;
            }
        }

        /// <summary>
        /// Structures that LOCK a territory (Territory_Claims.md §3): a
        /// building on a resource node, a Fortress, a curse node.
        /// </summary>
        public static bool IsLocking(EntityManager em, Entity e)
            => em.HasComponent<FortressTag>(e)
            || em.HasComponent<GathererHutTag>(e)
            || em.HasComponent<MineTag>(e)
            || em.HasComponent<VeilstoneMineTag>(e)
            || em.HasComponent<SmelterTag>(e)
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
            using var ents = _buildingQuery.ToEntityArray(Allocator.Temp);
            using var facs = _buildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _buildingQuery.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (hps[i].Value <= 0) continue;                    // collapsing
                if (em.HasComponent<UnderConstruction>(e)) continue; // a foundation grants nothing
                bool match = fortresses
                    ? em.HasComponent<FortressTag>(e)
                    : em.HasComponent<SmallNodeTag>(e);
                if (!match) continue;
                int side = (int)facs[i].Value;
                if (side < 0 || side >= Sides) continue;
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
                var p = xfs[i].Position;
                if (RegionMap.NearestRegion(p.x, p.z) != t) continue;
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

        /// <summary>Tell the local player when ground changes hands with
        /// them on either side of it. Presentation only — no sim state.</summary>
        private static void Announce(int t, int side, bool lost)
        {
            int local = (int)GameSettings.LocalPlayerFaction;
            if (side != local) return;
            string name = RegionMap.NameOf(t);
            if (string.IsNullOrEmpty(name)) name = TheWaningBorder.Core.Localization.Loc.T("a territory");
            if (lost)
                SimSignals.NotifyError(string.Format(
                    TheWaningBorder.Core.Localization.Loc.T("Lost {0} — its buildings collapse"), name));
            else
                SimSignals.Notify(string.Format(
                    TheWaningBorder.Core.Localization.Loc.T("Claimed {0}"), name));
        }
    }
}
