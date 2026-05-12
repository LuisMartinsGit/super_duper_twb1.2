// EquipmentTierConfig.cs
// Per-tier stat multipliers + upgrade costs for the equipment tier system.
// Lives in Core/Settings/ alongside UnitRankConfig (the analogous per-unit
// veterancy config). Spec §4.1-§4.4.
//
// Multipliers apply to Damage and Defense on each unit. Tier-specific
// magical effects (shield bar at Crystal, duplicate squad at Veilsteel,
// revive at Glow) are gated by reading EquipmentTier elsewhere — they're
// not stat multipliers.

using TheWaningBorder.Core;

namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// Static lookup tables for the equipment tier system.
    /// </summary>
    public static class EquipmentTierConfig
    {
        /// <summary>
        /// Stat multiplier applied to Damage + Defense values. Stacks
        /// multiplicatively with UnitRankConfig.MultiplierFor (veterancy).
        /// </summary>
        public static float StatMultiplier(EquipmentTier tier) => tier switch
        {
            EquipmentTier.Base      => 1.00f,
            EquipmentTier.Iron      => 1.15f,
            EquipmentTier.Crystal   => 1.30f,
            EquipmentTier.Veilsteel => 1.50f,
            EquipmentTier.Glow      => 1.75f,
            _ => 1.00f,
        };

        /// <summary>
        /// Resource cost to upgrade a single unit-class tier (faction-wide).
        /// Costs roughly mirror UnitRankConfig.CostFor but scaled up — this
        /// is an army-wide upgrade, not a single unit's rank-up.
        /// </summary>
        public static Cost UpgradeCost(EquipmentTier from, EquipmentTier to)
        {
            // Only adjacent upgrades are valid (Base→Iron→Crystal→Veilsteel→Glow).
            // The router rejects non-adjacent calls; this helper assumes valid input.
            return to switch
            {
                EquipmentTier.Iron      => new Cost { Iron = 200, Supplies = 150 },
                EquipmentTier.Crystal   => new Cost { Crystal = 150, Iron = 200 },
                EquipmentTier.Veilsteel => new Cost { Veilsteel = 80, Crystal = 200 },
                EquipmentTier.Glow      => new Cost { Glow = 20, Veilsteel = 150 },
                _ => default,
            };
        }

        /// <summary>
        /// Seconds it takes to research a tier upgrade (placeholder — the
        /// research-system wiring is a follow-up slice; the upgrade applies
        /// immediately for now).
        /// </summary>
        public static float ResearchTime(EquipmentTier to) => to switch
        {
            EquipmentTier.Iron      => 45f,
            EquipmentTier.Crystal   => 60f,
            EquipmentTier.Veilsteel => 75f,
            EquipmentTier.Glow      => 90f,
            _ => 0f,
        };

        // ==================== Shield bar (spec §4.2-§4.4) ====================

        /// <summary>
        /// Shield-bar max HP for a given tier. Base + Iron have no shield;
        /// Crystal+ grants a second HP layer that absorbs damage before
        /// touching Health.
        /// </summary>
        public static int ShieldBarMax(EquipmentTier tier) => tier switch
        {
            EquipmentTier.Crystal   => 50,
            EquipmentTier.Veilsteel => 80,
            EquipmentTier.Glow      => 120,
            _ => 0,
        };

        /// <summary>Shield HP regenerated per second once the regen gate opens.</summary>
        public const float ShieldBarRegenPerSecond = 5f;

        /// <summary>Seconds of "no damage" required before shield regen kicks in.</summary>
        public const float ShieldBarRegenDelay = 3f;

        // ==================== Glow revive (spec §4.2 Glow tier) ====================

        /// <summary>Fraction of Max HP the on-death Glow revive restores.</summary>
        public const float GlowReviveHealthPercent = 0.5f;

        // ==================== Siege shield aura (spec §4.3 Crystal+) ====================

        /// <summary>Aura radius for Crystal+ siege units (spec §4.3).</summary>
        public const float SiegeShieldAuraRadius = 8f;

        /// <summary>Bonus shield HP granted by Siege Crystal aura to each ally in range.</summary>
        public const int SiegeShieldAuraCrystalBonus = 30;

        /// <summary>Bonus at Veilsteel (stacking up from Crystal).</summary>
        public const int SiegeShieldAuraVeilsteelBonus = 50;

        /// <summary>Bonus at Glow.</summary>
        public const int SiegeShieldAuraGlowBonus = 75;

        // ==================== Hero phase shield (spec §4.4 Crystal+) ====================

        /// <summary>Seconds between phase-shield absorbs.</summary>
        public const float HeroPhaseShieldCooldown = 12f;

        /// <summary>Fraction of damage absorbed by a charged phase shield (per tier).</summary>
        public const float HeroPhaseShieldReductionCrystal = 0.50f;
        public const float HeroPhaseShieldReductionVeilsteel = 0.65f;
        public const float HeroPhaseShieldReductionGlow = 0.80f;
    }
}
