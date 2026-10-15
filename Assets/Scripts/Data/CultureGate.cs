// CultureGate.cs
// Which culture a unit id belongs to, and whether a faction may field it.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY THIS EXISTS IN THE RUNTIME ASSEMBLY
//
// The rule lived ONLY in the UI (EntityExtractors.Training), so it filtered
// the player's training panel and nothing else. The AI trains from a
// building's raw trains[] list, and an Age 0 Barracks legitimately lists
// Alanthor_* and Feraldis_* ids side by side (see CLAUDE.md -- a cultured
// building is the same entity renamed, so the roster is culture-gated by id
// prefix at RUNTIME).
//
// With no runtime gate the AI trained the whole list. Measured across 13
// matches, with every AI on Alanthor or no culture at all:
//
//     Alanthor_Swordsman   29.2%
//     Feraldis_Spearman    28.7%   <-- nobody was Feraldis
//     Spearman             28.3%
//
// The even three-way split is the tell. SimpleAISystem's composition spreader
// picks whichever trainable unit is furthest below an even share, so a unit
// the faction can never legitimately own is permanently the most
// under-represented thing on the list and gets trained forever.
//
// Presentation may reference Runtime but never the reverse, so the canonical
// rule lives here and the UI forwards to it. Two copies of a rule like this
// drift, and the drift is invisible until it reaches a match log.
// ─────────────────────────────────────────────────────────────────────────

namespace TheWaningBorder.Data
{
    public static class CultureGate
    {
        /// <summary>
        /// The culture a unit id requires, or <see cref="Cultures.None"/> when
        /// any faction may field it.
        /// </summary>
        public static byte RequiredCultureForUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return Cultures.None;
            if (unitId.StartsWith("Alanthor_")) return Cultures.Alanthor;
            if (unitId.StartsWith("Feraldis_")) return Cultures.Feraldis;
            if (unitId.StartsWith("Runai_")) return Cultures.Runai;

            // The capital's Alanthor units carry no culture prefix -- their ids are
            // stable across the factory and the ability catalog -- but they are
            // Alanthor-exclusive.
            if (unitId == "Ledger" || unitId == "King Lexor") return Cultures.Alanthor;

            return Cultures.None;
        }

        /// <summary>
        /// True when a faction of <paramref name="factionCulture"/> may train
        /// <paramref name="unitId"/>.
        ///
        /// A faction with no culture yet (Age 0) may train only the universal
        /// units. It must NOT get the pick of all three rosters just because it
        /// has not chosen -- that is the bug this file documents.
        /// </summary>
        public static bool CanFactionTrain(string unitId, byte factionCulture)
        {
            byte need = RequiredCultureForUnit(unitId);
            return need == Cultures.None || need == factionCulture;
        }

        /// <summary>
        /// True when a universal unit is REPLACED by a culture's own variant,
        /// so only the variant should be offered.
        ///
        /// Feraldis fields its own Spearman (less HP, more attack -- see
        /// docs/Design/Age_1_Feraldis.md), which supersedes the Age 0 one.
        /// </summary>
        public static bool IsSupersededByCulture(string unitId, byte factionCulture)
            => factionCulture == Cultures.Feraldis && (unitId == "Spearman" || unitId == "Worker");

        /// <summary>
        /// The unit a faction of <paramref name="factionCulture"/> actually
        /// trains when <paramref name="unitId"/> is asked for: the culture's
        /// own variant of a superseded unit (Feraldis: Worker → Slave, Spearman
        /// → Feraldis_Spearman), else the id unchanged. Applied in
        /// TrainCommandDirect, so the AI's floors and any stale order still
        /// produce the right unit.
        /// </summary>
        public static string CultureVariant(string unitId, byte factionCulture)
        {
            if (!IsSupersededByCulture(unitId, factionCulture)) return unitId;
            return unitId == "Worker" ? "Feraldis_Slave" : "Feraldis_" + unitId;
        }

        // ── buildings ────────────────────────────────────────────────────
        //
        // The same rule for what a faction may PLACE (2026-10-13). It lived
        // only in the build palette (EntityExtractors.GetRequiredCulture), and
        // that copy missed the Archery Range — an Alanthor building with no
        // culture prefix — so a Feraldis slave was offered it, and nothing in
        // CommandRouter refused the placement for the AI or a lockstep order.
        // The palette and the router both ask THIS now.

        /// <summary>
        /// The culture a building id requires, or <see cref="Cultures.None"/>
        /// when any faction may place it (the Age 0 buildings, both landmarks,
        /// the sect buildings).
        /// </summary>
        public static byte RequiredCultureForBuilding(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return Cultures.None;
            // The gate is shared wall content (Age0/Buildings/Wall/Gate): a
            // palisade takes gates too, so its Alanthor_ id names no culture.
            if (buildingId == "Alanthor_WallGate") return Cultures.None;
            if (buildingId.StartsWith("Alanthor_")) return Cultures.Alanthor;
            if (buildingId.StartsWith("Feraldis_")) return Cultures.Feraldis;
            if (buildingId.StartsWith("Runai_")) return Cultures.Runai;
            // Unprefixed culture buildings. The Archery Range is Alanthor's
            // Age 1 ranged building (Civs/Alanthor/Buildings/ArcheryRange);
            // Thessara's Bazaar is Runai's.
            if (buildingId == "ArcheryRange") return Cultures.Alanthor;
            if (buildingId == "ThessarasBazaar") return Cultures.Runai;
            // Thessara's Crossing is the Runai LANDMARK: placed in Age 0 by a
            // faction with no culture yet, so it gates on no culture (the
            // edition gate, CultureConfig.IsComingSoon, keeps it out of A / B).
            return Cultures.None;
        }

        /// <summary>
        /// Building ids that still have a factory row but are placeable by no
        /// one. The stand-alone Feraldis Longhouse is the cultured Barracks
        /// now (docs/Design/Age_1_Feraldis.md §7.2, 2026-10-13), and the Totem
        /// Tower is retired (§7.7).
        /// </summary>
        public static bool IsRetiredBuilding(string buildingId)
            => buildingId == "Feraldis_Longhouse" || buildingId == "Feraldis_Tower"
               // The Runai redesign (docs/Design/Age_1_Runai.md §4.7, 2026-10-13).
               || buildingId == "Runai_Outpost" || buildingId == "Runai_TradeHub"
               || buildingId == "Runai_TradingPost" || buildingId == "ThessarasBazaar"
               || buildingId == "Runai_Vault" || buildingId == "Runai_VeilsteelFoundry";

        /// <summary>
        /// True when a faction of <paramref name="factionCulture"/> may place
        /// <paramref name="buildingId"/>. A faction with no culture yet may
        /// place only the universal buildings.
        /// </summary>
        public static bool CanFactionBuild(string buildingId, byte factionCulture)
        {
            if (IsRetiredBuilding(buildingId)) return false;
            byte need = RequiredCultureForBuilding(buildingId);
            return need == Cultures.None || need == factionCulture;
        }
    }
}
