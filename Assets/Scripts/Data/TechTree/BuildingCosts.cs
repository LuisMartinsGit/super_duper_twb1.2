// BuildCosts.cs
// Static lookup table for building costs
// Provides quick access to costs without TechTreeDB lookup
// Part of: Data/

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Data
{
    /// <summary>
    /// Static lookup for building construction costs.
    /// Used for quick UI cost display and affordability checks.
    /// For full building data, use TechTreeDB.
    /// </summary>
    public static class BuildCosts
    {
        // ═══════════════════════════════════════════════════════════════════════
        // COST DATABASE
        // ═══════════════════════════════════════════════════════════════════════
        
        private static readonly Dictionary<string, Cost> _byId = new()
        {
            // Era 1 - Core Buildings
            { "Hall",           Cost.Of(supplies: 0) },                             // Starting building
            { "Hut",            Cost.Of(supplies: 80) },                            // Population provider
            { "GatherersHut",   Cost.Of(supplies: 120, iron: 10) },                 // Resource dropoff
            { "Barracks",       Cost.Of(supplies: 220, iron: 40) },                 // Military training
            
            // Era 1 - Religious/Magic Buildings
            { "Shrine",            Cost.Of(supplies: 300, crystal: 100) },              // Shrine of Ahridan (alias)
            { "ShrineOfAhridan",   Cost.Of(supplies: 300, crystal: 100) },              // Shrine of Ahridan (choice building)
            { "TempleOfRidan",     Cost.Of(supplies: 300, crystal: 100) },              // Temple of Ridan (choice building)
            { "Vault",          Cost.Of(supplies: 300, crystal: 100) },              // Vault of Almiérra (alias)
            { "VaultOfAlmierra",Cost.Of(supplies: 300, crystal: 100) },              // Vault of Almiérra

            // Era 1 - Advanced Buildings
            { "Keep",           Cost.Of(supplies: 300, crystal: 100) },              // Fiendstone Keep (alias)
            { "FiendstoneKeep", Cost.Of(supplies: 300, crystal: 100) },              // Fiendstone Keep
            
            // Runai Culture Buildings
            { "Runai_Outpost",           Cost.Of(supplies: 140, iron: 20) },
            { "Runai_TradeHub",          Cost.Of(supplies: 240, iron: 40) },
            { "Runai_TradingPost",       Cost.Of(supplies: 200, iron: 30) },
            { "ThessarasBazaar",            Cost.Of(supplies: 600, iron: 200, crystal: 100) },
            { "Runai_SiegeWorkshop",     Cost.Of(supplies: 320, iron: 140, crystal: 60) },
            { "Runai_Vault",             Cost.Of(supplies: 1500, iron: 250, crystal: 200) },
            { "Runai_VeilsteelFoundry",  Cost.Of(supplies: 450, iron: 120, crystal: 100) },

            // Feraldis Culture Buildings
            { "Feraldis_BeastPen",       Cost.Of(supplies: 150, iron: 30) },
            { "Feraldis_HuntingLodge",   Cost.Of(supplies: 160, iron: 20) },
            { "Feraldis_LoggingStation", Cost.Of(supplies: 160, iron: 20) },
            { "Feraldis_Foundry",        Cost.Of(supplies: 200, iron: 80, crystal: 30) },
            { "Feraldis_Tower",     Cost.Of(supplies: 120, iron: 60) },
            { "Feraldis_Longhouse",      Cost.Of(supplies: 260, iron: 100) },
            { "Feraldis_SiegeYard",      Cost.Of(supplies: 260, iron: 120, crystal: 40) },

            // Alanthor Culture Buildings
            { "KingsCourt",              Cost.Of(supplies: 500, iron: 150, crystal: 50) },
            { "Alanthor_Wall",           Cost.Of(supplies: 50, iron: 20) },
            { "Alanthor_WallTower",      Cost.Of(supplies: 60, iron: 30) },
            { "Alanthor_WallGate",       Cost.Of(supplies: 40, iron: 15) },
            { "Alanthor_Tower",     Cost.Of(supplies: 140, iron: 70) },
            { "Alanthor_Garrison",       Cost.Of(supplies: 220, iron: 90) },
            { "Alanthor_Stable",    Cost.Of(supplies: 260, iron: 120, crystal: 40) },
            { "Alanthor_SiegeYard",      Cost.Of(supplies: 260, iron: 100, crystal: 60) },
            { "Alanthor_Smelter",        Cost.Of(supplies: 220, iron: 100) },
            { "Alanthor_Crucible",       Cost.Of(supplies: 300, crystal: 80, veilsteel: 30) },

            // task-063 phase 2a: chapel resource cost. Adoption RP cost is
            // separate — handled by SectAdoption.OnChapelCompleted, not here.
            // The 12 Chapel_Sect_<id> entries are added programmatically below
            // (one shared cost — Phase 5 polish may differentiate).

            // task-063 phase 1: 12 old Sect_<UniqueBuilding> cost entries
            // removed alongside their creators. Phase 2 reintroduces sect-
            // unique buildings (Reliquary / Workshop Eternal / Oath-Stone / etc.)
            // — costs will land here when those creators are written.
        };

        /// <summary>
        /// Per-chapel material cost (in addition to RP). Shared across all 12
        /// chapels in Phase 2a. Values match the rough scale of Era-2 secondary
        /// buildings — affordability guard is the RP economy, not resources.
        /// </summary>
        private static readonly Cost ChapelMaterialCost = Cost.Of(supplies: 200, crystal: 80);

        // Inject the 12 chapel entries into the dictionary at static-init time
        // so callers can use the same TryGet path as for any other building.
        static BuildCosts()
        {
            for (int i = 0; i < TheWaningBorder.Economy.SectConfig.SectCount; i++)
            {
                string sectId   = TheWaningBorder.Economy.SectConfig.IdAt(i);
                string chapelId = TheWaningBorder.Economy.SectConfig.ChapelIdFor(sectId);
                _byId[chapelId] = ChapelMaterialCost;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Try to get the cost for a building by ID.
        /// </summary>
        /// <param name="id">Building ID (e.g., "Barracks", "Hut")</param>
        /// <param name="cost">Output cost if found</param>
        /// <returns>True if the building was found</returns>
        public static bool TryGet(string id, out Cost cost) => _byId.TryGetValue(id, out cost);
        
        /// <summary>
        /// Get the cost for a building, or zero cost if not found.
        /// </summary>
        /// <param name="id">Building ID</param>
        /// <returns>Cost of the building, or zero cost if not in database</returns>
        public static Cost Get(string id)
        {
            return _byId.TryGetValue(id, out var cost) ? cost : default;
        }
        
        /// <summary>
        /// Check if a building ID exists in the cost database.
        /// </summary>
        public static bool Exists(string id) => _byId.ContainsKey(id);
        
        /// <summary>
        /// Get all registered building IDs.
        /// </summary>
        public static IEnumerable<string> AllBuildingIds => _byId.Keys;
        
        /// <summary>
        /// Register a custom building cost at runtime.
        /// Useful for mods or dynamic content.
        /// </summary>
        public static void Register(string id, Cost cost)
        {
            _byId[id] = cost;
        }
        
        /// <summary>
        /// Register a custom building cost at runtime with individual values.
        /// </summary>
        public static void Register(string id, int supplies = 0, int iron = 0,
                                    int crystal = 0, int veilsteel = 0, int glow = 0)
        {
            _byId[id] = Cost.Of(supplies, iron, crystal, veilsteel, glow);
        }

        /// <summary>
        /// Reverse-map a built building entity to its cost-table ID using the
        /// tag components that BuildingFactory stamped at creation time. Returns
        /// null if no known tag is present (e.g. legacy or sect-unique entities).
        ///
        /// Centralized here so SelfDestructSystem (refund-on-self-destruct),
        /// SectRuinRefundSystem (Ruin Lv I 12% refund), and future cost-aware
        /// readers all share one mapping. Wall-instance entities are mapped to
        /// the per-culture wall ID since BuildCosts keys them per culture.
        /// (task-063 phase 2d)
        /// </summary>
        public static string IdFromEntity(EntityManager em, Entity entity)
        {
            // Era 1 core
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<GathererHutTag>(entity)) return "GatherersHut";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";

            // Era 1 choice
            if (em.HasComponent<ShrineTag>(entity)) return "ShrineOfAhridan";
            if (em.HasComponent<TempleOfRidanTag>(entity)
                || em.HasComponent<TempleTag>(entity)) return "TempleOfRidan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";

            // Runai
            if (em.HasComponent<OutpostTag>(entity)) return "Runai_Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Runai_TradeHub";
            if (em.HasComponent<BazaarTag>(entity)) return "ThessarasBazaar";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Runai_SiegeWorkshop";

            // Alanthor
            if (em.HasComponent<SmelterTag>(entity)) return "Alanthor_Smelter";
            if (em.HasComponent<CrucibleTag>(entity)) return "Alanthor_Crucible";
            if (em.HasComponent<WatchTowerTag>(entity)) return "Alanthor_Tower";
            if (em.HasComponent<GarrisonTag>(entity)) return "Alanthor_Garrison";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Alanthor_Stable";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Alanthor_SiegeYard";

            // Feraldis
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Feraldis_HuntingLodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Feraldis_LoggingStation";
            if (em.HasComponent<WarbrandFoundryTag>(entity)) return "Feraldis_Foundry";
            if (em.HasComponent<LonghouseTag>(entity)) return "Feraldis_Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Feraldis_Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Feraldis_SiegeYard";

            // Walls / wall instances — map to the generic Alanthor wall ID; refund
            // here is small and identical across cultures, so a per-culture branch
            // is overkill until Phase 5 polish.
            if (em.HasComponent<WallTowerTag>(entity)) return "Alanthor_WallTower";
            if (em.HasComponent<WallGateTag>(entity)) return "Alanthor_WallGate";
            if (em.HasComponent<WallInstanceTag>(entity)
                || em.HasComponent<WallTag>(entity)
                || em.HasComponent<WallHubTag>(entity)
                || em.HasComponent<WallSegmentTag>(entity)) return "Alanthor_Wall";

            // Chapels: SectConfig owns their ids; resolve via ChapelTag.SectId.
            if (em.HasComponent<ChapelTag>(entity))
            {
                var sectId = em.GetComponentData<ChapelTag>(entity).SectId.ToString();
                return TheWaningBorder.Economy.SectConfig.ChapelIdFor(sectId);
            }

            return null;
        }

        /// <summary>
        /// Synchronize costs from TechTreeDB (authoritative source).
        /// Call this after TechTreeDB is loaded to override hardcoded defaults.
        /// </summary>
        public static void SyncFromTechTree()
        {
            if (TechTreeDB.Instance == null) return;

            // Copy keys to avoid modifying dictionary during iteration
            var keys = new System.Collections.Generic.List<string>(_byId.Keys);
            foreach (var id in keys)
            {
                if (TechTreeDB.Instance.TryGetBuilding(id, out var def) && def.cost != null)
                {
                    _byId[id] = Cost.Of(
                        supplies: def.cost.Supplies,
                        iron: def.cost.Iron,
                        crystal: def.cost.Crystal,
                        veilsteel: def.cost.Veilsteel,
                        glow: def.cost.Glow
                    );
                }
            }
        }
    }
}