// BuildingCostStampSystem.cs
// task-112 M4 -- owns the NavDirtyTiles singleton (the per-tick
// "which tiles changed" set the incremental rebuilder + cache
// invalidator drain) and the NavGenerationCounter singleton.
//
// Runs immediately after CostFieldStampSystem so it observes the
// current tick's per-cell stamps. The dirty-tile set is computed by
// comparing the current cost slab against a one-tick-old shadow copy:
// any cell whose cost value changed since the previous tick marks its
// tile (cell.x / TileSize, cell.z / TileSize) dirty. Wall gates
// (WallGateTag) are intentionally excluded -- their portal-graph
// representation ships in M5 (climb / gate-ground / gate-rampart
// kinds); for M4 the stamp side ignores them so the M4 incremental
// rebuild doesn't churn on every gate state flip.
//
// Determinism notes (DR-6):
//   * Writes to the dirty hash-set are serialised through a single
//     IJob (NavDirtyTiles.DirtyTileIndices is a Persistent
//     NativeHashSet<int>); parallelism is over cells inside the job
//     via a thread-local list later folded into the set on a single
//     thread. M4 ships the simpler single-thread variant -- iteration
//     order is already non-deterministic on a hash-set, but consumers
//     snapshot + sort ascending before reading (covered in the
//     IncrementalPortalRebuildSystem drain).
//   * Shadow copy uses Allocator.Persistent + the same row-major
//     layout as NavCostField.Cost so the per-cell compare is a flat
//     byte-equal walk -- bit-identical across machines.
//
// Allocation owner: this system. Disposed in OnDestroy.
//
// Location: Assets/Scripts/Systems/Navigation/BuildingCostStampSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M4 -- per-tick dirty-tile diff system. Owns the
    /// <see cref="NavDirtyTiles"/> + <see cref="NavGenerationCounter"/>
    /// singletons.
    ///
    /// Runs after <see cref="CostFieldStampSystem"/> so the cost slab
    /// is final this tick. Diffs the new cost slab against a one-tick-
    /// old shadow copy; any cell whose value changed marks the tile it
    /// sits in dirty. The shadow copy is then refreshed.
    ///
    /// Wall-gate entities (M5) are not specially excluded here -- the
    /// per-cell diff just observes the stamped cost, and gates haven't
    /// been added to the stamp surface in M4. The M5 plan inserts gate
    /// portal nodes via WallPortalDetectionSystem, not via the cell
    /// stamp; that ordering remains intact.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CostFieldStampSystem))]
    public partial struct BuildingCostStampSystem : ISystem
    {
        private Entity _dirtyEntity;
        private Entity _genEntity;
        private byte _initialised;
        private NativeArray<byte> _shadowCost;
        private int _shadowWidth;
        private int _shadowHeight;

        // NOT [BurstCompile]: BC1028 -- CreateEntity is managed.
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            if (_initialised == 0)
            {
                _initialised = 1;
                var grid = SystemAPI.GetSingleton<NavGridSingleton>();

                // Tile-index domain: tiles per layer in the M4 portal-graph
                // (16x16 cells per CCD-4). Capacity sized to one entry per
                // tile so insertion never reallocates.
                int tileSize = PortalGraphSingleton.TileSize;
                int tilesX = (grid.Width + tileSize - 1) / tileSize;
                int tilesZ = (grid.Height + tileSize - 1) / tileSize;
                int tileCap = math.max(64, tilesX * tilesZ);

                var dirtySet = new NativeHashSet<int>(tileCap, Allocator.Persistent);

                _dirtyEntity = em.CreateEntity(typeof(NavDirtyTiles));
                em.SetComponentData(_dirtyEntity, new NavDirtyTiles
                {
                    DirtyTileIndices = dirtySet,
                    Generation = 0,
                });

                _genEntity = em.CreateEntity(typeof(NavGenerationCounter));
                em.SetComponentData(_genEntity, new NavGenerationCounter
                {
                    CurrentGeneration = 0,
                    CommittedGeneration = 0,
                });

                _shadowWidth = grid.Width;
                _shadowHeight = grid.Height;
                _shadowCost = new NativeArray<byte>(
                    grid.Width * grid.Height, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }

            if (!SystemAPI.HasSingleton<NavCostField>()) return;
            if (!SystemAPI.HasSingleton<NavDirtyTiles>()) return;

            var cost = SystemAPI.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated) return;
            // Sanity: the shadow allocation matches the field. NavGridBootstrap
            // never resizes the field, but if it grows in M5 the shadow
            // re-allocates here to keep the diff valid.
            // task-112 M5: shadow is sized for ALL layers so the diff
            // observes wall stamps onto the Rampart slab too.
            int expectedShadowLen = cost.Width * cost.Height * cost.LayerCount;
            if (cost.Width != _shadowWidth || cost.Height != _shadowHeight
                || _shadowCost.Length != expectedShadowLen)
            {
                if (_shadowCost.IsCreated) _shadowCost.Dispose();
                _shadowCost = new NativeArray<byte>(
                    expectedShadowLen, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _shadowWidth = cost.Width;
                _shadowHeight = cost.Height;
            }

            // Drain any in-flight stampers that wrote into cost.Cost this tick.
            state.Dependency.Complete();

            var dirty = SystemAPI.GetSingleton<NavDirtyTiles>();
            if (!dirty.DirtyTileIndices.IsCreated) return;

            // Single-thread diff (deterministic by construction). Tile-set
            // size is bounded by total tile count (~1024 at 512x512 / 16
            // tile size) so the walk is sub-ms even on a full restamp.
            int tileSize2 = PortalGraphSingleton.TileSize;
            int tilesXForDiff = (cost.Width + tileSize2 - 1) / tileSize2;
            int layerArea = cost.Width * cost.Height;

            // Walk every layer's slab. Cells on layers >= 1 still map to
            // tiles in the layer-0 tile-index space (one tile-index per
            // (tileX, tileZ) pair, layer-aware portals share the index).
            int total = layerArea * cost.LayerCount;
            for (int i = 0; i < total; i++)
            {
                byte cur = cost.Cost[i];
                byte prev = _shadowCost[i];
                if (cur == prev) continue;

                int layerCellIdx = i % layerArea;
                int x = layerCellIdx % cost.Width;
                int z = layerCellIdx / cost.Width;
                int tileX = x / tileSize2;
                int tileZ = z / tileSize2;
                int tileIndex = tileZ * tilesXForDiff + tileX;
                dirty.DirtyTileIndices.Add(tileIndex);
                _shadowCost[i] = cur;
            }

            // Persist the dirty set header back (DirtyTileIndices is a
            // reference-typed handle so the Add above is observable, but
            // we re-set to keep ECS chunk metadata coherent).
            SystemAPI.SetSingleton(dirty);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_initialised == 0) return;

            var em = state.EntityManager;
            if (em.Exists(_dirtyEntity) && em.HasComponent<NavDirtyTiles>(_dirtyEntity))
            {
                var d = em.GetComponentData<NavDirtyTiles>(_dirtyEntity);
                if (d.DirtyTileIndices.IsCreated) d.DirtyTileIndices.Dispose();
            }

            if (_shadowCost.IsCreated) _shadowCost.Dispose();
        }
    }
}
