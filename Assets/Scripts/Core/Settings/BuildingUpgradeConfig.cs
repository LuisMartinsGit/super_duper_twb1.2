// Static tables for the building upgrade system: per-level cost,
// duration, and stat multipliers. Single source of truth — both the
// command helper (cost check / spend) and the upgrade system (apply
// stats) read from here.
//
// Stats are absolute over base, NOT cumulative. e.g., a Hall at lvl 2
// has 1.15x base HP (not 1.10 * 1.15). This matches the spec phrasing
// "Hall_al_2 (increase HP by 15%)" — single bump from uncultured base.
//
// Calculator alignment (tools/calculator/techtree.json, 2026-08): every
// ladder runs L1-L3. L1 is FREE — granted at age-up by
// BuildingCultureAutoLevelSystem the moment the faction's culture
// completes. Only L2 and L3 are paid manual upgrades.

using TheWaningBorder.Core;

namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// Per-level configuration for the building upgrade system.
    /// </summary>
    public static class BuildingUpgradeConfig
    {
        // Calculator caps every ladder at 3 levels (L1 free at age-up,
        // L2/L3 paid). All multiplier arrays below are length-4
        // (index 0=base .. 3=L3).
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

        /// <summary>Hall / King's Court multi-target count per level
        /// (calculator: 1 at Lv1, 3 at Lv2, 6 at Lv3).</summary>
        public static readonly int[] HallMaxTargets = { 1, 1, 3, 6 };

        /// <summary>Hut +pop per level past base (0 / 5 / 10 / 15).</summary>
        public static readonly int[] HutBonusPop = { 0, 5, 10, 15 };

        // ──────────────────────────────────────────────────────────────────
        // UPGRADE DURATIONS (seconds; index 1..3 corresponds to TARGET level)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fallback time spent in BuildingUpgrading before stats apply.
        /// Index = TARGET level. Index 0 unused (no upgrade to lvl 0).
        /// Per-building curves override this via
        /// <see cref="GetUpgradeDuration"/>.
        /// </summary>
        public static readonly float[] UpgradeDuration = { 0f, 20f, 30f, 45f };

        /// <summary>
        /// Per-building upgrade duration (calculator values). Falls back to
        /// the global <see cref="UpgradeDuration"/> curve for buildings the
        /// calculator doesn't override.
        /// </summary>
        public static float GetUpgradeDuration(string buildingId, byte targetLevel)
        {
            if (targetLevel < 1 || targetLevel > MaxLevel)
                return 0f;
            return (buildingId, targetLevel) switch
            {
                ("Hall" or "KingsCourt", 2)       => 65f,
                ("Hall" or "KingsCourt", 3)       => 90f,
                ("Hut", 2)                        => 45f,
                ("Hut", 3)                        => 60f,
                ("VaultOfAlmierra" or "ShrineOfRidan", 2) => 30f,
                ("VaultOfAlmierra" or "ShrineOfRidan", 3) => 45f,
                ("Alanthor_RoyalStable", 2)       => 30f,
                ("Alanthor_RoyalStable", 3)       => 45f,
                ("Alanthor_Wall", 2)              => 20f,
                ("Alanthor_Wall", 3)              => 30f,
                ("Alanthor_Tower", 2)             => 25f,
                ("Alanthor_Tower", 3)             => 40f,
                ("Alanthor_SiegeYard", 2)         => 35f,
                ("Alanthor_SiegeYard", 3)         => 50f,
                ("Alanthor_Smelter", 2)           => 45f,
                ("Alanthor_Smelter", 3)           => 60f,
                _ => UpgradeDuration[targetLevel],
            };
        }

        // ──────────────────────────────────────────────────────────────────
        // COSTS (per buildingId, per TARGET level)
        // ──────────────────────────────────────────────────────────────────
        //
        // No glow for buildings (per spec). L1 is free everywhere (granted
        // at age-up by BuildingCultureAutoLevelSystem); L2/L3 costs come
        // straight from the calculator.

        /// <summary>
        /// Lookup cost for a given building type + target level. Returns
        /// false if the combination isn't recognized. Target level 1 is
        /// free (zero cost) — the culture auto-level normally grants it,
        /// this just keeps a manual L0→L1 request consistent.
        /// </summary>
        public static bool TryGetCost(string buildingId, byte targetLevel, out Cost cost)
        {
            cost = default;
            if (targetLevel < 1 || targetLevel > MaxLevel) return false;

            switch (buildingId)
            {
                case "Hall":
                case "KingsCourt":
                    // King's Court ladder (calculator): L1 free at age-up,
                    // then two paid rungs with veilsteel as the apex sink.
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 400, Iron = 100, Veilstone = 40, Veilsteel = 5 },
                        3 => new Cost { Supplies = 600, Iron = 220, Veilstone = 160, Veilsteel = 15 },
                        _ => default,
                    };
                    return true;
                case "Barracks":
                case "ArcheryRange":
                    // Archery Range mirrors Barracks pricing — same tier of
                    // military training building, parallel upgrade curve.
                    // (Leveled ArcheryRange IS the Alanthor Practice Range.)
                    cost = targetLevel switch
                    {
                        1 => new Cost { Supplies = 80, Iron = 20 },
                        2 => new Cost { Supplies = 160, Iron = 40, Veilstone = 10 },
                        3 => new Cost { Supplies = 320, Iron = 80, Veilstone = 30 },
                        _ => default,
                    };
                    return true;
                case "Hut":
                    // Hut/House ladder (calculator): L1 free at age-up.
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 240, Iron = 50, Veilstone = 15, Veilsteel = 5 },
                        3 => new Cost { Supplies = 360, Iron = 75, Veilstone = 50, Veilsteel = 35 },
                        _ => default,
                    };
                    return true;
                case "GatherersHut":
                    // Alanthor "Guild" level ladder — 3 levels (canon costs).
                    cost = targetLevel switch
                    {
                        1 => new Cost { Supplies = 120, Iron = 25, Veilstone = 5 },
                        2 => new Cost { Supplies = 240, Iron = 50, Veilstone = 15, Veilsteel = 5 },
                        3 => new Cost { Supplies = 360, Iron = 75, Veilstone = 40, Veilsteel = 20 },
                        _ => default,
                    };
                    return true;
                // Choice-building ladders (calculator): L1 free at age-up,
                // L2/L3 paid. Shared curve for Vault and Shrine.
                case "Mine":
                case "VeilstoneMine":
                    // THE ANSWER TO A THINNING SEAM, and the recurring sink for
                    // the two currencies that had almost nothing to buy.
                    //
                    // Regions.md §4 has always said mines are upgradeable and
                    // each level adds another 25/min, but neither had a ladder
                    // here at all — TryGetCost fell through to false, so the
                    // upgrade button never appeared and the only way to raise
                    // yield was to take more ground.
                    //
                    // Priced in veilstone and veilsteel on purpose. Those are
                    // what a territory-holding faction accumulates and what it
                    // could not spend (one logged AI banked 15,242 veilstone),
                    // and now that nodes DEPLETE, upgrading is the standing
                    // answer to falling income — a sink that recurs for as long
                    // as the map does.
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 260, Iron = 90, Veilstone = 80 },
                        3 => new Cost { Supplies = 420, Iron = 160, Veilstone = 220, Veilsteel = 40 },
                        _ => default,
                    };
                    return true;
                case "VaultOfAlmierra":
                case "ShrineOfRidan":
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 200, Iron = 50, Veilstone = 15 },
                        3 => new Cost { Supplies = 400, Iron = 100, Veilstone = 40 },
                        _ => default,
                    };
                    return true;
                case "Alanthor_RoyalStable":
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 160, Iron = 40, Veilstone = 10 },
                        3 => new Cost { Supplies = 320, Iron = 80, Veilstone = 30 },
                        _ => default,
                    };
                    return true;
                case "Alanthor_Tower":
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 120, Iron = 60, Veilstone = 20 },
                        3 => new Cost { Supplies = 240, Iron = 120, Veilstone = 60 },
                        _ => default,
                    };
                    return true;
                case "Alanthor_Wall":
                    // Wall hub levels (calculator: L2 80S/40I, L3 160S/80I/40V).
                    // Stat-only bumps via the standard multiplier arrays.
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 80,  Iron = 40 },
                        3 => new Cost { Supplies = 160, Iron = 80, Veilstone = 40 },
                        _ => default,
                    };
                    return true;
                case "Alanthor_SiegeYard":
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 180, Iron = 60, Veilstone = 20 },
                        3 => new Cost { Supplies = 340, Iron = 120, Veilstone = 60 },
                        _ => default,
                    };
                    return true;
                case "Alanthor_Smelter":
                    // The Smelter ladder is the veilsteel engine ramp
                    // (1/2/3 veilsteel per 10 s at Lv1/2/3 — ForgeConversionSystem).
                    cost = targetLevel switch
                    {
                        1 => default,
                        2 => new Cost { Supplies = 500, Iron = 250, Veilstone = 120, Veilsteel = 30 },
                        3 => new Cost { Supplies = 700, Iron = 350, Veilstone = 200, Veilsteel = 60 },
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
