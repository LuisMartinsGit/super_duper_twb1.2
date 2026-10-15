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
        /// <summary>Shares the mobile Ballista's procedural art (pid 385) — an emplaced
        /// engine is the same machine, bolted down. Its own model can take
        /// this pid's place without touching anything here.</summary>
        public const int PresentationID = Ballista.PresentationID;   // the mobile engine's procedural art
        public const string Id = "Alanthor_EmplacedBallista";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Unit(Id);

            byte shotTrajectory = ShotTrajectory.Flat;
            if (!string.IsNullOrEmpty(def.trajectory)) shotTrajectory = ShotTrajectory.Parse(def.trajectory);

            var entity = EntityBuild.CreateEntity(em);
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new UnitTag { Class = UnitClass.Siege });
            EntityBuild.Add(em, entity, default(ArcherTag));
            EntityBuild.Add(em, entity, default(SiegeTag));
            // Single-shot fire path + the CatapultVisual-driven projectile,
            // exactly as the mobile Ballista uses.
            EntityBuild.Add(em, entity, default(CatapultTag));
            EntityBuild.Add(em, entity, default(BallistaShooterTag));
            EntityBuild.Add(em, entity, default(EmplacedEngineTag));
            // Holds for ever: ClearAllCommands keeps this tag on an engine, so
            // no order can make it chase, retreat or return to a guard post.
            EntityBuild.Add(em, entity, default(HoldPositionTag));
            EntityBuild.Add(em, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, entity, new Damage { Value = (int)def.damage });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = def.lineOfSight });
            EntityBuild.Add(em, entity, new Target { Value = Entity.Null });
            EntityBuild.Add(em, entity, new Radius { Value = def.radius });
            EntityBuild.Add(em, entity, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });
            EntityBuild.Add(em, entity, new ArcherState
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
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.Ranged });
            EntityBuild.Add(em, entity, new DamageTypeData { Value = DamageType.Siege });
            EntityBuild.Add(em, entity, new Defense
            {
                Melee = def.defense.melee, Ranged = def.defense.ranged,
                Siege = def.defense.siege, Magic = def.defense.magic,
            });
            EntityBuild.Add(em, entity, UnitTagParse.Bonus(def.bonusVsTags));
            EntityBuild.Add(em, entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0,
            });
            return EntityBuild.Done(entity);
        }
    }
}
