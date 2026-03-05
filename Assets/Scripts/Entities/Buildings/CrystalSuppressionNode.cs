// File: Assets/Scripts/Entities/Buildings/CrystalSuppressionNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Suppression Node - a sub-node that debuffs nearby enemies
    /// with reduced defense, attack, and speed via a Suppression aura.
    /// </summary>
    public static class CrystalSuppressionNode
    {
        private const int DefaultHP = 600;
        private const float DefaultRadius = 1.5f;
        private const int DefaultBuildCost = 200;
        private const int PresentationID = 314;

        // Aura defaults
        private const float AuraRadius = 20f;
        private const float AuraDefPenalty = 0.15f;
        private const float AuraAttPenalty = 0.15f;
        private const float AuraSpeedPenalty = 0.1f;

        /// <summary>
        /// Create CrystalSuppressionNode using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.White)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(Health),
                typeof(Radius),
                typeof(BuildingTag),
                typeof(CrystalTag),
                typeof(CrystalSubNodeTag),
                typeof(SuppressionAura),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Suppression });
            em.SetComponentData(entity, new SuppressionAura
            {
                Radius = AuraRadius,
                DefPenalty = AuraDefPenalty,
                AttPenalty = AuraAttPenalty,
                SpeedPenalty = AuraSpeedPenalty
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = DefaultBuildCost });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }

        /// <summary>
        /// Create CrystalSuppressionNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Suppression });
            ecb.AddComponent(entity, new SuppressionAura
            {
                Radius = AuraRadius,
                DefPenalty = AuraDefPenalty,
                AttPenalty = AuraAttPenalty,
                SpeedPenalty = AuraSpeedPenalty
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = DefaultBuildCost });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }
    }
}
