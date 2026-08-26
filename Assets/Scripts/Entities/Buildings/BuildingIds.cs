// BuildingIds.cs
// Entity -> building id, the inverse of BuildingFactory's recipe table.
//
// This tag switch used to be a private helper inside
// UI/Panels/EntityExtractors.Names.cs, exposed through a GetBuildingIdPublic
// wrapper because the AI needed it too. Asking 'which building is this' is
// identity, not presentation -- and routing it through the UI layer meant
// SimpleAISystem named a panel helper to read its own economy.
//
// BuildingFactory maps id -> recipe; this maps entity -> id. They belong
// together, and a new building needs a row in both.

using Unity.Entities;

namespace TheWaningBorder.Entities
{
    /// <summary>Building identity, by the tag an entity carries.</summary>
    public static class BuildingIds
    {
        /// <summary>The tech-tree id of a building entity, or null when it
        /// carries no known building tag.</summary>
        public static string Of(Entity entity, EntityManager em)
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
            if (em.HasComponent<MendingHallTag>(entity)) return "Sect_MendingHall";
            if (em.HasComponent<StoneholdTag>(entity)) return "Sect_Stonehold";
            if (em.HasComponent<MusterYardTag>(entity)) return "Sect_MusterYard";
            if (em.HasComponent<VeilworksTag>(entity)) return "Sect_Veilworks";
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
    }
}
