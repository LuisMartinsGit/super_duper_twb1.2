using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Trebuchet unit - Alanthor culture long-range AoE siege engine
    /// (calculator 2026-08: Siege Yard Lv 3). Outranges every other engine
    /// and every tower, lobs ONE slow high-arc stone that bursts for AoE
    /// radius 6 (AOEShooterData, the old Catapult component), +80 vs
    /// Building — but it MUST UNPACK to fire: TrebuchetState (co-located
    /// TrebuchetComponents.cs) starts packed, TrebuchetDeploySystem flips
    /// Deployed after 3 s standing with a live target, and RangedCombatSystem
    /// refuses to aim/fire while Deployed == 0. Any movement packs it again.
    ///
    /// CatapultTag keeps the single-AOE-stone fire path (the non-catapult
    /// siege branch is a 3-bolt pierce volley) and routes the shot to the
    /// catapult FX presentation; the hang-time formula there gives the lob
    /// its slow 2-3 s flight.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Trebuchet
    {
        // Default stats (calculator: tools/calculator/techtree.json,
        // id "alanthor_siegeyard_trebuchet" — 200 HP / 60 dmg / 6.0 cd /
        // range 12-38 / LoS 30 / speed 2.4 / 50 s train / pop 3 /
        // 320 S + 180 I + 100 V + 20 Vs / defense 0-1-2-0 / AoE 6).
        // Nominal lob speed; in practice the catapult hang-time formula in
        // RangedCombatSystem overrides high-arc CatapultTag shots to a slow
        // 2-3 s flight scaled by range.
        public const int PresentationID = 348;

        /// <summary>Create Trebuchet using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Trebuchet using EntityCommandBuffer for deferred creation.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Alanthor_Trebuchet");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            float cooldown = def.attackCooldown;
            float aoeRadius = def.aoeRadius;
        

            // Projectile profile: HIGH arc lob, slow stone. SO trajectory /
            // projectileSpeed win when authored.
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
            // Single lobbed AOE stone + catapult FX + hang-time flight (see
            // class doc) instead of the 3-bolt pierce volley.
            creator.AddComponent<CatapultTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new MoveSpeed { Value = speed });
            creator.AddComponent(entity, new Damage { Value = (int)damage });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 3 });

            // Siege ranged state — high lob, slow stone.
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

            // Splash — RangedCombatSystem copies this onto every stone as
            // AOEProjectile (the old Catapult pattern).
            creator.AddComponent(entity, new AOEShooterData { Radius = aoeRadius });

            // Deploy state: spawns PACKED — TrebuchetDeploySystem runs the
            // 3 s set-up once it stands with a live target.
            creator.AddComponent(entity, new TrebuchetState { Deployed = 0, Timer = 0f });

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Siege });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 2, Magic = 0 });

            // SO bonus-vs-tags (+80 vs Building). Design canon for this siege
            // unit, so it must survive a missing / ungenerated TechTreeCatalog
            // entry or an unparseable SO list: fall back to a hard +80 vs
            // Building when the parsed component comes back empty (Ballista
            // pattern).
            var bonusVsTags = UnitTagParse.Bonus(def != null ? def.bonusVsTags : null);
            if (bonusVsTags.IsEmpty)
                bonusVsTags = new BonusVsTags
                {
                    Mask0 = (uint)UnitTagBits.Building,
                    Amount0 = 80,
                };
            creator.AddComponent(entity, bonusVsTags);

            return entity;
        }
    }
}
