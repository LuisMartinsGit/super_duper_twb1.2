using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Longbowman unit — Alanthor Archery Range Lv 3 tier.
    ///
    /// Very long range and damage but very slow rate of fire and fragile.
    /// Mirrors Archer.cs structure (single CreateInternal through
    /// IEntityCreator) and reuses ArcherState for the ranged-combat state
    /// machine — the Longbowman is a stat profile, not a new behaviour.
    ///
    /// One creator for both recipe ids: "Alanthor_Longbowman" is canonical,
    /// "Longbowman" is kept as an alias for reference stability (pid 205).
    /// </summary>
    public static class Longbowman
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "Alanthor_Longbowman").
        private const int PresentationID = 205;  // Crossbowman=204; sit next to it.

        /// <summary>Create Longbowman using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Longbowman using EntityCommandBuffer for deferred creation.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            // Load stats from the catalog (canonical SO id "Alanthor_Longbowman").

            var def = TechCatalog.Unit("Alanthor_Longbowman");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            float cooldown = def.attackCooldown;
        

            // Projectile profile (design 2026-07-04): trajectory + speed from the SO.
            byte shotTrajectory = ShotTrajectory.High;
            float shotSpeed = 0f;
            var pdef = TechCatalog.Unit("Alanthor_Longbowman");
            if (!string.IsNullOrEmpty(pdef.trajectory)) shotTrajectory = ShotTrajectory.Parse(pdef.trajectory);
            shotSpeed = pdef.projectileSpeed;
        

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Ranged });
            creator.AddComponent<ArcherTag>(entity);          // shared ranged state machine
            creator.AddComponent<LongbowmanTag>(entity);      // task-110 distinguishing tag
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Reuse ArcherState — Longbowman is a stat profile, not a new
            // behaviour. The aim/fire/retreat machine works identically.
            creator.AddComponent(entity, new ArcherState
            {
                Trajectory = shotTrajectory,
                ProjectileSpeed = shotSpeed,
                AimTimer = 0,
                AimTimeRequired = def.aimTime,
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
    // LongbowmanTag is defined in Core/Components/UnitComponents.cs (global namespace)
}
