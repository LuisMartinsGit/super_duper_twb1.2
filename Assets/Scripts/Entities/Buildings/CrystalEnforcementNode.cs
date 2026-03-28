// File: Assets/Scripts/Entities/Buildings/CrystalEnforcementNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Enforcement Node - a sub-node that buffs nearby crystal allies
    /// with increased defense, attack, and speed via an Enforcement aura.
    /// </summary>
    public static class CrystalEnforcementNode
    {
        /// <summary>
        /// Create CrystalEnforcementNode using EntityManager.
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
                typeof(EnforcementAura),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = EnforcementNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = EnforcementNodeHP, Max = EnforcementNodeHP });
            em.SetComponentData(entity, new Radius { Value = EnforcementNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Enforcement });
            em.SetComponentData(entity, new EnforcementAura
            {
                Radius = EnforcementAuraRadius,
                DefBonus = EnforcementAuraDefBonus,
                AttBonus = EnforcementAuraAttBonus,
                SpeedBonus = EnforcementAuraSpeedBonus
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = EnforcementNodeBuildCost });

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
        /// Create CrystalEnforcementNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = EnforcementNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = EnforcementNodeHP, Max = EnforcementNodeHP });
            ecb.AddComponent(entity, new Radius { Value = EnforcementNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Enforcement });
            ecb.AddComponent(entity, new EnforcementAura
            {
                Radius = EnforcementAuraRadius,
                DefBonus = EnforcementAuraDefBonus,
                AttBonus = EnforcementAuraAttBonus,
                SpeedBonus = EnforcementAuraSpeedBonus
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = EnforcementNodeBuildCost });

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
