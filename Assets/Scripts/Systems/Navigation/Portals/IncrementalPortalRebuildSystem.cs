// IncrementalPortalRebuildSystem.cs
// task-112 M4 -- drains NavDirtyTiles each tick and, if any tile is
// dirty, rebuilds the portal-graph blob + invalidates the NavFlowCache
// slabs that intersect the dirty set.
//
// M4 ships a "full graph rebuild on any dirty signal" -- the
// architecture explicitly defers CSR surgery (partial node-range
// replacement inside the blob) to M6/M7 polish:
//   "F: PortalGraphRebuildSystem does a full graph rebuild (not
//    partial CSR surgery) on dirty signal. Justified by dirty-only
//    triggering (not per-frame) and bounded work. CSR surgery
//    deferred to M6/M7 polish."
// The cost of a full rebuild at M4 scale (512x512 grid, ~1024 tiles)
// is still well under the S9 per-tick budget, and it keeps the M4
// commit's blast radius small.
//
// Determinism notes:
//   * NavDirtyTiles.DirtyTileIndices is iterated by snapshotting to
//     a NativeList<int> and sorting ASC (DR-6 / the dirty-set is a
//     NativeHashSet whose iteration order is non-deterministic).
//   * The new blob is built synchronously on the main thread — inside
//     RebuildPortalGraphJob via Run(), so the whole build is
//     Burst-compiled (2026-09-03: the managed build was a 70-85 ms
//     hitch per building placement at 1026x1026 grid scale) — and the
//     swap uses the CCD-5 protocol (drain dependency, publish new
//     handle, dispose old AFTER publish). Single sync point per tick,
//     only when dirty.
//   * Cache invalidation walks the slot keys in slot-index order so
//     evictions happen in a stable order across machines.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M4 incremental rebuild system. Drains
    /// <see cref="NavDirtyTiles"/>; on any dirty signal, runs a full
    /// portal-graph rebuild (same shape as
    /// <see cref="PortalGraphBuildSystem"/>'s one-shot build) and
    /// publishes the new blob via the CCD-5 swap protocol. Then
    /// invalidates every <see cref="NavFlowCache"/> slab whose
    /// <c>TileIndex</c> appears in the dirty set.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BuildingCostStampSystem))]
    [UpdateBefore(typeof(AbstractPathfinderSystem))]
    public partial struct IncrementalPortalRebuildSystem : ISystem
    {
        private Entity _mirrorEntity;
        private byte _mirrorInitialised;
        /// <summary>Mirror of PortalOwnerBitsMirror.Bits, disposable after a wipe.</summary>
        private NativeArray<ushort> _bits;

        // NOT [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]: BlobBuilder + EntityManager.GetComponentData
        // are managed entry points.
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavDirtyTiles>();
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
            state.RequireForUpdate<PortalGraphSingleton>();
            _mirrorInitialised = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var dirty = SystemAPI.GetSingleton<NavDirtyTiles>();
            if (!dirty.DirtyTileIndices.IsCreated) return;

            int dirtyCount = dirty.DirtyTileIndices.Count;
            if (dirtyCount == 0) return;

            // ── Snapshot + sort dirty tile indices (DR-6) ──────────────
            // NativeHashSet iteration is non-deterministic; snapshot to a
            // sorted list so downstream work (cache eviction, log emit)
            // happens in stable order.
            var dirtyTiles = new NativeList<int>(dirtyCount, Allocator.Temp);
            foreach (var t in dirty.DirtyTileIndices)
                dirtyTiles.Add(t);
            // Insertion sort -- dirty count is small (worst case a few
            // dozen even on a wall placement that hits multiple tiles).
            for (int i = 1; i < dirtyTiles.Length; i++)
            {
                int v = dirtyTiles[i];
                int j = i - 1;
                while (j >= 0 && dirtyTiles[j] > v)
                {
                    dirtyTiles[j + 1] = dirtyTiles[j];
                    j--;
                }
                dirtyTiles[j + 1] = v;
            }

            // ── CCD-5: drain in-flight nav deps before reading cost field ──
            state.Dependency.Complete();

            // ── Full graph rebuild (same shape as PortalGraphBuildSystem) ──
            var cost = SystemAPI.GetSingleton<NavCostField>();
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            if (!cost.Cost.IsCreated) { dirtyTiles.Dispose(); return; }

            int tileSize = PortalGraphSingleton.TileSize;
            int tilesX = (grid.Width + tileSize - 1) / tileSize;
            int tilesZ = (grid.Height + tileSize - 1) / tileSize;

            // Wall spec snapshot for the Burst job. job.Run() is synchronous,
            // so the AsArray view over the live spec list is safe.
            NativeArray<WallPortalSpec> wallSpecs = default;
            bool ownWallSpecs = false;
            if (SystemAPI.HasSingleton<WallPortalSpecList>())
            {
                var specList = SystemAPI.GetSingleton<WallPortalSpecList>();
                if (specList.Specs.IsCreated && specList.Specs.Length > 0)
                    wallSpecs = specList.Specs.AsArray();
            }
            if (!wallSpecs.IsCreated)
            {
                wallSpecs = new NativeArray<WallPortalSpec>(0, Allocator.TempJob);
                ownWallSpecs = true;
            }

            // The whole build (detection scan, node/edge assembly, edge sort,
            // CSR, blob) runs Burst-compiled via Run(). It used to execute as
            // plain managed IL on the main thread — PortalDetectionJob was
            // invoked through .Execute() (which bypasses Burst) and the edge
            // sort was a managed Array.Sort with a delegate — which at
            // 1026x1026 grid scale was a 70-85 ms hitch on every building
            // placement. Same-tick synchronous on every path, so nothing
            // about availability timing changes for lockstep.
            var outBlob = new NativeReference<BlobAssetReference<PortalGraphBlob>>(
                Allocator.TempJob);
            new RebuildPortalGraphJob
            {
                Cost = cost.Cost,
                Grid = grid,
                TileSize = tileSize,
                TilesX = tilesX,
                TilesZ = tilesZ,
                WallSpecs = wallSpecs,
                OutBlob = outBlob,
            }.Run();
            BlobAssetReference<PortalGraphBlob> newBlob = outBlob.Value;
            outBlob.Dispose();
            if (ownWallSpecs) wallSpecs.Dispose();

            // ── CCD-5 publish: drain in-flight, swap, dispose old AFTER ──
            // state.Dependency.Complete() already called above; safe to swap.
            var graphSingleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            var oldBlob = graphSingleton.Graph;
            graphSingleton.Graph = newBlob;
            graphSingleton.Generation++;
            graphSingleton.Built = 1;
            SystemAPI.SetSingleton(graphSingleton);

            // Bump NavGenerationCounter so request consumers can tell the
            // graph generation moved this tick.
            if (SystemAPI.HasSingleton<NavGenerationCounter>())
            {
                var gen = SystemAPI.GetSingleton<NavGenerationCounter>();
                gen.CurrentGeneration = graphSingleton.Generation;
                gen.CommittedGeneration = graphSingleton.Generation;
                SystemAPI.SetSingleton(gen);
            }

            if (oldBlob.IsCreated) oldBlob.Dispose();

            // task-112 M5 -- rebuild the PortalOwnerBitsMirror sized to
            // match the new node count. Defaults each slot to (OwnerAny,
            // Open) so non-gate portals stay freely traversable.
            // GateStateSystem later mutates gate-portal slots in place.
            RebuildOwnerBitsMirror(ref state, em, graphSingleton);

            // ── Cache invalidation: drop every slab whose TileIndex is dirty ──
            if (SystemAPI.HasSingleton<NavFlowCache>())
            {
                var cache = SystemAPI.GetSingleton<NavFlowCache>();
                if (cache.Slots.IsCreated)
                {
                    for (int i = 0; i < dirtyTiles.Length; i++)
                    {
                        InvalidateTile(ref cache, dirtyTiles[i]);
                    }
                    SystemAPI.SetSingleton(cache);
                }
            }

            // ── Drain the dirty set + bump its generation. ──
            dirty.DirtyTileIndices.Clear();
            dirty.Generation++;
            SystemAPI.SetSingleton(dirty);

            dirtyTiles.Dispose();
        }

        public void OnDestroy(ref SystemState state)
        {
            // Mirror, not the component — the entity may already be wiped.
            if (_bits.IsCreated) _bits.Dispose();
        }

        /// <summary>
        /// task-112 M5 -- (re)allocate the <see cref="PortalOwnerBitsMirror"/>
        /// singleton so its <c>Bits</c> array length matches the new graph
        /// node count, then initialise each slot from the node's PortalKind
        /// + OwnerId. Non-gate portals get (OwnerAny, Open); gate portals
        /// get (gate.OwnerId, Open=1) by default. <c>GateStateSystem</c>
        /// then flips the open bit per gate-runtime-state.
        ///
        /// Disposes any previous Bits allocation. Single-thread main-thread
        /// work -- runs only on the rebuild tick.
        /// </summary>
        private void RebuildOwnerBitsMirror(ref SystemState state,
            EntityManager em, PortalGraphSingleton graphSingleton)
        {
            int nodeCount = graphSingleton.Graph.Value.Nodes.Length;
            // Lazy-create the singleton on the first rebuild — and re-create it
            // after the end-of-match wipe, which destroys it while this system
            // survives. A stale latch left GetSingleton below throwing on the
            // first rebuild, and functionally lost every gate's ownership /
            // open-closed bits so GateStateSystem could not flip a gate.
            if (_mirrorInitialised == 0
                || !em.Exists(_mirrorEntity)
                || !em.HasComponent<PortalOwnerBitsMirror>(_mirrorEntity))
            {
                if (_bits.IsCreated) _bits.Dispose();
                _bits = new NativeArray<ushort>(nodeCount, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);

                _mirrorInitialised = 1;
                _mirrorEntity = em.CreateEntity(typeof(PortalOwnerBitsMirror));
                em.SetComponentData(_mirrorEntity, new PortalOwnerBitsMirror
                {
                    Bits = _bits,
                    Generation = graphSingleton.Generation,
                });
            }

            var mirror = SystemAPI.GetSingleton<PortalOwnerBitsMirror>();
            if (mirror.Bits.IsCreated && mirror.Bits.Length != nodeCount)
            {
                mirror.Bits.Dispose();
                mirror.Bits = new NativeArray<ushort>(nodeCount, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                _bits = mirror.Bits;   // keep the mirror handle in step
            }

            ref var graph = ref graphSingleton.Graph.Value;
            for (int i = 0; i < nodeCount; i++)
            {
                var node = graph.Nodes[i];
                // Non-gate portals: any owner, open. Gate portals: gate's
                // owner faction id, open by default (GateStateSystem will
                // close them when no friendly is nearby).
                bool isGate = node.PortalKind == PortalNode.KindGateGround
                    || node.PortalKind == PortalNode.KindGateRampart;
                if (isGate)
                {
                    mirror.Bits[i] = PortalOwnerBitsMirror.Pack(node.OwnerId, true);
                }
                else
                {
                    mirror.Bits[i] = PortalOwnerBitsMirror.Pack(-1, true);
                }
            }

            mirror.Generation = graphSingleton.Generation;
            SystemAPI.SetSingleton(mirror);
        }

        /// <summary>
        /// Evict every cache slab whose <see cref="NavFlowCacheKey.TileIndex"/>
        /// equals the given tile. Walks slots in index order so the
        /// eviction set is byte-stable across machines.
        ///
        /// Adds the "InvalidateTile" entry point the architecture's M4
        /// section calls out for the cache. Returns the number of slabs
        /// evicted.
        /// </summary>
        public static int InvalidateTile(ref NavFlowCache cache, int tileIndex)
        {
            int evicted = 0;
            for (int i = 0; i < cache.SlotCount; i++)
            {
                var slot = cache.Slots[i];
                if (slot.Valid == 0) continue;
                var key = cache.SlotKeys[i];
                if (key.TileIndex != tileIndex) continue;

                // Remove from the index then free the slot.
                cache.SlotIndex.Remove(key);
                slot.Valid = 0;
                cache.Slots[i] = slot;
                evicted++;
            }
            return evicted;
        }

    }

    /// <summary>
    /// The full portal-graph build as one Burst-compiled synchronous job
    /// (invoked with Run()): detection scan, inter-tile node/edge assembly,
    /// wall-portal append, intra-tile edges, edge sort, CSR prefix sums and
    /// the blob itself. Output is the finished blob (Persistent) via
    /// <see cref="OutBlob"/>; the caller publishes it under CCD-5.
    /// Same shapes and orderings as the managed build it replaces — DR-4
    /// node order, DR-5 edge order (total comparator), so graph bytes are
    /// identical across machines.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct RebuildPortalGraphJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Cost;
        public NavGridSingleton Grid;
        public int TileSize;
        public int TilesX;
        public int TilesZ;
        [ReadOnly] public NativeArray<WallPortalSpec> WallSpecs;
        public NativeReference<BlobAssetReference<PortalGraphBlob>> OutBlob;

        /// <summary>Total order over edges: (From, To, Cost). ProfileMask is
        /// constant (0xFF) across every emitter, so equal keys are equal
        /// structs and the unstable sort cannot introduce nondeterminism.</summary>
        internal struct EdgeOrder : System.Collections.Generic.IComparer<PortalEdge>
        {
            public int Compare(PortalEdge a, PortalEdge b)
            {
                if (a.FromPortalId != b.FromPortalId) return a.FromPortalId - b.FromPortalId;
                if (a.ToPortalId != b.ToPortalId) return a.ToPortalId - b.ToPortalId;
                return a.Cost.CompareTo(b.Cost);
            }
        }

        public void Execute()
        {
            var portals = new NativeList<PortalSpec>(4096, Allocator.Temp);
            var detect = new PortalDetectionJob
            {
                Cost = Cost,
                Width = Grid.Width,
                Height = Grid.Height,
                TileSize = TileSize,
                TilesX = TilesX,
                TilesZ = TilesZ,
                Portals = portals,
            };
            detect.Execute(); // inlined into THIS job's Burst compilation

            int specCount = portals.Length;
            int interTileNodeCount = specCount * 2;

            // task-112 M5 -- build into NativeList so the wall-portal
            // appender can grow them by climb / gate node pairs.
            var nodes = new NativeList<PortalNode>(interTileNodeCount + 64, Allocator.Temp);
            var edges = new NativeList<PortalEdge>(interTileNodeCount + 64, Allocator.Temp);

            for (int i = 0; i < specCount; i++)
            {
                var spec = portals[i];
                int nodeAId = i * 2;
                int nodeBId = i * 2 + 1;

                nodes.Add(new PortalNode
                {
                    Id = nodeAId,
                    CellIndex = spec.CellIndex,
                    TileIndex = spec.TileIndex,
                    PortalKind = PortalNode.KindInterTile,
                    OwnerId = 0,
                    Layer = 0,
                });
                nodes.Add(new PortalNode
                {
                    Id = nodeBId,
                    CellIndex = spec.NeighbourCellIndex,
                    TileIndex = spec.NeighbourTileIndex,
                    PortalKind = PortalNode.KindInterTile,
                    OwnerId = 0,
                    Layer = 0,
                });

                edges.Add(new PortalEdge
                {
                    FromPortalId = nodeAId,
                    ToPortalId = nodeBId,
                    Cost = (ushort)NavFlowConstants.StepCardinal,
                    ProfileMask = 0xFF,
                });
                edges.Add(new PortalEdge
                {
                    FromPortalId = nodeBId,
                    ToPortalId = nodeAId,
                    Cost = (ushort)NavFlowConstants.StepCardinal,
                    ProfileMask = 0xFF,
                });
            }

            // task-112 M5: append wall-derived portals (climb + gate kinds).
            if (WallSpecs.Length > 0)
            {
                WallPortalGraphAppender.Append(in Grid, WallSpecs,
                    nodes, edges, TileSize, TilesX);
            }

            int nodeCount = nodes.Length;

            // Intra-tile Manhattan edges (matches PortalGraphBuildSystem).
            // M5: include the new wall portal nodes in the per-tile pairing
            // so the A* search can hop through them within a tile too.
            var intraEdges = new NativeList<PortalEdge>(nodeCount, Allocator.Temp);
            PortalIntraTileEdges.Build(Grid, nodes.AsArray(), intraEdges, Cost);

            int totalEdgeCount = edges.Length + intraEdges.Length;
            var allEdges = new NativeArray<PortalEdge>(totalEdgeCount, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < edges.Length; i++) allEdges[i] = edges[i];
            for (int i = 0; i < intraEdges.Length; i++) allEdges[edges.Length + i] = intraEdges[i];
            intraEdges.Dispose();

            NativeSortExtension.Sort(allEdges, new EdgeOrder());

            var nodeFirstEdge = new NativeArray<int>(nodeCount + 1, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            for (int i = 0; i < totalEdgeCount; i++)
                nodeFirstEdge[allEdges[i].FromPortalId + 1]++;
            for (int i = 1; i <= nodeCount; i++)
                nodeFirstEdge[i] += nodeFirstEdge[i - 1];

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<PortalGraphBlob>();
            root.TileSize = TileSize;
            root.TilesX = TilesX;
            root.TilesZ = TilesZ;

            var nodesArr = builder.Allocate(ref root.Nodes, nodeCount);
            for (int i = 0; i < nodeCount; i++) nodesArr[i] = nodes[i];

            var edgesArr = builder.Allocate(ref root.Edges, totalEdgeCount);
            for (int i = 0; i < totalEdgeCount; i++) edgesArr[i] = allEdges[i];

            var firstArr = builder.Allocate(ref root.NodeFirstEdge, nodeCount + 1);
            for (int i = 0; i <= nodeCount; i++) firstArr[i] = nodeFirstEdge[i];

            OutBlob.Value = builder.CreateBlobAssetReference<PortalGraphBlob>(Allocator.Persistent);
            builder.Dispose();

            portals.Dispose();
            nodes.Dispose();
            edges.Dispose();
            allEdges.Dispose();
            nodeFirstEdge.Dispose();
        }
    }
}
