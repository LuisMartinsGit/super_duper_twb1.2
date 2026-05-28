// WallAutoSegmentSystem.cs
// task-109 Phase 4 — retroactive auto-segment formation between same-faction Wall Hubs.
//
// BFME2-style wall semantics: whenever any two completed friendly Wall Hubs end up
// within MaxAutoSegmentDistance of each other and are NOT already connected (via the
// WallHubLink buffer), an auto-segment spawns linking them. This complements the
// placement-time chain in BuildCommandPannel.SpawnWallHub (which only fires when the
// player is actively chain-placing) — this system handles cases (b) "hub finishes
// construction within range of an older hub" and (c) "hub destroyed, then rebuilt
// nearby" per task.md R4.
//
// Determinism: hub pairs are iterated in (Entity.Index, Entity.Version) order before
// any structural change is committed, so all peers in a lockstep session reach the
// same hub-graph topology. Snapshot-then-mutate is used (collect candidate pairs into
// a NativeList, then CreateSegment after the query is closed).
//
// Polled at 0.5 s (PollInterval). The full O(N²) pair scan at typical hub counts
// (< 30 per faction) is ~435 distance checks per tick — negligible. Roster-change
// cache invalidation is intentionally NOT implemented (premature optimisation).
//
// Location: Assets/Scripts/Systems/Buildings/WallAutoSegmentSystem.cs

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    // [DisableAutoCreation] — retroactive same-faction hub auto-segment is
    // replaced by the explicit per-hub "Build Wall" action (each new hub
    // spawns its own connecting segment at placement time, so we no longer
    // need a polling system to backfill the topology). Keeping the file as
    // reference; remove this disable attribute to re-enable the previous
    // BFME2-style auto-connect-by-proximity behaviour.
    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WallSegmentCleanupSystem))]
    public partial struct WallAutoSegmentSystem : ISystem
    {
        /// <summary>
        /// Maximum world-space (XZ) distance between two hubs for an auto-segment to spawn.
        /// Phase 1 canonical: 16 m (= 8 wall instances per segment). See
        /// docs/Design/Age_1_Alanthor.md § Wall System.
        /// </summary>
        public const float MaxAutoSegmentDistance = 16f;
        private const float MaxAutoSegmentDistanceSq = MaxAutoSegmentDistance * MaxAutoSegmentDistance;

        /// <summary>Poll cadence. Matches WallSegmentCleanupSystem.PollInterval.</summary>
        private const float PollInterval = 0.5f;

        private float _timer;
        private EntityQuery _hubQuery;

        public void OnCreate(ref SystemState state)
        {
            _timer = 0f;
            _hubQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<WallHubTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>(),
                },
                // Only completed hubs participate — hubs still under construction
                // would form segments that immediately need rebuilding when the hub
                // entity transitions out of UnderConstruction. The chain-placement
                // path in BuildCommandPannel.SpawnWallHub handles adjacent-placement
                // segments at placement time directly.
                None = new[] { ComponentType.ReadOnly<UnderConstruction>() },
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            _timer -= SystemAPI.Time.DeltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;

            var em = state.EntityManager;

            if (_hubQuery.IsEmpty) return;

            var hubs = _hubQuery.ToEntityArray(Allocator.Temp);
            if (hubs.Length < 2)
            {
                hubs.Dispose();
                return;
            }

            // Deterministic sort by (Entity.Index, Entity.Version). Stable across peers
            // because Entity.Index/Version are deterministic in a lockstep session
            // (NetworkIdGenerator tick-partitioned slots keep entity creation order in
            // sync). Hub counts are small (< 30 per faction in practice), so an
            // in-place insertion sort is the simplest correct option.
            SortByEntityKey(hubs);

            // Pull transforms + factions per sorted hub. Because we sorted `hubs`
            // independently of the query's internal order, we must look up each hub's
            // components explicitly rather than relying on _hubQuery.ToComponentDataArray.
            int n = hubs.Length;
            var positions = new NativeArray<float3>(n, Allocator.Temp);
            var factionIds = new NativeArray<Faction>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
            {
                positions[i] = em.GetComponentData<LocalTransform>(hubs[i]).Position;
                factionIds[i] = em.GetComponentData<FactionTag>(hubs[i]).Value;
            }

            // Snapshot candidate pairs BEFORE any structural change. CreateSegment
            // issues em.CreateEntity + em.AddBuffer which would invalidate live
            // entity-array handles.
            var pairs = new NativeList<HubPair>(16, Allocator.Temp);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    // Same-faction filter (per R4 / Edge Cases: cross-faction hubs
                    // never auto-connect).
                    if (factionIds[i] != factionIds[j]) continue;

                    // Distance filter (XZ-plane, squared — avoids a sqrt per pair).
                    float dx = positions[i].x - positions[j].x;
                    float dz = positions[i].z - positions[j].z;
                    float distSq = dx * dx + dz * dz;
                    // Inclusive at boundary (≤) per task spec edge cases.
                    if (distSq > MaxAutoSegmentDistanceSq) continue;

                    // Already-connected guard. AlanthorWall.AreHubsConnected walks the
                    // WallHubLink buffer on hubA looking for hubB.
                    if (AlanthorWall.AreHubsConnected(em, hubs[i], hubs[j])) continue;

                    pairs.Add(new HubPair
                    {
                        HubA = hubs[i],
                        HubB = hubs[j],
                        Faction = factionIds[i],
                    });
                }
            }

            // Now safe to perform structural changes — query is closed via the Temp
            // entity-array snapshot. CreateSegment also updates both hubs'
            // WallHubLink buffers, so subsequent pairs in the same tick see the new
            // link via AreHubsConnected (but we already filtered above; this is
            // belt-and-braces for the 3+ hub cluster case where multiple pairs land
            // in one snapshot).
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];

                // Defensive exists check — a hub destroyed mid-tick (e.g. by
                // DeathSystem running after WallSegmentCleanupSystem but before us
                // in the same SimulationSystemGroup pass) must not crash
                // CreateSegment. In practice the destroyed hub vanishes from the
                // next poll's query, so this is belt-and-braces.
                if (!em.Exists(pair.HubA) || !em.Exists(pair.HubB)) continue;

                // Re-check connection state in case an earlier pair this tick already
                // linked these two hubs (e.g. cluster of 3 hubs where pair (A,B) and
                // pair (A,C) both ran and the second iteration sees A already linked
                // by the first — but B != C so this guard does not actually trigger
                // for unique pairs; included for clarity).
                if (AlanthorWall.AreHubsConnected(em, pair.HubA, pair.HubB)) continue;

                AlanthorWall.CreateSegment(em, pair.HubA, pair.HubB, pair.Faction);
            }

            pairs.Dispose();
            positions.Dispose();
            factionIds.Dispose();
            hubs.Dispose();
        }

        /// <summary>
        /// In-place insertion sort on an Entity array by (Index, Version). N is small
        /// (typically &lt; 30); insertion sort is the simplest correct option and
        /// avoids unsafe-quicksort or burst-jobs overhead. Stable.
        /// </summary>
        private static void SortByEntityKey(NativeArray<Entity> arr)
        {
            int n = arr.Length;
            for (int i = 1; i < n; i++)
            {
                Entity key = arr[i];
                int j = i - 1;
                while (j >= 0 && CompareEntities(arr[j], key) > 0)
                {
                    arr[j + 1] = arr[j];
                    j--;
                }
                arr[j + 1] = key;
            }
        }

        /// <summary>
        /// Compare two entities by (Index, Version) ascending. Returns negative if a &lt; b,
        /// zero if equal, positive if a &gt; b.
        /// </summary>
        private static int CompareEntities(Entity a, Entity b)
        {
            if (a.Index != b.Index) return a.Index - b.Index;
            return a.Version - b.Version;
        }

        private struct HubPair
        {
            public Entity HubA;
            public Entity HubB;
            public Faction Faction;
        }
    }
}
