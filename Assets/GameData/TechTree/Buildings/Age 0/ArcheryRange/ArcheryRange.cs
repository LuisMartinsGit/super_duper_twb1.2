// File: Assets/GameData/TechTree/Buildings/Age 0/ArcheryRange/ArcheryRange.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Archery Range building — universal ranged training facility.
    /// Mirrors Barracks layout; trains Archers (and other ranged units listed
    /// in the TechTree "ArcheryRange.trains" array).
    ///
    /// Fix #219: the two Create overloads share one generic CreateInternal via IEntityCreator.
    /// </summary>
    public static class ArcheryRange
    {
        // Default stats (used if TechTreeDB is unavailable).
        private const float DefaultHP = 600f;
        private const float DefaultLoS = 14f;
        private const float DefaultRadius = 0.8f;
        public const int PresentationID = 511;  // Barracks=510; sit next to it.

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => CreateInternal(new EmCreator(em), position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => CreateInternal(new EcbCreator(ecb), position, faction);

        private static Entity CreateInternal<TCreator>(TCreator creator, float3 position, Faction faction)
            where TCreator : struct, IEntityCreator
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechCatalog.TryGetBuilding("ArcheryRange", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = creator.CreateEntity();

            creator.AddComponent(entity, new PresentationId { Id = PresentationID });
            creator.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            creator.AddComponent(entity, new FactionTag { Value = faction });
            creator.AddComponent(entity, new BuildingTag { IsBase = 0 });
            creator.AddComponent<ArcheryRangeTag>(entity);
            creator.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            creator.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Barracks");
            creator.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            creator.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            creator.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            creator.AddBuffer<TrainQueueItem>(entity);
            creator.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            creator.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            creator.AddBuffer<ResearchQueueItem>(entity);

            creator.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            creator.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }
    }
}
