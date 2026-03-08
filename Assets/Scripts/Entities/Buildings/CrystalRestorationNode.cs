// File: Assets/Scripts/Entities/Buildings/CrystalRestorationNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Restoration Node - a sub-node that heals nearby crystal entities
    /// (buildings and units) over time via a Restoration aura.
    /// </summary>
    public static class CrystalRestorationNode
    {
        private const int DefaultHP = 400;
        private const float DefaultRadius = 1.5f;
        private const int DefaultBuildCost = 120;
        private const int PresentationID = 315;

        // Aura defaults
        private const float AuraRadius = 15f;
        private const float AuraHealPerSecond = 5f;

        /// <summary>
        /// Create CrystalRestorationNode using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.White)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(Health),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(BuildingTag),
                typeof(CrystalTag),
                typeof(CrystalSubNodeTag),
                typeof(RestorationAura),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            var gridSize = BuildingSizeConfig.GetSize("CrystalRestorationNode");
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Restoration });
            em.SetComponentData(entity, new RestorationAura
            {
                Radius = AuraRadius,
                HealPerSecond = AuraHealPerSecond,
                HealTimer = 0f
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = DefaultBuildCost });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }

        /// <summary>
        /// Create CrystalRestorationNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            var gridSize = BuildingSizeConfig.GetSize("CrystalRestorationNode");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Restoration });
            ecb.AddComponent(entity, new RestorationAura
            {
                Radius = AuraRadius,
                HealPerSecond = AuraHealPerSecond,
                HealTimer = 0f
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = DefaultBuildCost });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }
    }
}
