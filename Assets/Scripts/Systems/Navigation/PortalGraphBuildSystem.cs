// PortalGraphBuildSystem.cs
// task-112 M3 -- assembles the portal graph blob from a cost-field
// snapshot using PortalDetectionJob. Single-threaded one-shot build at
// world init (M4 makes it incremental + driven by S1 dirty-tile events).
//
// CSR layout produced (matches PortalGraphBlob in NavComponents.cs):
//   * Nodes:        one PortalNode per detected portal, sorted by
//                   (TileIndex asc, CellIndex asc) -- DR-4.
//   * NodeFirstEdge: length == Nodes.Length + 1; trailing sentinel ==
//                   Edges.Length. Edges for node i live in
//                   [NodeFirstEdge[i] .. NodeFirstEdge[i+1]).
//   * Edges:        flat, within a node sorted by ToPortalId asc -- DR-5.
//
// M3 emits TWO edges per detected boundary span (the "cross-tile" edge):
//   * From the portal node sitting on the lower-indexed tile to the
//     portal node sitting on the higher-indexed tile (paired by
//     PortalSpec.NeighbourCellIndex matching another emit's CellIndex).
//   * Reverse edge (high -> low).
// Intra-tile edges (M3 stretch from spec S3) are deferred to M4 to keep
// the M3 scope sized -- the A* still works (it just walks portal -> portal
// through tile-boundary edges only), and for the SW->NE 512x512 corner
// test the abstract path is just a chain of inter-tile portals.
//
// CCD-5 swap protocol:
//   1. Build new blob on the main thread (we're inside an ISystem.OnUpdate).
//   2. Complete state.Dependency (no nav jobs in-flight for M3's one-shot).
//   3. Assign new blob to singleton; bump Generation.
//   4. Dispose the previous blob if any.
//
// Location: Assets/Scripts/Systems/Navigation/PortalGraphBuildSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// One-shot system that builds the inter-tile portal graph after the
    /// cost field has been stamped at least once. Runs in
    /// <see cref="SimulationSystemGroup"/> AFTER
    /// <see cref="CostFieldStampSystem"/> so the cost slab is final this
    /// tick.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CostFieldStampSystem))]
    public partial struct PortalGraphBuildSystem : ISystem
    {
        private Entity _graphEntity;
        private byte _initialised;
        private byte _builtOnce;

        // NOTE: deliberately NO system-side mirror of the graph blob, unlike
        // the other nav singletons. PortalGraphSingleton.Graph has TWO
        // publishers — this system and IncrementalPortalRebuildSystem, which
        // swaps in its own blob and disposes the old one. A mirror here goes
        // stale the moment that system rebuilds, and disposing it later threw
        // "The BlobAssetReference is not valid. Likely it has already been
        // unloaded or released." The singleton is the single source of truth
        // for who owns the live blob; GameBootstrap disposes it just before
        // the end-of-match wipe destroys the entity holding it.

        // NOT [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]: BC1028 (EntityManager.CreateEntity is managed).
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            _builtOnce = 0;
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Lazy singleton creation -- can't do it in OnCreate (managed
            // CreateEntity trips BC1028 even though OnCreate isn't
            // [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]'d on this system; keep it consistent with the
            // M1 bootstrap pattern).
            // Existence-gated: the end-of-match wipe destroys this singleton
            // while the system survives, and the publish path below calls
            // GetSingleton unguarded. _builtOnce must reset too, or the graph
            // for the NEW map is never built.
            if (_initialised == 0
                || !em.Exists(_graphEntity)
                || !em.HasComponent<PortalGraphSingleton>(_graphEntity))
            {
                // Nothing to free here: if the singleton is gone its blob was
                // already disposed by GameBootstrap's pre-wipe pass, and if it
                // never existed there is no blob yet.
                _initialised = 1;
                _builtOnce = 0;
                _graphEntity = em.CreateEntity(typeof(PortalGraphSingleton));
                em.SetComponentData(_graphEntity, new PortalGraphSingleton
                {
                    Graph = default,
                    Generation = 0,
                    Built = 0,
                });
            }

            // M3 one-shot: build once after the cost field has had at least
            // one stamp pass. M4 replaces this with an incremental rebuild
            // driven by NavDirtyTiles.
            if (_builtOnce != 0) return;
            if (!SystemAPI.HasSingleton<NavCostField>()) return;
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;

            var cost = SystemAPI.GetSingleton<NavCostField>();
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            if (!cost.Cost.IsCreated) return;
            if (cost.Generation == 0) return; // wait for first stamp

            // Make sure any in-flight jobs that read the cost field have
            // drained -- the build below reads it on the main thread.
            state.Dependency.Complete();

            int tileSize = PortalGraphSingleton.TileSize;
            int tilesX = (grid.Width + tileSize - 1) / tileSize;
            int tilesZ = (grid.Height + tileSize - 1) / tileSize;

            // Worst-case portal upper bound: every boundary cell could be
            // a 1-wide span. Boundary cell count ~= 2 * tilesX * tilesZ *
            // tileSize. At 512x512 that's ~16K, comfortably within
            // initial capacity 4096 -- NativeList grows automatically.
            var portals = new NativeList<PortalSpec>(4096, Allocator.TempJob);

            var detect = new PortalDetectionJob
            {
                Cost = cost.Cost,
                Width = grid.Width,
                Height = grid.Height,
                TileSize = tileSize,
                TilesX = tilesX,
                TilesZ = tilesZ,
                Portals = portals,
            };
            // Run inline on the main thread -- M3 build is one-shot and
            // the detect job's outputs feed the main-thread assembly
            // immediately. Avoids the IJobEntity vs IJob extension
            // overload ambiguity that Schedule() without an argument hits.
            detect.Execute();

            // ── Assemble into the CSR blob. We've got pairs of portal
            // specs (this side + neighbour side). Each spec contributes
            // ONE portal node (on the lower-indexed tile side) PLUS
            // ONE portal node (on the higher-indexed tile side -- the
            // neighbour cell). Pair edges link them across the boundary.
            //
            // Node-id assignment order: walk the spec list in detection
            // order (already tile-row-major / boundary-axis-asc) and
            // assign IDs as we go. Each spec emits two nodes (this side,
            // then neighbour side). Final node ids are dense [0..count).
            //
            // After all nodes are emitted, sort the edge list by
            // (FromPortalId asc, ToPortalId asc) and build NodeFirstEdge.

            int specCount = portals.Length;
            int nodeCount = specCount * 2;

            var nodes = new NativeArray<PortalNode>(nodeCount, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            // Edges: 2 per spec (forward + reverse).
            var edges = new NativeArray<PortalEdge>(specCount * 2, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            // Compute tileIndex helper.
            for (int i = 0; i < specCount; i++)
            {
                var spec = portals[i];
                int nodeAId = i * 2;       // lower-indexed tile side
                int nodeBId = i * 2 + 1;   // higher-indexed tile side

                nodes[nodeAId] = new PortalNode
                {
                    Id = nodeAId,
                    CellIndex = spec.CellIndex,
                    TileIndex = spec.TileIndex,
                    PortalKind = PortalNode.KindInterTile,
                    OwnerId = 0,
                    Layer = 0,
                };
                nodes[nodeBId] = new PortalNode
                {
                    Id = nodeBId,
                    CellIndex = spec.NeighbourCellIndex,
                    TileIndex = spec.NeighbourTileIndex,
                    PortalKind = PortalNode.KindInterTile,
                    OwnerId = 0,
                    Layer = 0,
                };

                // Forward edge nodeA -> nodeB (cost = 1 cell hop, octile cardinal=10).
                edges[i * 2] = new PortalEdge
                {
                    FromPortalId = nodeAId,
                    ToPortalId = nodeBId,
                    Cost = (ushort)NavFlowConstants.StepCardinal,
                    ProfileMask = 0xFF,
                };
                // Reverse edge nodeB -> nodeA.
                edges[i * 2 + 1] = new PortalEdge
                {
                    FromPortalId = nodeBId,
                    ToPortalId = nodeAId,
                    Cost = (ushort)NavFlowConstants.StepCardinal,
                    ProfileMask = 0xFF,
                };
            }

            // M3 stretch: intra-tile portal-to-portal edges. The clean
            // implementation would flood-fill each tile from each portal
            // and emit a single weighted edge to every other portal on
            // that tile. For M3's "SW corner -> NE corner on 512x512" test
            // we can short-circuit by emitting a Manhattan-distance edge
            // between every pair of portals that share a tile. This gives
            // A* a connected graph (otherwise the per-tile portals are
            // isolated stars that can't be traversed across) at the cost
            // of slightly over-estimating intra-tile distances. M4
            // replaces this with the per-tile flood-fill that the spec
            // S3 calls for.
            var intraEdges = new NativeList<PortalEdge>(nodeCount, Allocator.Temp);
            PortalIntraTileEdges.Build(grid, nodes, intraEdges, cost.Cost);

            int totalEdgeCount = edges.Length + intraEdges.Length;
            var allEdges = new NativeArray<PortalEdge>(totalEdgeCount, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < edges.Length; i++) allEdges[i] = edges[i];
            for (int i = 0; i < intraEdges.Length; i++) allEdges[edges.Length + i] = intraEdges[i];
            intraEdges.Dispose();

            // Sort edges by (FromPortalId asc, ToPortalId asc) -- DR-5.
            // NativeArray<T>.Sort uses an IComparer that BurstCompile
            // accepts; for the one-shot main-thread build we just use
            // Array.Sort on a managed copy to keep this readable.
            var managedEdges = new PortalEdge[totalEdgeCount];
            for (int i = 0; i < totalEdgeCount; i++) managedEdges[i] = allEdges[i];
            System.Array.Sort(managedEdges, (a, b) =>
            {
                if (a.FromPortalId != b.FromPortalId) return a.FromPortalId - b.FromPortalId;
                if (a.ToPortalId != b.ToPortalId) return a.ToPortalId - b.ToPortalId;
                // Cost as final key makes the comparison TOTAL: Array.Sort is
                // unstable, so if duplicate (From,To) pairs ever appear, the
                // survivor order would otherwise differ per peer.
                return a.Cost.CompareTo(b.Cost);
            });

            // Build NodeFirstEdge by scanning the sorted edge list.
            var nodeFirstEdge = new NativeArray<int>(nodeCount + 1, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            // Count edges per node first.
            for (int i = 0; i < totalEdgeCount; i++)
                nodeFirstEdge[managedEdges[i].FromPortalId + 1]++;
            // Prefix sum.
            for (int i = 1; i <= nodeCount; i++)
                nodeFirstEdge[i] += nodeFirstEdge[i - 1];
            // Sentinel: nodeFirstEdge[nodeCount] should now equal totalEdgeCount.

            // ── Build the blob ──────────────────────────────────────────
            BlobAssetReference<PortalGraphBlob> newBlob;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<PortalGraphBlob>();
                root.TileSize = tileSize;
                root.TilesX = tilesX;
                root.TilesZ = tilesZ;

                var nodesArr = builder.Allocate(ref root.Nodes, nodeCount);
                for (int i = 0; i < nodeCount; i++) nodesArr[i] = nodes[i];

                var edgesArr = builder.Allocate(ref root.Edges, totalEdgeCount);
                for (int i = 0; i < totalEdgeCount; i++) edgesArr[i] = managedEdges[i];

                var firstArr = builder.Allocate(ref root.NodeFirstEdge, nodeCount + 1);
                for (int i = 0; i <= nodeCount; i++) firstArr[i] = nodeFirstEdge[i];

                newBlob = builder.CreateBlobAssetReference<PortalGraphBlob>(Allocator.Persistent);
            }

            portals.Dispose();
            nodes.Dispose();
            edges.Dispose();
            allEdges.Dispose();
            nodeFirstEdge.Dispose();

            // ── Publish (CCD-5 swap) ────────────────────────────────────
            if (!SystemAPI.HasSingleton<PortalGraphSingleton>())
            {
                // The singleton was wiped mid-build; drop this graph rather
                // than throwing, and let the next tick rebuild from scratch.
                if (newBlob.IsCreated) newBlob.Dispose();
                return;
            }
            var singleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            var oldBlob = singleton.Graph;
            singleton.Graph = newBlob;
            singleton.Generation++;
            singleton.Built = 1;
            SystemAPI.SetSingleton(singleton);

            if (oldBlob.IsCreated) oldBlob.Dispose();

            _builtOnce = 1;
        }

        public void OnDestroy(ref SystemState state)
        {
            // Dispose via the SINGLETON, which is the authority on the live
            // blob — IncrementalPortalRebuildSystem may have swapped it since
            // we published. If the entity is already gone, GameBootstrap's
            // pre-wipe pass disposed the blob and there is nothing to do.
            var em = state.EntityManager;
            if (em.Exists(_graphEntity) && em.HasComponent<PortalGraphSingleton>(_graphEntity))
            {
                var s = em.GetComponentData<PortalGraphSingleton>(_graphEntity);
                if (s.Graph.IsCreated) s.Graph.Dispose();
            }
        }

    }
}
