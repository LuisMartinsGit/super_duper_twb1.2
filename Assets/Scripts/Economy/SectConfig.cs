// SectConfig.cs
// Static identity table for the 12 sects of the redesigned religion system.
// Every sect string ID, cluster mapping, index, and cost-rule lives here;
// effect data lives on SectDefinition ScriptableObjects loaded by SectDatabase.
//
// Phase 1 (task-063): only the data layer + index/cluster lookups are wired.
// Effect bodies are added per-lever in Phase 2.
//
// Location: Assets/Scripts/Economy/SectConfig.cs

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Cultural cluster a sect belongs to. Drives same-culture vs cross-culture
    /// adoption-cost rules (2 vs 3 RP) and visual / lore grouping in the
    /// adoption UI. The faction's *current* culture is read from
    /// <c>FactionProgress.Culture</c> (a separate axis — a Runaii faction can
    /// adopt Feraldis sects, just at higher RP cost).
    /// </summary>
    public enum SectCluster : byte
    {
        Alanthor = 0,
        Runaii   = 1,
        Feraldis = 2,
    }

    /// <summary>
    /// Which of the four levers a sect's effect lives on. Each lever has its
    /// own independent upgrade level (0 = not yet purchased, 1/2/3 = Lv I/II/III).
    /// Adoption grants Lv I on every lever automatically — see SectAdoption.
    /// </summary>
    public enum SectLeverKind : byte
    {
        Passive     = 0,
        Building    = 1,
        Unit        = 2,
        ActivePower = 3,
    }

    /// <summary>
    /// Static metadata + cost-schedule constants for the 12-sect religion system.
    /// All sect-string lookups go through here so renames stay greppable.
    /// </summary>
    public static class SectConfig
    {
        // ═══════════════════════════════════════════════════════════════════
        // SECT IDS — the 12-sect roster (3 clusters × 4 sects)
        // ═══════════════════════════════════════════════════════════════════

        // Alanthor — Recovery / Fortification / Craftsman knowledge
        public const string Antiquity   = "Sect_Antiquity";
        public const string Renewal     = "Sect_Renewal";
        public const string Fortitude   = "Sect_Fortitude";
        public const string Reclamation = "Sect_Reclamation";

        // Runaii — Doctrine / Secrecy / Crystal veneration
        public const string Silence     = "Sect_Silence";
        public const string Justice     = "Sect_Justice";
        public const string Veneration  = "Sect_Veneration";
        public const string Witness     = "Sect_Witness";

        // Feraldis — Industry / Profanity / Vengeance
        public const string War   = "Sect_War";
        public const string Ash   = "Sect_Ash";
        public const string Ruin  = "Sect_Ruin";
        public const string Wrath = "Sect_Wrath";

        /// <summary>Total number of sects (constant — design rule).</summary>
        public const int SectCount = 12;

        // ─── Chapel building IDs ────────────────────────────────────────────
        // Adoption mechanism: building a chapel inside the Temple's 6-slot
        // system grants the matching sect. One chapel id per sect.
        // BuildingFactory registers each as "Chapel_<SectId>".

        public static string ChapelIdFor(string sectId) => "Chapel_" + sectId;

        /// <summary>True if the building id is one of the 12 sect chapels.</summary>
        public static bool IsChapelId(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return false;
            if (!buildingId.StartsWith("Chapel_")) return false;
            return IsKnownSect(buildingId.Substring("Chapel_".Length));
        }

        /// <summary>Inverse — extracts the sect id from a chapel building id, or null.</summary>
        public static string SectIdFromChapelId(string chapelBuildingId)
        {
            if (!IsChapelId(chapelBuildingId)) return null;
            return chapelBuildingId.Substring("Chapel_".Length);
        }

        /// <summary>
        /// Stable index → string-id table. Sizes <c>SectAdoptionState</c> arrays.
        /// Order is locked: Alanthor first, then Runaii, then Feraldis,
        /// each cluster in design-doc order.
        /// </summary>
        public static readonly string[] AllSectIds =
        {
            Antiquity, Renewal, Fortitude, Reclamation,   // 0..3   Alanthor
            Silence,   Justice, Veneration, Witness,      // 4..7   Runaii
            War,       Ash,     Ruin,       Wrath,        // 8..11  Feraldis
        };

        /// <summary>Cluster of each sect, indexed by sect-id-to-index.</summary>
        public static readonly SectCluster[] ClusterByIndex =
        {
            SectCluster.Alanthor, SectCluster.Alanthor, SectCluster.Alanthor, SectCluster.Alanthor,
            SectCluster.Runaii,   SectCluster.Runaii,   SectCluster.Runaii,   SectCluster.Runaii,
            SectCluster.Feraldis, SectCluster.Feraldis, SectCluster.Feraldis, SectCluster.Feraldis,
        };

        // ═══════════════════════════════════════════════════════════════════
        // ECONOMY CONSTANTS — design spec §2 Adoption Economy
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>One-time RP award when the Age-1 Shrine completes.</summary>
        public const int RpAwardShrine = 1;

        /// <summary>RP awarded on Age II / III / IV up. Plus the Shrine bonus,
        /// sum across the campaign = 25.</summary>
        public const int RpAwardAge2 = 6;
        public const int RpAwardAge3 = 8;
        public const int RpAwardAge4 = 10;

        /// <summary>Adoption cost — same cluster as faction's current culture.</summary>
        public const int AdoptCostSameCulture = 2;
        /// <summary>Adoption cost — different cluster.</summary>
        public const int AdoptCostCrossCulture = 3;

        /// <summary>Lv I → Lv II upgrade cost (per lever).</summary>
        public const int UpgradeCostLv1ToLv2 = 2;
        /// <summary>Lv II → Lv III upgrade cost (per lever).</summary>
        public const int UpgradeCostLv2ToLv3 = 3;

        /// <summary>
        /// Hard cap on adopted sects per faction. Enforced naturally by the
        /// Temple's 6 chapel slots — each chapel = one adopted sect.
        /// </summary>
        public const int MaxAdoptedSects = 6;

        /// <summary>
        /// Carryover divisor: unspent points entering the next age are halved
        /// (rounded down). Spec §2: "4 unspent → 2 next age".
        /// </summary>
        public const int CarryoverDivisor = 2;

        // ═══════════════════════════════════════════════════════════════════
        // LOOKUPS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Returns the [0..11] index for a sect id, or -1 if unknown.</summary>
        public static int IndexOf(string sectId)
        {
            if (string.IsNullOrEmpty(sectId)) return -1;
            for (int i = 0; i < AllSectIds.Length; i++)
                if (AllSectIds[i] == sectId) return i;
            return -1;
        }

        /// <summary>Returns the string id for an index, or null if out of range.</summary>
        public static string IdAt(int index)
        {
            if (index < 0 || index >= AllSectIds.Length) return null;
            return AllSectIds[index];
        }

        /// <summary>Returns the cluster of a sect, or Alanthor if unknown.</summary>
        public static SectCluster ClusterOf(string sectId)
        {
            int idx = IndexOf(sectId);
            return idx < 0 ? SectCluster.Alanthor : ClusterByIndex[idx];
        }

        /// <summary>True if the sect id is one of the 12 known sects.</summary>
        public static bool IsKnownSect(string sectId) => IndexOf(sectId) >= 0;

        /// <summary>
        /// RP awarded on entering the given age (2/3/4). 0 otherwise.
        /// The Age-1 Shrine bonus is a separate one-time award handled by
        /// BuildingConstructionSystem.GrantShrineRPBonus → see RpAwardShrine.
        /// </summary>
        public static int RpAwardForAge(int age)
        {
            return age switch
            {
                2 => RpAwardAge2,
                3 => RpAwardAge3,
                4 => RpAwardAge4,
                _ => 0,
            };
        }

        /// <summary>
        /// Adoption cost for a faction with culture <paramref name="factionCulture"/>
        /// adopting <paramref name="sectId"/>. Same cluster → 2, cross → 3.
        /// Unknown sect → -1.
        /// </summary>
        public static int AdoptionCost(string sectId, byte factionCulture)
        {
            int idx = IndexOf(sectId);
            if (idx < 0) return -1;
            var sectCluster = ClusterByIndex[idx];
            var factionCluster = ClusterFromCulture(factionCulture);
            return sectCluster == factionCluster ? AdoptCostSameCulture : AdoptCostCrossCulture;
        }

        /// <summary>Cost for a lever upgrade at a given current level. -1 if not upgradeable.</summary>
        public static int UpgradeCost(int currentLevel)
        {
            return currentLevel switch
            {
                1 => UpgradeCostLv1ToLv2,
                2 => UpgradeCostLv2ToLv3,
                _ => -1, // 0 = not adopted yet, 3 = already maxed
            };
        }

        /// <summary>
        /// Map a <see cref="Cultures"/> byte to its <see cref="SectCluster"/>.
        /// <c>Cultures.None</c> (e.g. pre-age-up) maps to Alanthor for cost-math
        /// purposes; faction can't adopt yet anyway since RP is age-gated.
        /// </summary>
        public static SectCluster ClusterFromCulture(byte culture)
        {
            return culture switch
            {
                Cultures.Alanthor => SectCluster.Alanthor,
                Cultures.Runai    => SectCluster.Runaii,
                Cultures.Feraldis => SectCluster.Feraldis,
                _                 => SectCluster.Alanthor,
            };
        }
    }
}
