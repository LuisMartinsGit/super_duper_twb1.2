using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Alanthor crossbowman — heavy ranged infantry trained at the Practice
    /// Range (Lv 2 unlock). Slow, thumping bolt; shines against high-HP /
    /// heavy-armor targets.
    ///
    /// The ONLY crossbowman in the game (calculator 2026-08): the legacy
    /// Age 0 "Crossbowman" was retired and its recipe id now aliases to this
    /// creator. TechTreeDB id is <c>"Alanthor_Crossbowman"</c>.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class AlanthorCrossbowman
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "Alanthor_Crossbowman").
        private const float DefaultHP = 70f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 18f;
        private const float DefaultLoS = 22f;
        private const float DefaultMinRange = 6f;
        private const float DefaultMaxRange = 18f;
        private const float DefaultCooldown = 3.0f;
        private const float DefaultAimTime = 0.35f;
        private const int PresentationID = 335;

        /// <summary>
        /// Create Alanthor Crossbowman using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Alanthor Crossbowman using EntityCommandBuffer for deferred creation.
        /// </summary>
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

            if (TechCatalog.TryGetUnit("Alanthor_Crossbowman", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            // Projectile profile (design 2026-07-04): trajectory + speed from the SO.
            byte shotTrajectory = ShotTrajectory.Flat;
            float shotSpeed = 55f;
            if (TechCatalog.TryGetUnit("Alanthor_Crossbowman", out var pdef))
            {
                if (!string.IsNullOrEmpty(pdef.trajectory)) shotTrajectory = ShotTrajectory.Parse(pdef.trajectory);
                if (pdef.projectileSpeed > 0f) shotSpeed = pdef.projectileSpeed;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Archer-specific state for ranged behavior
            creator.AddComponent(entity, new ArcherState
            {
                Trajectory = shotTrajectory,
                ProjectileSpeed = shotSpeed,
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0
            });

            // Combat type tags (calculator: armor "ranged", defense 0/2/0/0)
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 2, Siege = 0, Magic = 0 });

            return entity;
        }
    }
}
