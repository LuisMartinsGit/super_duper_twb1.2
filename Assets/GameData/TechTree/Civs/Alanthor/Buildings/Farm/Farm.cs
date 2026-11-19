// Farm.cs
// THE FARM (Alanthor, Age 1 — docs/Design/Age_1_Alanthor.md § The Farm,
// Unification decision 55 (c)). Built by Workers on the player's own ground,
// never on a resource node, at most Farm.asset maxPerFaction per player
// (BuildingFactory.AtFactionCap). It holds up to garrisonSlots Workers —
// the wall garrison's slots, so the right-click, the Empty button, the
// lockstep GarrisonWall / UngarrisonWall commands and the eject-on-death
// sweep all serve it unchanged — and pays its level's
// suppliesPerWorkerPerMinute for each Worker inside, scaled by the free share
// of its crop ring (FarmRing, FarmField; decision 57). An unfinished or empty
// Farm pays nothing.
//
// LEVELS (decision 57): Farm_Lvl1..3 in this folder, faction-wide like every
// level (BuildingUpgradeable, FactionBuildingLevels; ladder id = BuildingId).
// Every number is on Farm.asset and those level SOs.
//
// ITS OWN HANDS (decision 83, 2026-11-19): a finished Farm with a free slot
// produces one Worker every garrisonWorkerSeconds, free, straight into the
// slot (FarmSystem, while its owner has population room), and the Workers
// that finish building it go inside unless they have something queued
// (BuildingConstructionSystem -> StepInBuilders). Its model is placeholder
// art: the Alanthor Guild's level-1 prefab, on all three levels, until the
// Farm gets its own.

using Unity.Collections;
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
            EntityBuild.Add(ecb, entity, default(FarmField));
            EntityBuild.Add(ecb, entity, default(FarmWorkerClock));
            EntityBuild.Add(ecb, entity, default(BuildingUpgradeable));
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
            EntityBuild.Add(em, entity, default(FarmField));
            EntityBuild.Add(em, entity, default(FarmWorkerClock));
            EntityBuild.Add(em, entity, default(BuildingUpgradeable));
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

        /// <summary>
        /// A Worker that finished building a Farm goes into it (decision 83)
        /// unless it has something else queued: a shift-queued build site or a
        /// queued command.
        /// </summary>
        public static bool HasQueuedAction(EntityManager em, Entity worker)
        {
            if (em.HasBuffer<QueuedBuildSite>(worker) && em.GetBuffer<QueuedBuildSite>(worker).Length > 0) return true;
            return em.HasBuffer<QueuedCommand>(worker) && em.GetBuffer<QueuedCommand>(worker).Length > 0;
        }

        /// <summary>Puts the builders of finished Farms inside them
        /// (decision 83): each pair is a Worker and the Farm it was building.
        /// Structural - call outside any query iteration. A full Farm, or a
        /// Worker with something queued, is left alone.</summary>
        public static void StepInBuilders(EntityManager em, NativeList<Entity> workers, NativeList<Entity> farms)
        {
            for (int i = 0; i < workers.Length; i++)
            {
                var w = workers[i];
                var f = farms[i];
                if (!em.Exists(w) || !em.Exists(f) || HasQueuedAction(em, w)) continue;
                if (em.HasComponent<Health>(w) && em.GetComponentData<Health>(w).Value <= 0) continue;
                if (!WallGarrison.CanGarrison(em, w, f)) continue;
                WallGarrison.Enter(em, w, f);
            }
        }

        /// <summary>Supplies a minute this Farm pays right now: Workers inside
        /// x its level's suppliesPerWorkerPerMinute x the cached free share of
        /// its crop ring (FarmField, per-mille), rounded down; 0 while it is
        /// unfinished. Integer arithmetic only — FarmSystem pays exactly this.
        /// Before the Vault's bonus (FactionEconomy.Add applies that).</summary>
        public static int SuppliesPerMinute(EntityManager em, Entity farm)
        {
            if (!em.Exists(farm) || em.HasComponent<UnderConstruction>(farm)) return 0;
            int free = FarmRing.CachedFreePermille(em, farm);
            if (free <= 0) return 0;
            return SuppliesPerMinute(WallGarrison.OccupantCount(em, farm), PerWorkerPerMinute(em, farm), free);
        }

        /// <summary>The pay formula alone: <paramref name="workers"/> x
        /// <paramref name="perWorker"/> x <paramref name="freePermille"/> / 1000,
        /// rounded down (the placement preview and the AI use it too).</summary>
        public static int SuppliesPerMinute(int workers, int perWorker, int freePermille)
            => (int)((long)workers * perWorker * math.clamp(freePermille, 0, 1000) / 1000);

        /// <summary>Supplies a minute ONE Worker pays in this Farm at its
        /// faction's Farm level (Farm_LvlN.suppliesPerWorkerPerMinute) before
        /// the field scales it.</summary>
        public static int PerWorkerPerMinute(EntityManager em, Entity farm)
        {
            if (!em.HasComponent<FactionTag>(farm)) return 0;
            return PerWorkerPerMinute(em, em.GetComponentData<FactionTag>(farm).Value);
        }

        /// <summary>Supplies a minute one Worker pays at
        /// <paramref name="faction"/>'s Farm level. The Farm is Age 1, so its
        /// level is at least 1; a missing level SO is a data bug, logged once.</summary>
        public static int PerWorkerPerMinute(EntityManager em, Faction faction)
        {
            byte level = TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, BuildingId);
            if (level < 1) level = 1;
            var def = TheWaningBorder.Core.Settings.BuildingUpgradeConfig.LevelDef(Cultures.Alanthor, BuildingId, level);
            if (def != null) return def.suppliesPerWorkerPerMinute;
            if (!_missingLevelLogged)
            {
                _missingLevelLogged = true;
                UnityEngine.Debug.LogError($"[Farm] No Farm_Lvl{level} level SO for {BuildingId} — the Farm pays " +
                    "nothing. Author it in Civs/Alanthor/Buildings/Farm/ and list it in TechTreeCatalog.buildingLevels.");
            }
            return 0;
        }

        private static bool _missingLevelLogged;
    }
}
