// File: Assets/Scripts/Entities/Cadaver.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal node - static mineable entity that yields crystal.
    /// Spawned when a crystal creature dies. Miners gather crystal from it
    /// and deliver to Halls or Gatherer's Huts.
    /// </summary>
    public static class Cadaver
    {
        private const int DefaultCrystal = 300;
        private const float DefaultRadius = 0.8f;
        private const int PresentationID = 301;

        /// <summary>
        /// Create Cadaver using EntityManager with default crystal amount.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position)
        {
            return Create(em, position, DefaultCrystal);
        }

        /// <summary>
        /// Create Cadaver using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position)
        {
            return Create(ecb, position, DefaultCrystal);
        }

        /// <summary>
        /// Create Cadaver using EntityManager with a custom crystal amount.
        /// Used by death drop system to set loot based on entity build cost.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, int crystalAmount)
        {
            return Create(em, position, crystalAmount, DefaultRadius);
        }

        /// <summary>
        /// Create Cadaver using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, int crystalAmount)
        {
            return Create(ecb, position, crystalAmount, DefaultRadius);
        }

        /// <summary>
        /// Create Cadaver using EntityManager with custom crystal amount and radius.
        /// Main node deaths produce larger cadavers.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, int crystalAmount, float radius)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(CadaverTag),
                typeof(CadaverState),
                typeof(Radius)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new CadaverState
            {
                RemainingCrystal = crystalAmount,
                Depleted = 0
            });
            em.SetComponentData(entity, new Radius { Value = radius });

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }

        /// <summary>
        /// Create Cadaver using EntityCommandBuffer with custom crystal amount and radius.
        /// Main node deaths produce larger cadavers.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, int crystalAmount, float radius)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent<CadaverTag>(entity);
            ecb.AddComponent(entity, new CadaverState
            {
                RemainingCrystal = crystalAmount,
                Depleted = 0
            });
            ecb.AddComponent(entity, new Radius { Value = radius });

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            return entity;
        }
    }
}
