// File: Assets/Scripts/Entities/Buildings/CrystalMainNode.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crystal Main Node - the central hive of the Crystal Curse faction.
    /// Spawned at map start, spreads cursed ground, and controls crystal AI behavior.
    /// Uses Faction.White so existing targeting treats it as enemy to all players.
    /// </summary>
    public static class CrystalMainNode
    {
        private const int DefaultHP = 4000;
        private const float DefaultRadius = 2.5f;
        private const float DefaultSpreadRadius = 15f;
        private const float DefaultSpreadPerTick = 1f;
        private const float DefaultTickInterval = 45f;
        private const float DefaultIncomePerTick = 0f;
        private const float DefaultHarassTimer = 60f;
        private const int DefaultBuildCost = 2000;
        private const int PresentationID = 310;

        // Main node self-defense turret — fires at attackers
        private const float AttackRange = 18f;
        private const int AttackDamage = 25;
        private const float AttackCooldown = 1.2f;
        private const int AttackMaxTargets = 3;

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
                typeof(BuildingTag),
                typeof(CrystalTag),
                typeof(CrystalMainNodeTag),
                typeof(CrystalNode),
                typeof(CrystalNodeLevel),
                typeof(CrystalAIState),
                typeof(CrystalResourceValue),
                typeof(BuildingRangedAttack),
                typeof(Defense)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new CrystalNode
            {
                IsMain = 1,
                SpreadPerTick = DefaultSpreadPerTick,
                SpreadRadius = DefaultSpreadRadius,
                IncomePerTick = DefaultIncomePerTick,
                TickInterval = DefaultTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            em.SetComponentData(entity, new CrystalNodeLevel { Value = 1 });
            em.SetComponentData(entity, new CrystalAIState
            {
                HarassTimer = DefaultHarassTimer,
                BuildTimer = 0f,
                UnitSpawnTimer = 0f,
                Phase = 0
            });
            em.SetComponentData(entity, new CrystalResourceValue
            {
                BuildCost = DefaultBuildCost
            });

            // Self-defense turret
            em.SetComponentData(entity, new BuildingRangedAttack
            {
                Range = AttackRange,
                Damage = AttackDamage,
                Cooldown = AttackCooldown,
                Timer = 0f,
                MaxTargets = AttackMaxTargets
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

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new Health { Value = DefaultHP, Max = DefaultHP });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalMainNodeTag>(entity);
            ecb.AddComponent(entity, new CrystalNode
            {
                IsMain = 1,
                SpreadPerTick = DefaultSpreadPerTick,
                SpreadRadius = DefaultSpreadRadius,
                IncomePerTick = DefaultIncomePerTick,
                TickInterval = DefaultTickInterval,
                TickTimer = 0f,
                CurrentRingRadius = 0f,
                Enabled = 1
            });
            ecb.AddComponent(entity, new CrystalNodeLevel { Value = 1 });
            ecb.AddComponent(entity, new CrystalAIState
            {
                HarassTimer = DefaultHarassTimer,
                BuildTimer = 0f,
                UnitSpawnTimer = 0f,
                Phase = 0
            });
            ecb.AddComponent(entity, new CrystalResourceValue
            {
                BuildCost = DefaultBuildCost
            });

            // Self-defense turret
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = AttackRange,
                Damage = AttackDamage,
                Cooldown = AttackCooldown,
                Timer = 0f,
                MaxTargets = AttackMaxTargets
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
