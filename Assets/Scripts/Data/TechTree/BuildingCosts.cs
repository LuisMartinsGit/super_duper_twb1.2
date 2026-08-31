// BuildingCosts.cs -- holds the BuildCosts table. The file name does not
// match the type on purpose: 30+ design-doc and task links point at this
// path, same reasoning as BuildCommandPannel.cs.
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
            // Starting Hall is spawned for free by PlayerSpawnSystem (doesn't
            // consult this table). Cost applies only to additional Halls a
            // builder places post-age-up — capped at 6 per faction in
            // BuilderCommandPanel.SpawnSelectedBuilding.
            // The claim price (docs/Design/Regions.md §2). A Hall is what
            // takes a territory, so it is the largest single purchase in
            // the game — and the only one that grows the economy.
            { "Hall",           Cost.Of(supplies: 600, iron: 200) },
            { "Hut",            Cost.Of(supplies: 80) },                            // Population provider
            { "GatherersHut",   Cost.Of(supplies: 120, iron: 10) },                 // Gathering support building
            { "Barracks",       Cost.Of(supplies: 220, iron: 40) },                 // Military training
            { "ArcheryRange",   Cost.Of(supplies: 180, iron: 50) },                 // Ranged training

            // Era 1 - Religious/Magic Buildings
            // Balance 2026-07: choice-building costs reduced 30% (were
            // 300/100) alongside the age-up cost cut — docs/Design/Age_0.md.
            // 2026-07-25 techtree pass: 70 Veilstone restored (210 S + 70 V).
            { "Shrine",            Cost.Of(supplies: 210, veilstone: 70) }, // Shrine of Ridan (alias)
            { "ShrineOfRidan",     Cost.Of(supplies: 210, veilstone: 70) }, // Shrine of Ridan (choice building)
            { "ShrineOfAhridan",   Cost.Of(supplies: 210, veilstone: 70) }, // legacy id alias (pre-rename saves/build orders)
            { "TempleOfRidan",     Cost.Of(supplies: 210, veilstone: 70) }, // Temple of Ridan (choice building)
            { "Vault",          Cost.Of(supplies: 210, veilstone: 70) }, // Vault of Almiérra (alias)
            { "VaultOfAlmierra",Cost.Of(supplies: 210, veilstone: 70) }, // Vault of Almiérra

            // Era 1 - Advanced Buildings
            { "Keep",           Cost.Of(supplies: 210, veilstone: 70) }, // Fiendstone Keep (alias)
            { "FiendstoneKeep", Cost.Of(supplies: 210, veilstone: 70) }, // Fiendstone Keep
            
            // Runai Culture Buildings
            { "Runai_Outpost",           Cost.Of(supplies: 140, iron: 20) },
            { "Runai_TradeHub",          Cost.Of(supplies: 240, iron: 40) },
            { "Runai_TradingPost",       Cost.Of(supplies: 200, iron: 30) },
            { "ThessarasBazaar",            Cost.Of(supplies: 600, iron: 200, veilstone: 100) },
            { "Runai_SiegeWorkshop",     Cost.Of(supplies: 320, iron: 140, veilstone: 60) },
            { "Runai_Vault",             Cost.Of(supplies: 1500, iron: 250, veilstone: 200) },
            { "Runai_VeilsteelFoundry",  Cost.Of(supplies: 450, iron: 120, veilstone: 100) },

            // Feraldis Culture Buildings
            { "Feraldis_BeastPen",       Cost.Of(supplies: 150, iron: 30) },
            { "Feraldis_HuntingLodge",   Cost.Of(supplies: 160, iron: 20) },
            { "Feraldis_LoggingStation", Cost.Of(supplies: 160, iron: 20) },
            { "Feraldis_Foundry",        Cost.Of(supplies: 200, iron: 80, veilstone: 30) },
            { "Feraldis_Tower",     Cost.Of(supplies: 120, iron: 60) },
            { "Feraldis_Longhouse",      Cost.Of(supplies: 260, iron: 100) },
            { "Feraldis_SiegeYard",      Cost.Of(supplies: 260, iron: 120, veilstone: 40) },
            // Cheap on purpose — the totem's real cost is the blood you had
            // to spill on the spot before you could plant it.
            { "Feraldis_WarTotem",       Cost.Of(supplies: 60, iron: 20) },
            { "Feraldis_Pasture",        Cost.Of(supplies: 200, iron: 60) },
            { "Feraldis_HallOfAxes",     Cost.Of(supplies: 180, iron: 50) },
            // SUPPLIES ONLY, deliberately. The Mine is the bootstrap for
            // ore income, so charging ore for it is circular — and for
            // Feraldis (whose Workers cannot gather at all) it was a hard
            // deadlock: the 2026-08-05 match had both Feraldis AIs sitting
            // on 13k-23k supplies and ZERO iron, unable to build anything.
            // Ore extractors are priced in IRON first (Regions.md §4,
            // 2026-08-30): supply-priced they duelled the 600-supply Hall for
            // the famine currency and expansion stopped — six of eight batch
            // matches with zero claims. Iron is the currency that piles up.
            { "Mine",                    Cost.Of(supplies: 90, iron: 140) },
            { "VeilstoneMine",           Cost.Of(supplies: 90, iron: 160) },
            { "Alanthor_Sawyer",         Cost.Of(supplies: 150, iron: 40) },

            // Alanthor Culture Buildings
            { "KingsCourt",              Cost.Of(supplies: 500, iron: 150, veilstone: 50) },
            { "Alanthor_Wall",           Cost.Of(supplies: 50, iron: 20) },
            { "Alanthor_WallTower",      Cost.Of(supplies: 60, iron: 30) },
            { "Alanthor_WallGate",       Cost.Of(supplies: 40, iron: 15) },
            { "Alanthor_Tower",     Cost.Of(supplies: 140, iron: 70) },
            // Alanthor_PracticeRange removed — the Practice Range is the
            // LEVELED Archery Range, not a separate placeable building.
            { "Alanthor_SiegeYard",      Cost.Of(supplies: 260, iron: 100, veilstone: 60) },
            // Forge: deliberately steep — it passively generates veilsteel with
            // no inputs and is build-limited to 1 per faction (directive 2026-07-04).
            // The Crucible was deleted (calculator 2026-08); the Smelter absorbs
            // its veilsteel-engine role via the Lv1-3 upgrade ladder.
            { "Alanthor_Smelter",        Cost.Of(supplies: 240, iron: 320) },
            { "Alanthor_RoyalStable",    Cost.Of(supplies: 220, iron: 80) },

            // Sect buildings — one per sect, unlocked by adopting that sect,
            // capped at 5 per faction (SectBuilding.CapPerFaction). Priced as a
            // mid-tier production building: cheap enough that a sect you commit
            // to is worth building out, dear enough that five of them is a real
            // investment rather than a formality.
            { "Sect_Reliquary",          Cost.Of(supplies: 300, iron: 120, veilstone: 40) },
            { "Sect_MendingHall",        Cost.Of(supplies: 260, iron: 90,  veilstone: 30) },
            // Stonehold is the tankiest non-Hall structure in the game, so it
            // pays for that in iron rather than being the cheapest wall available.
            { "Sect_Stonehold",          Cost.Of(supplies: 280, iron: 160, veilstone: 30) },
            { "Sect_Veilworks",          Cost.Of(supplies: 300, iron: 110, veilstone: 60) },
            // Muster Yard is a working yard rather than a hall: cheapest of
            // the sect buildings in supplies, heaviest in iron, because what
            // it sells is gear.
            { "Sect_MusterYard",         Cost.Of(supplies: 250, iron: 150, veilstone: 30) },

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
        /// chapels. Now demands Iron as well as Supplies + Veilstone so adoption
        /// matters as an economic commitment, not just an RP spend.
        /// </summary>
        public static readonly Cost ChapelMaterialCost =
            Cost.Of(supplies: 250, veilstone: 100, iron: 60);

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
                                    int veilstone = 0, int veilsteel = 0, int glow = 0)
        {
            _byId[id] = Cost.Of(supplies, iron, veilstone, veilsteel, glow);
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
            if (em.HasComponent<ShrineTag>(entity)) return "ShrineOfRidan";
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
            if (em.HasComponent<ReliquaryTag>(entity)) return "Sect_Reliquary";
            if (em.HasComponent<MendingHallTag>(entity)) return "Sect_MendingHall";
            if (em.HasComponent<StoneholdTag>(entity)) return "Sect_Stonehold";
            if (em.HasComponent<VeilworksTag>(entity)) return "Sect_Veilworks";
            if (em.HasComponent<MusterYardTag>(entity)) return "Sect_MusterYard";
            if (em.HasComponent<WatchTowerTag>(entity)) return "Alanthor_Tower";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Alanthor_SiegeYard";

            // Feraldis
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Feraldis_HuntingLodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Feraldis_LoggingStation";
            if (em.HasComponent<WarbrandFoundryTag>(entity)) return "Feraldis_Foundry";
            if (em.HasComponent<LonghouseTag>(entity)) return "Feraldis_Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Feraldis_Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Feraldis_SiegeYard";
            if (em.HasComponent<WarTotemTag>(entity)) return "Feraldis_WarTotem";
            if (em.HasComponent<PastureTag>(entity)) return "Feraldis_Pasture";
            if (em.HasComponent<MineTag>(entity)) return "Mine";
            if (em.HasComponent<VeilstoneMineTag>(entity)) return "VeilstoneMine";
            if (em.HasComponent<SawyerTag>(entity)) return "Alanthor_Sawyer";

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
            if (!TechCatalog.IsReady) return;

            // Copy keys to avoid modifying dictionary during iteration
            var keys = new System.Collections.Generic.List<string>(_byId.Keys);
            foreach (var id in keys)
            {
                if (TechCatalog.TryGetBuilding(id, out var def) && def.cost != null)
                {
                    _byId[id] = Cost.Of(
                        supplies: def.cost.Supplies,
                        iron: def.cost.Iron,
                        veilstone: def.cost.Veilstone,
                        veilsteel: def.cost.Veilsteel
                    );
                }
            }
        }
    }
}