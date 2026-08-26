// Runai Trading Post — numbered lane post (PostNumber assigned by TradingPostSystem).
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
    public static class TradingPost
    {
        /// <summary>
        /// Runai Trading Post — numbered chain node. Max 10 per faction.
        /// PostNumber assigned by TradingPostSystem on construction complete.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = 800f, los = 16f;
            if (TechCatalog.TryGetBuilding("Runai_TradingPost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(BuildingSize));
            em.SetComponentData(entity, new PresentationId { Id = 355 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            // Use BuildingSizeConfig footprint (was previously a 1m circular
            // Radius which left huge walkable corners around the building).
            var gridSize = BuildingSizeConfig.GetSize("Runai_TradingPost");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.SetComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<TradingPostTag>(entity);
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 800f, los = 16f;
            if (TechCatalog.TryGetBuilding("Runai_TradingPost", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 355 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            // Match the EM variant: use the BuildingSizeConfig footprint so
            // PassabilityBuildingSync blocks the rectangular footprint.
            var gridSize = BuildingSizeConfig.GetSize("Runai_TradingPost");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<TradingPostTag>(entity);
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            return entity;
        }
    }
}
