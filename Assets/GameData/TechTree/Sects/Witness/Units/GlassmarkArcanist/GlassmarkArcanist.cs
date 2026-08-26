using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// GlassmarkArcanist unit - Mirror Rite sect ranged magic unit.
    /// Glass cannon with highest magic damage but lowest HP among sect units.
    /// Uses ArcherState for ranged attack behavior with Magic damage type.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class GlassmarkArcanist
    {
        private const float DefaultHP = 100f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 18f;
        private const float DefaultLoS = 19f;
        private const float DefaultMinRange = 0f;
        private const float DefaultMaxRange = 15f;
        private const float DefaultCooldown = 1.5f;
        private const float DefaultAimTime = 0.4f;
        private const int PresentationID = 376;

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

            if (TechCatalog.TryGetUnit("Sect_GlassmarkArcanist", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Magic });
            creator.AddComponent<ArcherTag>(entity);
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

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 1 });
            creator.AddComponent<SectUniqueUnitTag>(entity);
            creator.AddComponent(entity, new UnitAbility { Id = AbilityId.MirrorShield, CooldownDuration = 22f, CooldownRemaining = 0f, Range = 0f });

            return entity;
        }
    }
}
