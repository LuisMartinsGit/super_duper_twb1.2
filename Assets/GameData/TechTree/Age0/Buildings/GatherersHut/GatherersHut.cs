using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// GatherersHut building - generates passive Supplies income.
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class GatherersHut
    {
        private const int PresentationID = 101;

        /// <summary>
        /// Create GatherersHut using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create GatherersHut using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building("GatherersHut");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent(entity, new GathererHutTag());
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("GatherersHut");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            // NO SuppliesIncome and NO coverage readout. The hut does not earn
            // its own keep any more: docs/Design/Regions.md §4 makes the
            // TERRITORY the unit of income and the hut a flat boost to the
            // territory it stands in, paid by TerritoryIncomeSystem. Leaving
            // SuppliesIncome on the entity would have it paid twice — once by
            // ApplySuppliesIncomeSystem and again in the territory tick.

            // Research host: the hut offers the Guild Survey techs
            // (GatherersHut.asset research list) — the research UI only
            // surfaces for entities carrying ResearchState.
            creator.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ResearchQueueItem>(entity);

            // Combat type tags
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Alanthor "Guild" level ladder (L1-L3). BuildingCultureAutoLevelSystem
            // bumps this to L1 at age-up; the player upgrades L2/L3 manually.
            creator.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }

        /// <summary>
        /// Create GatherersHut under construction using EntityCommandBuffer.
        /// </summary>
        public static Entity CreateUnderConstruction(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("GatherersHut");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;
            float buildTime = def.buildTime;
            // Note: BuildingDef doesn't have buildTime, using default

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new GathererHutTag());
            ecb.AddComponent(entity, new Health { Value = 1, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("GatherersHut");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new UnderConstruction { Progress = 0f, Total = buildTime });
            ecb.AddComponent(entity, new Buildable { BuildTimeSeconds = buildTime });

            // Research host (Guild Surveys) — see the completed-hut factory.
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Guild level ladder marker (see completed factory). Harmless while
            // UnderConstruction — both upgrade systems skip that state.
            ecb.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }
    }
}