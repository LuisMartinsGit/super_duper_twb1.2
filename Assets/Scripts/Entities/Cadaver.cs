// File: Assets/Scripts/Entities/Cadaver.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal node — static mineable entity that yields crystal. Spawned when a
    /// crystal creature dies or when starter patches are seeded at game start.
    /// Visual radius and LocalTransform.Scale are derived from the crystal amount
    /// (cube-root scaling so resource volume tracks the value).
    ///
    /// Cadavers placed within <see cref="MergeRadius"/> of an existing non-depleted
    /// cadaver merge into one carrying the summed crystal value (see
    /// <see cref="CreateOrMerge"/>) — prevents flicker-spam when an entity's
    /// death animation lingers and keeps the gathered field tidy.
    /// </summary>
    public static class Cadaver
    {
        public const int DefaultCrystal = 300;
        public const float MergeRadius = 4f;

        private const int PresentationID = 301;

        // Reference: 80 crystal → scale 1.0 / radius 0.6 (matches starter patch).
        private const float RefAmount = 80f;
        private const float BaseRadius = 0.6f;
        private const float MinScale = 0.6f;
        private const float MaxScale = 4f;

        /// <summary>Visual scale applied to LocalTransform from the crystal amount.</summary>
        public static float ComputeScale(int crystalAmount)
        {
            float a = math.max(1f, crystalAmount);
            return math.clamp(math.pow(a / RefAmount, 1f / 3f), MinScale, MaxScale);
        }

        /// <summary>Collider/selection radius from the crystal amount.</summary>
        public static float ComputeRadius(int crystalAmount) => BaseRadius * ComputeScale(crystalAmount);

        /// <summary>
        /// Create a cadaver, or merge into an existing non-depleted cadaver within
        /// <see cref="MergeRadius"/>. On merge the existing cadaver is destroyed and
        /// a new one is created at its position carrying the summed crystal value
        /// (and the recomputed visual scale). Returns Entity.Null if the cap is hit
        /// for new creations; merges always proceed since they don't grow node count.
        /// </summary>
        public static Entity CreateOrMerge(EntityManager em, float3 position, int crystalAmount, int maxNodes = int.MaxValue)
        {
            if (crystalAmount <= 0) return Entity.Null;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<CadaverState>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var states = query.ToComponentDataArray<CadaverState>(Allocator.Temp);

            float mergeSqr = MergeRadius * MergeRadius;
            for (int i = 0; i < entities.Length; i++)
            {
                if (states[i].Depleted != 0) continue;
                float2 a = new float2(transforms[i].Position.x, transforms[i].Position.z);
                float2 b = new float2(position.x, position.z);
                if (math.distancesq(a, b) <= mergeSqr)
                {
                    int totalCrystal = states[i].RemainingCrystal + crystalAmount;
                    float3 mergedPos = transforms[i].Position;
                    em.DestroyEntity(entities[i]);
                    return Create(em, mergedPos, totalCrystal);
                }
            }

            if (entities.Length >= maxNodes) return Entity.Null;
            return Create(em, position, crystalAmount);
        }

        public static Entity Create(EntityManager em, float3 position) => Create(em, position, DefaultCrystal);

        public static Entity Create(EntityCommandBuffer ecb, float3 position) => Create(ecb, position, DefaultCrystal);

        public static Entity Create(EntityManager em, float3 position, int crystalAmount)
        {
            float scale = ComputeScale(crystalAmount);
            float radius = ComputeRadius(crystalAmount);

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(CadaverTag),
                typeof(CadaverState),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, scale));
            em.SetComponentData(entity, new CadaverState
            {
                RemainingCrystal = crystalAmount,
                MaxCrystal = crystalAmount,
                Depleted = 0
            });
            em.SetComponentData(entity, new Radius { Value = radius });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, int crystalAmount)
        {
            float scale = ComputeScale(crystalAmount);
            float radius = ComputeRadius(crystalAmount);

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, scale));
            ecb.AddComponent<CadaverTag>(entity);
            ecb.AddComponent(entity, new CadaverState
            {
                RemainingCrystal = crystalAmount,
                MaxCrystal = crystalAmount,
                Depleted = 0
            });
            ecb.AddComponent(entity, new Radius { Value = radius });

            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }
    }
}
