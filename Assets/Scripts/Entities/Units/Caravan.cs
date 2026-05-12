// File: Assets/Scripts/Entities/Units/Caravan.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Caravan unit — Runai auto-trade unit.
    /// Travels between trade nodes, depositing Supplies on arrival.
    /// Spec refinement #3 (no separate patrols): caravans fight back when
    /// threatened (Damage + Target + AttackCooldown), are tougher (HP bumped
    /// from 120 to 200), and become numerous as the trade network grows.
    /// PatrolThreatDetectionSystem flips them controllable when an enemy is
    /// within range of their lane.
    /// Killed caravans drop 50% of carried cargo to the killer's faction.
    /// </summary>
    public static class Caravan
    {
        private const float DefaultHP = 200f;        // bumped from 120 — refinement #3 "hard to kill"
        private const float DefaultSpeed = 5.6f;
        private const float DefaultLoS = 12f;        // bumped from 8 so they can spot threats
        private const float DefaultDamage = 6f;      // light counter-attack — refinement #3 "fight back"
        private const float DefaultAttackRange = 1.6f;
        private const float DefaultAttackCooldown = 1.4f;
        private const float DefaultRadius = 0.4f;
        // PresentationID intentionally outside PresentationSpawnSystem's PrefabPaths
        // dictionary so no prefab is instantiated. CaravanVisualSystem builds the
        // procedural desert-traveler GameObject instead. The previous value 401
        // collided with "Procedural/Rock" and spawned rocks under every caravan.
        private const int PresentationID = 405;

        /// <summary>
        /// Create Caravan using EntityManager.
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
                typeof(LineOfSight),
                typeof(Radius),
                typeof(PopulationCost),
                typeof(DesiredDestination),
                typeof(Damage),
                typeof(Target),
                typeof(AttackCooldown)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new UnitTag { Class = UnitClass.Economy });
            em.SetComponentData(entity, new Health { Value = (int)DefaultHP, Max = (int)DefaultHP });
            em.SetComponentData(entity, new MoveSpeed { Value = DefaultSpeed });
            em.SetComponentData(entity, new LineOfSight { Radius = DefaultLoS });
            em.SetComponentData(entity, new Radius { Value = DefaultRadius });
            em.SetComponentData(entity, new PopulationCost { Amount = 0 });
            em.SetComponentData(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Refinement #3: caravans fight back. TargetingSystem fills Target
            // with the nearest enemy in LOS; MeleeCombatSystem (via Damage +
            // AttackCooldown) deals the counter-blow. Movement systems keep
            // pumping DesiredDestination so the caravan continues its route
            // between hostile engagements.
            em.SetComponentData(entity, new Damage { Value = (int)DefaultDamage });
            em.SetComponentData(entity, new Target { Value = Entity.Null });
            em.SetComponentData(entity, new AttackCooldown { Cooldown = DefaultAttackCooldown, Timer = 0f });

            // Caravan-specific components (RunaiTraderState added by RunaiTradeHubSystem after creation)
            em.AddComponent<CaravanTag>(entity);
            // Spawn autonomous — PatrolThreatDetectionSystem strips this tag when
            // an enemy is within range and restores it when the lane is peaceful.
            em.AddComponent<NotControllableTag>(entity);
            em.AddComponentData(entity, new LastDamagedByFaction { Value = faction });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Melee });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }

        /// <summary>
        /// Create Caravan using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            ecb.AddComponent(entity, new Health { Value = (int)DefaultHP, Max = (int)DefaultHP });
            ecb.AddComponent(entity, new MoveSpeed { Value = DefaultSpeed });
            ecb.AddComponent(entity, new LineOfSight { Radius = DefaultLoS });
            ecb.AddComponent(entity, new Radius { Value = DefaultRadius });
            ecb.AddComponent(entity, new PopulationCost { Amount = 0 });
            ecb.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Refinement #3: caravans fight back.
            ecb.AddComponent(entity, new Damage { Value = (int)DefaultDamage });
            ecb.AddComponent(entity, new Target { Value = Entity.Null });
            ecb.AddComponent(entity, new AttackCooldown { Cooldown = DefaultAttackCooldown, Timer = 0f });

            // Caravan-specific components (RunaiTraderState added by RunaiTradeHubSystem after creation)
            ecb.AddComponent<CaravanTag>(entity);
            ecb.AddComponent<NotControllableTag>(entity);
            ecb.AddComponent(entity, new LastDamagedByFaction { Value = faction });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
}
