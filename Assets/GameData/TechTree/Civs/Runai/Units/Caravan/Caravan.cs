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
        // PresentationID intentionally outside PresentationSpawnSystem's PrefabPaths
        // dictionary so no prefab is instantiated. CaravanVisualSystem builds the
        // procedural desert-traveler GameObject instead. The previous value 401
        // collided with "Procedural/Rock" and spawned rocks under every caravan.
        public const int PresentationID = 405;

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
            var def = TechCatalog.Unit("Runai_Caravan");

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new UnitTag { Class = UnitClass.Economy });
            creator.AddComponent(entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            creator.AddComponent(entity, new MoveSpeed { Value = def.speed });
            creator.AddComponent(entity, new LineOfSight { Radius = def.lineOfSight });
            creator.AddComponent(entity, new Radius { Value = def.radius });
            creator.AddComponent(entity, new PopulationCost { Amount = 0 });
            creator.AddComponent(entity, new DesiredDestination { Position = float3.zero, Has = 0 });

            // Refinement #3: caravans fight back.
            creator.AddComponent(entity, new Damage { Value = (int)def.damage });
            creator.AddComponent(entity, new Target { Value = Entity.Null });
            creator.AddComponent(entity, new AttackCooldown { Cooldown = def.attackCooldown, Timer = 0f });

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
