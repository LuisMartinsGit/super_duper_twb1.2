// AITuning.cs
// Centralized AI configuration constants.
// Tweak these values to adjust AI behavior without digging into system code.

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Centralized tuning constants for the AI subsystem.
    /// Gathered from AIBuildingManager and AIEconomyManager so they
    /// can be adjusted in one place (and eventually exposed to a UI or config file).
    /// </summary>
    public static class AITuning
    {
        // ═══════════════════════════════════════════════════════════════════
        // BUILDING MANAGER
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>How often (seconds) the AI checks whether to start new constructions.</summary>
        public static float BuildCheckInterval = 3.0f;

        /// <summary>Default number of builders the AI tries to maintain.</summary>
        public static int TargetBuilders = 3;

        /// <summary>Absolute cap on builders the AI will train.</summary>
        public static int MaxBuilders = 5;

        // ═══════════════════════════════════════════════════════════════════
        // ECONOMY MANAGER
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>How often (seconds) the AI re-evaluates mine assignments.</summary>
        public static float MineCheckInterval = 5.0f;

        /// <summary>Target miners to assign per iron deposit.</summary>
        public static int TargetMinersPerMine = 3;

        /// <summary>Hard cap on total miners in early game (3 mines x 3 each).</summary>
        public static int MaxMiners = 9;

        /// <summary>Below this Supplies threshold the AI flags NeedsMoreSupplyIncome.</summary>
        public static int MinSuppliesThreshold = 200;

        /// <summary>Number of GathererHuts the AI tries to build in early game.</summary>
        public static int TargetGatherersHuts = 3;

        /// <summary>Crystal amount required before AI attempts a choice building.</summary>
        public static int CrystalForChoiceBuilding = 100;

        /// <summary>How often (seconds) the AI checks if it can build a choice building.</summary>
        public static float ChoiceBuildingCheckInterval = 15.0f;
    }
}
