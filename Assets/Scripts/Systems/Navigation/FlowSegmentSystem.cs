// FlowSegmentSystem.cs
// task-112 M3 -- per-tile segmented flow generation with LRU caching.
//
// Replaces the M1 WholeMapFlowSystem -- M3 deletes the whole-map flow
// generator (and the NavFlowFieldM1 singleton) per the architecture.
// For each unit that holds a NavPathResult, the system ensures the
// current tile + the next-tile-along-the-path are present in the
// NavFlowCache. On miss, an integrate + write job is scheduled to
// compute the tile-local integration field (Dijkstra back from the
// exit-portal cell) and the per-cell direction byte.
//
// The cache is a fixed-size pool of 256 slabs (16x16 cells each).
// Eviction is LRU keyed by tick counter; slot picked = the live slot
// with the lowest LastUsedTick.
//
// Determinism:
//   * Tick counter is sim-tick driven (incremented at OnUpdate start),
//     not wall-clock.
//   * Integration sweep is a single-thread Burst IJob per missing tile
//     (per-request parallelism comes from scheduling multiple
//     IntegrateTileJobs in flight at once -- safe because each writes
//     to a disjoint slab in the pool).
//   * Hash key = (TileIndex << 16) | (ExitPortalId << 8) | ProfileHash --
//     integer only.
//
// Location: Assets/Scripts/Systems/Navigation/FlowSegmentSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Owns the <see cref="NavFlowCache"/> singleton. Allocates the slab
    /// pool at first OnUpdate; disposes in OnDestroy.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AbstractPathfinderSystem))]
    public partial struct FlowSegmentSystem : ISystem
    {
        private Entity _cacheEntity;
        private byte _initialised;
        private EntityQuery _pathQuery;

        // System-side mirrors of the cache pools — the only handles that
        // survive the end-of-match wipe, and so the only way to free them.
        private NativeHashMap<NavFlowCacheKey, int> _slotIndex;
        private NativeArray<NavFlowCacheSlot> _slots;
        private NativeArray<NavFlowCacheKey> _slotKeys;
        private NativeArray<byte> _dirPool;
        private NativeArray<uint> _integrationPool;

        private void ReleaseCache()
        {
            if (_slotIndex.IsCreated) _slotIndex.Dispose();
            if (_slots.IsCreated) _slots.Dispose();
            if (_slotKeys.IsCreated) _slotKeys.Dispose();
            if (_dirPool.IsCreated) _dirPool.Dispose();
            if (_integrationPool.IsCreated) _integrationPool.Dispose();
            _slotIndex = default;
            _slots = default;
            _slotKeys = default;
            _dirPool = default;
            _integrationPool = default;
        }

        /// <summary>
        /// This system previously had NO OnDestroy, so its five persistent
        /// containers were never freed at all.
        /// </summary>
        public void OnDestroy(ref SystemState state)
        {
            ReleaseCache();
        }

        // true = the per-leg slab integration in OnUpdate is retired
        // (pathfinding redesign 2026-07-05). static readonly, NOT const:
        // a const guard makes the retired loop provably unreachable, and
        // the resulting CS0162 cannot be pragma-suppressed — Entities
        // source-gen re-emits the method body into a generated file
        // without pragmas.
        private static readonly bool SlabIntegrationRetired = true;

        // NOT [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]: BC1028 -- CreateEntity is managed.
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            state.RequireForUpdate<PortalGraphSingleton>();
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();

            _pathQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, NavPathResult, NavPathPortal>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Existence-gated like the rest of the nav singletons. The
            // functional impact here is small (the consuming path is retired)
            // but the leak was not: five persistent containers per match, and
            // this system had no OnDestroy at all, so nothing ever freed them.
            if (_initialised == 0
                || !em.Exists(_cacheEntity)
                || !em.HasComponent<NavFlowCache>(_cacheEntity))
            {
                ReleaseCache();

                _initialised = 1;
                int tileArea = PortalGraphSingleton.TileSize * PortalGraphSingleton.TileSize;
                int slots = NavFlowCache.DefaultSlotCount;

                var cache = new NavFlowCache
                {
                    SlotIndex = new NativeHashMap<NavFlowCacheKey, int>(slots * 2, Allocator.Persistent),
                    Slots = new NativeArray<NavFlowCacheSlot>(slots, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory),
                    SlotKeys = new NativeArray<NavFlowCacheKey>(slots, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory),
                    DirPool = new NativeArray<byte>(slots * tileArea, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                    IntegrationPool = new NativeArray<uint>(slots * tileArea, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                    SlotCount = slots,
                    TileArea = tileArea,
                    TickCounter = 0,
                };

                _cacheEntity = em.CreateEntity(typeof(NavFlowCache));
                em.SetComponentData(_cacheEntity, cache);

                _slotIndex = cache.SlotIndex;
                _slots = cache.Slots;
                _slotKeys = cache.SlotKeys;
                _dirPool = cache.DirPool;
                _integrationPool = cache.IntegrationPool;
            }

            // RETIRED (pathfinding redesign 2026-07-05): per-leg slab
            // integration is no longer consumed — FlowFollowSystem samples
            // the whole-map goal fields from GoalFlowFieldSystem instead.
            // The NavFlowCache singleton stays allocated (created above)
            // because IncrementalPortalRebuildSystem still evicts against
            // it; the per-unit integrate loop below is skipped entirely.
            if (SlabIntegrationRetired) return;

        }
    }
}
