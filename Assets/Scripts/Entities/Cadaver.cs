// File: Assets/Scripts/Entities/Cadaver.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
        /// Create Cadaver using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position)
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
                RemainingCrystal = DefaultCrystal,
                Depleted = 0
            });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });

            return entity;
        }

        /// <summary>
        /// Create Cadaver using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent<CadaverTag>(entity);
            ecb.AddComponent(entity, new CadaverState
            {
                RemainingCrystal = DefaultCrystal,
                Depleted = 0
            });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });

            return entity;
        }
    }
}
