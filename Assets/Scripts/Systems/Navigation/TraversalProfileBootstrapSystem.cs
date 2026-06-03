// TraversalProfileBootstrapSystem.cs
// task-112 M5 -- builds the TraversalProfileBlob singleton once at world
// init. M5 ships three profiles:
//   0  DefaultGround   -- ground-only, can-climb=1, owner=-1
//   1  DefaultRampart  -- rampart-only, can-climb=0, owner=-1
//   2  Climbable       -- both layers, can-climb=1, owner=-1
// Owner is recorded per profile so a future "siege ram has its own
// profile" expansion can vary by player; M5 keeps profiles faction-
// agnostic and lets <see cref="GateRuntimeState.OwnerId"/> drive the
// gate gating instead.
//
// Determinism: cos/sin-free; built once on the main thread via
// BlobBuilder. Burst-safe construction (the helper math is integer).
//
// Location: Assets/Scripts/Systems/Navigation/TraversalProfileBootstrapSystem.cs

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// One-shot init system that allocates the
    /// <see cref="TraversalProfileSingleton"/> entity + blob. Mirrors the
    /// allocation pattern of <see cref="NavGridBootstrapSystem"/>
    /// (lazy in OnUpdate, NOT [BurstCompile] because CreateEntity is
    /// managed -- BC1028).
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(NavGridBootstrapSystem))]
    public partial struct TraversalProfileBootstrapSystem : ISystem
    {
        private Entity _entity;
        private byte _initialised;

        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_initialised != 0) return;
            _initialised = 1;

            BlobAssetReference<TraversalProfileBlob> blob;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<TraversalProfileBlob>();
                var profiles = builder.Allocate(ref root.Profiles, 3);

                // 0 -- DefaultGround
                profiles[0].FootprintSize = 1;
                profiles[0].AllowedLayersMask = 0x01; // ground only
                profiles[0].CanClimb = 1;
                profiles[0].OwnerId = -1;
                var tcm0 = builder.Allocate(ref profiles[0].TerrainCostMultipliers, 4);
                for (int i = 0; i < 4; i++) tcm0[i] = 255; // ~1.0

                // 1 -- DefaultRampart
                profiles[1].FootprintSize = 1;
                profiles[1].AllowedLayersMask = 0x02; // rampart only
                profiles[1].CanClimb = 0;
                profiles[1].OwnerId = -1;
                var tcm1 = builder.Allocate(ref profiles[1].TerrainCostMultipliers, 4);
                for (int i = 0; i < 4; i++) tcm1[i] = 255;

                // 2 -- Climbable (default for swordsmen / infantry)
                profiles[2].FootprintSize = 1;
                profiles[2].AllowedLayersMask = 0x03; // both layers
                profiles[2].CanClimb = 1;
                profiles[2].OwnerId = -1;
                var tcm2 = builder.Allocate(ref profiles[2].TerrainCostMultipliers, 4);
                for (int i = 0; i < 4; i++) tcm2[i] = 255;

                blob = builder.CreateBlobAssetReference<TraversalProfileBlob>(Allocator.Persistent);
            }

            var em = state.EntityManager;
            _entity = em.CreateEntity(typeof(TraversalProfileSingleton));
            em.SetComponentData(_entity, new TraversalProfileSingleton
            {
                Profiles = blob,
            });
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_initialised == 0) return;
            var em = state.EntityManager;
            if (em.Exists(_entity) && em.HasComponent<TraversalProfileSingleton>(_entity))
            {
                var s = em.GetComponentData<TraversalProfileSingleton>(_entity);
                if (s.Profiles.IsCreated) s.Profiles.Dispose();
            }
        }
    }
}
