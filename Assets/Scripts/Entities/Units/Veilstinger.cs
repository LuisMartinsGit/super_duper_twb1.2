// File: Assets/Scripts/Entities/Units/Veilstinger.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Veilstinger unit - dual-laser ranged glass cannon for the Crystal faction.
    /// Fires at up to two targets simultaneously. Very fragile but high damage.
    /// Uses VeilstingerState instead of ArcherState for dual-target tracking.
    /// No population cost - crystal faction uses crystal resource economy.
    /// </summary>
    public static class Veilstinger
    {
        private const float DefaultHP = 65f;
        private const float DefaultSpeed = 4.0f;
        private const float DefaultDamage = 18f;
        private const float DefaultLoS = 28f;
        private const float DefaultMinRange = 8f;
        private const float DefaultMaxRange = 24f;
        private const float DefaultAimTime = 0.2f;
        private const float DefaultRadius = 0.5f;
        private const int PresentationID = 321;

        /// <summary>
        /// Create Veilstinger using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Veilstinger", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(CrystalTag),
                typeof(CrystalUnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(LineOfSight),
                typeof(VeilstingerState),
                typeof(Target),
                typeof(Radius),
                typeof(CrystalResourceValue)
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
            em.SetComponentData(entity, new Radius { Value = radius });
            em.SetComponentData(entity, new CrystalResourceValue { BuildCost = 150 });

            // Veilstinger-specific dual-target state
            em.SetComponentData(entity, new VeilstingerState
            {
                Target1 = Entity.Null,
                Target2 = Entity.Null,
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsFiring = 0
            });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Magic });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Ranged });

            return entity;
        }

        /// <summary>
        /// Create Veilstinger using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Veilstinger", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            ecb.AddComponent<CrystalTag>(entity);
            ecb.AddComponent<CrystalUnitTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = radius });
            ecb.AddComponent(entity, new CrystalResourceValue { BuildCost = 150 });

            // Veilstinger-specific dual-target state
            ecb.AddComponent(entity, new VeilstingerState
            {
                Target1 = Entity.Null,
                Target2 = Entity.Null,
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsFiring = 0
            });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });

            return entity;
        }
    }
}
