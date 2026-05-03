// Litharch.cs
// Support healer unit - can heal friendly units
// Location: Assets/Scripts/Entities/Units/Litharch.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Litharch - Era 1 support unit specialized in healing.
    /// Trained at Shrine of Ridan.
    /// 
    /// Abilities:
    /// - Heals friendly units over time
    /// - Light combat capability for self-defense
    /// </summary>
    public static class Litharch
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ═══════════════════════════════════════════════════════════════════════

        public const int PresentationID = 207;

        // Default stats (used if TechTreeDB not available)
        private const float DefaultHP = 60f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 5f;
        private const float DefaultLoS = 10f;
        private const float DefaultHealRate = 8f;      // HP healed per second
        private const float DefaultHealRange = 4f;     // Range to heal targets
        private const float DefaultCooldown = 1.5f;    // Attack cooldown

        // ═══════════════════════════════════════════════════════════════════════
        // FACTORY
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Create a Litharch entity using EntityCommandBuffer.
        /// Loads stats from TechTreeDB if available.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float healRate = DefaultHealRate;
            float healRange = DefaultHealRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Litharch", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.healsPerSecond > 0) healRate = def.healsPerSecond;
                if (def.attackRange > 0) healRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = ecb.CreateEntity();

            // Core identity
            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Support });

            // MovementSystem's query requires DesiredDestination. AIMilitaryManager
            // issues movement via ecb.SetComponent<DesiredDestination>; without
            // this baked in, AI-trained Litharchs sat at the spawn point as
            // paperweights. Mirrors Miner.cs:54-58. (task-062 G-3)
            ecb.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Litharch-specific tag
            ecb.AddComponent<LitharchTag>(entity);

            // Stats
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new MoveSpeed { Value = speed });
            ecb.AddComponent(entity, new Damage { Value = (int)damage });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            ecb.AddComponent(entity, new Radius { Value = 0.5f });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            ecb.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Targeting
            ecb.AddComponent(entity, new Target { Value = Entity.Null });

            // Healer capability
            ecb.AddComponent(entity, new CanHeal
            {
                HealRate = healRate,
                HealRange = healRange
            });

            // Healer-specific state
            ecb.AddComponent(entity, new LitharchState
            {
                HealTarget = Entity.Null,
                HealTimer = 0f,
                IsHealing = 0,
                SearchTimer = 0f
            });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            ecb.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 2 });

            return entity;
        }

        /// <summary>
        /// Create a Litharch entity using EntityManager (immediate).
        /// Mirrors the ECB overload but calls EntityManager directly so the
        /// returned Entity is a real (non-deferred) handle. The earlier
        /// "build via ECB then Playback" version returned the deferred handle
        /// (Index=-1), which crashed any caller that subsequently asked
        /// EntityManager about it (TrainingSystem.SpawnUnit's rally-point
        /// lookup tripped this for AI-trained Litharchs).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            // Load stats from TechTreeDB (same source as the ECB overload).
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float healRate = DefaultHealRate;
            float healRange = DefaultHealRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Litharch", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.healsPerSecond > 0) healRate = def.healsPerSecond;
                if (def.attackRange > 0) healRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = em.CreateEntity();

            // Core identity
            em.AddComponentData(entity, new PresentationId { Id = PresentationID });
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.AddComponentData(entity, new FactionTag { Value = faction });
            em.AddComponentData(entity, new UnitTag { Class = UnitClass.Support });

            // MovementSystem's query requires DesiredDestination. AIMilitaryManager
            // issues movement via SetComponent<DesiredDestination>; without this
            // baked in, AI-trained Litharchs sat at the spawn point as
            // paperweights. Mirrors Miner.cs:54-58. (task-062 G-3)
            em.AddComponentData(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Litharch-specific tag
            em.AddComponent<LitharchTag>(entity);

            // Stats
            em.AddComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.AddComponentData(entity, new MoveSpeed { Value = speed });
            em.AddComponentData(entity, new Damage { Value = (int)damage });
            em.AddComponentData(entity, new LineOfSight { Radius = los });
            em.AddComponentData(entity, new Radius { Value = 0.5f });
            em.AddComponentData(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            em.AddComponentData(entity, new PopulationCost { Amount = 1 });

            // Targeting
            em.AddComponentData(entity, new Target { Value = Entity.Null });

            // Healer capability
            em.AddComponentData(entity, new CanHeal
            {
                HealRate = healRate,
                HealRange = healRange
            });

            // Healer-specific state
            em.AddComponentData(entity, new LitharchState
            {
                HealTarget = Entity.Null,
                HealTimer = 0f,
                IsHealing = 0,
                SearchTimer = 0f
            });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Magic });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            em.AddComponentData(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 2 });

            return entity;
        }
    }
}