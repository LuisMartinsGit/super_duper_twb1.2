// File: Assets/Scripts/Entities/Units/TradePatrol.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Trade patrol unit — free uncontrollable unit that patrols between two trading posts.
    /// 5 spawned per lane. Uses PatrolTag + PatrolWaypoint for back-and-forth movement.
    /// Auto-engages enemies via TargetingSystem (PatrolTag acts like AttackMoveTag).
    /// </summary>
    public static class TradePatrol
    {
        private const float DefaultHP = 80f;
        private const float DefaultSpeed = 5.0f;
        private const float DefaultDamage = 8f;
        private const float DefaultLoS = 10f;
        private const float DefaultCooldown = 1.5f;
        private const float DefaultRadius = 0.5f;
        private const int PresentationID = 403;

        /// <summary>
        /// Create a trade patrol unit with patrol waypoints between two posts.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction,
            Entity postA, Entity postB, float3 posA, float3 posB)
        {
            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(UnitTag),
                typeof(Health),
                typeof(MoveSpeed),
                typeof(Damage),
                typeof(AttackCooldown),
                typeof(LineOfSight),
                typeof(Target),
                typeof(Radius),
                typeof(PopulationCost),
                typeof(DesiredDestination),
                typeof(PatrolTag),
                typeof(PatrolAgent)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Melee });
            em.SetComponentData(entity, new Health { Value = (int)DefaultHP, Max = (int)DefaultHP });
            em.SetComponentData(entity, new MoveSpeed { Value = DefaultSpeed });
            em.SetComponentData(entity, new Damage { Value = (int)DefaultDamage });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = DefaultCooldown, Timer = 0f });
            em.SetComponentData(entity, new LineOfSight { Radius = DefaultLoS });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new PopulationCost { Amount = 0 });
            em.SetComponentData(entity, new DesiredDestination { Position = posA, Has = 1 });
            em.SetComponentData(entity, new PatrolAgent { Index = 0, Loop = 1, WaitTimer = 0f });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Melee });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            em.AddComponentData(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            // Trade patrol data — links to lane endpoints
            em.AddComponentData(entity, new TradePatrolData { PostA = postA, PostB = postB });

            // Patrol waypoints — back and forth between the two posts
            var waypoints = em.AddBuffer<PatrolWaypoint>(entity);
            waypoints.Add(new PatrolWaypoint { Position = posA, WaitSeconds = 0f });
            waypoints.Add(new PatrolWaypoint { Position = posB, WaitSeconds = 0f });

            return entity;
        }
    }
}
