// Alanthor Siege Yard — trains siege engines.
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
    public static class AlanthorSiegeYard
    {
        /// <summary>
        /// Alanthor Siege Yard — trains Ballista.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_SiegeYard");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius), typeof(ProductionState));
            EntityBuild.Add(em, entity, new PresentationId { Id = 357 });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_SiegeYard");
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.Add(em, entity, default(SiegeYardTag));
            EntityBuild.AddBuffer<ProductionQueueItem>(em, entity);
            EntityBuild.Add(em, entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_SiegeYard").
            EntityBuild.Add(em, entity, default(BuildingUpgradeable));
            return EntityBuild.Done(entity);
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building("Alanthor_SiegeYard");
            float hp = def.hp;
            float los = def.lineOfSight;
            float radius = def.radius;

            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = 357 });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)hp, Max = (int)hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = los });
            var gridSize = BuildingSizeConfig.GetSize("Alanthor_SiegeYard");
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, new ProductionState { Busy = 0, Remaining = 0 });
            EntityBuild.Add(ecb, entity, default(SiegeYardTag));
            EntityBuild.AddBuffer<ProductionQueueItem>(ecb, entity);
            EntityBuild.Add(ecb, entity, new RallyPoint { Position = position + new float3(3f, 0, 3f), Has = 1 });
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            // Lv1-3 ladder (BuildingUpgradeConfig "Alanthor_SiegeYard").
            EntityBuild.Add(ecb, entity, default(BuildingUpgradeable));
            return EntityBuild.Done(entity);
        }
    }
}
