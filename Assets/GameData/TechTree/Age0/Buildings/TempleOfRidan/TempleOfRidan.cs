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
            var def = TechCatalog.Building("TempleOfRidan");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(em, 
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(BuildingSize)
            );

            EntityBuild.Add(em, entity, new PresentationId { Id = 521 });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });
            // Use BuildingSizeConfig so PassabilityBuildingSync blocks the full
            // 4x4 footprint instead of falling back to the legacy circular Radius
            // (which left walkable corners around the Temple).
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            EntityBuild.Add(em, entity, default(TempleOfRidanTag));
            EntityBuild.Add(em, entity, default(TempleTag)); // legacy alias, still read by queries

            // Glow storage lives on the Temple per spec refinement #2
            // (the standalone ShardrootReliquary was deleted).
            EntityBuild.Add(em, entity, new ShardrootStored { Amount = 0 });
            EntityBuild.Add(em, entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            // Research state for sect techs
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);

            // Initialize 6 empty chapel slots — one per ground decal around the
            // Temple. Six matches SectConfig.MaxAdoptedSects (= the design cap).
            EntityBuild.AddBuffer<TempleChapelSlot>(em, entity);

            // Combat type tags
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Commit the build (one structural change), THEN fill the slots.
            EntityBuild.Done(entity);
            var slotBuffer = em.GetBuffer<TempleChapelSlot>(entity);
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

            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("TempleOfRidan");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(ecb);

            EntityBuild.Add(ecb, entity, new PresentationId { Id = 521 });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });
            // BuildingSize so PassabilityBuildingSync blocks the full footprint.
            var gridSize = BuildingSizeConfig.GetSize("TempleOfRidan");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });

            EntityBuild.Add(ecb, entity, default(TempleOfRidanTag));
            EntityBuild.Add(ecb, entity, default(TempleTag));

            // Glow storage lives on the Temple per spec refinement #2.
            EntityBuild.Add(ecb, entity, new ShardrootStored { Amount = 0 });
            EntityBuild.Add(ecb, entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });

            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);

            // Initialize 6 empty chapel slots — matches SectConfig.MaxAdoptedSects.
            EntityBuild.AddBuffer<TempleChapelSlot>(ecb, entity);
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });

            // Commit the build, THEN fill the slots (SetBuffer plays back
            // after the build's adds).
            EntityBuild.Done(entity);
            var slotBuffer = ecb.SetBuffer<TempleChapelSlot>(entity);
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

            return entity;
        }
    }
}
