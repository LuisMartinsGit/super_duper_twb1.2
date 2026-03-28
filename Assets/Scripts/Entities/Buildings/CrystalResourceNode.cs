// File: Assets/Scripts/Entities/Buildings/CrystalResourceNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Resource Node - a brittle sub-node that spreads cursed ground
    /// at a smaller radius than the main node and generates crystal income.
    /// </summary>
    public static class CrystalResourceNode
    {
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

            em.SetComponentData(entity, new PresentationId { Id = ResourceNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = ResourceNodeHP, Max = ResourceNodeHP });
            em.SetComponentData(entity, new Radius { Value = ResourceNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Resource });
            em.SetComponentData(entity, new CrystalNode
            {
                SpreadPerTick = ResourceNodeSpreadPerTick,
                SpreadRadius = ResourceNodeSpreadRadius,
                TickInterval = ResourceNodeTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = ResourceNodeBuildCost });
            em.SetComponentData(entity, new OwnerNode { Value = Entity.Null });

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

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

            ecb.AddComponent(entity, new PresentationId { Id = ResourceNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = ResourceNodeHP, Max = ResourceNodeHP });
            ecb.AddComponent(entity, new Radius { Value = ResourceNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Resource });
            ecb.AddComponent(entity, new CrystalNode
            {
                SpreadPerTick = ResourceNodeSpreadPerTick,
                SpreadRadius = ResourceNodeSpreadRadius,
                TickInterval = ResourceNodeTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = ResourceNodeBuildCost });
            ecb.AddComponent(entity, new OwnerNode { Value = Entity.Null });

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });

            return entity;
        }
    }
}
