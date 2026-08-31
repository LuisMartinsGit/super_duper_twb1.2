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

            // King's Court units carry no culture prefix -- their ids are
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
            => factionCulture == Cultures.Feraldis && unitId == "Spearman";
    }
}
