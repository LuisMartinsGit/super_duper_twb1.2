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
        public const int PresentationID = 344;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Feraldis_Firethrower");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            float cooldown = def.attackCooldown;
        

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
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });
            creator.AddComponent(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = def.aimTime,
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
