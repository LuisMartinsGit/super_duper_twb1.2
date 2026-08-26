// Turns a TechTree id ("Alanthor_RoyalStable", "Chapel_Sect_Renewal") into the
// name the player sees ("Royal Stable", "Renewal Chapel").
//
// Used by UnitFactory / BuildingFactory to stamp the DisplayName component at
// creation time. Resolution order:
//   1. the catalog's authored display name (BuildingDef.name / UnitDef.name)
//   2. a prettified form of the id itself
// so a building or unit is never nameless, even when it is missing from the
// catalog or was added to a factory before its SO existed.

using System.Text;
using Unity.Collections;
using TheWaningBorder.Data;

namespace TheWaningBorder.Core
{
    public static class DisplayNames
    {
        /// <summary>Unit name as a blittable FixedString, ready for the
        /// DisplayName component. Truncates rather than throwing — a name too
        /// long for the payload should clip in the UI, not kill the spawn.</summary>
        public static FixedString64Bytes ForUnitFixed(string unitId) => ToFixed(ForUnit(unitId));

        /// <summary>Building name as a blittable FixedString. See <see cref="ForUnitFixed"/>.</summary>
        public static FixedString64Bytes ForBuildingFixed(string buildingId)
            => ToFixed(ForBuilding(buildingId));

        private static FixedString64Bytes ToFixed(string s)
        {
            var fs = new FixedString64Bytes();
            fs.CopyFromTruncated(s);
            return fs;
        }

        /// <summary>Culture / grouping prefixes that are context in the id but
        /// noise in the selection header — the player already knows their culture.</summary>
        private static readonly string[] StrippedPrefixes =
        {
            "Chapel_Sect_", "Sect_", "Alanthor_", "Runai_", "Feraldis_", "Border_",
        };

        /// <summary>Display name for a unit id (e.g. "Feraldis_WarboarRider" → "Warboar Rider").</summary>
        public static string ForUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return "Unit";

            if (TechCatalog.TryGetUnit(unitId, out var def) && !string.IsNullOrEmpty(def.name))
                return def.name;

            return Prettify(unitId);
        }

        /// <summary>Display name for a building id (e.g. "Alanthor_SiegeYard" → "Siege Yard").</summary>
        public static string ForBuilding(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return "Building";

            if (TechCatalog.TryGetBuilding(buildingId, out var def) && !string.IsNullOrEmpty(def.name))
                return def.name;

            // Chapels are one creator parameterized by sect, so they are never
            // individually registered in the catalog: "Chapel_Sect_Renewal" →
            // "Renewal Chapel". Without this they all read as plain "Building".
            if (buildingId.StartsWith("Chapel_", System.StringComparison.Ordinal))
                return Prettify(buildingId) + " Chapel";

            return Prettify(buildingId);
        }

        /// <summary>
        /// Strip the culture prefix and split the remaining PascalCase id into
        /// words: "Runai_VeilsteelFoundry" → "Veilsteel Foundry". Acronym runs
        /// stay glued ("KingsCourtHQ" → "Kings Court HQ").
        /// </summary>
        public static string Prettify(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;

            string core = id;
            for (int i = 0; i < StrippedPrefixes.Length; i++)
            {
                if (core.Length > StrippedPrefixes[i].Length &&
                    core.StartsWith(StrippedPrefixes[i], System.StringComparison.Ordinal))
                {
                    core = core.Substring(StrippedPrefixes[i].Length);
                    break;
                }
            }

            core = core.Replace('_', ' ');

            var sb = new StringBuilder(core.Length + 8);
            for (int i = 0; i < core.Length; i++)
            {
                char c = core[i];

                // Insert a space before an uppercase letter that starts a new
                // word: preceded by a lowercase/digit, or the last letter of an
                // acronym run followed by a lowercase ("HQBuilding" → "HQ Building").
                if (i > 0 && char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    char prev = core[i - 1];
                    bool startsWord = !char.IsUpper(prev)
                                   || (i + 1 < core.Length && char.IsLower(core[i + 1]));
                    if (startsWord) sb.Append(' ');
                }

                sb.Append(c);
            }

            return sb.ToString().Trim();
        }
    }
}
