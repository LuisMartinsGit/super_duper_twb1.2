// File: Assets/Scripts/Entities/Buildings/CrystalRestorationNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Restoration Node - a sub-node that heals nearby crystal entities
    /// (buildings and units) over time via a Restoration aura.
    /// </summary>
    public static class CrystalRestorationNode
    {
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

            em.SetComponentData(entity, new PresentationId { Id = RestorationNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = RestorationNodeHP, Max = RestorationNodeHP });
            em.SetComponentData(entity, new Radius { Value = RestorationNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Restoration });
            em.SetComponentData(entity, new RestorationAura
            {
                Radius = RestorationAuraRadius,
                HealPerSecond = RestorationAuraHealPerSecond,
                HealTimer = 0f
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = RestorationNodeBuildCost });

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
        /// Create CrystalRestorationNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = RestorationNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = RestorationNodeHP, Max = RestorationNodeHP });
            ecb.AddComponent(entity, new Radius { Value = RestorationNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Restoration });
            ecb.AddComponent(entity, new RestorationAura
            {
                Radius = RestorationAuraRadius,
                HealPerSecond = RestorationAuraHealPerSecond,
                HealTimer = 0f
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = RestorationNodeBuildCost });

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
