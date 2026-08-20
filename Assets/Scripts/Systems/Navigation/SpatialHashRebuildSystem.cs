// SpatialHashRebuildSystem.cs
// task-112 M2: clears and re-populates the NavSpatialHash every tick from
// every entity carrying UnitTag + LocalTransform. The hash is consumed by
// SteeringSystem to look up neighbours for separation / unit-avoidance /
// cohesion forces (see DR-1 / DR-2).
//
// Design:
//   * Single-threaded Burst IJob for the populate step. NativeParallel-
//     MultiHashMap inserts are not parallel-safe without per-bucket locks,
//     and the chunk-walk order of ToEntityArray is deterministic per the
//     project's memory feedback. So we insert in chunk-walk order from one
//     thread -- iteration order inside a bucket is then insertion order,
//     which is byte-stable across machines.
//   * Capacity is grown lazily: if the unit count this tick exceeds the
//     current capacity, the map is reallocated to 2x the count.
//   * Allocator.Persistent for the map (lifetime == world); Allocator.Temp-
//     Job for the per-tick entity-array snapshot.
//
// Update ordering: runs after FlowFollowSystem (so it sees this tick's
// unit positions) and before SteeringSystem (which RequireForUpdate's
// the hash singleton).
//
// Location: Assets/Scripts/Systems/Navigation/SpatialHashRebuildSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Owns the <see cref="NavSpatialHash"/> singleton. Allocates the
    /// underlying <see cref="NativeParallelMultiHashMap{TKey, TValue}"/>
    /// at <c>OnCreate</c> (NOT [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]'d -- it does an
    /// <see cref="EntityManager.CreateEntity(System.Type[])"/> which trips
    /// BC1028). Disposes the map in <c>OnDestroy</c>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFollowSystem))]
    public partial struct SpatialHashRebuildSystem : ISystem
    {
        // Initial capacity for the multimap. Doubled lazily when the unit
        // count exceeds it. 1024 covers Phase2Test's 300-unit setup plus
        // headroom; later phases (Phase4Test = 50 units, Phase6Test = 60
        // units) all fit too.
        public const int InitialCapacity = 1024;

        private EntityQuery _unitQuery;
        private Entity _hashEntity;
        private byte _initialised;
        /// <summary>System-side mirror of the singleton's map, so a rebuild
        /// after the end-of-match entity wipe can dispose the orphaned
        /// allocation instead of leaking it.</summary>
        private NativeParallelMultiHashMap<int, Entity> _map;

        // NOT [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]: BC1028 (EntityManager.CreateEntity is managed).
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;

            // Cache the entity query in OnCreate per project convention.
            _unitQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // One-shot lazy init: allocate the singleton on the first tick.
            // We can't do this in OnCreate because CreateEntity is managed
            // (BC1028 again) and we want OnCreate Burst-friendly for any
            // future Burst-only init.
            // Existence-gated, not latch-gated — see NavRequestSchedulerSystem
            // for the full reasoning. GameBootstrap's end-of-match wipe
            // destroys this ordinary gameplay entity while the system lives
            // on, so a one-shot latch left every match after the first with no
            // NavSpatialHash and a dead nav stack.
            if (_initialised == 0
                || !em.Exists(_hashEntity)
                || !em.HasComponent<NavSpatialHash>(_hashEntity))
            {
                // Dispose the orphaned map from the previous match rather than
                // leaking one persistent hash map per match.
                if (_map.IsCreated) _map.Dispose();
                _map = new NativeParallelMultiHashMap<int, Entity>(
                    InitialCapacity, Allocator.Persistent);

                _initialised = 1;
                _hashEntity = em.CreateEntity(typeof(NavSpatialHash));
                em.SetComponentData(_hashEntity, new NavSpatialHash
                {
                    Map = _map,
                    CellSize = NavSpatialHash.DefaultCellSize,
                    BucketCount = InitialCapacity,
                    Generation = 0,
                });
            }

            // Pull singleton struct (it's a value type holding a NativeParallel-
            // MultiHashMap handle). We mutate the header and write back.
            var hash = SystemAPI.GetSingleton<NavSpatialHash>();

            int unitCount = _unitQuery.CalculateEntityCount();
            if (unitCount == 0)
            {
                // No units this tick: clear the map and bump generation so
                // SteeringSystem sees an empty-but-fresh hash.
                hash.Map.Clear();
                hash.Generation++;
                SystemAPI.SetSingleton(hash);
                return;
            }

            // Grow capacity if the unit count overflowed our buckets. We
            // grow to 2x the count so subsequent ticks don't churn.
            if (unitCount > hash.BucketCount)
            {
                int newCapacity = math.max(hash.BucketCount * 2, unitCount * 2);
                hash.Map.Capacity = newCapacity;
                hash.BucketCount = newCapacity;
            }

            // Snapshot entities + transforms in chunk-walk order. This
            // ordering is what gives the populate job its deterministic
            // insertion sequence per DR-2.
            var entities = _unitQuery.ToEntityArray(Allocator.TempJob);
            var transforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            // Clear then re-populate. Both run as a single IJob so the
            // insert order is the chunk-walk order of `entities`.
            var clearJob = new ClearHashJob
            {
                Map = hash.Map,
            };
            var clearHandle = clearJob.Schedule(state.Dependency);

            var populateJob = new PopulateHashJob
            {
                Entities = entities,
                Transforms = transforms,
                Map = hash.Map,
                CellSize = hash.CellSize,
            };
            var populateHandle = populateJob.Schedule(clearHandle);

            // Dispose the temp arrays after the populate job reads them.
            state.Dependency = entities.Dispose(populateHandle);
            state.Dependency = transforms.Dispose(state.Dependency);

            // Bump generation + write the updated header back. (The map
            // handle inside `hash` is the same as the singleton's because
            // NativeParallelMultiHashMap is a struct-of-handles -- we
            // only need to write back the metadata changes.)
            hash.Generation++;
            SystemAPI.SetSingleton(hash);
        }

        public void OnDestroy(ref SystemState state)
        {
            // Dispose the system-side mirror: after an end-of-match wipe the
            // entity is gone and reading the component would miss the
            // allocation entirely.
            if (_map.IsCreated) _map.Dispose();
        }
    }

    /// <summary>
    /// Wipes the multimap in preparation for re-populate. Single-thread by
    /// design (the multimap's Clear is O(buckets) and dirt-cheap relative
    /// to the rest of the tick).
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct ClearHashJob : IJob
    {
        public NativeParallelMultiHashMap<int, Entity> Map;

        public void Execute()
        {
            Map.Clear();
        }
    }

    /// <summary>
    /// Inserts every (cellKey, entity) pair into the multimap in
    /// chunk-walk order. Single-thread to keep insertion order stable per
    /// DR-2; bucket iteration order then equals insertion order, which is
    /// the contract <c>SteeringSystem</c>'s force-accumulation relies on.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct PopulateHashJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<LocalTransform> Transforms;
        public NativeParallelMultiHashMap<int, Entity> Map;
        public float CellSize;

        public void Execute()
        {
            for (int i = 0; i < Entities.Length; i++)
            {
                var pos = Transforms[i].Position;
                NavSpatialHash.WorldToCell(in pos, CellSize, out int cx, out int cz);
                int key = NavSpatialHash.PackKey(cx, cz);
                Map.Add(key, Entities[i]);
            }
        }
    }
}
