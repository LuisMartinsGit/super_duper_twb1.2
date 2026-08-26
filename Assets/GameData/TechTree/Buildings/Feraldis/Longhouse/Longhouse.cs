// Feraldis Longhouse — Feraldis HQ; trains units.
//
// Extracted from BuildingFactory (2026-08-12): each building's creation
// code lives with its data, per the TechTree co-location convention.
// BuildingFactory keeps only the id -> recipe dispatch.

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Entities
{
    public static class Longhouse
    {
        /// <summary>
        /// Feraldis Longhouse — batch-trains units. Has BatchTrainingTag.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            // Doc §5.7 #11: cultured Barracks — base 800 × 1.10 = 880 at L1.
            float hp = 880f, los = 14f, radius = 1.8f;
            if (TechCatalog.TryGetBuilding("Feraldis_Longhouse", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(TrainingState));
            em.SetComponentData(entity, new PresentationId { Id = 360 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Longhouse");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });
            em.AddComponent<LonghouseTag>(entity);
            em.AddComponent<BatchTrainingTag>(entity);
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            // Doc §5.7 #11: cultured Barracks — base 800 × 1.10 = 880 at L1.
            float hp = 880f, los = 14f, radius = 1.8f;
            if (TechCatalog.TryGetBuilding("Feraldis_Longhouse", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 360 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Longhouse");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });
            ecb.AddComponent<LonghouseTag>(entity);
            ecb.AddComponent<BatchTrainingTag>(entity);
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }
    }
}
