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
    }
}
