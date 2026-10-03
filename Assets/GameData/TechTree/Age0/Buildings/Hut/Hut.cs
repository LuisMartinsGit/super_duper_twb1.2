using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Hut building - housing structure.
    /// Provides population capacity only (no resource generation).
    /// Fix #219: EM/ECB share a single generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Hut
    {
        public const int PresentationID = 102;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building("Hut");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = creator.CreateEntity();
            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent<HutTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Hut");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });

            // Design §5.1: Feraldis Houses do not contribute pop — they're raider-spawn buildings.
            // FeraldisPopOverride caps the faction at 200 instantly at age-up.
            if (FactionColors.GetFactionCulture(faction) != Cultures.Feraldis)
                creator.AddComponent(entity, new PopulationProvider { Amount = def.populationProvided });

            // Combat type tags
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            creator.AddComponent<BuildingUpgradeable>(entity);

            // The production queue carries the House's level-ups (one queue
            // for units, research and levels — CommandRouter.MaxProductionQueue).
            creator.AddComponent(entity, new ProductionState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ProductionQueueItem>(entity);

            return entity;
        }
    }
    // HutTag is defined in BuildingComponents.cs (global namespace)
}
