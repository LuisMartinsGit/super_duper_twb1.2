// File: Assets/Scripts/Entities/Units/Crossbowman.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Crossbowman unit — Era 1 Archery Range L2 tier (task-110).
    ///
    /// Slow but heavy-hitting ranged infantry. Mirrors Archer.cs structure
    /// (single CreateInternal goes through IEntityCreator so EM and ECB paths
    /// share one implementation) and reuses ArcherState for the aim / fire /
    /// retreat state machine — the Crossbowman is a stat profile, not a new
    /// behaviour.
    ///
    /// Distinct from <see cref="AlanthorCrossbowman"/>, which is the Alanthor
    /// cultural variant trained at the Alanthor Practice Range.
    /// </summary>
    public static class Crossbowman
    {
        // Default stats (used if TechTreeDB unavailable). Values mirror the
        // PLAYTEST PLACEHOLDER stat block in TechTree.json + Age_0.md.
        private const float DefaultHP = 70f;
        private const float DefaultSpeed = 3.5f;
        private const float DefaultDamage = 18f;
        private const float DefaultLoS = 22f;
        private const float DefaultMinRange = 6f;
        private const float DefaultMaxRange = 18f;
        private const float DefaultCooldown = 3.0f;
        private const float DefaultAimTime = 0.5f;
        private const int PresentationID = 204;  // Archer=202; sit next to it.

        /// <summary>Create Crossbowman using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Crossbowman using EntityCommandBuffer for deferred creation.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            // Load stats from TechTreeDB (PLAYTEST PLACEHOLDER values land in
            // TechTree.json under id "Crossbowman").
            float hp = DefaultHP;
            float speed = DefaultSpeed;
            float damage = DefaultDamage;
            float los = DefaultLoS;
            float minRange = DefaultMinRange;
            float maxRange = DefaultMaxRange;
            float cooldown = DefaultCooldown;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit("Crossbowman", out var def))
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
            creator.AddComponent<ArcherTag>(entity);          // shared ranged state machine
            creator.AddComponent<CrossbowmanTag>(entity);     // task-110 distinguishing tag
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = 0.5f });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Reuse ArcherState — Crossbowman is a stat profile, not a new
            // behaviour. The aim/fire/retreat machine works identically.
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

            // Combat type tags — same shape as Archer for v1.
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 0, Magic = 0 });

            return entity;
        }
    }
    // CrossbowmanTag is defined in Core/Components/UnitComponents.cs (global namespace)
}
