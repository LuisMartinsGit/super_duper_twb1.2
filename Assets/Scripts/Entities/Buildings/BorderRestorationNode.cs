// File: Assets/Scripts/Entities/Buildings/BorderRestorationNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstone Restoration Node - a sub-node that heals nearby veilstone entities
    /// (buildings and units) over time via a Restoration aura.
    /// </summary>
    public static class BorderRestorationNode
    {
        /// <summary>
        /// Create BorderRestorationNode using EntityManager.
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
                typeof(RestorationAura),
                typeof(VeilstoneWorth)
            );

            em.SetComponentData(entity, new PresentationId { Id = RestorationNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = RestorationNodeHP, Max = RestorationNodeHP });
            em.SetComponentData(entity, new Radius { Value = RestorationNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new BorderSubNodeTag { Type = BorderSubNodeType.Restoration });
            em.SetComponentData(entity, new RestorationAura
            {
                Radius = RestorationAuraRadius,
                HealPerSecond = RestorationAuraHealPerSecond,
                HealTimer = 0f
            });
            em.SetComponentData(entity, new VeilstoneWorth { BuildCost = RestorationNodeBuildCost });

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
        /// Create BorderRestorationNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.Border)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = RestorationNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = RestorationNodeHP, Max = RestorationNodeHP });
            ecb.AddComponent(entity, new Radius { Value = RestorationNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<BorderTag>(entity);
            ecb.AddComponent(entity, new BorderSubNodeTag { Type = BorderSubNodeType.Restoration });
            ecb.AddComponent(entity, new RestorationAura
            {
                Radius = RestorationAuraRadius,
                HealPerSecond = RestorationAuraHealPerSecond,
                HealTimer = 0f
            });
            ecb.AddComponent(entity, new VeilstoneWorth { BuildCost = RestorationNodeBuildCost });

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
