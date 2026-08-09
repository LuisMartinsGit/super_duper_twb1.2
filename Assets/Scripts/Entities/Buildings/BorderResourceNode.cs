// File: Assets/Scripts/Entities/Buildings/BorderResourceNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstone Resource Node - a brittle sub-node that spreads border ground
    /// at a smaller radius than the main node and generates veilstone income.
    /// </summary>
    public static class BorderResourceNode
    {
        /// <summary>
        /// Create BorderResourceNode using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.Border)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(Health),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(BuildingTag),
                typeof(BorderTag),
                typeof(BorderSubNodeTag),
                typeof(BorderNode),
                typeof(BorderSpreadState),
                typeof(VeilstoneWorth),
                typeof(OwnerNode)
            );

            em.SetComponentData(entity, new PresentationId { Id = ResourceNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = ResourceNodeHP, Max = ResourceNodeHP });
            em.SetComponentData(entity, new Radius { Value = ResourceNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new BorderSubNodeTag { Type = BorderSubNodeType.Resource });
            em.SetComponentData(entity, new BorderNode
            {
                SpreadRadius = ResourceNodeSpreadRadius,
                Enabled = 1
            });
            em.SetComponentData(entity, new BorderSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            em.SetComponentData(entity, new VeilstoneWorth { BuildCost = ResourceNodeBuildCost });
            em.SetComponentData(entity, new OwnerNode { Value = Entity.Null });

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });

            // Construction window — drives the staggered rise animation.
            // BorderConstructionSystem advances Progress at 1s / s.
            em.AddComponentData(entity, new UnderConstruction { Progress = 0f, Total = 60f });

            return entity;
        }

        /// <summary>
        /// Create BorderResourceNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.Border)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = ResourceNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = ResourceNodeHP, Max = ResourceNodeHP });
            ecb.AddComponent(entity, new Radius { Value = ResourceNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<BorderTag>(entity);
            ecb.AddComponent(entity, new BorderSubNodeTag { Type = BorderSubNodeType.Resource });
            ecb.AddComponent(entity, new BorderNode
            {
                SpreadRadius = ResourceNodeSpreadRadius,
                Enabled = 1
            });
            ecb.AddComponent(entity, new BorderSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            ecb.AddComponent(entity, new VeilstoneWorth { BuildCost = ResourceNodeBuildCost });
            ecb.AddComponent(entity, new OwnerNode { Value = Entity.Null });

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });

            // Construction window — drives the staggered rise animation.
            ecb.AddComponent(entity, new UnderConstruction { Progress = 0f, Total = 60f });

            return entity;
        }
    }
}
