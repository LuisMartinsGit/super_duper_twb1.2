// File: Assets/Scripts/Entities/Units/Hunter.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hunter unit - Feraldis culture axe thrower.
    /// Short-range ranged unit that throws axes. Never retreats — fights in melee
    /// when enemies close in. Unarmored but high damage and good mobility.
    /// Uses ArcherState with MinRange=0 so they keep firing at point blank.
    /// </summary>
    public static class Hunter
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 90f;
        private const float DefaultSpeed = 5.7f;
        private const float DefaultDamage = 16f;
        private const float DefaultLoS = 12f;
        private const float DefaultMinRange = 0f;
        private const float DefaultMaxRange = 8f;
        private const float DefaultCooldown = 1.2f;
        private const float DefaultAimTime = 0.3f;
        private const int PresentationID = 338;

        /// <summary>
        /// Create Hunter using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_Hunter", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange >= 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(ArcherTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(LineOfSight),
                typeof(ArcherState),
                typeof(Target),
                typeof(Radius),
                typeof(AttackCooldown),
                typeof(PopulationCost)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Ranged });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            em.SetComponentData(entity, new PopulationCost { Amount = 1 });

            // Axe thrower state — MinRange=0 means never retreats, fights at point blank
            em.SetComponentData(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0
            });

            // Combat type tags — Melee damage type (throwing axes), unarmored
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Melee });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            em.AddComponentData(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }

        /// <summary>
        /// Create Hunter using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Feraldis_Hunter", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange >= 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            ecb.AddComponent<ArcherTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = 0.5f });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            ecb.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Axe thrower state — MinRange=0, never retreats
            ecb.AddComponent(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0
            });

            // Combat type tags — Melee damage type (throwing axes), unarmored
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            ecb.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
