// File: Assets/GameData/TechTree/Units/Alanthor/Ballista/Ballista.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Ballista unit - Alanthor culture siege weapon (calculator 2026-08:
    /// replaced the Alanthor Catapult; Siege Yard Lv 1). Fires a single
    /// FLAT heavy bolt — no AOE, no lob — with +30 vs Building; slow 4 s
    /// reload. Shots carry CatapultShotTag (via CatapultTag) so the visual
    /// prefab's CatapultVisual arm driver + shot FX stay in charge of the
    /// projectile presentation until dedicated ballista art lands.
    ///
    /// Trained under id "Alanthor_Ballista"; "Alanthor_Catapult" is kept as
    /// a recipe alias so AI build orders / scenarios keep resolving.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Ballista
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "Alanthor_Ballista" — 220 HP / 40 dmg / 4.0 cd / range 6-22 /
        // LoS 26 / speed 3.2 / 38 s train / pop 2 / 180 S + 80 I + 40 V).
        private const float DefaultHP = 220f;
        private const float DefaultSpeed = 3.2f;
        private const float DefaultDamage = 40f;
        private const float DefaultLoS = 26f;
        private const float DefaultMinRange = 6f;
        private const float DefaultMaxRange = 22f;
        private const float DefaultCooldown = 4.0f;
        private const float DefaultAimTime = 1.0f;
        private const float DefaultRadius = 0.8f;
        private const float DefaultProjectileSpeed = 55f;
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

            // Canonical id first; the retired Catapult id keeps working while
            // the JSON fallback catalog still carries it.
            if (!TechCatalog.TryGetUnit("Alanthor_Ballista", out var def))
                TechCatalog.TryGetUnit("Alanthor_Catapult", out def);
            if (def != null)
            {
                if (def.hp > 0) hp = def.hp;
                if (def.speed > 0) speed = def.speed;
                if (def.damage > 0) damage = def.damage;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.minAttackRange > 0) minRange = def.minAttackRange;
                if (def.attackRange > 0) maxRange = def.attackRange;
                if (def.attackCooldown > 0) cooldown = def.attackCooldown;
            }

            // Projectile profile: FLAT bolt (calculator "single-target bolt"),
            // SO trajectory/projectileSpeed win when authored.
            byte shotTrajectory = ShotTrajectory.Flat;
            float shotSpeed = DefaultProjectileSpeed;
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.trajectory)) shotTrajectory = ShotTrajectory.Parse(def.trajectory);
                if (def.projectileSpeed > 0f) shotSpeed = def.projectileSpeed;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent<SiegeTag>(entity);
            // Keeps the single-shot fire path (the non-catapult siege branch
            // in RangedCombatSystem is a 3-bolt volley) and routes the shot
            // to the CatapultVisual-driven presentation.
            creator.AddComponent<CatapultTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });

            // Siege ranged state — flat bolt, crossbow trajectory family.
            creator.AddComponent(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = DefaultAimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0,
                Trajectory = shotTrajectory,
                ProjectileSpeed = shotSpeed,
            });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 2, Magic = 0 });

            // NO AOEShooterData: the calculator ballista is a single-target
            // bolt — the old catapult's bursting AOE stone retired with it.

            // SO bonus-vs-tags (+30 vs Building). The anti-building bonus is
            // design canon for this siege unit, so it must survive a missing /
            // ungenerated TechTreeCatalog entry or an unparseable SO list:
            // fall back to a hard +30 vs Building when the parsed component
            // comes back empty.
            var bonusVsTags = UnitTagParse.Bonus(def != null ? def.bonusVsTags : null);
            if (bonusVsTags.IsEmpty)
                bonusVsTags = new BonusVsTags
                {
                    Mask0 = (uint)UnitTagBits.Building,
                    Amount0 = 30,
                };
            creator.AddComponent(entity, bonusVsTags);

            return entity;
        }
    }
}
