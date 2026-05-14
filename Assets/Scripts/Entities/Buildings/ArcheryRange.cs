// File: Assets/Scripts/Entities/Buildings/ArcheryRange.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Archery Range building — universal ranged training facility.
    /// Mirrors Barracks layout; trains Archers (and other ranged units listed
    /// in the TechTree "ArcheryRange.trains" array).
    /// </summary>
    public static class ArcheryRange
    {
        // Default stats (used if TechTreeDB is unavailable).
        private const float DefaultHP = 600f;
        private const float DefaultLoS = 14f;
        private const float DefaultRadius = 0.8f;
        public const int PresentationID = 511;  // Barracks=510; sit next to it.

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("ArcheryRange", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(ArcheryRangeTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(TrainingState),
                typeof(Radius),
                typeof(BuildingSize)
            );

            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Barracks"); // share Barracks footprint
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });

            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Research capability — kept for parity with Barracks (techs land later).
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = DefaultHP;
            float los = DefaultLoS;
            float radius = DefaultRadius;

            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetBuilding("ArcheryRange", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = PresentationID });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent<ArcheryRangeTag>(entity);
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Barracks");
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent<BuildingUpgradeable>(entity);

            return entity;
        }
    }
}
