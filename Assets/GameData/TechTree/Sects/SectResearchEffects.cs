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
// Shipped so far: the four Alanthor researches and War's. The remaining seven
// land with their clusters' passes.
//
// IMPORTANT: before this file existed the four Alanthor techs parsed, appeared
// on their sect buildings and charged full price for NOTHING - they had no
// consumer anywhere in the codebase. Adding a research id here without also
// adding its call site reproduces that bug.

namespace TheWaningBorder.Economy
{
    public static class SectResearchEffects
    {
        // ── Tech ids. Must match TechTree.json and TechTreeParser's allowlist ──
        public const string RoyalIndex       = "RoyalIndex";       // Antiquity
        public const string FieldHospital    = "FieldHospital";    // Renewal
        public const string DeepFoundations  = "DeepFoundations";  // Fortitude
        public const string WardensLedger    = "WardensLedger";    // Reclamation
        public const string EndlessMuster    = "EndlessMuster";    // War

        private static bool Has(Faction faction, string techId)
        {
            var research = FactionResearchState.Instance;
            return research != null && research.HasResearched(faction, techId);
        }

        // ── Antiquity: Royal Index ────────────────────────────────────────────
        // "All technologies and building upgrades take 30% less time and 10%
        // fewer resources."

        /// <summary>Multiplier on research and building-upgrade TIME.</summary>
        public static float ResearchTimeMultiplier(Faction faction)
            => Has(faction, RoyalIndex) ? 0.70f : 1f;

        /// <summary>Multiplier on research and building-upgrade COST.</summary>
        public static float ResearchCostMultiplier(Faction faction)
            => Has(faction, RoyalIndex) ? 0.90f : 1f;

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
            => Has(faction, DeepFoundations) && IsDefensiveStructure(buildingId) ? 0.80f : 1f;

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

        // ── Reclamation: Warden's Ledger ──────────────────────────────────────
        // "Veilstone yields +25%, and every cursed node is harvestable
        // regardless of tier."

        /// <summary>Multiplier on every veilstone gather tick.</summary>
        public static float VeilstoneYieldMultiplier(Faction faction)
            => Has(faction, WardensLedger) ? 1.25f : 1f;

        // The design's second Warden's Ledger clause - "every cursed node is
        // harvestable regardless of tier" - has NO consumer here on purpose:
        // there is no node tier gate anywhere in the codebase for it to
        // override. Shipping an IgnoresNodeTierGate() that nothing calls would
        // read as implemented while doing nothing, which is exactly the state
        // this file exists to end. The clause lands when node tiers do.

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
