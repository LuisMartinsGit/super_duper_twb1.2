// The one research each sect sells (docs/Design/Sects.md section 1, the
// [RESEARCH] line of every sect entry).
//
// Every sect research is a faction-wide economy or production modifier, so
// none of them fit the TechEffects stat block in TechnologyDef - that channel
// rewrites unit stats on existing entities, and these change the price and the
// pace of things that have not been built yet. They follow the Conscription
// idiom instead: the tech is a flag, and the consuming site multiplies by the
// number it gets from here. Concentrating the numbers in one file is what keeps
// them greppable when the design doc moves.
//
// Shipped so far: the four Alanthor researches (Cartography, Field Hospital,
// Deep Foundations, Charter of Works — Sects.md §4, 2026-10-15) and War's.
// The remaining seven land with their clusters' passes.
//
// IMPORTANT: before this file existed the four Alanthor techs parsed, appeared
// on their sect buildings and charged full price for NOTHING - they had no
// consumer anywhere in the codebase. Adding a research id here without also
// adding its call site reproduces that bug.

namespace TheWaningBorder.Economy
{
    public static class SectResearchEffects
    {
        // ── Tech ids. Must match the TechDefSO ids in TechTreeCatalog ──
        public const string Cartography      = "Cartography";      // Antiquity
        public const string FieldHospital    = "FieldHospital";    // Renewal
        public const string DeepFoundations  = "DeepFoundations";  // Fortitude
        public const string CharterOfWorks   = "CharterOfWorks";   // Prosperity
        public const string EndlessMuster    = "EndlessMuster";    // War

        private static bool Has(Faction faction, string techId)
        {
            var research = FactionResearchState.Instance;
            return research != null && research.HasResearched(faction, techId);
        }

        // ── Antiquity: Cartography (Sects.md §4, 2026-10-15) ─────────────────
        // 2026-10-19: "every territory you hold is fully visible to you; lost
        // ground falls back to explored". Consumed by FogOfWarSystem (the fog
        // grid: a per-territory cell mask stamped visible, exactly to the
        // border) and FactionSight (the simulation's sight test).

        /// <summary>Does this faction see every territory it holds, whole?</summary>
        public static bool RevealsHeldTerritory(Faction faction) => Has(faction, Cartography);

        /// <summary>Research and building-upgrade TIME multiplier. Royal Index
        /// (Antiquity's old research) was replaced by Cartography 2026-10-15;
        /// no sect research speeds research any more.</summary>
        public static float ResearchTimeMultiplier(Faction faction) => 1f;

        /// <summary>Research COST multiplier (the retired Royal Index; neutral).</summary>
        public static float ResearchCostMultiplier(Faction faction) => 1f;

        // ── Renewal: Field Hospital ───────────────────────────────────────────
        // "Your Litharchs unlock Deploy Field Hospital."
        //
        // The only sect research that is a pure UNLOCK rather than a number, so
        // it has no multiplier here. Its consumers are the two ability-grant
        // sites that both read the raw tech id: TechEffectSystem's
        // GrantLitharchFieldHospital (re-arms Litharchs already on the field the
        // moment it finishes) and ApplySpawnPassives (arms every Litharch
        // trained after). The id constant lives here anyway so the twelve sect
        // researches stay greppable from one place.

        // ── Fortitude: Deep Foundations ───────────────────────────────────────
        // "Defensive structures cost 20% less and build 30% faster."

        /// <summary>Multiplier on a building's placement cost. Deep Foundations
        /// applies only to defensive structures, so the caller passes the id and
        /// <see cref="IsDefensiveStructure"/> decides.</summary>
        public static float BuildingCostMultiplier(Faction faction, string buildingId)
            => (Has(faction, DeepFoundations) && IsDefensiveStructure(buildingId) ? 0.80f : 1f)
             * (Has(faction, CharterOfWorks) && IsExtractor(buildingId) ? 0.80f : 1f);

        /// <summary>
        /// Multiplier on construction SPEED (higher is faster). Deep Foundations
        /// is the only sect research that touches construction now that Renewal
        /// sells Field Hospital instead of Mason's Charter, so this applies to
        /// defensive structures only.
        /// </summary>
        public static float ConstructionSpeedMultiplier(Faction faction, string buildingId)
        {
            float m = 1f;
            if (Has(faction, DeepFoundations) && IsDefensiveStructure(buildingId)) m *= 1.30f;
            return m;
        }

        /// <summary>
        /// What counts as a "defensive structure" for Deep Foundations: the
        /// fortification line (walls, hubs, gates, towers) and the Fortitude
        /// sect's own blockhouse, which the design calls out as built to be shot
        /// at. Halls and production buildings are deliberately excluded - the
        /// research is a wall-keeper's tech, not a general discount.
        /// </summary>
        public static bool IsDefensiveStructure(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return false;
            if (buildingId == "Sect_Stonehold") return true;
            return buildingId.Contains("Wall") || buildingId.Contains("Tower");
        }

        // ── Prosperity: Charter of Works (Sects.md §4, 2026-10-15) ───────────
        // "Extractors cost less to build and to level up." The build half is
        // BuildingCostMultiplier above (read by BuildCosts.For); the level
        // half is LevelCostMultiplier (read by UpgradeBuildingCommandHelper).

        /// <summary>An extractor for Charter of Works: anything that stands on
        /// a node and pays its slot ladder.</summary>
        public static bool IsExtractor(string buildingId) => buildingId switch
        {
            "GatherersHut" or "Mine" or "VeilstoneMine" or "Alanthor_TradingOutpost" => true,
            _ => false,
        };

        /// <summary>Multiplier on a building LEVEL's price.</summary>
        public static float LevelCostMultiplier(Faction faction, string buildingId)
            => Has(faction, CharterOfWorks) && IsExtractor(buildingId) ? 0.80f : 1f;

        // ── War: Endless Muster ───────────────────────────────────────────────
        // "Military buildings train two units at once. Queue depth is
        // unchanged."

        /// <summary>
        /// How many queue slots a building may have in production at once.
        /// Two with Endless Muster, one without. Queue DEPTH is untouched by
        /// design - the research buys throughput, not a longer queue.
        /// </summary>
        public static int ConcurrentTrainingSlots(Faction faction)
            => Has(faction, EndlessMuster) ? 2 : 1;
    }
}
