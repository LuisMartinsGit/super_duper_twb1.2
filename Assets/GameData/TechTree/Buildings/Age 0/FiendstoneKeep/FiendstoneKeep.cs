// Fiendstone Keep — Age 0 choice building (Feraldis-leaning capital).
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
    public static class FiendstoneKeep
    {
        /// <summary>
        /// Create Fiendstone Keep (Feraldis capital).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = 2000f;
            float los = 18f;
            float radius = 2.4f;

            if (TechCatalog.TryGetBuilding("FiendstoneKeep", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            // Reinforced Walls (Age 0 Keep tech): keeps built after the
            // research start with the +20% Max HP bonus (existing keeps are
            // bumped by TechEffectSystem when the research completes).
            var keepResearch = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (keepResearch != null && keepResearch.HasResearched(faction, "ReinforcedWalls"))
                hp *= 1.2f;

            var entity = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(PopulationProvider)
            );

            em.SetComponentData(entity, new PresentationId { Id = 540 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 1 }); // Is a base building
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("FiendstoneKeep");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.SetComponentData(entity, new PopulationProvider { Amount = 20 });

            em.AddComponent<FiendstoneKeepTag>(entity);
            em.AddComponent<ChoiceBuildingTag>(entity);
            // Auto-fire per Age 0 design (Q#3): 4 targets, 20 dmg / 2.0 s, range 30.
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 30f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 4
            });

            // Keep tech ladder (emplacements / towers / walls) researches here.
            em.AddComponentData(entity, new ResearchState { Busy = 0, Remaining = 0 });
            em.AddBuffer<ResearchQueueItem>(entity);

            // Wing slots (choice-building leveling): the Keep levels by
            // building up to three wings — see KeepWingSystem.
            em.AddComponentData(entity, new KeepWings());

            // Combat type tags
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });

            return entity;
        }

        /// <summary>
        /// Create Fiendstone Keep using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 2000f;
            float los = 18f;
            float radius = 2.4f;

            if (TechCatalog.TryGetBuilding("FiendstoneKeep", out var def))
            {
                if (def.hp > 0) hp = def.hp;
                if (def.lineOfSight > 0) los = def.lineOfSight;
                if (def.radius > 0) radius = def.radius;
            }

            var entity = ecb.CreateEntity();

            ecb.AddComponent(entity, new PresentationId { Id = 540 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 1 }); // Is a base building
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("FiendstoneKeep");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent(entity, new PopulationProvider { Amount = 20 });

            ecb.AddComponent<FiendstoneKeepTag>(entity);
            ecb.AddComponent<ChoiceBuildingTag>(entity);
            // Auto-fire per Age 0 design (Q#3): 4 targets, 20 dmg / 2.0 s, range 30.
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 30f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 4
            });

            // Keep tech ladder (emplacements / towers / walls) researches here.
            ecb.AddComponent(entity, new ResearchState { Busy = 0, Remaining = 0 });
            ecb.AddBuffer<ResearchQueueItem>(entity);

            // Wing slots (choice-building leveling): the Keep levels by
            // building up to three wings — see KeepWingSystem.
            ecb.AddComponent(entity, new KeepWings());

            // Combat type tags
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

            return entity;
        }
    }
}
