// File: Assets/Scripts/Entities/Units/ArchivistAdept.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// ArchivistAdept unit - Veiled Memory sect ranged magic unit.
    /// Fragile long-range caster with good damage output.
    /// Uses ArcherState for ranged attack behavior with Magic damage type.
    /// </summary>
    public static class ArchivistAdept
    {
        private const float DefaultHP = 110f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 14f;
        private const float DefaultLoS = 18f;
        private const float DefaultMinRange = 0f;
        private const float DefaultMaxRange = 14f;
        private const float DefaultCooldown = 1.6f;
        private const float DefaultAimTime = 0.4f;
        private const int PresentationID = 373;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Sect_ArchivistAdept", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(UnitTag), typeof(ArcherTag), typeof(Health), typeof(MoveSpeed),
                typeof(Damage), typeof(LineOfSight), typeof(ArcherState), typeof(Target),
                typeof(Radius), typeof(AttackCooldown), typeof(PopulationCost)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Magic });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new MoveSpeed { Value = speed });
            em.SetComponentData(entity, new Damage { Value = (int)damage });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            em.SetComponentData(entity, new PopulationCost { Amount = 1 });

            em.SetComponentData(entity, new ArcherState
            {
                CurrentTarget = Entity.Null,
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                HeightRangeMod = 4f,
                IsRetreating = 0,
                IsFiring = 0
            });

            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Magic });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            em.AddComponentData(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 2 });
            em.AddComponent<SectUniqueUnitTag>(entity);

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Sect_ArchivistAdept", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Magic });
            ecb.AddComponent<ArcherTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = 0.5f });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            ecb.AddComponent(entity, new PopulationCost { Amount = 1 });

            ecb.AddComponent(entity, new ArcherState
            {
                CurrentTarget = Entity.Null,
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                HeightRangeMod = 4f,
                IsRetreating = 0,
                IsFiring = 0
            });

            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            ecb.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 2 });
            ecb.AddComponent<SectUniqueUnitTag>(entity);

            return entity;
        }
    }
}
