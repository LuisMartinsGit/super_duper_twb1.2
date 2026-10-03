// Alanthor Watch Tower — defensive tower.
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
    public static class WatchTower
    {
        /// <summary>
        /// Alanthor Watch Tower — ranged defense; every stat from Tower.asset,
        /// three levels (Age_1_Alanthor.md § Watch Tower levels).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_Tower");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = em.CreateEntity(typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            em.SetComponentData(entity, new PresentationId { Id = 354 });
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new BuildingTag { IsBase = 0 });
            em.SetComponentData(entity, new Health { Value = (int)hp, Max = (int)hp });
            em.SetComponentData(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Tower");
            em.SetComponentData(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            em.AddComponentData(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            em.AddComponent<WatchTowerTag>(entity);
            // Level 1's attack IS the def's attack; L2 / L3 come from
            // def.levels when the upgrade lands (BuildingUpgradeSystem).
            em.AddComponentData(entity, new BuildingRangedAttack
            {
                Range = def.attack.range, Damage = (int)def.attack.damage,
                Cooldown = def.attack.cooldown, Timer = 0f, MaxTargets = def.attack.maxTargets,
            });
            em.AddComponentData(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(entity, new DamageTypeData { Value = DamageType.Ranged });
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Tower").
            em.AddComponent<BuildingUpgradeable>(entity);
            // The garrison (Tower.asset garrisonSlots): foot units go inside
            // and each adds arrows to the tower's volley (WallGarrison).
            var slots = em.AddBuffer<WallGarrisonSlot>(entity);
            for (int i = 0; i < def.garrisonSlots; i++) slots.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_Tower");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new PresentationId { Id = 354 });
            ecb.AddComponent(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            ecb.AddComponent(entity, new FactionTag { Value = faction });
            ecb.AddComponent(entity, new BuildingTag { IsBase = 0 });
            ecb.AddComponent(entity, new Health { Value = (int)hp, Max = (int)hp });
            ecb.AddComponent(entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Tower");
            ecb.AddComponent(entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            ecb.AddComponent(entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            ecb.AddComponent<WatchTowerTag>(entity);
            ecb.AddComponent(entity, new BuildingRangedAttack
            {
                Range = def.attack.range, Damage = (int)def.attack.damage,
                Cooldown = def.attack.cooldown, Timer = 0f, MaxTargets = def.attack.maxTargets,
            });
            ecb.AddComponent(entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Tower").
            ecb.AddComponent<BuildingUpgradeable>(entity);
            // The garrison (Tower.asset garrisonSlots) — see the EM overload.
            var slots = ecb.AddBuffer<WallGarrisonSlot>(entity);
            for (int i = 0; i < def.garrisonSlots; i++) slots.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            return entity;
        }
    }
}
