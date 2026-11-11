// Farm.cs
// THE FARM (Alanthor, Age 1 — docs/Design/Age_1_Alanthor.md § The Farm,
// Unification decision 55 (c)). Built by Workers on the player's own ground,
// never on a resource node, at most Farm.asset maxPerFaction per player
// (BuildingFactory.AtFactionCap). It holds up to garrisonSlots Workers —
// the wall garrison's slots, so the right-click, the Empty button, the
// lockstep GarrisonWall / UngarrisonWall commands and the eject-on-death
// sweep all serve it unchanged — and pays suppliesPerWorkerPerMinute for
// each Worker inside (FarmSystem). An unfinished or empty Farm pays nothing.
//
// Every number is on Farm.asset. Its model is placeholder art: the Alanthor
// Guild's level-1 prefab, until the Farm gets its own.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Data;

namespace TheWaningBorder.Entities
{
    public static class Farm
    {
        /// <summary>The Farm's building id (Farm.asset).</summary>
        public const string BuildingId = "Alanthor_Farm";
        /// <summary>Next to the Archery Range (511); its SO says the same.</summary>
        public const int PresentationID = 512;

        public static Entity Create(EntityManager em, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(em, typeof(PresentationId), typeof(LocalTransform), typeof(FactionTag),
                typeof(BuildingTag), typeof(Health), typeof(LineOfSight), typeof(Radius));
            AddCommon(em, entity, position, faction, def);
            EntityBuild.AddBuffer<WallGarrisonSlot>(em, entity);
            EntityBuild.Done(entity);   // one structural change, then fill
            var slots = em.GetBuffer<WallGarrisonSlot>(entity);
            for (int i = 0; i < def.garrisonSlots; i++) slots.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            return entity;
        }

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
        {
            var def = TechCatalog.Building(BuildingId);
            var entity = EntityBuild.CreateEntity(ecb);
            EntityBuild.Add(ecb, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(ecb, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(ecb, entity, new FactionTag { Value = faction });
            EntityBuild.Add(ecb, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(ecb, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(ecb, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(ecb, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(ecb, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(ecb, entity, default(FarmTag));
            EntityBuild.Add(ecb, entity, default(FarmPay));
            EntityBuild.Add(ecb, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
            EntityBuild.AddBuffer<WallGarrisonSlot>(ecb, entity);
            EntityBuild.Done(entity);   // the build's adds, then SetBuffer fills
            var slots = ecb.SetBuffer<WallGarrisonSlot>(entity);
            for (int i = 0; i < def.garrisonSlots; i++) slots.Add(new WallGarrisonSlot { Occupant = Entity.Null });
            return entity;
        }

        private static void AddCommon(EntityManager em, Entity entity, float3 position, Faction faction, BuildingDef def)
        {
            EntityBuild.Add(em, entity, new PresentationId { Id = PresentationID });
            EntityBuild.Add(em, entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            EntityBuild.Add(em, entity, new FactionTag { Value = faction });
            EntityBuild.Add(em, entity, new BuildingTag { IsBase = 0 });
            EntityBuild.Add(em, entity, new Health { Value = (int)def.hp, Max = (int)def.hp });
            EntityBuild.Add(em, entity, new LineOfSight { Radius = def.lineOfSight });
            var gridSize = BuildingSizeConfig.GetSize(BuildingId);
            EntityBuild.Add(em, entity, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(gridSize) });
            EntityBuild.Add(em, entity, new BuildingSize { Width = gridSize.x, Height = gridSize.y });
            EntityBuild.Add(em, entity, default(FarmTag));
            EntityBuild.Add(em, entity, default(FarmPay));
            EntityBuild.Add(em, entity, new ArmorTypeData { Value = ArmorType.StructureHuman });
        }

        /// <summary>True when <paramref name="unit"/> may go into a Farm:
        /// a Worker (WorkerTag), not a hero.</summary>
        public static bool IsFarmhand(EntityManager em, Entity unit)
        {
            if (unit == Entity.Null || !em.Exists(unit)) return false;
            if (!em.HasComponent<UnitTag>(unit) || em.HasComponent<BuildingTag>(unit)) return false;
            if (!em.HasComponent<WorkerTag>(unit)) return false;
            return !em.HasComponent<HeroLevel>(unit);
        }

        /// <summary>Supplies a minute this Farm pays right now: Workers inside
        /// x suppliesPerWorkerPerMinute; 0 while it is unfinished. Before the
        /// Vault's bonus (FactionEconomy.Add applies that).</summary>
        public static int SuppliesPerMinute(EntityManager em, Entity farm)
        {
            if (!em.Exists(farm) || em.HasComponent<UnderConstruction>(farm)) return 0;
            return WallGarrison.OccupantCount(em, farm) * TechCatalog.Building(BuildingId).suppliesPerWorkerPerMinute;
        }
    }
}
