// File: Assets/Scripts/Entities/Buildings/CrystalMainNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Main Node - the central hive of the Crystal Curse faction.
    /// Spawned at map start, spreads cursed ground, and controls crystal AI behavior.
    /// Uses Faction.White so existing targeting treats it as enemy to all players.
    /// </summary>
    public static class CrystalMainNode
    {
        /// <summary>
        /// Create CrystalMainNode using EntityManager.
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
                typeof(CrystalMainNodeTag),
                typeof(CrystalNode),
                typeof(CrystalSpreadState),
                typeof(CrystalNodeLevel),
                typeof(CrystalAIState),
                typeof(CrystalTrainingState),
                typeof(CrystalResourceValue),
                typeof(BuildingRangedAttack),
                typeof(Defense)
            );

            em.SetComponentData(entity, new PresentationId { Id = MainNodePresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            em.SetComponentData(entity, new Radius { Value = MainNodeRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            em.SetComponentData(entity, new CrystalSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            em.SetComponentData(entity, new CrystalNodeLevel { Value = 1 });
            em.SetComponentData(entity, new CrystalAIState
            {
                HarassTimer = MainNodeHarassTimer,
                BuildTimer = 0f,
                UnitSpawnTimer = 0f,
                Phase = 0
            });
            em.SetComponentData(entity, new CrystalResourceValue
            {
                BuildCost = MainNodeBuildCost
            });

            // Self-defense turret
            em.SetComponentData(entity, new BuildingRangedAttack
            {
                Range = MainNodeAttackRange,
                Damage = MainNodeAttackDamage,
                Cooldown = MainNodeAttackCooldown,
                Timer = 0f,
                MaxTargets = MainNodeAttackMaxTargets
            });
            em.SetComponentData(entity, new Defense { Melee = 15, Ranged = 15, Siege = 10, Magic = 10 });

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
        /// Create CrystalMainNode using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = MainNodePresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = MainNodeHP, Max = MainNodeHP });
            ecb.AddComponent(entity, new Radius { Value = MainNodeRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalMainNodeTag>(entity);
            ecb.AddComponent(entity, new CrystalNode
            {
                SpreadRadius = MainNodeSpreadRadius,
                Enabled = 1
            });
            ecb.AddComponent(entity, new CrystalSpreadState { TickTimer = 0f, CurrentRingRadius = 0f });
            ecb.AddComponent(entity, new CrystalNodeLevel { Value = 1 });
            ecb.AddComponent(entity, new CrystalAIState
            {
                HarassTimer = MainNodeHarassTimer,
                BuildTimer = 0f,
                UnitSpawnTimer = 0f,
                Phase = 0
            });
            ecb.AddComponent(entity, new CrystalTrainingState
            {
                TrainingUnitType = 0,
                TimeRemaining = 0f,
                TotalTime = 0f
            });
            ecb.AddComponent(entity, new CrystalResourceValue
            {
                BuildCost = MainNodeBuildCost
            });

            // Self-defense turret
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = MainNodeAttackRange,
                Damage = MainNodeAttackDamage,
                Cooldown = MainNodeAttackCooldown,
                Timer = 0f,
                MaxTargets = MainNodeAttackMaxTargets
            });
            ecb.AddComponent(entity, new Defense { Melee = 15, Ranged = 15, Siege = 10, Magic = 10 });

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
