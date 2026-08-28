// AlanthorCatapult.cs
// Alanthor Siege Yard, unit 3 of 4 (Ballista / Battering Ram / Catapult /
// Trebuchet — roster settled 2026-08-27).
//
// This unit exists to split a job the Ballista used to do alone. The two are
// deliberately opposite halves of the siege tier:
//
//   Ballista  — FLAT bolt, single target, no splash, +30 vs Building.
//               Picks one hard thing and drills it.
//   Catapult  — LOBBED stone, AOE radius 3, bonus vs massed INFANTRY.
//               Picks one crowd and deletes it.
//
// Note the Catapult is the ANTI-UNIT piece, not a second wall-breaker: the
// Trebuchet already owns +80 vs Building, and giving the Catapult a building
// bonus too would make the Ballista and Trebuchet redundant with each other.
// Wide area damage, strong against units (design 2026-08-27).
//
// Naming: the class is AlanthorCatapult, not Catapult, because
// TheWaningBorder.Entities.Catapult is already the RUNAI catapult
// (Civs/Runai/Buildings/SiegeWorkshop/Units/Catapult/). The two are separate
// entities with separate ids, stats and prefabs that happen to share a
// player-facing word — see the id note in UnitFactory.
//
// Shots carry CatapultTag so CatapultVisual's arm driver and the lobbed shot
// FX stay in charge of the projectile presentation.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Alanthor Catapult — lobbed area siege, trained at the Siege Yard.
    /// Trained under id "Alanthor_Catapult".
    /// </summary>
    public static class AlanthorCatapult
    {
        // Defaults used when the SO / TechTreeCatalog entry is unavailable.
        // Sits between the Ballista (40 dmg single target) and the Trebuchet
        // (long-range building killer): less punch per hit than the bolt, but
        // it lands on everyone standing near the impact.
        private const int PresentationID = 350;

        /// <summary>Create via EntityManager (immediate).</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create via EntityCommandBuffer (deferred).</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : IEntityCreator
        {
            var def = TechCatalog.Unit("Alanthor_Catapult");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            float cooldown = def.attackCooldown;
            float aoeRadius = def.aoeRadius;
        

            // Projectile profile: LOBBED stone. The arc is the whole point —
            // it is what lets the Catapult fire over its own infantry line.
            byte shotTrajectory = ShotTrajectory.High;
            if (!string.IsNullOrEmpty(def.trajectory)) shotTrajectory = ShotTrajectory.Parse(def.trajectory);
            float shotSpeed = def.projectileSpeed;
        

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Siege });
            creator.AddComponent<ArcherTag>(entity);
            creator.AddComponent<SiegeTag>(entity);
            creator.AddComponent<CatapultTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 2 });

            creator.AddComponent(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = def.aimTime,
                CooldownTimer = 0,
                MinRange = minRange,
                MaxRange = maxRange,
                IsRetreating = 0,
                IsFiring = 0,
                Trajectory = shotTrajectory,
                ProjectileSpeed = shotSpeed,
            });

            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 2, Magic = 0 });

            // The splash IS the unit. Unlike the Ballista this must never be
            // omitted, so it is added unconditionally from the resolved radius.
            creator.AddComponent(entity, new AOEShooterData { Radius = aoeRadius });

            // Anti-infantry, NOT anti-building — see the header note. Falls back
            // to a hard +20 vs Infantry when the SO list is missing or
            // unparseable, so the unit never ships as a plain-damage lobber.
            var bonusVsTags = UnitTagParse.Bonus(def != null ? def.bonusVsTags : null);
            if (bonusVsTags.IsEmpty)
                bonusVsTags = new BonusVsTags
                {
                    Mask0 = (uint)UnitTagBits.Infantry,
                    Amount0 = 20,
                };
            creator.AddComponent(entity, bonusVsTags);

            return entity;
        }
    }
}
