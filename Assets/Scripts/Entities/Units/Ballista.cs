// File: Assets/Scripts/Entities/Units/Ballista.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Ballista unit - Alanthor culture siege ranged unit.
    /// Longest range siege weapon in the game. Very slow reload but devastating damage.
    /// Uses ArcherState for ranged aim mechanics with SiegeTag for identification.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Ballista
    {
        // Default stats (used if TechTreeDB unavailable)
        private const float DefaultHP = 220f;
        private const float DefaultSpeed = 2.8f;
        private const float DefaultDamage = 50f;
        private const float DefaultLoS = 26f;
        private const float DefaultMinRange = 10f;
        private const float DefaultMaxRange = 24f;
        private const float DefaultCooldown = 3.0f;
        private const float DefaultAimTime = 1.0f;
        private const float DefaultRadius = 0.8f;
        private const int PresentationID = 337;

        /// <summary>
        /// Create Ballista using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Ballista using EntityCommandBuffer for deferred creation.
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

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Alanthor_Ballista", out var def))
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
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent<SiegeTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });

            // Archer-specific state for siege ranged behavior
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
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Structure });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 3, Magic = 0 });

            return entity;
        }
    }
}
