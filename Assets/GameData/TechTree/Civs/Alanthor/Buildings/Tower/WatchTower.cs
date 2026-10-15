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

            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            EntityBuild.Add(em, entity, new PresentationId { Id = 354 });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Tower");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(WatchTowerTag));
            // Level 1's attack IS the def's attack; L2 / L3 come from
            // def.levels when the upgrade lands (BuildingUpgradeSystem).
            EntityBuild.Add(em, entity, new BuildingRangedAttack
            {
                Range = def.attack.range, Damage = (int)def.attack.damage,
                Cooldown = def.attack.cooldown, Timer = 0f, MaxTargets = def.attack.maxTargets,
            });
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(em, entity, new DamageTypeData { Value = DamageType.Ranged });
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Tower").
            EntityBuild.Add(em, entity, default(BuildingUpgradeable));
            // The garrison (Tower.asset garrisonSlots): foot units go inside
            // and each adds arrows to the tower's volley (WallGarrison).
            EntityBuild.AddBuffer<WallGarrisonSlot>(em, entity);
            EntityBuild.Done(entity);   // one structural change, then fill
            var slots = em.GetBuffer<WallGarrisonSlot>(entity);
            for (int i = 0; i < def.garrisonSlots; i++) slots.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_Tower");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = 354 });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_Tower");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(WatchTowerTag));
            EntityBuild.Add(ecb, entity, new BuildingRangedAttack
            {
                Range = def.attack.range, Damage = (int)def.attack.damage,
                Cooldown = def.attack.cooldown, Timer = 0f, MaxTargets = def.attack.maxTargets,
            });
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.Add(ecb, entity, new DamageTypeData { Value = DamageType.Ranged });
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_Tower").
            EntityBuild.Add(ecb, entity, default(BuildingUpgradeable));
            // The garrison (Tower.asset garrisonSlots) — see the EM overload.
            EntityBuild.AddBuffer<WallGarrisonSlot>(ecb, entity);
            EntityBuild.Done(entity);   // the build's adds, then SetBuffer fills
            var slots = ecb.SetBuffer<WallGarrisonSlot>(entity);
            for (int i = 0; i < def.garrisonSlots; i++) slots.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            return entity;
        }
    }
}
