// File: Assets/Scripts/Entities/Buildings/BorderSuppressionNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstone Suppression Node - a sub-node that debuffs nearby enemies
    /// with reduced defense, attack, and speed via a Suppression aura.
    /// </summary>
    public static class BorderSuppressionNode
    {
        /// <summary>
        /// Create BorderSuppressionNode using EntityManager.
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
                typeof(SuppressionAura),
                typeof(VeilstoneWorth)
            );

            em.SetComponentData(entity, new PresentationId { Id = SuppressionNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = SuppressionNodeHP, Max = SuppressionNodeHP });
            em.SetComponentData(entity, new Radius { Value = SuppressionNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new BorderSubNodeTag { Type = BorderSubNodeType.Suppression });
            em.SetComponentData(entity, new SuppressionAura
            {
                Radius = SuppressionAuraRadius,
                DefPenalty = SuppressionAuraDefPenalty,
                AttPenalty = SuppressionAuraAttPenalty,
                SpeedPenalty = SuppressionAuraSpeedPenalty
            });
            em.SetComponentData(entity, new VeilstoneWorth { BuildCost = SuppressionNodeBuildCost });

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
        /// Create BorderSuppressionNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.Border)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = SuppressionNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = SuppressionNodeHP, Max = SuppressionNodeHP });
            ecb.AddComponent(entity, new Radius { Value = SuppressionNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<BorderTag>(entity);
            ecb.AddComponent(entity, new BorderSubNodeTag { Type = BorderSubNodeType.Suppression });
            ecb.AddComponent(entity, new SuppressionAura
            {
                Radius = SuppressionAuraRadius,
                DefPenalty = SuppressionAuraDefPenalty,
                AttPenalty = SuppressionAuraAttPenalty,
                SpeedPenalty = SuppressionAuraSpeedPenalty
            });
            ecb.AddComponent(entity, new VeilstoneWorth { BuildCost = SuppressionNodeBuildCost });

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
