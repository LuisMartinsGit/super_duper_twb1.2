// File: Assets/Scripts/Entities/Units/Creature.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hostile NPC creature - wanders near spawn, attacks all factions.
    /// Uses Faction.White so existing targeting treats it as enemy to everyone.
    /// When killed, leaves a cadaver mineable for crystal.
    /// </summary>
    public static class Creature
    {
        private const float DefaultHP = 400f;
        private const float DefaultSpeed = 2.5f;
        private const float DefaultDamage = 25f;
        private const float DefaultLoS = 12f;
        private const float DefaultAttackCooldown = 1.8f;
        private const float DefaultRadius = 0.6f;
        private const float DefaultWanderInterval = 8f;
        private const float DefaultWanderRadius = 8f;
        private const int PresentationID = 300;

        /// <summary>
        /// Create Creature using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction = Faction.White)
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
                typeof(CreatureTag),
                typeof(CreatureState),
                typeof(GuardPoint)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Melee });
            em.SetComponentData(entity, new Health { Value = (int)DefaultHP, Max = (int)DefaultHP });
            em.SetComponentData(entity, new MoveSpeed { Value = DefaultSpeed });
            em.SetComponentData(entity, new Damage { Value = (int)DefaultDamage });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = DefaultAttackCooldown, Timer = 0f });
            em.SetComponentData(entity, new LineOfSight { Radius = DefaultLoS });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new CreatureState
            {
                HomePosition = position,
                WanderTimer = DefaultWanderInterval,
                WanderInterval = DefaultWanderInterval,
                WanderRadius = DefaultWanderRadius
            });
            em.SetComponentData(entity, new GuardPoint
            {
                Position = position,
                Has = 1
            });

            return entity;
        }

        /// <summary>
        /// Create Creature using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction = Faction.White)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Melee });
            ecb.AddComponent(entity, new Health { Value = (int)DefaultHP, Max = (int)DefaultHP });
            ecb.AddComponent(entity, new MoveSpeed { Value = DefaultSpeed });
            ecb.AddComponent(entity, new Damage { Value = (int)DefaultDamage });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = DefaultAttackCooldown, Timer = 0f });
            ecb.AddComponent(entity, new LineOfSight { Radius = DefaultLoS });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent<CreatureTag>(entity);
            ecb.AddComponent(entity, new CreatureState
            {
                HomePosition = position,
                WanderTimer = DefaultWanderInterval,
                WanderInterval = DefaultWanderInterval,
                WanderRadius = DefaultWanderRadius
            });
            ecb.AddComponent(entity, new GuardPoint
            {
                Position = position,
                Has = 1
            });

            return entity;
        }
    }
}
