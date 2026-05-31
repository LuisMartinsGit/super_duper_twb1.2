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
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
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
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Caravan using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            creator.AddComponent(entity, new Health { Value = (int)DefaultHP, Max = (int)DefaultHP });
            creator.AddComponent(entity, new MoveSpeed { Value = DefaultSpeed });
            creator.AddComponent(entity, new LineOfSight { Radius = DefaultLoS });
            creator.AddComponent(entity, new Radius { Value = DefaultRadius });
            creator.AddComponent(entity, new PopulationCost { Amount = 0 });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Refinement #3: caravans fight back.
            creator.AddComponent(entity, new Damage { Value = (int)DefaultDamage });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = DefaultAttackCooldown, Timer = 0f });

            // Caravan-specific components (RunaiTraderState added by RunaiTradeHubSystem after creation)
            creator.AddComponent<CaravanTag>(entity);
            creator.AddComponent<NotControllableTag>(entity);
            creator.AddComponent(entity, new LastDamagedByFaction { Value = faction });
            creator.AddComponent(entity, new DamageTypeData { Value = DamageType.Melee });
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
}
