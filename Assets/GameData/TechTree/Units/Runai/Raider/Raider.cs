using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Raider unit - Runai culture mounted archer.
    /// Fast cavalry that shoots while moving. Fragile but extremely mobile.
    /// Uses ArcherState for ranged behavior; battalion movement lets them fire on the move.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Raider
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 120f;
        private const float DefaultSpeed = 7.2f;
        private const float DefaultDamage = 10f;
        private const float DefaultLoS = 15f;
        private const float DefaultMinRange = 3f;
        private const float DefaultMaxRange = 14f;
        private const float DefaultCooldown = 1.4f;
        private const float DefaultAimTime = 0.3f;
        private const float DefaultRadius = 0.6f;
        private const int PresentationID = 332;

        /// <summary>Create Raider using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Raider using EntityCommandBuffer for deferred creation.</summary>
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

            if (TechCatalog.TryGetUnit("Runai_Raider", out var def))
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
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent<CavalryTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Mounted archer state
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

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Cavalry });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
