// WorkerToolsEffects.cs
// The Hall's "tools" research line, read live from FactionResearchState by
// BuildingConstructionSystem — the same shape SectResearchEffects uses for
// Deep Foundations, which is the only other research that touches build rate.
//
// -- Why this exists -------------------------------------------------------
// Stone Tools was authored as `gatherSpeedMult: 1.15`, applied to
// MinerState.GatherSpeedMultiplier. Workers no longer gather (Regions.md §4:
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
    /// Worker build-speed multipliers granted by the Hall's tools research.
    /// </summary>
    public static class WorkerToolsEffects
    {
        // The four tiers the Hall's authored panel shows as one chain slot
        // (BuildingActionLayouts "Hall"). StoneTools is hosted at the Hall;
        // the three upper tiers are hosted at the KING'S COURT — the Hall's
        // Age-1 cultured form — and live under Civs/Alanthor/Buildings/
        // KingsCourt/Research/. Do NOT author Hall-hosted copies of them: two
        // assets sharing one id means the catalog silently keeps one.
        public const string StoneTools = "StoneTools";
        public const string IronTools = "IronTools";
        public const string VeilstoneTools = "VeilstoneTools";
        public const string VeilsteelTools = "VeilsteelTools";

        /// <summary>
        /// Build speed per tier. The HIGHEST tier researched wins — these are
        /// the same workers holding better tools, not four sets at once, so
        /// they replace rather than compound. Compounding would have put the
        /// full ladder at x2.3 and made late construction instant.
        /// </summary>
        public const float StoneToolsBuildSpeed = 1.15f;
        public const float IronToolsBuildSpeed = 1.30f;
        public const float VeilstoneToolsBuildSpeed = 1.50f;
        public const float VeilsteelToolsBuildSpeed = 1.75f;

        /// <summary>
        /// Multiplier on a single builder's progress contribution. 1.0 when the
        /// faction has researched nothing in the line.
        /// </summary>
        public static float BuildSpeedMultiplier(Faction faction)
        {
            var research = FactionResearchState.Instance;
            if (research == null) return 1f;

            if (research.HasResearched(faction, VeilsteelTools)) return VeilsteelToolsBuildSpeed;
            if (research.HasResearched(faction, VeilstoneTools)) return VeilstoneToolsBuildSpeed;
            if (research.HasResearched(faction, IronTools)) return IronToolsBuildSpeed;
            if (research.HasResearched(faction, StoneTools)) return StoneToolsBuildSpeed;
            return 1f;
        }
    }
}
