// Alanthor Smelter (Forge) — passively generates veilsteel (limit 5 per faction).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Alanthor Smelter (Forge) building — passively generates veilsteel
    /// (ForgeConversionSystem: 1/2/3 per 10 s at building Lv1/2/3, no
    /// inputs). Expensive and build-limited to 5 per faction (cap raised
    /// from 1 in the endgame completeness pass — enforced in
    /// CommandRouter.IssuePlaceBuilding and the build-menu extractor) —
    /// output grows through both the Lv1-3 upgrade ladder
    /// (BuildingUpgradeConfig "Alanthor_Smelter") and additional
    /// Smelters. Hosts research (the armour
    /// tech ladders land here) via ResearchState + ResearchQueueItem, same
    /// pattern as the Barracks. ForgeStorage is kept only for its
    /// ConversionTimer field; the iron/veilstone storage is unused since the
    /// supply-chain conversion was removed (directive 2026-07-04).
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class Smelter
    {
        public const int PresentationID = 560;

        // Forge storage defaults

        /// <summary>
        /// Create completed Smelter using EntityManager.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        /// <summary>
        /// Create Smelter using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            var def = TechCatalog.Building("Alanthor_Smelter");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent(entity, new SmelterTag());
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Smelter");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new ForgeStorage
            {
                Iron = 0,
                Veilstone = 0,
                MaxIron = def.maxIron,
                MaxVeilstone = def.maxVeilstone,
                ConversionTimer = 0f
            });

            // Research capability (armour tech ladders research here) —
            // Barracks pattern.
            creator.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ResearchQueueItem>(entity);

            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Smelter") —
            // scales the veilsteel output 1/2/3 per 10 s.
            creator.AddComponent<BuildingUpgradeable>(entity);

            // Combat type tags
            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        /// <summary>
        /// Create Smelter under construction using EntityCommandBuffer.
        /// </summary>
        public static Entity CreateUnderConstruction(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_Smelter");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;
            float buildTime = def.buildTime;

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new SmelterTag());
            ecb.AddComponent(entity, new Health { Value = 1, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Smelter");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new UnderConstruction { Progress = 0f, Total = buildTime });
            ecb.AddComponent(entity, new Buildable { BuildTimeSeconds = buildTime });
            ecb.AddComponent(entity, new ForgeStorage
            {
                Iron = 0,
                Veilstone = 0,
                MaxIron = def.maxIron,
                MaxVeilstone = def.maxVeilstone,
                ConversionTimer = 0f
            });

            // Research capability (armour tech ladders research here) —
            // Barracks pattern.
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Smelter") —
            // scales the veilsteel output 1/2/3 per 10 s.
            ecb.AddComponent<BuildingUpgradeable>(entity);

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
