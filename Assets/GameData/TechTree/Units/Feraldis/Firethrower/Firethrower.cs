// File: Assets/GameData/TechTree/Units/Feraldis/Firethrower/Firethrower.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Feraldis Firethrower — hurls burning balls of fire. Where the shot
    /// lands on bloodsoaked ground, the BLOOD CATCHES FIRE: the pool is
    /// consumed and the whole patch burns, damaging everything in it
    /// (FeraldisIgnition, driven off the IgnitesBlood rider carried by each
    /// shot).
    ///
    /// This is the culture's release valve. Everything else in the Feraldis
    /// kit accumulates blood — frenzy ground, totem fuel — and the
    /// Firethrower spends it. On clean ground it is just a mediocre ranged
    /// attack, which is the point.
    ///
    /// Design: docs/Design/Age_1_Feraldis.md (2026-08-05 rev.2).
    /// </summary>
    public static class Firethrower
    {
        private const float DefaultHP = 100f;
        private const float DefaultSpeed = 5.0f;
        private const float DefaultDamage = 14f;
        private const float DefaultLoS = 14f;
        private const float DefaultMinRange = 2f;
        private const float DefaultMaxRange = 10f;
        private const float DefaultCooldown = 2.2f;
        private const float DefaultAimTime = 0.5f;
        public const int PresentationID = 344;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float cooldown = DefaultCooldown;

            if (TechCatalog.TryGetUnit("Feraldis_Firethrower", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange >= 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent<FeraldisUnitTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0
            });

            // The signature. RangedCombatSystem copies this onto every shot,
            // so a fireball still ignites after its thrower is killed.
            creator.AddComponent(entity, new IgnitesBlood
            {
                Radius = IgnitionRadius,
                DamagePerSecond = IgnitionDamagePerSecond,
                Duration = IgnitionSeconds,
            });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
