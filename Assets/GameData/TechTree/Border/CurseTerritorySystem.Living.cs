// CurseTerritorySystem.Living.cs
// THE LIVING CURSE — Curse_And_Shardroot.md §2.11 (2026-09-13, CURRENT).
//
// Supersedes the Waking (§2.8) and the army half of the Wrath (§2.10) when
// BorderSettings.livingCurse is on, which is the shipped default. The old
// model stays in the main file, untouched, behind the switch.
//
// What this file does, in the order it runs each check:
//
//   TickGarrisons   every `armySpawnSeconds` EVERY curse node in a held
//                   territory brings its own garrison up to `garrisonCap` x
//                   `armyGrowth`^n (2.13; per node since 2026-10-03,
//                   Territory_Claims.md §6.3). Nothing regrows between
//                   spawns: kill the army and the node is open.
//   TryExpand       every `expansionSeconds` the curse sends a CLAIM party
//                   at an adjacent, unlocked territory it does not hold, or
//                   fills a free node in ground it holds. It advances only
//                   on ground it can take: no raids (§6.7, 2026-10-03).
//   (every spawn)   one `shardrootChance` roll that a unit carries the
//                   Shardroot (2.13 rule 5); the wells then hold it no more.
//   (the holder)    "the curse wants it back" (§3.1): a player holding the
//                   Shardroot in a territory ADJACENT to the curse draws the
//                   harassment party as a HUNT aimed at the holder, and a
//                   garrison with the holder inside its territory goes for
//                   the holder before any other intruder.
//   ShepherdLiving  each defender GUARDS its node: it engages hostiles
//                   within `guardRadius` of it, and past `guardLeashRadius`
//                   drops the fight and walks back (§6.7, 2026-10-03). Merge parties march to the node,
//                   then sit inside it filling a progress bar; a hostile
//                   within `mergeDefendRadius` pulls them out and PAUSES
//                   the bar; a dead party resets it; a full bar turns the
//                   node (a curse node rises on it) and the territory.
//                   A Shardroot HUNT party (§6.6) presses the holder for as
//                   long as anyone holds it, then walks home as garrison.
//
// DETERMINISM. Everything here runs in-sim on every peer with no host gate.
// Orders go through the direct helpers, never the router, exactly as the
// existing wave code does and for the same reason: this is not a player's
// command, it is the world. Every roster walk sorts by entity so the order
// of structural changes is identical on every peer. RNG is the system's
// seeded `_rng`, and every draw happens on every peer.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data.Border;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>Marks a curse unit as a defender of one territory, or a
    /// member of one merge party / raid. Replaces CurseWaveMember for the
    /// living model; the old wave shepherd ignores these.</summary>
    public struct CurseLivingMember : IComponentData
    {
        /// <summary>Home territory index (RegionMap).</summary>
        public int Home;
        /// <summary>0 = garrison, 1 = merge party, 2 = raid.</summary>
        public byte Role;
        /// <summary>Merge party / raid id, or -1 for garrison.</summary>
        public int Party;
        /// <summary>The curse node this unit guards (its position). A
        /// garrison engages only within guardRadius of it and is leashed to
        /// it (Territory_Claims.md §6.7). Set when the unit joins a garrison.</summary>
        public float3 Guard;
    }

    public partial class CurseTerritorySystem
    {
        private const byte RoleGarrison = 0;
        private const byte RoleMerge = 1;
        private const byte RoleRaid = 2;

        private const byte MergeMarching = 0;
        private const byte MergeMerging = 1;
        private const byte MergeDefending = 2;

        private sealed class MergeState
        {
            public int Territory;
            public Entity Node;
            public float3 NodePos;
            public float Progress;      // 0..1
            public byte Phase;
            public double StartedAt;
            public double NextThinkAt;
        }

        private sealed class RaidState
        {
            public int Home;
            public int Target;
            public float3 Objective;
            public double StartedAt;
            public double NextThinkAt;
            public bool Returning;
        }

        /// <summary>Territory -> sim time its next whole-army spawn is due
        /// (2.13 rule 1). Nothing regrows in between.</summary>
        private readonly Dictionary<int, double> _nextArmyAt = new();
        /// <summary>Territory -> spawns it has made; the exponent of 2.13 rule 3.</summary>
        private readonly Dictionary<int, int> _armySpawns = new();
        private readonly Dictionary<int, MergeState> _merges = new();
        private readonly Dictionary<int, RaidState> _raids = new();
        private int _nextPartyId = 1;
        private double _nextExpandAt = -1.0;

        private static readonly ComponentType[] QT_Living =
        {
            ComponentType.ReadOnly<CurseLivingMember>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Living;

        private void ResetLiving()
        {
            _nextArmyAt.Clear();
            _armySpawns.Clear();
            _merges.Clear();
            _raids.Clear();
            _nextPartyId = 1;
            _nextExpandAt = -1.0;
            _noNodeSince = -1.0;
            _shardrootGuaranteed = false;
        }

        /// <summary>Sim time the curse was first seen with no node, or -1.</summary>
        private double _noNodeSince = -1.0;

        /// <summary>
        /// THE CURSE CAN BE DRIVEN BACK, NEVER OUT (Territory_Claims.md §6.5):
        /// left with no node at all, it raises a fresh one after
        /// reseedSeconds, on a random resource node outside every start
        /// territory. It is the only source of religion points (Religion.md),
        /// so a curse wiped from the map would end a layer of the game.
        /// </summary>
        private void TickReseed(EntityManager em, double now, BorderSettingsSO s)
        {
            if (_curseNodeCount > 0) { _noNodeSince = -1.0; return; }
            if (_noNodeSince < 0.0) { _noNodeSince = now; return; }
            if (now - _noNodeSince < s.reseedSeconds) return;

            var excluded = new List<int>();
            var fq = QueryFacXf<FortressTag>(em);
            using (var fx = fq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < fx.Length; i++)
                {
                    int t = RegionMap.NearestRegion(fx[i].Position.x, fx[i].Position.z);
                    if (t != RegionMap.None) excluded.Add(t);
                }
            if (CurseNodeSeeding.TryReseedOne(em, excluded, ref _rng, out float3 at))
            {
                int t = RegionMap.NearestRegion(at.x, at.z);
                // Ground under a fresh node is the curse's at once — the same
                // grant a seeded node gets at tick 0.
                if (t != RegionMap.None && TerritoryOwnership.OwnerOf(t) == TerritoryOwnership.Natural)
                    TerritoryOwnership.ForceClaim(t, TerritoryOwnership.Curse);
                SimSignals.Ping(at, SimPingKind.Curse, 15f, big: true);
                SimSignals.Notify(Loc.T("The curse rises again!"));
                UnityEngine.Debug.Log($"[CurseTerritory] RESEED — a curse node rises at ({at.x:F0},{at.z:F0}).");
            }
            _noNodeSince = -1.0;
        }

        /// <summary>
        /// The Shardroot backstop (Territory_Claims.md §6.6): the wells that
        /// used to guarantee it are gone, so the first curse spawn after
        /// shardrootGuaranteeSeconds carries it if it is not out yet.
        /// </summary>
        private void TickShardrootGuarantee(EntityManager em, double now, BorderSettingsSO s)
        {
            if (s.shardrootGuaranteeSeconds <= 0f || now < s.shardrootGuaranteeSeconds) return;
            _shardrootGuaranteed = true;
        }

        /// <summary>Set once the guarantee time has passed: the next roll
        /// always succeeds.</summary>
        private bool _shardrootGuaranteed;

        /// <summary>Tier index for the match minute (replaces wrath as the
        /// composition dial).</summary>
        private static int TierForNow(BorderSettingsSO s, double now)
        {
            if (s.TierCount == 0) return -1;
            int i = (int)math.floor(now / 60.0 / math.max(1f, s.minutesPerTier));
            return math.clamp(i, 0, s.TierCount - 1);
        }

        private Entity SpawnCurseUnit(EntityManager em, BorderSettingsSO.ArmyTier tier,
                                      int ordinal, float3 origin)
        {
            float angle = _rng.NextFloat(0f, math.PI * 2f);
            float dist = _rng.NextFloat(2f, 6f);
            float sx = origin.x + math.cos(angle) * dist;
            float sz = origin.z + math.sin(angle) * dist;
            var pos = new float3(sx, TerrainUtility.GetHeight(sx, sz), sz);
            int m = math.max(1, tier.TotalUnits);
            int k = ordinal % m;
            return k < tier.godsplinters
                ? TheWaningBorder.Entities.Godsplinter.Create(em, pos, Faction.Border)
                : k < tier.godsplinters + tier.veilstingers
                    ? TheWaningBorder.Entities.Veilstinger.Create(em, pos, Faction.Border)
                    : TheWaningBorder.Entities.Crystalling.Create(em, pos, Faction.Border);
        }

        // ── garrisons ────────────────────────────────────────────────────────

        /// <summary>2.13 rules 1-3: every armySpawnSeconds a held territory
        /// brings its garrison up to garrisonCap x armyGrowth^n in ONE spawn.
        /// Survivors count; between spawns nothing regrows, which is the
        /// window in which a well can be verbed.</summary>
        private void TickGarrisons(EntityManager em, double now, BorderSettingsSO s, float bonus)
        {
            int tierIndex = TierForNow(s, now);
            if (tierIndex < 0) return;
            var tier = s.Tier(tierIndex);
            if (tier == null || tier.TotalUnits == 0) return;

            // Garrison members by home territory, one walk.
            var q = QC_Living.Get(em, QT_Living);
            using var members = q.ToComponentDataArray<CurseLivingMember>(Allocator.Temp);

            _scratchHeld.Clear();
            foreach (int t in _held) _scratchHeld.Add(t);
            _scratchHeld.Sort();

            for (int i = 0; i < _scratchHeld.Count; i++)
            {
                int t = _scratchHeld[i];
                // A garrison rises from a NODE (Territory_Claims.md §6.3).
                // Ground the curse holds by standing on it, with no node yet,
                // fields nothing — its claimants are its only defence.
                if (!_nodesByTerritory.TryGetValue(t, out var nodes) || nodes.Count == 0) continue;
                if (!_nextArmyAt.TryGetValue(t, out double at))
                {
                    // First army almost at once, staggered by territory so
                    // several newly held territories do not spawn on one tick.
                    _nextArmyAt[t] = now + (t % 5) * 4.0;
                    continue;
                }
                if (now < at) continue;

                // EVERY NODE FIELDS ITS OWN GARRISON (2026-10-03): the size
                // is per node, and each node is topped up from the guards it
                // has. A guard whose node died counts for the nearest node
                // left (ShepherdLiving re-points it there).
                _armySpawns.TryGetValue(t, out int n);
                int size = (int)math.round(s.garrisonCap * math.pow(math.max(1f, s.armyGrowth), n) * bonus);
                var have = _scratchGuardCounts;
                have.Clear();
                for (int k = 0; k < nodes.Count; k++) have.Add(0);
                for (int m = 0; m < members.Length; m++)
                    if (members[m].Role == RoleGarrison && members[m].Home == t)
                        have[NearestNodeIndex(nodes, members[m].Guard)]++;

                _scratchWave.Clear();
                int spawned = 0, survived = 0;
                for (int k = 0; k < nodes.Count; k++)
                {
                    survived += have[k];
                    int toSpawn = math.max(0, size - have[k]);
                    for (int u = 0; u < toSpawn; u++)
                    {
                        var e = SpawnCurseUnit(em, tier, have[k] + u, nodes[k]);
                        em.AddComponentData(e, new CurseLivingMember
                            { Home = t, Role = RoleGarrison, Party = -1, Guard = nodes[k] });
                        _scratchWave.Add(e);
                    }
                    spawned += toSpawn;
                }
                _armySpawns[t] = n + 1;
                _nextArmyAt[t] = now + s.armySpawnSeconds / bonus;
                UnityEngine.Debug.Log($"[CurseTerritory] ARMY {n + 1} in territory {t} ({RegionMap.NameOf(t)}): " +
                    $"{nodes.Count} node(s) x {size}, {spawned} spawned, {survived} survived; " +
                    $"next in {s.armySpawnSeconds / bonus:0}s.");
                TryRollShardroot(em, s, _scratchWave, $"garrison army {n + 1} of territory {t}");
            }
        }

        private readonly List<int> _scratchGuardCounts = new();

        /// <summary>Index of the node in <paramref name="nodes"/> nearest
        /// <paramref name="p"/> (0 when the list has one entry).</summary>
        private static int NearestNodeIndex(List<float3> nodes, float3 p)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int k = 0; k < nodes.Count; k++)
            {
                float d = Distance2(nodes[k], p);
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }

        // ── the Shardroot (2.13 rule 5) ─────────────────────────────────────

        private static readonly ComponentType[] QT_ShardrootState =
            { ComponentType.ReadWrite<ShardrootState>() };
        private static CachedEntityQuery QC_ShardrootState;

        /// <summary>One roll per spawn: shardrootChance that a unit of this
        /// spawn carries the artifact. Only while it is neither out nor
        /// claimed through a well; once out, the well path is closed
        /// (ShardrootState.Found = 1 is what TryAward and the surfacing
        /// backstop both check). The draw is on the sim RNG on every peer.</summary>
        private void TryRollShardroot(EntityManager em, BorderSettingsSO s, List<Entity> spawned, string what)
        {
            if (spawned.Count == 0 || (s.shardrootChance <= 0f && !_shardrootGuaranteed)) return;
            var q = QC_ShardrootState.Get(em, QT_ShardrootState);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.Found != 0) return;

            // Always draw, so the RNG stream is the same on every peer
            // whether or not the roll succeeds.
            float roll = _rng.NextFloat();
            int pick = _rng.NextInt(0, spawned.Count);
            if (roll >= s.shardrootChance && !_shardrootGuaranteed) return;

            var bearer = spawned[pick];
            state.Found = 1;
            em.SetComponentData(ents[0], state);
            em.AddComponentData(bearer, new ShardrootBearer
                { Amount = ShardrootState.ShardrootPower, Source = RitualKind.ViolentExtraction });
            em.AddComponent<ShardrootTag>(bearer);
            SimSignals.Notify(Loc.T("The SHARDROOT walks with the curse!"));
            var p = em.GetComponentData<LocalTransform>(bearer).Position;
            SimSignals.Ping(p, SimPingKind.Curse, 12f, big: true);
            UnityEngine.Debug.Log($"[CurseTerritory] SHARDROOT -- a unit of the {what} carries it " +
                $"(roll {roll:0.000} < {s.shardrootChance:0.000}); the wells hold it no more.");
        }

        /// <summary>The player holding the Shardroot (carrier, hero or
        /// enshrining Temple), as ShardrootSystem last published it. A curse
        /// bearer or a ground pickup is no holder: the curse does not hunt
        /// itself. Sim state on every peer, so every peer hunts the same.</summary>
        private static bool TryShardrootHolder(EntityManager em, out Faction holder,
                                               out float3 pos, out int territory)
        {
            holder = Faction.Border; pos = default; territory = RegionMap.None;
            var q = QC_ShardrootState.Get(em, QT_ShardrootState);
            if (q.IsEmptyIgnoreFilter) return false;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.Found == 0 || state.HolderFaction == Faction.Border) return false;
            holder = state.HolderFaction;
            pos = state.HolderPos;
            territory = RegionMap.NearestRegion(pos.x, pos.z);
            return territory != RegionMap.None;
        }

        // ── expansion ───────────────────────────────────────────────────────

        /// <summary>Resource nodes of ANY kind standing in a territory
        /// (Territory_Claims.md §6.3 — the curse builds on any node), sorted
        /// by entity so every peer picks the same one.</summary>
        private void NodesIn(EntityManager em, int territory, List<(Entity e, float3 p)> into)
        {
            into.Clear();
            Collect<VeilstoneOutcroppingTag>(em, territory, into);
            Collect<VeilsteelDepositTag>(em, territory, into);
            Collect<IronMineTag>(em, territory, into);
            Collect<SupplyNodeTag>(em, territory, into);
            into.Sort((a, b) => a.e.Index.CompareTo(b.e.Index));
        }

        private static void Collect<T>(EntityManager em, int territory, List<(Entity, float3)> into)
            where T : unmanaged, IComponentData
        {
            var q = QueryXf<T>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                if (RegionMap.NearestRegion(p.x, p.z) == territory) into.Add((ents[i], p));
            }
        }

        private readonly List<(Entity e, float3 p)> _scratchNodes = new();
        private readonly Dictionary<int, List<float3>> _hostilesByTerritory = new();

        /// <summary>
        /// One expansion dispatch (Territory_Claims.md §6.5, §6.7). A party
        /// of mergePartySize leaves a held territory for an adjacent one it
        /// does not hold and CAN TAKE — unclaimed, or claimed but not locked:
        /// it stands on the ground until the meter turns it, then raises a
        /// curse node on one of its resource nodes (the merge bar). Locked
        /// ground is never picked: the curse advances only to take, so there
        /// are no raids (2026-10-03). While a player holds the Shardroot the
        /// party goes for the holder instead, wherever they are (§6.6).
        /// </summary>
        private void TryExpand(EntityManager em, double now, BorderSettingsSO s, float bonus)
        {
            if (_held.Count == 0) return;

            _scratchCandidates.Clear();
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (_held.Contains(r)) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (TerritoryOwnership.IsLocked(r)) continue;   // cannot be taken: not a target (§6.7)
                bool adjacent = false;
                foreach (int h in _held)
                    if (AreAdjacent(h, r)) { adjacent = true; break; }
                if (adjacent) _scratchCandidates.Add(r);
            }
            _scratchCandidates.Sort();
            // Always draw, so the RNG stream is the same on every peer
            // whatever the branch below.
            int draw = _rng.NextInt(0, math.max(1, _scratchCandidates.Count));

            bool hunt = TryShardrootHolder(em, out var holderFaction, out float3 holderPos, out int holderTerritory);

            // FILL BEFORE SPREADING (2026-09-29): the curse takes EVERY
            // resource node in the ground it holds before it reaches for new
            // ground. A held territory with a node still free gets the merge
            // party — from that territory itself — and a curse node rises on
            // it. Only the Shardroot hunt outranks this.
            if (!hunt && _merges.Count == 0
                && TryFindUntakenNode(em, out int fillT, out Entity fillNode, out float3 fillPos))
            {
                SendFillParty(em, now, s, bonus, fillT, fillNode, fillPos);
                return;
            }

            int pick;
            if (hunt)
            {
                // THE CURSE IGNORES EVERYONE BUT THE HOLDER (§6.6).
                pick = holderTerritory;
            }
            else
            {
                if (_scratchCandidates.Count == 0) return;
                if (_merges.Count > 0) return;   // one takeover at a time: it is the curse's whole attention
                pick = _scratchCandidates[draw];
            }

            // Which held territory sends the party: one with a node (a
            // garrison source), nearest to the pick by seed, index breaking
            // ties, so every peer agrees.
            int from = -1;
            float fromD = float.MaxValue;
            var pickSeed = RegionMap.SeedOf(pick);
            _scratchHeld.Clear();
            foreach (int h in _held) _scratchHeld.Add(h);
            _scratchHeld.Sort();
            for (int i = 0; i < _scratchHeld.Count; i++)
            {
                int h = _scratchHeld[i];
                if (!_anchors.ContainsKey(h)) continue;
                var hs = RegionMap.SeedOf(h);
                float d = math.lengthsq(new float2(hs.x - pickSeed.x, hs.y - pickSeed.y));
                if (d < fromD) { fromD = d; from = h; }
            }
            if (from < 0) return;
            float3 origin = WaveOrigin(em, from);

            int tierIndex = TierForNow(s, now);
            var tier = tierIndex >= 0 ? s.Tier(tierIndex) : null;
            if (tier == null || tier.TotalUnits == 0) return;

            bool claim = !hunt;
            NodesIn(em, pick, _scratchNodes);

            int party = _nextPartyId++;
            int partySize = (int)math.round(s.mergePartySize * bonus);
            _scratchWave.Clear();
            for (int u = 0; u < partySize; u++)
            {
                var e = SpawnCurseUnit(em, tier, u, origin);
                em.AddComponentData(e, new CurseLivingMember
                    { Home = from, Role = claim ? RoleMerge : RoleRaid, Party = party, Guard = origin });
                _scratchWave.Add(e);
            }
            TryRollShardroot(em, s, _scratchWave, $"harassment party {party}");

            if (claim)
            {
                // Stand by a node when the territory has one (the node the
                // curse will raise its own on); by the seed otherwise.
                Entity node = Entity.Null;
                float3 stand;
                if (_scratchNodes.Count > 0) (node, stand) = _scratchNodes[0];
                else stand = new float3(pickSeed.x, TerrainUtility.GetHeight(pickSeed.x, pickSeed.y), pickSeed.y);

                _merges[party] = new MergeState
                {
                    Territory = pick, Node = node, NodePos = stand,
                    Progress = 0f, Phase = MergeMarching, StartedAt = now, NextThinkAt = 0.0,
                };
                TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                    em, _scratchWave, stand, FormationShape.Box, attackMove: true);
                SimSignals.Ping(stand, SimPingKind.Curse, 10f, big: true);
                UnityEngine.Debug.Log($"[CurseTerritory] CLAIM — party {party} of {_scratchWave.Count} " +
                    $"marches to stand on territory {pick} ({RegionMap.NameOf(pick)}) from {from}.");
            }
            else
            {
                float3 target = holderPos;
                _raids[party] = new RaidState
                {
                    Home = from, Target = pick, Objective = target,
                    StartedAt = now, NextThinkAt = 0.0, Returning = false,
                };
                TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                    em, _scratchWave, target, FormationShape.Box, attackMove: true);
                SimSignals.Ping(target, SimPingKind.Curse, 10f);
                UnityEngine.Debug.Log($"[CurseTerritory] HUNT — party {party} of {_scratchWave.Count} hunts " +
                    $"the Shardroot holder ({holderFaction}) in territory {pick} ({RegionMap.NameOf(pick)}).");
            }
        }

        /// <summary>
        /// The first free resource node (any kind) in ground the curse holds:
        /// no curse node on it and no building of anyone's. Territories and
        /// nodes are walked in index order, so every peer picks the same one.
        /// Only territories that already field a garrison (have a node) send
        /// a party.
        /// </summary>
        private bool TryFindUntakenNode(EntityManager em, out int territory, out Entity node, out float3 pos)
        {
            territory = RegionMap.None; node = Entity.Null; pos = default;
            _scratchHeld.Clear();
            foreach (int h in _held) _scratchHeld.Add(h);
            _scratchHeld.Sort();

            var bq = QueryXf<BuildingTag>(em);
            using var bx = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < _scratchHeld.Count; i++)
            {
                int t = _scratchHeld[i];
                if (!_anchors.ContainsKey(t)) continue;
                NodesIn(em, t, _scratchNodes);
                for (int n = 0; n < _scratchNodes.Count; n++)
                {
                    var p = _scratchNodes[n].p;
                    bool taken = false;
                    for (int b = 0; b < bx.Length && !taken; b++)
                    {
                        float dx = bx[b].Position.x - p.x, dz = bx[b].Position.z - p.z;
                        taken = dx * dx + dz * dz <= 2.5f * 2.5f;
                    }
                    if (taken) continue;
                    territory = t; node = _scratchNodes[n].e; pos = p;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A merge party from a held territory onto one of its own
        /// free nodes. The ground is already the curse's, so the merge bar
        /// starts filling as soon as the party arrives.</summary>
        private void SendFillParty(EntityManager em, double now, BorderSettingsSO s, float bonus,
                                   int territory, Entity node, float3 nodePos)
        {
            int tierIndex = TierForNow(s, now);
            var tier = tierIndex >= 0 ? s.Tier(tierIndex) : null;
            if (tier == null || tier.TotalUnits == 0) return;

            float3 origin = WaveOrigin(em, territory);
            int party = _nextPartyId++;
            int partySize = (int)math.round(s.mergePartySize * bonus);
            _scratchWave.Clear();
            for (int u = 0; u < partySize; u++)
            {
                var e = SpawnCurseUnit(em, tier, u, origin);
                em.AddComponentData(e, new CurseLivingMember
                    { Home = territory, Role = RoleMerge, Party = party, Guard = nodePos });
                _scratchWave.Add(e);
            }
            TryRollShardroot(em, s, _scratchWave, $"fill party {party}");

            _merges[party] = new MergeState
            {
                Territory = territory, Node = node, NodePos = nodePos,
                Progress = 0f, Phase = MergeMarching, StartedAt = now, NextThinkAt = 0.0,
            };
            TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                em, _scratchWave, nodePos, FormationShape.Box, attackMove: true);
            UnityEngine.Debug.Log($"[CurseTerritory] FILL — party {party} of {_scratchWave.Count} " +
                $"takes the free node at ({nodePos.x:F0},{nodePos.z:F0}) in its own territory " +
                $"{territory} ({RegionMap.NameOf(territory)}).");
        }

        // ── shepherd ────────────────────────────────────────────────────────

        private void ShepherdLiving(EntityManager em, double now, BorderSettingsSO s)
        {
            var q = QC_Living.Get(em, QT_Living);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var members = q.ToComponentDataArray<CurseLivingMember>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Hostiles by territory, one walk, so each garrison's check is a
            // dictionary lookup rather than a query.
            // Reused across ticks: every list is emptied, never dropped, so the
            // lookups below see exactly the per-tick content (an empty list
            // reads as "no intruders", as a missing key did).
            var hostiles = _hostilesByTerritory;
            foreach (var kv in hostiles) kv.Value.Clear();
            {
                var hq = QueryFacXf<UnitTag>(em);
                using var hx = hq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var hf = hq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < hx.Length; i++)
                {
                    if (hf[i].Value == Faction.Border) continue;
                    var p = hx[i].Position;
                    int t = RegionMap.NearestRegion(p.x, p.z);
                    if (t == RegionMap.None) continue;
                    if (!hostiles.TryGetValue(t, out var list)) hostiles[t] = list = new List<float3>();
                    list.Add(p);
                }
            }

            // The Shardroot holder, if a player has it (the hunt parties use it).
            bool holderKnown = TryShardrootHolder(em, out _, out float3 holderAt, out _);

            // ── garrison: GUARD THE NODE (Territory_Claims.md §6.7) ──
            // A defender engages only what comes within guardRadius of its
            // node and never chases past guardLeashRadius: the curse is a
            // target players choose to attack, not a force that roams.
            float guardR2 = s.guardRadius * s.guardRadius;
            float leashR2 = s.guardLeashRadius * s.guardLeashRadius;
            for (int i = 0; i < ents.Length; i++)
            {
                var m = members[i];
                if (m.Role != RoleGarrison) continue;
                var e = ents[i];
                var p = xfs[i].Position;

                // Its node died: guard the nearest one left in its territory.
                if (_nodesByTerritory.TryGetValue(m.Home, out var nodes) && nodes.Count > 0)
                {
                    var g = nodes[NearestNodeIndex(nodes, m.Guard)];
                    if (Distance2(g, m.Guard) > 4f) { m.Guard = g; em.SetComponentData(e, m); }
                }

                float fromGuard = Distance2(p, m.Guard);
                bool hasTarget = em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null;
                bool moving = em.HasComponent<DesiredDestination>(e)
                              && em.GetComponentData<DesiredDestination>(e).Has != 0;

                if (hasTarget)
                {
                    // Leash: past guardLeashRadius the fight is dropped (a
                    // move order clears the target and holds auto-targeting
                    // off until the unit is back).
                    if (fromGuard > leashR2)
                        TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute(em, e, m.Guard);
                    continue;
                }

                // Idle and off its post: walk back.
                if (fromGuard > guardR2)
                {
                    if (!moving)
                        TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute(em, e, m.Guard);
                    continue;
                }

                // On its post: anyone hostile inside the guard radius?
                if (moving) continue;
                hostiles.TryGetValue(m.Home, out var intruders);
                if (intruders == null) continue;
                float bestD = guardR2; float3 best = default; bool found = false;
                for (int k = 0; k < intruders.Count; k++)
                {
                    float d = Distance2(intruders[k], m.Guard);
                    if (d <= bestD) { bestD = d; best = intruders[k]; found = true; }
                }
                if (found)
                    TheWaningBorder.Core.Commands.Types.AttackMoveCommandHelper.Execute(em, e, best);
            }

            // ── merge parties ──
            _scratchHeld.Clear();
            foreach (var kv in _merges) _scratchHeld.Add(kv.Key);
            _scratchHeld.Sort();
            for (int k = 0; k < _scratchHeld.Count; k++)
            {
                int party = _scratchHeld[k];
                var ms = _merges[party];
                if (now < ms.NextThinkAt) continue;
                ms.NextThinkAt = now + WaveThinkSeconds;

                _scratchWave.Clear();
                float3 centre = float3.zero;
                int fighting = 0;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (members[i].Party != party || members[i].Role != RoleMerge) continue;
                    _scratchWave.Add(ents[i]);
                    centre += xfs[i].Position;
                    if (em.HasComponent<Target>(ents[i]) && em.GetComponentData<Target>(ents[i]).Value != Entity.Null) fighting++;
                }
                bool nodeGone = ms.Node != Entity.Null && !em.Exists(ms.Node);
                if (_scratchWave.Count == 0 || nodeGone)
                {
                    // The party is dead (or the node is gone): the takeover stops.
                    UnityEngine.Debug.Log($"[CurseTerritory] MERGE party {party} lost — takeover of " +
                        $"territory {ms.Territory} stops at {ms.Progress:P0}.");
                    _merges.Remove(party);
                    continue;
                }
                centre /= _scratchWave.Count;

                // Any hostile close to the node?
                float3 threat = default; bool threatened = false; float bestD = float.MaxValue;
                if (hostiles.TryGetValue(ms.Territory, out var near))
                    for (int i = 0; i < near.Count; i++)
                    {
                        float d = Distance2(near[i], ms.NodePos);
                        if (d <= s.mergeDefendRadius * s.mergeDefendRadius && d < bestD)
                        { bestD = d; threat = near[i]; threatened = true; }
                    }

                switch (ms.Phase)
                {
                    case MergeMarching:
                        if (Distance2(centre, ms.NodePos) <= 6f * 6f || now - ms.StartedAt > 90.0)
                        {
                            ms.Phase = MergeMerging;
                            UnityEngine.Debug.Log($"[CurseTerritory] MERGE party {party} is inside the node; " +
                                $"{s.mergeSeconds:F0}s to turn territory {ms.Territory}.");
                        }
                        break;

                    case MergeMerging:
                        if (threatened)
                        {
                            // Summoned out: the bar pauses, the party defends.
                            ms.Phase = MergeDefending;
                            TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                                em, _scratchWave, threat, FormationShape.Box, attackMove: true);
                            break;
                        }
                        // Territory_Claims.md §6.5: the party CLAIMS by
                        // standing (the ownership meter does the counting);
                        // the node only rises on ground that is already the
                        // curse's. Locked under it by a player's extractor or
                        // Fortress: the claim is over (see below).
                        if (TerritoryOwnership.OwnerOf(ms.Territory) != TerritoryOwnership.Curse)
                        {
                            // Locked: the claim is over and the party goes
                            // home — the curse does not raid (§6.7, 2026-10-03).
                            if (TerritoryOwnership.IsLocked(ms.Territory))
                                SendMergeHome(em, party);
                            break;
                        }
                        if (ms.Node == Entity.Null)
                        {
                            // Nothing to build on: the party holds the ground
                            // as its garrison, by standing on it.
                            HoldWithoutNode(em, party, ms);
                            break;
                        }
                        ms.Progress += (float)(WaveThinkSeconds / s.mergeSeconds);
                        if (ms.Progress >= 1f)
                            CompleteMerge(em, party, ms);
                        break;

                    case MergeDefending:
                        if (!threatened && fighting == 0)
                        {
                            // Threat gone: back inside, bar resumes where it paused.
                            ms.Phase = MergeMerging;
                            TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                                em, _scratchWave, ms.NodePos, FormationShape.Box, attackMove: false);
                        }
                        else if (threatened && fighting == 0)
                        {
                            // Still someone there and nobody engaged: go again.
                            TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                                em, _scratchWave, threat, FormationShape.Box, attackMove: true);
                        }
                        break;
                }
            }

            // ── Shardroot hunt parties (§6.6) ──
            // The only party that goes for a player rather than for ground.
            // It presses the holder for as long as anyone holds the Shardroot,
            // then walks home and rejoins the garrison.
            _scratchHeld.Clear();
            foreach (var kv in _raids) _scratchHeld.Add(kv.Key);
            _scratchHeld.Sort();
            for (int k = 0; k < _scratchHeld.Count; k++)
            {
                int party = _scratchHeld[k];
                var rs = _raids[party];
                if (now < rs.NextThinkAt) continue;
                rs.NextThinkAt = now + WaveThinkSeconds;

                _scratchWave.Clear();
                int fighting = 0;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (members[i].Party != party || members[i].Role != RoleRaid) continue;
                    _scratchWave.Add(ents[i]);
                    if (em.HasComponent<Target>(ents[i]) && em.GetComponentData<Target>(ents[i]).Value != Entity.Null) fighting++;
                }
                if (_scratchWave.Count == 0) { _raids.Remove(party); continue; }

                if (!rs.Returning && !holderKnown)
                {
                    rs.Returning = true;
                    TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                        em, _scratchWave, WaveOrigin(em, rs.Home), FormationShape.Box, attackMove: false);
                    continue;
                }
                if (rs.Returning)
                {
                    // Home: become garrison and forget the hunt.
                    float3 home = WaveOrigin(em, rs.Home);
                    bool allHome = true;
                    for (int i = 0; i < _scratchWave.Count; i++)
                    {
                        var p = em.GetComponentData<LocalTransform>(_scratchWave[i]).Position;
                        if (Distance2(p, home) > 20f * 20f) { allHome = false; break; }
                    }
                    if (allHome)
                    {
                        for (int i = 0; i < _scratchWave.Count; i++)
                        {
                            var e = _scratchWave[i];
                            var m = em.GetComponentData<CurseLivingMember>(e);
                            m.Role = RoleGarrison; m.Party = -1; m.Guard = home;
                            em.SetComponentData(e, m);
                        }
                        _raids.Remove(party);
                    }
                    continue;
                }
                if (fighting == 0)
                {
                    rs.Objective = holderAt;
                    TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                        em, _scratchWave, holderAt, FormationShape.Box, attackMove: true);
                }
            }
        }

        /// <summary>A claim party on a node-less territory the meter has
        /// turned: it stays as the territory's garrison and holds it by
        /// standing on it. No node, so no army will ever spawn there.</summary>
        private void HoldWithoutNode(EntityManager em, int party, MergeState ms)
        {
            for (int i = 0; i < _scratchWave.Count; i++)
            {
                var e = _scratchWave[i];
                var m = em.GetComponentData<CurseLivingMember>(e);
                m.Home = ms.Territory; m.Role = RoleGarrison; m.Party = -1;
                m.Guard = em.GetComponentData<LocalTransform>(e).Position;
                em.SetComponentData(e, m);
            }
            _merges.Remove(party);
            _held.Add(ms.Territory);
            UnityEngine.Debug.Log($"[CurseTerritory] HELD — territory {ms.Territory} " +
                $"({RegionMap.NameOf(ms.Territory)}) claimed by standing; it has no node to raise.");
        }

        /// <summary>A player locked the ground under a claim party: the claim
        /// is over, and the party walks home to rejoin its garrison
        /// (Territory_Claims.md §6.5, §6.7 — the curse does not raid).</summary>
        private void SendMergeHome(EntityManager em, int party)
        {
            for (int i = 0; i < _scratchWave.Count; i++)
            {
                var e = _scratchWave[i];
                var m = em.GetComponentData<CurseLivingMember>(e);
                m.Role = RoleGarrison; m.Party = -1; m.Guard = WaveOrigin(em, m.Home);
                em.SetComponentData(e, m);
                TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute(em, e, m.Guard);
            }
            _merges.Remove(party);
        }

        /// <summary>The bar is full: a curse node rises on the merged node,
        /// hazing the patch, and the territory is the curse's. The party
        /// becomes the new territory's first garrison.</summary>
        private void CompleteMerge(EntityManager em, int party, MergeState ms)
        {
            // The territory is already the curse's (the meter turned it); the
            // node LOCKS it (Territory_Claims.md §3) and fields its garrison.
            var anchor = TheWaningBorder.Entities.SmallNode.Create(em, ms.NodePos);
            _anchors[ms.Territory] = anchor;
            _held.Add(ms.Territory);

            for (int i = 0; i < _scratchWave.Count; i++)
            {
                var e = _scratchWave[i];
                var m = em.GetComponentData<CurseLivingMember>(e);
                m.Home = ms.Territory; m.Role = RoleGarrison; m.Party = -1; m.Guard = ms.NodePos;
                em.SetComponentData(e, m);
            }
            _merges.Remove(party);

            SimSignals.Ping(ms.NodePos, SimPingKind.Curse, 15f, big: true);
            SimSignals.Notify(Loc.T("The curse has taken a territory!"));
            UnityEngine.Debug.Log($"[CurseTerritory] TAKEN — territory {ms.Territory} " +
                $"({RegionMap.NameOf(ms.Territory)}) turned by merge; anchor at " +
                $"({ms.NodePos.x:F0},{ms.NodePos.z:F0}). Curse holds {_held.Count} territories.");
        }
    }
}
