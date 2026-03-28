// File: Assets/Scripts/Entities/Buildings/CrystalTurretNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Turret Node - a defensive sub-node that auto-fires projectiles
    /// at enemies within range. Leverages the existing BuildingCombatSystem
    /// via BuildingRangedAttack + BuildingTag components.
    /// </summary>
    public static class CrystalTurretNode
    {
        /// <summary>
        /// Create CrystalTurretNode using EntityManager.
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
                typeof(BuildingRangedAttack),
                typeof(CrystalResourceValue)
            );

            em.SetComponentData(entity, new PresentationId { Id = TurretNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = TurretNodeHP, Max = TurretNodeHP });
            em.SetComponentData(entity, new Radius { Value = TurretNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Turret });
            em.SetComponentData(entity, new BuildingRangedAttack
            {
                Range = TurretRange,
                Damage = TurretDamage,
                Cooldown = TurretCooldown,
                Timer = 0f,
                MaxTargets = TurretMaxTargets
            });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = TurretNodeBuildCost });

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Structure });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Magic });

            return entity;
        }

        /// <summary>
        /// Create CrystalTurretNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = TurretNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = TurretNodeHP, Max = TurretNodeHP });
            ecb.AddComponent(entity, new Radius { Value = TurretNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent(entity, new CrystalSubNodeTag { Type = CrystalSubNodeType.Turret });
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = TurretRange,
                Damage = TurretDamage,
                Cooldown = TurretCooldown,
                Timer = 0f,
                MaxTargets = TurretMaxTargets
            });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = TurretNodeBuildCost });

            // Assign network ID for multiplayer lockstep synchronization
            ecb.AddComponent(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });

            return entity;
        }
    }
}
