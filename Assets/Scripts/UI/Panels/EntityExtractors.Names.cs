// EntityExtractors.Names.cs
// Display-name and id resolution: tag-component -> building name/id maps,
// PresentationId -> unit display name, and unit-id -> TechTreeDB name lookup.

using Unity.Entities;
using TheWaningBorder.Data;

namespace TheWaningBorder.UI
{
    public static partial class EntityInfoExtractor
    {
        /// <summary>
        /// Resolve a unit-id string (e.g. "Swordsman") to a human-readable
        /// display name. Prefers TechTreeDB.unit.name; falls back to the
        /// id itself when not registered.
        /// </summary>
        /// <summary>
        /// Display name for the selection header UI: building name for
        /// buildings, unit name for everything else. Public wrapper over
        /// the private resolvers below.
        /// </summary>
        public static string GetSelectionDisplayName(Entity entity, EntityManager em)
        {
            // Authoritative: stamped at creation from the id the caller asked
            // for (see UnitFactory / BuildingFactory). The tag-ladder and
            // PresentationId resolvers below are only reached by entities built
            // outside those factories (resource nodes, wall segments, and
            // anything hand-assembled by ScenarioSetup).
            // Cultured renames: a handful of carryover buildings are called
            // something else once a culture is adopted. Checked before the
            // stamped name, which records the Age 0 identity.
            string cultured = CulturedBuildingName(entity, em);
            if (cultured != null) return cultured;

            if (em.HasComponent<DisplayName>(entity))
            {
                var stamped = em.GetComponentData<DisplayName>(entity).Value;
                if (stamped.Length > 0) return stamped.ToString();
            }

            if (em.HasComponent<BuildingTag>(entity)) return GetBuildingName(entity, em);
            return GetUnitName(entity, em);
        }

        /// <summary>
        /// Name for an Age 0 carryover building that its owner's culture
        /// renames (docs/Design/Age_1_Feraldis.md, Age_1_Alanthor.md — a
        /// cultured building is the SAME entity under a new name, not a new
        /// building). Returns null when no rename applies.
        ///
        /// This is the partial stand-in for the full rename layer
        /// (task-cultured-rename-layer-071).
        /// </summary>
        private static string CulturedBuildingName(Entity entity, EntityManager em)
        {
            if (!em.HasComponent<FactionTag>(entity)) return null;

            bool isBarracks = em.HasComponent<BarracksTag>(entity);
            bool isRange = em.HasComponent<ArcheryRangeTag>(entity);
            bool isHut = em.HasComponent<GathererHutTag>(entity);
            if (!isBarracks && !isRange && !isHut) return null;

            var faction = em.GetComponentData<FactionTag>(entity).Value;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);

            if (isBarracks)
            {
                return culture switch
                {
                    Cultures.Feraldis => "War Hall",
                    Cultures.Alanthor => "Garrison",
                    _ => null,
                };
            }

            if (isRange)
            {
                return culture switch
                {
                    Cultures.Feraldis => "Thrower Camp",
                    Cultures.Alanthor => "Practice Range",
                    _ => null,
                };
            }

            // Gatherer's Hut: Feraldis huts ARE Raider Camps (they stop
            // gathering entirely), so the name has to say so.
            return em.HasComponent<RaiderCampTag>(entity) ? "Raider Camp" : null;
        }

        private static string ArmorTypeDisplayName(ArmorType type)
        {
            return type switch
            {
                ArmorType.InfantryLight => "Light Infantry",
                ArmorType.InfantryHeavy => "Heavy Infantry",
                ArmorType.Ranged => "Ranged",
                ArmorType.Cavalry => "Cavalry",
                ArmorType.Structure => "Structure",
                ArmorType.StructureHuman => "Structure",
                _ => type.ToString()
            };
        }

        /// <summary>"+15 vs Cavalry, +10 vs Building" from the BonusVsTags
        /// slots; multi-bit masks list every tag name.</summary>
        private static string BuildBonusText(BonusVsTags bonus)
        {
            var sb = new System.Text.StringBuilder();
            AppendBonusSlot(sb, bonus.Mask0, bonus.Amount0);
            AppendBonusSlot(sb, bonus.Mask1, bonus.Amount1);
            AppendBonusSlot(sb, bonus.Mask2, bonus.Amount2);
            AppendBonusSlot(sb, bonus.Mask3, bonus.Amount3);
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static void AppendBonusSlot(System.Text.StringBuilder sb, uint mask, int amount)
        {
            if (mask == 0 || amount == 0) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(amount > 0 ? "+" : "").Append(amount).Append(" vs ");
            bool first = true;
            foreach (UnitTagBits bit in System.Enum.GetValues(typeof(UnitTagBits)))
            {
                if (bit == UnitTagBits.None || (mask & (uint)bit) == 0) continue;
                if (!first) sb.Append('/');
                sb.Append(bit.ToString());
                first = false;
            }
        }

        private static string ResolveUnitDisplayName(string unitId)
        {
            if (TechCatalog.TryGetUnit(unitId, out var udef)
                && !string.IsNullOrEmpty(udef.name))
                return udef.name;
            return unitId;
        }

        private static string GetBuildingName(Entity entity, EntityManager em)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<ArcheryRangeTag>(entity)) return "Archery Range";
            if (em.HasComponent<GathererHutTag>(entity)) return "Gatherer's Hut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<DepotTag>(entity)) return "Depot";
            if (em.HasComponent<WorkshopTag>(entity)) return "Workshop";
            if (em.HasComponent<ShrineTag>(entity)) return "Shrine of Ahridan";
            if (em.HasComponent<TempleOfRidanTag>(entity)) return "Temple of Ridan";
            if (em.HasComponent<VaultTag>(entity)) return "Vault of Almiérra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "Fiendstone Keep";
            // Display label changed Smelter → Forge per the user's UI request.
            // The ECS tag, building id ("Alanthor_Smelter"), factory, and the
            // ForgeStorage/ForgeConversionSystem pipeline are all unchanged.
            if (em.HasComponent<SmelterTag>(entity)) return "Forge";
            if (em.HasComponent<ReliquaryTag>(entity)) return "The Reliquary";
            if (em.HasComponent<WallHubTag>(entity)) return "Wall Hub";
            if (em.HasComponent<WallTowerTag>(entity)) return "Wall Tower";
            if (em.HasComponent<WallGateTag>(entity)) return "Wall Gate";
            if (em.HasComponent<WallInstanceTag>(entity)) return "Wall";
            if (em.HasComponent<WallSegmentTag>(entity)) return "Wall Segment";
            // Runai culture buildings
            if (em.HasComponent<OutpostTag>(entity)) return "Runai Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Trade Hub";
            if (em.HasComponent<TradingPostTag>(entity)) return "Trading Post";
            if (em.HasComponent<BazaarTag>(entity)) return "Thessara's Bazaar";
            if (em.HasComponent<BazaarWagonTag>(entity)) return "Bazaar Wagon";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Siege Workshop";
            // Alanthor culture buildings
            if (em.HasComponent<WatchTowerTag>(entity)) return "Watch Tower";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Siege Yard";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Royal Stable";
            // Feraldis culture buildings
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Hunting Lodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Logging Station";
            if (em.HasComponent<LonghouseTag>(entity)) return "Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Totem Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Siege Yard";
            // Border faction buildings
            if (em.HasComponent<BorderMainNodeTag>(entity)) return "Veilstone Hive";
            if (em.HasComponent<BorderSubNodeTag>(entity))
            {
                var subType = em.GetComponentData<BorderSubNodeTag>(entity).Type;
                return subType switch
                {
                    BorderSubNodeType.Resource => "Veilstone Wellspring",
                    BorderSubNodeType.Enforcement => "Enforcement Spire",
                    BorderSubNodeType.Suppression => "Suppression Spire",
                    BorderSubNodeType.Restoration => "Restoration Bloom",
                    BorderSubNodeType.Turret => "Veilstone Turret",
                    _ => "Veilstone Node"
                };
            }
            // Tags that had no branch and so rendered as bare "Building".
            if (em.HasComponent<WarbrandFoundryTag>(entity)) return "Warbrand Foundry";
            if (em.HasComponent<ChapelTag>(entity))
            {
                // One creator, twelve sects — name from the sect it serves.
                var sectId = em.GetComponentData<ChapelTag>(entity).SectId;
                return TheWaningBorder.Core.DisplayNames.Prettify(sectId.ToString()) + " Chapel";
            }
            return "Building";
        }

        private static string GetUnitName(Entity entity, EntityManager em)
        {
            // Use PresentationId for precise unit identification
            if (em.HasComponent<PresentationId>(entity))
            {
                int pid = em.GetComponentData<PresentationId>(entity).Id;
                string name = GetUnitNameByPresentationId(pid);
                if (name != null) return name;
            }

            // Legacy fallback for units without PresentationId. Workers
            // (formerly Builder + Miner) now share a single display name
            // — the per-class branches just disambiguate combat units.
            if (em.HasComponent<CanBuild>(entity)) return "Worker";
            if (em.HasComponent<MinerTag>(entity)) return "Worker";

            if (em.HasComponent<UnitTag>(entity))
            {
                var unitTag = em.GetComponentData<UnitTag>(entity);
                return unitTag.Class switch
                {
                    UnitClass.Melee => "Swordsman",
                    UnitClass.Ranged => "Archer",
                    UnitClass.Scout => "Scout",
                    UnitClass.Support => "Litharch",
                    UnitClass.Siege => "Siege Unit",
                    UnitClass.Economy => "Worker",
                    UnitClass.Miner => "Worker",
                    // UnitClass.Magic and any future class: name the class rather
                    // than returning a bare "Unit" (Scholar/Acolyte hit this before
                    // their PIDs were mapped).
                    _ => unitTag.Class.ToString()
                };
            }

            return "Unit";
        }

        /// <summary>
        /// Map PresentationId to display name for all unit types.
        /// Returns null if the ID is not recognized.
        /// </summary>
        private static string GetUnitNameByPresentationId(int pid)
        {
            return pid switch
            {
                // Era 1 core units. PID 200 (former Builder) + 203
                // (former Miner) both render as "Worker" now that the
                // two specialists are unified — existing entities loaded
                // from older saves still display the new name.
                200 => "Worker",
                201 => "Swordsman",
                202 => "Archer",
                203 => "Worker",
                // task-110: Era 1 Archery Range tier units
                205 => "Longbowman",
                206 => "Scout",
                207 => "Litharch",
                210 => "Berserker",
                // Veilstone units
                320 => "Crystalling",
                321 => "Veilstinger",
                322 => "Godsplinter",
                // Runai culture units
                330 => "Spearman",
                331 => "Skirmisher",
                332 => "Raider",
                333 => "Catapult",
                // Alanthor culture units
                334 => "Sentinel",
                // 405 is shared by Tinker and Caravan — PresentationId picks
                // the VISUAL, not the identity. That pair can only be told
                // apart by the DisplayName stamped at creation; this table is
                // the fallback for entities that never went through
                // UnitFactory.
                335 => "Crossbowman",
                336 => "Cataphract",
                337 => "Ballista",
                346 => "Nobleman",
                347 => "Battering Ram",
                348 => "Trebuchet",
                349 => "Outrider",
                // Feraldis culture units
                338 => "Hunter",
                339 => "Warboar Rider",
                340 => "Siege Ram",
                341 => "Raider",
                // Alanthor King's Court additions
                250 => "Ledger",
                251 => "King Lexor",
                // Religious / magic tier
                382 => "Scholar",
                384 => "Acolyte",
                386 => "Iconoclast",
                // New-roster sect unit levers (task-063)
                387 => "Lorekeeper",
                405 => "Tinker",
                406 => "Inquisitor",
                407 => "Warbreaker",
                410 => "Bazaar Wagon",
                // Sect unique units
                370 => "Scar Guard",
                371 => "Golem Autark",
                372 => "Stone Warden",
                373 => "Archivist Adept",
                374 => "Flame Warden",
                375 => "Vault Keeper",
                376 => "Glassmark Arcanist",
                377 => "Judicator",
                378 => "Ashblade",
                379 => "Brandbreaker",
                380 => "Chaincaster",
                381 => "Nullblade",
                _ => null
            };
        }
    }

    public static partial class EntityActionExtractor
    {
        /// <summary>
        /// Map entity to its TechTree building ID using tag components.
        /// </summary>
        private static string GetBuildingId(Entity entity, EntityManager em)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<ArcheryRangeTag>(entity)) return "ArcheryRange";
            if (em.HasComponent<GathererHutTag>(entity)) return "GatherersHut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<ShrineTag>(entity)) return "ShrineOfRidan";
            if (em.HasComponent<TempleOfRidanTag>(entity)) return "TempleOfRidan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";
            if (em.HasComponent<SmelterTag>(entity)) return "Alanthor_Smelter";
            if (em.HasComponent<ReliquaryTag>(entity)) return "Sect_Reliquary";
            // Runai culture buildings
            if (em.HasComponent<OutpostTag>(entity)) return "Runai_Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Runai_TradeHub";
            if (em.HasComponent<TradingPostTag>(entity)) return "Runai_TradingPost";
            if (em.HasComponent<BazaarTag>(entity)) return "ThessarasBazaar";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Runai_SiegeWorkshop";
            // Alanthor culture buildings
            if (em.HasComponent<WatchTowerTag>(entity)) return "Alanthor_Tower";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Alanthor_SiegeYard";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Alanthor_RoyalStable";
            // Feraldis culture buildings
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Feraldis_HuntingLodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Feraldis_LoggingStation";
            if (em.HasComponent<LonghouseTag>(entity)) return "Feraldis_Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Feraldis_Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Feraldis_SiegeYard";
            if (em.HasComponent<WarTotemTag>(entity)) return "Feraldis_WarTotem";
            if (em.HasComponent<PastureTag>(entity)) return "Feraldis_Pasture";
            if (em.HasComponent<MineTag>(entity)) return "Mine";
            // Sect chapels — dynamic building ID based on sect
            if (em.HasComponent<ChapelTag>(entity))
            {
                var chapelTag = em.GetComponentData<ChapelTag>(entity);
                return "Chapel_" + chapelTag.SectId.ToString();
            }
            return null;
        }

        /// <summary>Public accessor for the building-id resolver, used by
        /// <see cref="Panels.BuildingActionLayouts"/> to pick a fixed grid.</summary>
        public static string GetBuildingIdPublic(Entity entity, EntityManager em)
            => GetBuildingId(entity, em);

        /// <summary>Public accessor for the current-resources-as-Cost helper,
        /// used by <see cref="Panels.BuildingActionLayouts"/> for tooltips.</summary>
        public static TheWaningBorder.Core.Cost GetFactionResourcesAsCostPublic(
            EntityManager em, Faction faction)
            => GetFactionResourcesAsCost(em, faction);
    }
}
