// Feraldis Totem Tower — defensive tower.
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
    public static class TotemTower
    {
        /// <summary>
        /// Feraldis Totem Tower — ranged defense (15u range, 12 dmg, 2.0s CD).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            float hp = 900f, los = 18f, radius = 0.8f;
            if (TechCatalog.TryGetBuilding("Feraldis_Tower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 361 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Tower");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<TotemTowerTag>(entity);
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = 15f, Damage = 12, Cooldown = 2.0f, Timer = 0f, MaxTargets = 1
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            float hp = 900f, los = 18f, radius = 0.8f;
            if (TechCatalog.TryGetBuilding("Feraldis_Tower", out var def))
            { if (def.hp > 0) hp = def.hp; if (def.lineOfSight > 0) los = def.lineOfSight; if (def.radius > 0) radius = def.radius; }

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 361 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Feraldis_Tower");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<TotemTowerTag>(entity);
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = 15f, Damage = 12, Cooldown = 2.0f, Timer = 0f, MaxTargets = 1
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            return entity;
        }
    }
}
