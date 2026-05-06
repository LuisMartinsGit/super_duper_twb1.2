// File: Assets/Scripts/Core/Settings/BuildingUpgradeConfig.cs
// Static tables for the building upgrade system: per-level cost,
// duration, and stat multipliers. Single source of truth — both the
// command helper (cost check / spend) and the upgrade system (apply
// stats) read from here.
//
// Stats are absolute over base, NOT cumulative. e.g., a Hall at lvl 2
// has 1.15x base HP (not 1.10 * 1.15). This matches the spec phrasing
// "Hall_al_2 (increase HP by 15%)" — single bump from uncultured base.

using TheWaningBorder.Core;

namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// Per-level configuration for the building upgrade system.
    /// </summary>
    public static class BuildingUpgradeConfig
    {
        public const byte MaxLevel = 3;

        // ──────────────────────────────────────────────────────────────────
        // STAT MULTIPLIERS (level 0..3 — index 0 = base, 1..3 = cultured)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>HP multiplier vs base (1.0 = no change).</summary>
        public static readonly float[] HpMultiplier = { 1.00f, 1.10f, 1.15f, 1.20f };

        /// <summary>
        /// Train-time multiplier vs base (lower = faster).
        /// "Train speed +15%" means 15% MORE units per minute, so
        /// trainTime *= 1 / 1.15 = 0.870.
        /// </summary>
        public static readonly float[] TrainTimeMultiplier = { 1.00f, 1f / 1.15f, 1f / 1.25f, 1f / 1.40f };

        /// <summary>
        /// Attack-cooldown multiplier vs base (lower = faster).
        /// "Attack rate +10%" means 10% more shots per second, so
        /// cooldown *= 1 / 1.10 = 0.909.
        /// </summary>
        public static readonly float[] AttackCooldownMultiplier = { 1.00f, 1f / 1.10f, 1f / 1.15f, 1f / 1.20f };

        /// <summary>Hall multi-target count per level (1 / 1 / 2 / 4).</summary>
        public static readonly int[] HallMaxTargets = { 1, 1, 2, 4 };

        /// <summary>Hut +pop per level past base (0 / 5 / 10 / 15).</summary>
        public static readonly int[] HutBonusPop = { 0, 5, 10, 15 };

        // ──────────────────────────────────────────────────────────────────
        // UPGRADE DURATIONS (seconds; index 1..3 corresponds to TARGET level)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Time spent in BuildingUpgrading before stats apply. Index =
        /// TARGET level. Index 0 unused (no upgrade to lvl 0).
        /// </summary>
        public static readonly float[] UpgradeDuration = { 0f, 20f, 30f, 45f };

        // ──────────────────────────────────────────────────────────────────
        // COSTS (per buildingId, per TARGET level)
        // ──────────────────────────────────────────────────────────────────
        //
        // No glow for buildings (per spec). Costs scale roughly 2x per tier
        // and add a higher-tier resource at each step (iron at L1, +crystal
        // at L2/L3, +veilsteel at L3 for the Hall as the apex sink).

        /// <summary>
        /// Lookup cost for a given building type + target level. Returns
        /// false if the combination isn't recognized.
        /// </summary>
        public static bool TryGetCost(string buildingId, byte targetLevel, out Cost cost)
        {
            cost = default;
            if (targetLevel < 1 || targetLevel > MaxLevel) return false;

            switch (buildingId)
            {
                case "Hall":
                    cost = targetLevel switch
                    {
                        1 => new Cost { Supplies = 100, Iron = 25 },
                        2 => new Cost { Supplies = 200, Iron = 50, Crystal = 15 },
                        3 => new Cost { Supplies = 400, Iron = 100, Crystal = 40, Veilsteel = 5 },
                        _ => default,
                    };
                    return true;
                case "Barracks":
                    cost = targetLevel switch
                    {
                        1 => new Cost { Supplies = 80, Iron = 20 },
                        2 => new Cost { Supplies = 160, Iron = 40, Crystal = 10 },
                        3 => new Cost { Supplies = 320, Iron = 80, Crystal = 30 },
                        _ => default,
                    };
                    return true;
                case "Hut":
                    cost = targetLevel switch
                    {
                        1 => new Cost { Supplies = 60, Iron = 10 },
                        2 => new Cost { Supplies = 120, Iron = 25, Crystal = 5 },
                        3 => new Cost { Supplies = 240, Iron = 50, Crystal = 15 },
                        _ => default,
                    };
                    return true;
            }
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // CULTURE CODE (for prefab lookups in PR-2 — kept here so all
        // upgrade-related strings live in one place).
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Two-letter culture code used in prefab filenames (al / ru / fe).
        /// Returns empty string for None.
        /// </summary>
        public static string CultureCode(byte culture) => culture switch
        {
            Cultures.Alanthor => "al",
            Cultures.Runai    => "ru",
            Cultures.Feraldis => "fe",
            _                 => string.Empty,
        };
    }
}
