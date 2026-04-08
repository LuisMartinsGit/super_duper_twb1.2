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
        public static int TargetMinersPerMine = 2;

        /// <summary>Hard cap on total miners.</summary>
        public static int MaxMiners = 6;

        /// <summary>Below this Supplies threshold the AI flags NeedsMoreSupplyIncome.</summary>
        public static int MinSuppliesThreshold = 200;

        /// <summary>Number of GathererHuts the AI tries to build in early game.</summary>
        public static int TargetGatherersHuts = 3;

        /// <summary>Crystal amount required before AI attempts a choice building.</summary>
        public static int CrystalForChoiceBuilding = 100;

        /// <summary>How often (seconds) the AI checks if it can build a choice building.</summary>
        public static float ChoiceBuildingCheckInterval = 15.0f;

        // ═══════════════════════════════════════════════════════════════════
        // VAULT & SMELTER
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>How often (seconds) the AI checks whether to deposit into vaults.</summary>
        public static float VaultCheckInterval = 30.0f;

        /// <summary>How often (seconds) the AI checks whether to assign miners to smelters.</summary>
        public static float SmelterCheckInterval = 15.0f;

        /// <summary>Amount of resources the AI deposits per vault cycle.</summary>
        public static int VaultDepositAmount = 200;

        /// <summary>Resource surplus threshold before the AI starts depositing into vaults.</summary>
        public static int VaultSurplusThreshold = 500;

        /// <summary>Target number of miners assigned to supply each smelter.</summary>
        public static int SmelterTargetMiners = 2;

        // ═══════════════════════════════════════════════════════════════════
        // MILITARY MANAGER (Fix #231)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Minimum army size before the AI will issue offensive commands.</summary>
        public static int MinArmySize = 3;

        /// <summary>Maximum army size the AI will maintain.</summary>
        public static int MaxArmySize = 12;

        /// <summary>Target number of barracks the AI tries to build.</summary>
        public static int TargetBarracks = 2;

        /// <summary>Population headroom to leave free when queuing new units.</summary>
        public static int PopulationHeadroom = 2;

        // ═══════════════════════════════════════════════════════════════════
        // MISSION MANAGER (Fix #231)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Minimum own-military strength required before issuing a defend mission.</summary>
        public static int MinDefenseStrength = 3;

        /// <summary>Minimum own-military strength required before issuing an attack mission.</summary>
        public static int MinAttackStrength = 5;

        /// <summary>Seconds to wait before issuing a blind (no-intel) attack.</summary>
        public static float BlindAttackDelay = 180f;

        // ═══════════════════════════════════════════════════════════════════
        // TACTICAL MANAGER (Fix #231)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Range at which the AI considers units to be in engagement.</summary>
        public static float EngagementRange = 25f;

        /// <summary>Spacing between formation slots when deploying a battalion.</summary>
        public static float FormationSpacing = 3f;

        // ═══════════════════════════════════════════════════════════════════
        // SCOUTING BEHAVIOR (Fix #231)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Number of scouts the AI tries to maintain.</summary>
        public static int DesiredScouts = 2;

        /// <summary>World-space size of a scouting zone.</summary>
        public static float ScoutingZoneSize = 60f;

        // ═══════════════════════════════════════════════════════════════════
        // DEFENSE BEHAVIOR (Fix #231)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Radius around the base to detect approaching threats.</summary>
        public static float ThreatDetectionRadius = 50f;

        /// <summary>Radius for emergency reaction (units pulled to defend immediately).</summary>
        public static float EmergencyDefenseRadius = 25f;

        // ═══════════════════════════════════════════════════════════════════
        // CRYSTAL HUNT BEHAVIOR (Fix #231)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Range from base within which the AI hunts crystal entities.</summary>
        public static float CrystalHuntRange = 80f;

        /// <summary>Maximum hunters the AI assigns per crystal target.</summary>
        public static int MaxCrystalHuntersPerTarget = 3;
    }
}
