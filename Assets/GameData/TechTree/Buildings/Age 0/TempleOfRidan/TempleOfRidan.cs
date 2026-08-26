// Temple of Ridan — Age 2+ religion hub; carries the 6 chapel slots.
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
    public static class TempleOfRidan
    {
        /// <summary>
        /// Create Temple of Ridan — available to ALL cultures at Era 2.
        /// Has 8 BFME2-style expansion slots for sect chapels.
        /// Houses all sect unit training and tech research.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = 1500f;
            float los = 18f;
            float radius = 2.5f;

            if (TechCatalog.TryGetBuilding("TempleOfRidan", out var def))
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
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize),
                typeof(TrainingState)
            );

            em.SetComponentData(entity, new PresentationId { Id = 521 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            // Use BuildingSizeConfig so PassabilityBuildingSync blocks the full
            // 4x4 footprint instead of falling back to the legacy circular Radius
            // (which left walkable corners around the Temple).
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new TrainingState { Busy = 0, Remaining = 0 });

            em.AddComponent<TempleOfRidanTag>(entity);
            em.AddComponent<TempleTag>(entity); // Keep legacy tag for TempleUpgradeSystem compatibility
            em.AddComponentData(entity, new TempleLevel { Level = 1 });

            // Glow storage lives on the Temple per spec refinement #2
            // (the standalone GlowReliquary was deleted).
            em.AddComponentData(entity, new GlowStored { Amount = 0 });
            em.AddBuffer<TrainQueueItem>(entity);
            em.AddComponentData(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Research state for sect techs
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Initialize 6 empty chapel slots — one per ground decal around the
            // Temple. Six matches SectConfig.MaxAdoptedSects (= the design cap).
            var slotBuffer = em.AddBuffer<TempleChapelSlot>(entity);
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
            {
                slotBuffer.Add(new TempleChapelSlot
                {
                    Chapel = Entity.Null,
                    SectId = default,
                    State = 0,
                    BuildProgress = 0f,
                    BuildTime = 0f
                });
            }

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 1500f;
            float los = 18f;
            float radius = 2.5f;

            if (TechCatalog.TryGetBuilding("TempleOfRidan", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 521 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            // BuildingSize so PassabilityBuildingSync blocks the full footprint.
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new TrainingState { Busy = 0, Remaining = 0 });

            ecb.AddComponent<TempleOfRidanTag>(entity);
            ecb.AddComponent<TempleTag>(entity);
            ecb.AddComponent(entity, new TempleLevel { Level = 1 });

            // Glow storage lives on the Temple per spec refinement #2.
            ecb.AddComponent(entity, new GlowStored { Amount = 0 });
            ecb.AddBuffer<TrainQueueItem>(entity);
            ecb.AddComponent(entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Initialize 6 empty chapel slots — matches SectConfig.MaxAdoptedSects.
            var slotBuffer = ecb.AddBuffer<TempleChapelSlot>(entity);
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
            {
                slotBuffer.Add(new TempleChapelSlot
                {
                    Chapel = Entity.Null,
                    SectId = default,
                    State = 0,
                    BuildProgress = 0f,
                    BuildTime = 0f
                });
            }

            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            return entity;
        }
    }
}
