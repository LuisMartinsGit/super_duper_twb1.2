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
            var def = TechCatalog.Building("FiendstoneKeep");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            // Reinforced Walls (Age 0 Keep tech): keeps built after the
            // research start with the +20% Max HP bonus (existing keeps are
            // bumped by TechEffectSystem when the research completes).
            var keepResearch = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (keepResearch != null && keepResearch.HasResearched(faction, "ReinforcedWalls"))
                hp *= 1.2f;

            var entity = EntityBuild.CreateEntity(em, 
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(LineOfSight),
                typeof(Radius),
                typeof(PopulationProvider)
            );

            EntityBuild.Add(em, entity, new PresentationId { Id = 540 });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 1 }); // Is a base building
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("FiendstoneKeep");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, new PopulationProvider { Amount = 20 });

            EntityBuild.Add(em, entity, default(FiendstoneKeepTag));
            EntityBuild.Add(em, entity, default(ChoiceBuildingTag));
            // Auto-fire per Age 0 design (Q#3): 4 targets, 20 dmg / 2.0 s, range 30.
            EntityBuild.Add(em, entity, new BuildingRangedAttack
            {
                Range = 30f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 4
            });

            // Keep tech ladder (emplacements / towers / walls) researches here.
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);

            // Wing slots (choice-building leveling): the Keep levels by
            // building up to three wings — see KeepWingSystem.
            EntityBuild.Add(em, entity, new KeepWings());

            // Combat type tags
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, entity, new DamageTypeData { Value = DamageType.Ranged });

            return EntityBuild.Done(entity);
        }

        /// <summary>
        /// Create Fiendstone Keep using EntityCommandBuffer for deferred creation.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("FiendstoneKeep");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(ecb);

            EntityBuild.Add(ecb, entity, new PresentationId { Id = 540 });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 1 }); // Is a base building
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("FiendstoneKeep");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, new PopulationProvider { Amount = 20 });

            EntityBuild.Add(ecb, entity, default(FiendstoneKeepTag));
            EntityBuild.Add(ecb, entity, default(ChoiceBuildingTag));
            // Auto-fire per Age 0 design (Q#3): 4 targets, 20 dmg / 2.0 s, range 30.
            EntityBuild.Add(ecb, entity, new BuildingRangedAttack
            {
                Range = 30f, Damage = 20, Cooldown = 2f, Timer = 0f, MaxTargets = 4
            });

            // Keep tech ladder (emplacements / towers / walls) researches here.
            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);

            // Wing slots (choice-building leveling): the Keep levels by
            // building up to three wings — see KeepWingSystem.
            EntityBuild.Add(ecb, entity, new KeepWings());

            // Combat type tags
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, entity, new DamageTypeData { Value = DamageType.Ranged });

            return EntityBuild.Done(entity);
        }
    }
}
