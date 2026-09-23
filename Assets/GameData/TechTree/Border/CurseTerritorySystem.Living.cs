// CurseTerritorySystem.Living.cs
// THE LIVING CURSE — Curse_And_Shardroot.md §2.11 (2026-09-13, CURRENT).
//
// Supersedes the Waking (§2.8) and the army half of the Wrath (§2.10) when
// BorderSettings.livingCurse is on, which is the shipped default. The old
// model stays in the main file, untouched, behind the switch.
//
// What this file does, in the order it runs each check:
//
//   TickGarrisons   every `armySpawnSeconds` each curse-held territory
//                   spawns its garrison as ONE ARMY, `garrisonCap` x
//                   `armyGrowth`^n strong (2.13). Nothing regrows between
//                   spawns: kill the army and the territory is open.
//   TryExpand       every `expansionSeconds` the curse sends a HARASSMENT
//                   party at an adjacent territory it does not hold. One
//                   with a veilstone or veilsteel node gets a MERGE PARTY
//                   sent to that node; one without gets a RAID on its
//                   resource buildings and stays immune to takeover.
//   (every spawn)   one `shardrootChance` roll that a unit carries the
//                   Shardroot (2.13 rule 5); the wells then hold it no more.
//   ShepherdLiving  defenders attack anything hostile standing in their
//                   territory, CHASE it, and walk home after `leashSeconds`
//                   with nothing to fight. Merge parties march to the node,
//                   then sit inside it filling a progress bar; a hostile
//                   within `mergeDefendRadius` pulls them out and PAUSES
//                   the bar; a dead party resets it; a full bar turns the
//                   node (a curse node rises on it) and the territory.
//                   Raiders strike for `raidSeconds`, then walk home and
//                   become garrison.
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
        /// <summary>Sim time this unit was last seen outside its home
        /// territory with no target, for the leash. Negative = not
        /// currently abroad.</summary>
        public double AbroadSince;
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
        }

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
        private void TickGarrisons(EntityManager em, double now, BorderSettingsSO s)
        {
            int tierIndex = TierForNow(s, now);
            if (tierIndex < 0) return;
            var tier = s.Tier(tierIndex);
            if (tier == null || tier.TotalUnits == 0) return;

            // Standing count per territory, one walk.
            var q = QC_Living.Get(em, QT_Living);
            using var members = q.ToComponentDataArray<CurseLivingMember>(Allocator.Temp);
            var standing = new Dictionary<int, int>();
            for (int i = 0; i < members.Length; i++)
                if (members[i].Role == RoleGarrison)
                    standing[members[i].Home] = standing.TryGetValue(members[i].Home, out int c) ? c + 1 : 1;

            _scratchHeld.Clear();
            foreach (int t in _held) _scratchHeld.Add(t);
            _scratchHeld.Sort();

            for (int i = 0; i < _scratchHeld.Count; i++)
            {
                int t = _scratchHeld[i];
                if (!_nextArmyAt.TryGetValue(t, out double at))
                {
                    // First army almost at once, staggered by territory so
                    // several newly held territories do not spawn on one tick.
                    _nextArmyAt[t] = now + (t % 5) * 4.0;
                    continue;
                }
                if (now < at) continue;

                _armySpawns.TryGetValue(t, out int n);
                int size = (int)math.round(s.garrisonCap * math.pow(math.max(1f, s.armyGrowth), n));
                standing.TryGetValue(t, out int have);
                int toSpawn = math.max(0, size - have);

                float3 origin = WaveOrigin(em, t);
                _scratchWave.Clear();
                for (int u = 0; u < toSpawn; u++)
                {
                    var e = SpawnCurseUnit(em, tier, have + u, origin);
                    em.AddComponentData(e, new CurseLivingMember
                        { Home = t, Role = RoleGarrison, Party = -1, AbroadSince = -1.0 });
                    _scratchWave.Add(e);
                }
                _armySpawns[t] = n + 1;
                _nextArmyAt[t] = now + s.armySpawnSeconds;
                UnityEngine.Debug.Log($"[CurseTerritory] ARMY {n + 1} in territory {t} ({RegionMap.NameOf(t)}): " +
                    $"{toSpawn} spawned, {have} survived, {size} strong; next in {s.armySpawnSeconds:0}s.");
                TryRollShardroot(em, s, _scratchWave, $"garrison army {n + 1} of territory {t}");
            }
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
            if (spawned.Count == 0 || s.shardrootChance <= 0f) return;
            var q = QC_ShardrootState.Get(em, QT_ShardrootState);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.Found != 0) return;

            // Always draw, so the RNG stream is the same on every peer
            // whether or not the roll succeeds.
            float roll = _rng.NextFloat();
            int pick = _rng.NextInt(0, spawned.Count);
            if (roll >= s.shardrootChance) return;

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

        // ── expansion ───────────────────────────────────────────────────────

        /// <summary>Nodes (veilstone or veilsteel) standing in a territory,
        /// sorted by entity so every peer picks the same one.</summary>
        private void NodesIn(EntityManager em, int territory, List<(Entity e, float3 p)> into)
        {
            into.Clear();
            Collect<VeilstoneOutcroppingTag>(em, territory, into);
            Collect<VeilsteelDepositTag>(em, territory, into);
            into.Sort((a, b) => a.e.Index.CompareTo(b.e.Index));
        }

        private static void Collect<T>(EntityManager em, int territory, List<(Entity, float3)> into)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(),
                                         ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();
            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                if (RegionMap.NearestRegion(p.x, p.z) == territory) into.Add((ents[i], p));
            }
        }

        private readonly List<(Entity e, float3 p)> _scratchNodes = new();

        private void TryExpand(EntityManager em, double now, BorderSettingsSO s)
        {
            if (_held.Count == 0) return;
            if (_merges.Count > 0) return;    // one merge at a time: it is the curse's whole attention

            var hallCounts = CountPerRegion<HallTag>(em);

            _scratchCandidates.Clear();
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (_held.Contains(r)) continue;
                bool adjacent = false;
                foreach (int h in _held)
                    if (AreAdjacent(h, r)) { adjacent = true; break; }
                if (adjacent) _scratchCandidates.Add(r);
            }
            if (_scratchCandidates.Count == 0) return;
            _scratchCandidates.Sort();
            int pick = _scratchCandidates[_rng.NextInt(0, _scratchCandidates.Count)];

            // Which held neighbour sends the party: the first adjacent one in
            // index order, so every peer agrees.
            int from = -1;
            _scratchHeld.Clear();
            foreach (int h in _held) _scratchHeld.Add(h);
            _scratchHeld.Sort();
            for (int i = 0; i < _scratchHeld.Count; i++)
                if (AreAdjacent(_scratchHeld[i], pick)) { from = _scratchHeld[i]; break; }
            if (from < 0) return;
            float3 origin = WaveOrigin(em, from);

            int tierIndex = TierForNow(s, now);
            var tier = tierIndex >= 0 ? s.Tier(tierIndex) : null;
            if (tier == null || tier.TotalUnits == 0) return;

            // A home territory (one with a Hall) is never MERGED away from
            // under a player (kept from the old conquest rule); it can still
            // be raided.
            NodesIn(em, pick, _scratchNodes);
            bool mergeable = _scratchNodes.Count > 0 && hallCounts[pick] == 0;

            int party = _nextPartyId++;
            _scratchWave.Clear();
            for (int u = 0; u < s.mergePartySize; u++)
            {
                var e = SpawnCurseUnit(em, tier, u, origin);
                em.AddComponentData(e, new CurseLivingMember
                    { Home = from, Role = mergeable ? RoleMerge : RoleRaid, Party = party, AbroadSince = -1.0 });
                _scratchWave.Add(e);
            }
            TryRollShardroot(em, s, _scratchWave, $"harassment party {party}");

            if (mergeable)
            {
                var (node, npos) = _scratchNodes[0];
                _merges[party] = new MergeState
                {
                    Territory = pick, Node = node, NodePos = npos,
                    Progress = 0f, Phase = MergeMarching, StartedAt = now, NextThinkAt = 0.0,
                };
                TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                    em, _scratchWave, npos, FormationShape.Box, attackMove: false);
                SimSignals.Ping(npos, SimPingKind.Curse, 10f, big: true);
                UnityEngine.Debug.Log($"[CurseTerritory] MERGE — party {party} of {_scratchWave.Count} " +
                    $"marches on the node at ({npos.x:F0},{npos.z:F0}) in territory {pick} " +
                    $"({RegionMap.NameOf(pick)}) from {from}.");
            }
            else
            {
                // Node-less (or a home): immune to takeover, subject to a raid.
                if (!TryNearestHostileBuildingIn(em, pick, out float3 target))
                {
                    var seed = RegionMap.SeedOf(pick);
                    target = new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.y), seed.y);
                }
                _raids[party] = new RaidState
                {
                    Home = from, Target = pick, Objective = target,
                    StartedAt = now, NextThinkAt = 0.0, Returning = false,
                };
                TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                    em, _scratchWave, target, FormationShape.Box, attackMove: true);
                SimSignals.Ping(target, SimPingKind.Curse, 10f);
                UnityEngine.Debug.Log($"[CurseTerritory] RAID — party {party} of {_scratchWave.Count} " +
                    $"raids territory {pick} ({RegionMap.NameOf(pick)}); it has no node to take.");
            }
        }

        /// <summary>The harassment army's objective in a territory (2.13
        /// rule 4): the player's nearest RESOURCE building -- mine, veilstone
        /// mine, gatherer's hut -- and only when there is none, the nearest
        /// building of any kind. A hindrance, not a conquering force.</summary>
        private static bool TryNearestHostileBuildingIn(EntityManager em, int territory, out float3 pos)
        {
            pos = default;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(),
                                         ComponentType.ReadOnly<FactionTag>(),
                                         ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            q.Dispose();
            var seed = RegionMap.SeedOf(territory);
            float bestD = float.MaxValue, bestResD = float.MaxValue;
            float3 res = default;
            for (int i = 0; i < xfs.Length; i++)
            {
                if (facs[i].Value == Faction.Border) continue;
                var p = xfs[i].Position;
                if (RegionMap.NearestRegion(p.x, p.z) != territory) continue;
                float dx = p.x - seed.x, dz = p.z - seed.y;
                float d = dx * dx + dz * dz;
                bool resource = em.HasComponent<MineTag>(ents[i])
                    || em.HasComponent<VeilstoneMineTag>(ents[i])
                    || em.HasComponent<GathererHutTag>(ents[i]);
                if (resource && d < bestResD) { bestResD = d; res = p; }
                if (d < bestD) { bestD = d; pos = p; }
            }
            if (bestResD < float.MaxValue) { pos = res; return true; }
            return bestD < float.MaxValue;
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
            var hostiles = new Dictionary<int, List<float3>>();
            {
                var hq = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(),
                                              ComponentType.ReadOnly<FactionTag>(),
                                              ComponentType.ReadOnly<LocalTransform>());
                using var hx = hq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var hf = hq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                hq.Dispose();
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

            // ── garrison: defend, chase, leash ──
            for (int i = 0; i < ents.Length; i++)
            {
                var m = members[i];
                if (m.Role != RoleGarrison) continue;
                var e = ents[i];
                var p = xfs[i].Position;
                bool hasTarget = em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null;
                int here = RegionMap.NearestRegion(p.x, p.z);

                if (hasTarget)
                {
                    if (m.AbroadSince >= 0.0) { m.AbroadSince = -1.0; em.SetComponentData(e, m); }
                    continue;   // fighting: leave it to the fight (§2.11 rule 3: pursue)
                }

                if (here != m.Home)
                {
                    // Abroad with nothing to fight: leash.
                    if (m.AbroadSince < 0.0) { m.AbroadSince = now; em.SetComponentData(e, m); }
                    else if (now - m.AbroadSince >= s.leashSeconds)
                    {
                        TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute(
                            em, e, WaveOrigin(em, m.Home));
                        m.AbroadSince = -1.0; em.SetComponentData(e, m);
                    }
                    continue;
                }

                // At home and idle: is anyone in the territory?
                if (hostiles.TryGetValue(m.Home, out var intruders) && intruders.Count > 0)
                {
                    float bestD = float.MaxValue; float3 best = default;
                    for (int k = 0; k < intruders.Count; k++)
                    {
                        float d = Distance2(intruders[k], p);
                        if (d < bestD) { bestD = d; best = intruders[k]; }
                    }
                    bool moving = em.HasComponent<DesiredDestination>(e)
                                  && em.GetComponentData<DesiredDestination>(e).Has != 0;
                    if (!moving)
                        TheWaningBorder.Core.Commands.Types.AttackMoveCommandHelper.Execute(em, e, best);
                }
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
                if (_scratchWave.Count == 0 || !em.Exists(ms.Node))
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

            // ── raids ──
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

                if (!rs.Returning && now - rs.StartedAt >= s.raidSeconds)
                {
                    rs.Returning = true;
                    TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                        em, _scratchWave, WaveOrigin(em, rs.Home), FormationShape.Box, attackMove: false);
                    continue;
                }
                if (rs.Returning)
                {
                    // Home: become garrison and forget the raid.
                    float3 home = WaveOrigin(em, rs.Home);
                    bool allHome = true;
                    for (int i = 0; i < _scratchWave.Count; i++)
                    {
                        var e = _scratchWave[i];
                        var p = em.GetComponentData<LocalTransform>(e).Position;
                        if (Distance2(p, home) > 20f * 20f) { allHome = false; break; }
                    }
                    if (allHome)
                    {
                        for (int i = 0; i < _scratchWave.Count; i++)
                        {
                            var e = _scratchWave[i];
                            var m = em.GetComponentData<CurseLivingMember>(e);
                            m.Role = RoleGarrison; m.Party = -1; m.AbroadSince = -1.0;
                            em.SetComponentData(e, m);
                        }
                        _raids.Remove(party);
                    }
                    continue;
                }
                if (fighting == 0)
                {
                    // Nothing engaged: press the nearest hostile building in the target.
                    if (TryNearestHostileBuildingIn(em, rs.Target, out float3 obj))
                    {
                        rs.Objective = obj;
                        TheWaningBorder.Core.Commands.Types.FormationMoveCommandHelper.Execute(
                            em, _scratchWave, obj, FormationShape.Box, attackMove: true);
                    }
                }
            }
        }

        /// <summary>The bar is full: a curse node rises on the merged node,
        /// hazing the patch, and the territory is the curse's. The party
        /// becomes the new territory's first garrison.</summary>
        private void CompleteMerge(EntityManager em, int party, MergeState ms)
        {
            var anchor = TheWaningBorder.Entities.SmallNode.Create(em, ms.NodePos);
            _anchors[ms.Territory] = anchor;
            TerritoryOwnership.MarkCurseHeld(ms.Territory, true);
            _held.Add(ms.Territory);
            TerritoryOwnership.Recompute(em);

            for (int i = 0; i < _scratchWave.Count; i++)
            {
                var e = _scratchWave[i];
                var m = em.GetComponentData<CurseLivingMember>(e);
                m.Home = ms.Territory; m.Role = RoleGarrison; m.Party = -1; m.AbroadSince = -1.0;
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
