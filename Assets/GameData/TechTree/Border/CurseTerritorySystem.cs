// CurseTerritorySystem.cs
// The curse as a territorial power (docs/Design/Regions.md §3, 2026-08-31).
//
// The curse is NOT a full player — no economy, no tech, no build orders, no
// brain. It does exactly two things, and this file is both of them:
//
//   1. TAKE TERRITORY. It holds every territory with a live well (the pure
//      nodes — the verb objectives, never razed) from the first tick, and
//      every ConquerIntervalSeconds it conquers ONE random adjacent territory
//      that has no Hall and at least one veilstone node. A conquest is
//      instant, like a player's claim, and plants a destroyable anchor
//      (SmallNode). Kill the anchor and the territory reverts to Natural on
//      the next sync — the same claim-dies-with-its-structure rule players
//      live by.
//
//   2. SPAWN ARMIES. Every curse-held territory that carries veilstone
//      fields waves on the BorderSettings schedule (tier ladder + breathers),
//      marching on the nearest hostile Hall. Idle wave units re-march at the
//      nearest hostile building, so a wave that razes its target rolls on to
//      the next rather than standing in the ashes.
//
// Holdings are pushed into TerritoryOwnership (MarkCurseHeld), which stamps
// them into the ownership array on its normal Recompute — so the build gate,
// the income tick, the border ribbon and the AI all see curse ground through
// the exact same lens they see player ground. No influence map anywhere.

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

/// <summary>Marks a unit fielded by a curse territory wave. Carries no data —
/// membership is what lets the wave shepherd re-march idle units.</summary>
public struct CurseWaveMember : IComponentData { }

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CurseTerritorySystem : SystemBase
    {
        private const float CheckInterval = 5f;

        /// <summary>Seconds between conquest attempts. One territory per
        /// attempt, so the curse's footprint grows at a legible, counterable
        /// pace — comparable to a player expanding.</summary>
        private const float ConquerIntervalSeconds = 150f;

        /// <summary>Opening grace before the first conquest — players get to
        /// stand up before the map starts moving.</summary>
        private const float FirstConquerDelaySeconds = 240f;

        /// <summary>Hard cap on live wave units across all curse territories.
        /// Pressure, not a perf incident.</summary>
        private const int MaxLiveWaveUnits = 48;

        /// <summary>Samples along the seed-to-seed segment for Voronoi
        /// adjacency: two regions are neighbours when the walk between their
        /// seeds crosses only the two of them.</summary>
        private const int AdjacencySamples = 9;

        private float _timer;
        private double _nextConquerAt = -1.0;
        private int _rngEpoch = -1;
        private Unity.Mathematics.Random _rng;

        /// <summary>Territory -> the destroyable anchor claiming it. Wells are
        /// tracked separately (their territories are held for the well's
        /// lifetime, and wells only fall to the Feraldis verb).</summary>
        private readonly Dictionary<int, Entity> _anchors = new();

        /// <summary>Territory -> sim time its next wave may field.</summary>
        private readonly Dictionary<int, double> _nextWaveAt = new();

        private readonly HashSet<int> _held = new();
        private readonly List<int> _scratchHeld = new();
        private readonly List<int> _scratchCandidates = new();

        private EntityQuery _waveUnitQuery;

        protected override void OnCreate()
        {
            _waveUnitQuery = GetEntityQuery(ComponentType.ReadOnly<CurseWaveMember>());
        }

        protected override void OnUpdate()
        {
            if (!GameSettings.BorderEnabled) return;
            if (!RegionMap.Ready) return;

            // Per-match re-seed, same contract as BloodCurseSpawnSystem: the
            // system object outlives the scene, and a carried-over RNG stream
            // is a silent lockstep fork on the next draw.
            if (_rngEpoch != SimCadence.Epoch)
            {
                _rngEpoch = SimCadence.Epoch;
                _rng = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xC0C0A) | 1u);
                _anchors.Clear();
                _nextWaveAt.Clear();
                _held.Clear();
                _nextConquerAt = -1.0;
                _timer = 0f;
            }

            _timer -= SystemAPI.Time.DeltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;

            var em = EntityManager;
            double now = SystemAPI.Time.ElapsedTime;
            if (_nextConquerAt < 0.0)
                _nextConquerAt = now + FirstConquerDelaySeconds;

            SyncHoldings(em);

            if (now >= _nextConquerAt)
            {
                TryConquer(em);
                _nextConquerAt = now + ConquerIntervalSeconds;
            }

            TickWaves(em, now);
            ShepherdWaveUnits(em);
        }

        // ── holdings ────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuild the held set from what is actually alive — wells hold their
        /// territory for as long as they stand, anchors likewise — and push
        /// the diff into TerritoryOwnership. Deriving from live entities is
        /// what makes reversion free: DeathSystem destroys the anchor, the
        /// next sync fails the Exists check, the territory drops out.
        /// </summary>
        private void SyncHoldings(EntityManager em)
        {
            _scratchHeld.Clear();

            var wellQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = wellQ.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    int t = RegionMap.NearestRegion(xfs[i].Position.x, xfs[i].Position.z);
                    if (t != RegionMap.None && !_scratchHeld.Contains(t)) _scratchHeld.Add(t);
                }
            wellQ.Dispose();

            // Anchors: drop the dead, keep the living.
            var deadAnchors = new List<int>();
            foreach (var kv in _anchors)
                if (!em.Exists(kv.Value)) deadAnchors.Add(kv.Key);
            for (int i = 0; i < deadAnchors.Count; i++)
            {
                _anchors.Remove(deadAnchors[i]);
                _nextWaveAt.Remove(deadAnchors[i]);
                UnityEngine.Debug.Log($"[CurseTerritory] anchor in territory {deadAnchors[i]} " +
                           $"({RegionMap.NameOf(deadAnchors[i])}) destroyed — ground reverts to Natural.");
            }
            foreach (var kv in _anchors)
                if (!_scratchHeld.Contains(kv.Key)) _scratchHeld.Add(kv.Key);

            // Push the diff.
            bool changed = false;
            foreach (int t in _held)
                if (!_scratchHeld.Contains(t))
                {
                    TerritoryOwnership.MarkCurseHeld(t, false);
                    changed = true;
                }
            for (int i = 0; i < _scratchHeld.Count; i++)
                if (!_held.Contains(_scratchHeld[i]))
                {
                    TerritoryOwnership.MarkCurseHeld(_scratchHeld[i], true);
                    changed = true;
                }
            if (changed)
            {
                _held.Clear();
                for (int i = 0; i < _scratchHeld.Count; i++) _held.Add(_scratchHeld[i]);
                // Ownership is normally recomputed on the income tick; a
                // conquest or reversion should not wait up to 5 s to be
                // visible to the build gate and the borders.
                TerritoryOwnership.Recompute(em);
            }
        }

        // ── conquest ────────────────────────────────────────────────────────

        /// <summary>
        /// One conquest attempt: gather every territory adjacent to curse
        /// ground that has veilstone and no Hall, pick one at random, plant a
        /// destroyable anchor beside its veilstone. Candidates are collected
        /// in region-index order and drawn with the seeded RNG, so every
        /// lockstep peer conquers the same ground.
        /// </summary>
        private void TryConquer(EntityManager em)
        {
            if (_held.Count == 0) return;

            var veilstoneCounts = CountPerRegion<VeilstoneOutcroppingTag>(em);
            var hallCounts = CountPerRegion<HallTag>(em);

            _scratchCandidates.Clear();
            for (int r = 0; r < RegionMap.Count; r++)
            {
                if (_held.Contains(r)) continue;
                if (veilstoneCounts[r] <= 0) continue;
                if (hallCounts[r] > 0) continue;

                bool adjacent = false;
                foreach (int h in _held)
                    if (AreAdjacent(h, r)) { adjacent = true; break; }
                if (adjacent) _scratchCandidates.Add(r);
            }

            if (_scratchCandidates.Count == 0)
            {
                UnityEngine.Debug.Log("[CurseTerritory] conquest tick: no adjacent hall-less " +
                           "veilstone territory — the curse holds its ground.");
                return;
            }

            _scratchCandidates.Sort();
            int pick = _scratchCandidates[_rng.NextInt(0, _scratchCandidates.Count)];

            if (!TryAnchorSeat(em, pick, out float3 seat))
            {
                UnityEngine.Debug.Log($"[CurseTerritory] conquest of territory {pick} failed — " +
                           "no seatable ground for the anchor.");
                return;
            }

            var anchor = TheWaningBorder.Entities.SmallNode.Create(em, seat);
            _anchors[pick] = anchor;
            TerritoryOwnership.MarkCurseHeld(pick, true);
            _held.Add(pick);
            TerritoryOwnership.Recompute(em);

            SimSignals.Ping(seat, SimPingKind.Curse, 15f, big: true);
            SimSignals.Notify(Loc.T("The curse has taken a territory!"));
            UnityEngine.Debug.Log($"[CurseTerritory] CONQUEST — territory {pick} ({RegionMap.NameOf(pick)}) " +
                       $"taken; anchor at ({seat.x:F0},{seat.z:F0}). Curse holds {_held.Count} territories.");
        }

        /// <summary>Voronoi adjacency: walk the segment between the two seeds;
        /// neighbours are regions whose walk never crosses a third.</summary>
        private static bool AreAdjacent(int a, int b)
        {
            var sa = RegionMap.SeedOf(a);
            var sb = RegionMap.SeedOf(b);
            for (int i = 1; i < AdjacencySamples; i++)
            {
                float f = i / (float)AdjacencySamples;
                float x = math.lerp(sa.x, sb.x, f);
                float z = math.lerp(sa.y, sb.y, f);
                int r = RegionMap.NearestRegion(x, z);
                if (r != a && r != b) return false;
            }
            return true;
        }

        /// <summary>Anchor beside the region's veilstone nearest its seed —
        /// the curse grows from the stone. Steps toward the seed until the
        /// spot is passable and clear of the node's own cell.</summary>
        private bool TryAnchorSeat(EntityManager em, int region, out float3 seat)
        {
            seat = default;
            var seed = RegionMap.SeedOf(region);

            float3 best = default;
            float bestD = float.MaxValue;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    var p = xfs[i].Position;
                    if (RegionMap.RegionAt(p.x, p.z) != region) continue;
                    float dx = p.x - seed.x, dz = p.z - seed.y;
                    float d = dx * dx + dz * dz;
                    // Strict ordering with a deterministic tie-break so every
                    // peer picks the same node whatever the query order.
                    if (d < bestD - 0.01f
                        || (math.abs(d - bestD) <= 0.01f && (p.x < best.x || (p.x == best.x && p.z < best.z))))
                    {
                        bestD = d;
                        best = p;
                    }
                }
            q.Dispose();
            if (bestD == float.MaxValue) return false;

            float2 dir = math.normalizesafe(new float2(seed.x - best.x, seed.y - best.z),
                                            new float2(1f, 0f));
            var grid = PassabilityGrid.Instance;
            for (float step = 4f; step <= 16f; step += 2f)
            {
                float px = best.x + dir.x * step;
                float pz = best.z + dir.y * step;
                if (RegionMap.RegionAt(px, pz) != region) continue;
                if (grid != null && !grid.IsPassableForRadius(new float3(px, 0f, pz), 1f)) continue;
                seat = new float3(px, TerrainUtility.GetHeight(px, pz), pz);
                return true;
            }
            seat = new float3(best.x + dir.x * 4f,
                              TerrainUtility.GetHeight(best.x + dir.x * 4f, best.z + dir.y * 4f),
                              best.z + dir.y * 4f);
            return true;
        }

        private static int[] CountPerRegion<T>(EntityManager em) where T : unmanaged, IComponentData
        {
            var counts = new int[RegionMap.Count];
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    int r = RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z);
                    if (r >= 0 && r < counts.Length) counts[r]++;
                }
            q.Dispose();
            return counts;
        }

        // ── waves ───────────────────────────────────────────────────────────

        /// <summary>
        /// Field waves from curse-held veilstone territories on the
        /// BorderSettings schedule. Each territory runs its own breather
        /// clock, staggered by territory id so the map does not fire all its
        /// fronts in one frame.
        /// </summary>
        private void TickWaves(EntityManager em, double now)
        {
            var settings = BorderSettings.Get();
            if (settings == null || settings.TierCount == 0) return;
            if (now < settings.firstWaveDelaySeconds) return;

            settings.TryGetWave(now, out int tierIndex, out float breather);
            var tier = settings.Tier(math.min(tierIndex, settings.TierCount - 1));
            if (tier == null || tier.TotalUnits == 0) return;

            int liveWaveUnits = _waveUnitQuery.CalculateEntityCount();

            var veilstoneCounts = CountPerRegion<VeilstoneOutcroppingTag>(em);

            _scratchHeld.Clear();
            foreach (int t in _held) _scratchHeld.Add(t);
            _scratchHeld.Sort();

            for (int i = 0; i < _scratchHeld.Count; i++)
            {
                int t = _scratchHeld[i];
                if (t < 0 || t >= veilstoneCounts.Length || veilstoneCounts[t] <= 0)
                    continue;   // waves come from veilstone ground only

                if (!_nextWaveAt.TryGetValue(t, out double at))
                {
                    // First wave staggers by territory id so fronts open one
                    // after another, not all at once.
                    _nextWaveAt[t] = now + (t % 5) * 20.0;
                    continue;
                }
                if (now < at) continue;
                if (liveWaveUnits >= MaxLiveWaveUnits) break;

                float3 origin = WaveOrigin(em, t);
                if (!TryNearestHostileHall(em, origin, out float3 target)) return;

                int spawned = SpawnWave(em, tier, origin, target,
                                        MaxLiveWaveUnits - liveWaveUnits);
                liveWaveUnits += spawned;
                _nextWaveAt[t] = now + breather;

                SimSignals.Ping(origin, SimPingKind.Curse, 10f);
                UnityEngine.Debug.Log($"[CurseTerritory] WAVE — territory {t} ({RegionMap.NameOf(t)}) " +
                           $"fields {spawned} units (tier {tierIndex}) marching on " +
                           $"({target.x:F0},{target.z:F0}); next in {breather:F0}s.");
            }
        }

        /// <summary>Waves rise from the territory's anchor, or its well.</summary>
        private float3 WaveOrigin(EntityManager em, int territory)
        {
            if (_anchors.TryGetValue(territory, out var anchor) && em.Exists(anchor))
                return em.GetComponentData<LocalTransform>(anchor).Position;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            float3 best = default;
            bool found = false;
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    var p = xfs[i].Position;
                    if (RegionMap.NearestRegion(p.x, p.z) != territory) continue;
                    best = p;
                    found = true;
                    break;
                }
            q.Dispose();
            if (found) return best;

            var seed = RegionMap.SeedOf(territory);
            return new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.y), seed.y);
        }

        private int SpawnWave(EntityManager em, BorderSettingsSO.ArmyTier tier,
                              float3 origin, float3 target, int budget)
        {
            int total = math.min(tier.TotalUnits, budget);
            int spawned = 0;
            for (int u = 0; u < total; u++)
            {
                float angle = _rng.NextFloat(0f, math.PI * 2f);
                float dist = _rng.NextFloat(2f, 6f);
                float sx = origin.x + math.cos(angle) * dist;
                float sz = origin.z + math.sin(angle) * dist;
                var pos = new float3(sx, TerrainUtility.GetHeight(sx, sz), sz);

                Entity e = u < tier.godsplinters
                    ? TheWaningBorder.Entities.Godsplinter.Create(em, pos, Faction.Border)
                    : u < tier.godsplinters + tier.veilstingers
                        ? TheWaningBorder.Entities.Veilstinger.Create(em, pos, Faction.Border)
                        : TheWaningBorder.Entities.Crystalling.Create(em, pos, Faction.Border);

                em.AddComponent<CurseWaveMember>(e);
                em.SetComponentData(e, new DesiredDestination { Position = target, Has = 1 });
                spawned++;
            }
            return spawned;
        }

        /// <summary>
        /// Re-march idle wave units. TargetingSystem owns them while an enemy
        /// is in reach (it re-issues the chase every frame); this only touches
        /// units whose destination was consumed and who have nothing to fight
        /// — a razed site, a dead target — and points them at the nearest
        /// hostile building so the wave rolls on.
        /// </summary>
        private void ShepherdWaveUnits(EntityManager em)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<CurseWaveMember>(),
                ComponentType.ReadWrite<DesiredDestination>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();

            for (int i = 0; i < ents.Length; i++)
            {
                var dd = em.GetComponentData<DesiredDestination>(ents[i]);
                if (dd.Has != 0) continue;

                var pos = em.GetComponentData<LocalTransform>(ents[i]).Position;
                if (!TryNearestHostileBuilding(em, pos, out float3 next)) continue;

                // Standing on the objective already — leave it to targeting.
                float dx = next.x - pos.x, dz = next.z - pos.z;
                if (dx * dx + dz * dz < 20f * 20f) continue;

                em.SetComponentData(ents[i], new DesiredDestination { Position = next, Has = 1 });
            }
        }

        private static bool TryNearestHostileHall(EntityManager em, float3 from, out float3 pos)
            => TryNearestHostile<HallTag>(em, from, out pos);

        private static bool TryNearestHostileBuilding(EntityManager em, float3 from, out float3 pos)
        {
            if (TryNearestHostile<HallTag>(em, from, out pos)) return true;
            return TryNearestHostile<BuildingTag>(em, from, out pos);
        }

        private static bool TryNearestHostile<T>(EntityManager em, float3 from, out float3 pos)
            where T : unmanaged, IComponentData
        {
            pos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            q.Dispose();

            float bestD = float.MaxValue;
            for (int i = 0; i < xfs.Length; i++)
            {
                if (!Alliances.AreHostile(Faction.Border, facs[i].Value)) continue;
                var p = xfs[i].Position;
                float dx = p.x - from.x, dz = p.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; pos = p; }
            }
            return bestD < float.MaxValue;
        }
    }
}
