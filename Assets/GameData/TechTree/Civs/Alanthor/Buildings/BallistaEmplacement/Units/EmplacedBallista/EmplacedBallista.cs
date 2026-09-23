// Emplaced Ballista — the ENGINE half of the Ballista Emplacement
// (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet emplacements).
//
// It is a unit, but it NEVER moves: no MoveSpeed, no DesiredDestination, no
// population cost. Everything that moves a unit reads one of those, so a
// move order simply has nothing to act on — there is no "immobile" branch to
// keep in sync. It is spawned only by EmplacementCrewSystem, never trained.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    public static class EmplacedBallista
    {
        /// <summary>Shares the mobile Ballista's art (pid 338) — an emplaced
        /// engine is the same machine, bolted down. Its own model can take
        /// this pid's place without touching anything here.</summary>
        public const int PresentationID = 338;
        public const string Id = "Alanthor_EmplacedBallista";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Unit(Id);

            byte shotTrajectory = ShotTrajectory.Flat;
            if (!string.IsNullOrEmpty(def.trajectory)) shotTrajectory = ShotTrajectory.Parse(def.trajectory);

            var entity = em.CreateEntity();
            em.AddComponentData(entity, new PresentationId { Id = PresentationID });
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.AddComponentData(entity, new FactionTag { Value = faction });
            em.AddComponentData(entity, new UnitTag { Class = UnitClass.Siege });
            em.AddComponent<ArcherTag>(entity);
            em.AddComponent<SiegeTag>(entity);
            // Single-shot fire path + the CatapultVisual-driven projectile,
            // exactly as the mobile Ballista uses.
            em.AddComponent<CatapultTag>(entity);
            em.AddComponent<EmplacedEngineTag>(entity);
            em.AddComponentData(entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            em.AddComponentData(entity, new Damage { Value = (int)def.damage });
            em.AddComponentData(entity, new LineOfSight { Radius = def.lineOfSight });
            em.AddComponentData(entity, new Target { Value = Entity.Null });
            em.AddComponentData(entity, new Radius { Value = def.radius });
            em.AddComponentData(entity, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });
            em.AddComponentData(entity, new ArcherState
            {
                AimTimer = 0,
                AimTimeRequired = def.aimTime,
                CooldownTimer = 0,
                MinRange = def.minAttackRange,
                MaxRange = def.attackRange,
                IsRetreating = 0,
                IsFiring = 0,
                Trajectory = shotTrajectory,
                ProjectileSpeed = def.projectileSpeed,
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.Ranged });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Siege });
            em.AddComponentData(entity, new Defense
            {
                Melee = def.defense.melee, Ranged = def.defense.ranged,
                Siege = def.defense.siege, Magic = def.defense.magic,
            });
            em.AddComponentData(entity, UnitTagParse.Bonus(def.bonusVsTags));
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0,
            });
            return entity;
        }
    }
}
