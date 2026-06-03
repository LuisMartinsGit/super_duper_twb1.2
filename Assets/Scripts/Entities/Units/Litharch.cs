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
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Litharch
    {
        // ═══════════════════════════════════════════════════════════════════════
        // CONSTANTS
        // ═══════════════════════════════════════════════════════════════════════

        public const int PresentationID = 207;

        // Default stats (used if TechTreeDB not available).
        //
        // Damage starts at 0 per Complete.md §3.2 "Warrior priests": the
        // Litharch has no melee attack until the Shrine tech unlocks it.
        // Combined with the Damage<=0 short-circuit in TargetingSystem,
        // this keeps Litharchs from autonomously pursuing and engaging
        // enemies in their LOS — they stay in the back ranks healing.
        private const float DefaultHP = 60f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 0f;
        private const float DefaultLoS = 10f;
        private const float DefaultHealRate = 8f;      // HP healed per second
        private const float DefaultHealRange = 4f;     // Range to heal targets
        private const float DefaultCooldown = 1.5f;    // Attack cooldown (active once damage > 0)

        // ═══════════════════════════════════════════════════════════════════════
        // FACTORY
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Create a Litharch entity using EntityCommandBuffer for deferred creation.
        /// Loads stats from TechTreeDB if available.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        /// <summary>
        /// Create a Litharch entity using EntityManager (immediate).
        /// Calls EntityManager directly so the returned Entity is a real
        /// (non-deferred) handle. The earlier "build via ECB then Playback"
        /// version returned the deferred handle (Index=-1), which crashed any
        /// caller that subsequently asked EntityManager about it
        /// (TrainingSystem.SpawnUnit's rally-point lookup tripped this for
        /// AI-trained Litharchs).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            // Load stats from TechTreeDB
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float healRate = DefaultHealRate;
            float healRange = DefaultHealRange;
            float cooldown = DefaultCooldown;

            if (TechCatalog.TryGetUnit("Litharch", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.healsPerSecond > 0) healRate = def.healsPerSecond;
                if (def.attackRange > 0) healRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            var entity = creator.CreateEntity();

            // Core identity
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Support });

            // MovementSystem's query requires DesiredDestination. AIMilitaryManager
            // issues movement via creator.SetComponent<DesiredDestination>; without
            // this baked in, AI-trained Litharchs sat at the spawn point as
            // paperweights. Mirrors Miner.cs:54-58. (task-062 G-3)
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Litharch-specific tag
            creator.AddComponent<LitharchTag>(entity);

            // Stats
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Targeting
            creator.AddComponent(entity, new Target { Value = Entity.Null });

            // Healer capability
            creator.AddComponent(entity, new CanHeal
            {
                HealRate = healRate,
                HealRange = healRange
            });

            // Healer-specific state
            creator.AddComponent(entity, new LitharchState
            {
                HealTarget = Entity.Null,
                HealTimer = 0f,
                IsHealing = 0,
                SearchTimer = 0f
            });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Magic });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 2 });

            return entity;
        }
    }
}