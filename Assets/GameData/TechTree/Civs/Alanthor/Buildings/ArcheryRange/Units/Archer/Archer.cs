using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Archer unit - ranged infantry.
    /// Attacks from a distance, retreats when enemies get too close.
    ///
    /// Fix #219: the two Create overloads delegate to a single generic
    /// CreateInternal that goes through IEntityCreator. EM and ECB paths
    /// now share one implementation instead of maintaining two 80-line
    /// duplicates.
    /// </summary>
    public static class Archer
    {
        private const int PresentationID = 202;

        /// <summary>Create Archer using EntityManager.</summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>Create Archer using EntityCommandBuffer for deferred creation.</summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Unit("Alanthor_Archer");
            float hp = def.hp;
            float speed = def.speed;
            float damage = def.damage;
            float los = def.lineOfSight;
            float minRange = def.minAttackRange;
            float maxRange = def.attackRange;
            float cooldown = def.attackCooldown;
        

            // Projectile profile (design 2026-07-04): trajectory + speed from the SO.
            byte shotTrajectory = ShotTrajectory.Low;
            float shotSpeed = 0f;
            var pdef = TechCatalog.Unit("Alanthor_Archer");
            if (!string.IsNullOrEmpty(pdef.trajectory)) shotTrajectory = ShotTrajectory.Parse(pdef.trajectory);
            shotSpeed = pdef.projectileSpeed;
        

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
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = cooldown, Timer = 0f });
            creator.AddComponent(entity, new PopulationCost { Amount = 1 });

            // Archer-specific state
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

            // Combat type tags
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            creator.AddComponent(entity, new Defense { Melee = 0, Ranged = 1, Siege = 0, Magic = 0 });

            return entity;
        }
    }
    // ArcherTag and ArcherState are defined in Core/Components/UnitComponents.cs (global namespace)
}
