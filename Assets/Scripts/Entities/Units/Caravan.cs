// File: Assets/Scripts/Entities/Units/Caravan.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Caravan unit - Runai auto-trade unit.
    /// Travels between TradeHub and Outpost, depositing Supplies on arrival.
    /// Uncontrollable by the player. No attack capability.
    /// Killed caravans drop 50% of carried cargo to the killer's faction.
    /// </summary>
    public static class Caravan
    {
        private const float DefaultHP = 120f;
        private const float DefaultSpeed = 5.6f;
        private const float DefaultLoS = 8f;
        private const float DefaultRadius = 0.4f;
        private const int PresentationID = 401;

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
                typeof(DesiredDestination)
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

            // Caravan-specific components
            em.AddComponent<CaravanTag>(entity);
            em.AddComponentData(entity, new CaravanState
            {
                Origin = Entity.Null,
                Destination = Entity.Null,
                CurrentCargo = 0f,
                MaxCargo = 0f,
                IsReturning = 0,
                EscortEntity = Entity.Null
            });
            em.AddComponentData(entity, new LastDamagedByFaction { Value = faction });
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

            // Caravan-specific components
            ecb.AddComponent<CaravanTag>(entity);
            ecb.AddComponent(entity, new CaravanState
            {
                Origin = Entity.Null,
                Destination = Entity.Null,
                CurrentCargo = 0f,
                MaxCargo = 0f,
                IsReturning = 0,
                EscortEntity = Entity.Null
            });
            ecb.AddComponent(entity, new LastDamagedByFaction { Value = faction });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.InfantryLight });

            return entity;
        }
    }
}
