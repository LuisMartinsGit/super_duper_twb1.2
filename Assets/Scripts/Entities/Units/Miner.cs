// File: Assets/Scripts/Entities/Units/Miner.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Miner unit - gathers iron from deposits.
    /// Economy class unit with MinerTag and MinerState components.
    /// </summary>
    public static class Miner
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 50f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 2f;
        private const float DefaultLoS = 10f;
        private const float DefaultGatherSpeed = 1f;
        private const int DefaultCarryCapacity = 1;
        private const int PresentationID = 203;

        /// <summary>
        /// Create Miner using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Miner", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(MinerTag),
                typeof(MinerState),
                typeof(PopulationCost)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Miner });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            em.SetComponentData(entity, new MinerState
            {
                AssignedDeposit = Entity.Null,
                CurrentLoad = 0,
                GatherTimer = 0f,
                State = MinerWorkState.Idle,
                GatheringResource = 0,
                DropoffTarget = Entity.Null,
                GatherSpeedMultiplier = 1.0f,
                CarryCapacityBonus = 0
            });
            em.SetComponentData(entity, new PopulationCost { Amount = 1 });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Melee });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }

        /// <summary>
        /// Create Miner using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Miner", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Miner });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Radius { Value = 0.5f });
            ecb.AddComponent<MinerTag>(entity);
            ecb.AddComponent(entity, new MinerState
            {
                AssignedDeposit = Entity.Null,
                CurrentLoad = 0,
                GatherTimer = 0f,
                State = MinerWorkState.Idle,
                GatheringResource = 0,
                DropoffTarget = Entity.Null,
                GatherSpeedMultiplier = 1.0f,
                CarryCapacityBonus = 0
            });
            ecb.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
    // MinerTag, MinerWorkState, and MinerState are defined in Core/Components/UnitComponents.cs (global namespace)
}