// File: Assets/Scripts/Entities/Units/CaravanEscort.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Caravan Escort unit - Runai trade guard.
    /// Auto-follows its paired caravan and attacks nearby enemies.
    /// Dies when its caravan is destroyed.
    /// Uncontrollable by the player.
    /// </summary>
    public static class CaravanEscortUnit
    {
        private const float DefaultHP = 110f;
        private const float DefaultSpeed = 6.2f;
        private const float DefaultDamage = 10f;
        private const float DefaultLoS = 10f;
        private const float DefaultAttackCooldown = 1.5f;
        private const float DefaultRadius = 0.45f;
        private const int PresentationID = 402;

        /// <summary>
        /// Create CaravanEscort using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
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
                typeof(DesiredDestination)
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
            em.SetComponentData(entity, new PopulationCost { Amount = 0 });
            em.SetComponentData(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Combat type tags
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Melee });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            em.AddComponentData(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            // Escort-specific components
            em.AddComponent<CaravanEscortTag>(entity);
            em.AddComponentData(entity, new CaravanEscort
            {
                CaravanEntity = Entity.Null
            });

            return entity;
        }

        /// <summary>
        /// Create CaravanEscort using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
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
            ecb.AddComponent(entity, new PopulationCost { Amount = 0 });
            ecb.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Combat type tags
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });
            ecb.AddComponent(entity, new Defense { Melee = 0, Ranged = 0, Siege = 0, Magic = 0 });

            // Escort-specific components
            ecb.AddComponent<CaravanEscortTag>(entity);
            ecb.AddComponent(entity, new CaravanEscort
            {
                CaravanEntity = Entity.Null
            });

            return entity;
        }
    }
}
