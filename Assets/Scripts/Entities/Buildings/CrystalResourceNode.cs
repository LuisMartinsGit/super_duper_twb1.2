// File: Assets/Scripts/Entities/Buildings/CrystalResourceNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Resource Node - a brittle sub-node that spreads cursed ground
    /// at a smaller radius than the main node and generates crystal income.
    /// </summary>
    public static class CrystalResourceNode
    {
        private const int DefaultHP = 200;
        private const float DefaultRadius = 1.5f;
        private const float DefaultSpreadRadius = 8f;
        private const float DefaultSpreadPerTick = 1f;
        private const float DefaultTickInterval = 30f;
        private const float DefaultIncomePerTick = 0f;
        private const int DefaultBuildCost = 50;
        private const int PresentationID = 312;

        /// <summary>
        /// Create CrystalResourceNode using EntityManager.
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
                typeof(CrystalNode),
                typeof(CrystalResourceValue),
                typeof(OwnerNode)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Resource });
            em.SetComponentData(entity, new CrystalNode
            {
                IsMain = 0,
                SpreadPerTick = DefaultSpreadPerTick,
                SpreadRadius = DefaultSpreadRadius,
                IncomePerTick = DefaultIncomePerTick,
                TickInterval = DefaultTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = DefaultBuildCost });
            em.SetComponentData(entity, new OwnerNode { Value = Entity.Null });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }

        /// <summary>
        /// Create CrystalResourceNode using EntityCommandBuffer for deferred creation.
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
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Resource });
            ecb.AddComponent(entity, new CrystalNode
            {
                IsMain = 0,
                SpreadPerTick = DefaultSpreadPerTick,
                SpreadRadius = DefaultSpreadRadius,
                IncomePerTick = DefaultIncomePerTick,
                TickInterval = DefaultTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = DefaultBuildCost });
            ecb.AddComponent(entity, new OwnerNode { Value = Entity.Null });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }
    }
}
