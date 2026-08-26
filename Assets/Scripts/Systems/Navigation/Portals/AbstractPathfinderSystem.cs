// AbstractPathfinderSystem.cs
// task-112 M3 -- consumes per-entity NavPathRequest components and
// writes per-entity NavPathResult + NavPathPortal dynamic buffer. The
// A* algorithm itself lives in AbstractPathfinder.cs as a pure-function
// helper so the same code path is exercised by both the runtime
// system and the EditMode AbstractPathfinderTests.
//
// Bucket-queue tie-break (DR-3): see AbstractPathfinder.Solve --
// ascending portal id on equal f-scores. BucketWidth = 4 documented
// in the helper.
//
// Per-request parallelism: M3 runs requests sequentially on the main
// thread (small budget per tick, sub-ms total). The architecture
// describes IJobParallelFor over requests; the per-request work is
// pure (writes own entity only) so the upgrade is mechanical -- M6's
// NavRequestSchedulerSystem will do it as part of S9 budgeting.

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Runs after <see cref="PortalGraphBuildSystem"/>; consumes pending
    /// <see cref="NavPathRequest"/> components and writes
    /// <see cref="NavPathResult"/> + <see cref="NavPathPortal"/> buffer.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PortalGraphBuildSystem))]
    public partial struct AbstractPathfinderSystem : ISystem
    {
        public const int MaxRequestsPerTick = 8;

        private EntityQuery _pendingQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PortalGraphSingleton>();
            state.RequireForUpdate<NavGridSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _pendingQuery = SystemAPI.QueryBuilder()
                .WithAll<NavPathRequest>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_pendingQuery.IsEmpty) return;

            var portalSingleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (portalSingleton.Built == 0 || !portalSingleton.Graph.IsCreated) return;

            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            // Layer-0 cost slab — lets the A* region-gate its virtual
            // start/goal edges (blockers cutting a tile in two must not be
            // bridged; see NavTileRegions). Reading it on the main thread
            // needs in-flight stampers drained first.
            var costField = SystemAPI.GetSingleton<NavCostField>();
            state.Dependency.Complete();
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Snapshot pending requests in entity.Index ascending order so
            // the batch is processed deterministically (DR-12-shaped).
            using var entities = _pendingQuery.ToEntityArray(Allocator.Temp);
            using var requests = _pendingQuery.ToComponentDataArray<NavPathRequest>(Allocator.Temp);

            // task-112 M5: portal-owner-bits mirror (gates flipping in
            // place per CCD-5). Default array (uncreated) when the mirror
            // singleton doesn't exist yet -- A* treats every gate as
            // admissible in that fall-through.
            NativeArray<ushort> ownerBitsMirror = default;
            if (SystemAPI.HasSingleton<PortalOwnerBitsMirror>())
            {
                var m = SystemAPI.GetSingleton<PortalOwnerBitsMirror>();
                if (m.Bits.IsCreated) ownerBitsMirror = m.Bits;
            }

            int count = entities.Length;
            var sortedIdx = new NativeArray<int>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) sortedIdx[i] = i;
            // Insertion sort by entity.Index asc (short lists in M3).
            for (int i = 1; i < count; i++)
            {
                int k = sortedIdx[i];
                int j = i - 1;
                while (j >= 0 && entities[sortedIdx[j]].Index > entities[k].Index)
                {
                    sortedIdx[j + 1] = sortedIdx[j];
                    j--;
                }
                sortedIdx[j + 1] = k;
            }

            int processed = 0;
            ref var graph = ref portalSingleton.Graph.Value;

            for (int qi = 0; qi < count && processed < MaxRequestsPerTick; qi++)
            {
                int srcIdx = sortedIdx[qi];
                var entity = entities[srcIdx];
                var req = requests[srcIdx];

                if (req.Generation != portalSingleton.Generation)
                {
                    // Stale request: caller will re-issue on the next tick.
                    ecb.RemoveComponent<NavPathRequest>(entity);
                    continue;
                }

                var portals = new NativeList<int>(16, Allocator.Temp);

                // task-112 M5: resolve unit's owner faction (-1 = neutral)
                // so SolveGated can skip gate portals the unit can't pass.
                int unitOwnerId = -1;
                if (SystemAPI.HasComponent<FactionTag>(entity))
                {
                    unitOwnerId = (int)SystemAPI.GetComponent<FactionTag>(entity).Value;
                }

                byte status = AbstractPathfinder.SolveGated(ref graph, grid,
                    req.StartCell, req.GoalCell, portals,
                    ownerBitsMirror, unitOwnerId, costField.Cost);

                if (!SystemAPI.HasComponent<NavPathResult>(entity))
                    ecb.AddComponent<NavPathResult>(entity);
                ecb.SetComponent(entity, new NavPathResult
                {
                    Length = portals.Length,
                    Status = status,
                    Generation = portalSingleton.Generation,
                    CurrentPortalIndex = 0,
                });

                if (!SystemAPI.HasBuffer<NavPathPortal>(entity))
                    ecb.AddBuffer<NavPathPortal>(entity);

                var buf = ecb.SetBuffer<NavPathPortal>(entity);
                for (int p = 0; p < portals.Length; p++)
                    buf.Add(new NavPathPortal { PortalId = portals[p] });

                portals.Dispose();

                ecb.RemoveComponent<NavPathRequest>(entity);
                processed++;
            }

            sortedIdx.Dispose();
        }
    }
}
