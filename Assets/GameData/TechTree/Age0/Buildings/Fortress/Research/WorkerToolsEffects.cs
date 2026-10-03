// WorkerToolsEffects.cs
// The capital's "tools" research line, read live from FactionResearchState by
// BuildingConstructionSystem — the same shape SectResearchEffects uses for
// Deep Foundations, which is the only other research that touches build rate.
//
// -- Why this exists -------------------------------------------------------
// Stone Tools was authored as `gatherSpeedMult: 1.15`, applied to
// WorkerState.GatherSpeedMultiplier. Workers no longer gather (Regions.md §4:
// income comes from territory ticks, forests and mines, and the one remaining
// Worker only BUILDS), so the tech modified a multiplier nothing reads — a
// research a player could buy that did precisely nothing.
//
// The line now does the one thing a worker still does. It is deliberately read
// live rather than stamped onto units the way the damage ladders are: build
// rate is a property of the WORK, resolved per contribution tick, so there is
// no per-unit state to back-fill and no spawn path to keep in step.

using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Worker build-speed multipliers granted by the capital's tools research.
    /// </summary>
    public static class WorkerToolsEffects
    {
        // The four tiers the capital's authored panel shows as one chain slot
        // (BuildingActionLayouts "Fortress"). All four research AT the
        // Fortress: StoneTools (Age 0) lives in Age0/Buildings/Fortress/
        // Research/, the three Alanthor-gated upper tiers in
        // Civs/Alanthor/Buildings/Fortress/Research/. Do NOT author second
        // copies of them: two assets sharing one id means the catalog
        // silently keeps one.
        public const string StoneTools = "StoneTools";
        public const string IronTools = "IronTools";
        public const string VeilstoneTools = "VeilstoneTools";
        public const string VeilsteelTools = "VeilsteelTools";

        /// <summary>
        /// The effectsList stat each tier's TechDefSO carries its build speed
        /// in, as a percent (StoneTools.asset: BuildSpeed Pct 15 — Iron 30,
        /// Veilstone 50, Veilsteel 75). The HIGHEST tier researched wins —
        /// these are the same workers holding better tools, not four sets at
        /// once, so they replace rather than compound. Compounding would have
        /// put the full ladder at x2.3 and made late construction instant.
        /// </summary>
        public const string BuildSpeedStat = "BuildSpeed";

        /// <summary>
        /// Multiplier on a single worker's progress contribution. 1.0 when the
        /// faction has researched nothing in the line.
        /// </summary>
        public static float BuildSpeedMultiplier(Faction faction)
        {
            var research = FactionResearchState.Instance;
            if (research == null) return 1f;

            if (research.HasResearched(faction, VeilsteelTools)) return Multiplier(VeilsteelTools);
            if (research.HasResearched(faction, VeilstoneTools)) return Multiplier(VeilstoneTools);
            if (research.HasResearched(faction, IronTools)) return Multiplier(IronTools);
            if (research.HasResearched(faction, StoneTools)) return Multiplier(StoneTools);
            return 1f;
        }

        /// <summary>A tier's build-speed multiplier from its SO (1 + pct / 100).</summary>
        private static float Multiplier(string techId)
            => 1f + TechCatalog.TechEffect(techId, BuildSpeedStat) / 100f;
    }
}
