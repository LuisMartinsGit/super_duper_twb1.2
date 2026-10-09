using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.World
{
    /// <summary>
    /// Tuning numbers for <see cref="TerritoryIncomeSystem"/>: how much a
    /// node holds, how long it can live, what an empty slot, the Mine
    /// research, the Guild surveys, a territory claim and the Fortress level
    /// pay (docs/Design/Territory_Claims.md § 11.3,
    /// docs/Design/Veilstone_Economy.md § 5). The asset is
    /// TerritoryIncomeSystem.asset, beside TerritoryIncomeSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Every rate is PER MINUTE, the unit the player is shown.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/World/TerritoryIncomeSystem",
                     fileName = "TerritoryIncomeSystem")]
    public sealed class TerritoryIncomeSystemConfig : ScriptableObject, IComponentConfig
    {
        static TerritoryIncomeSystemConfig _i;
        public static TerritoryIncomeSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<TerritoryIncomeSystemConfig>());

        // ── node reserves (§ 11.3) ───────────────────────────────────────

        /// <summary>Units a fresh supply node can pay out.</summary>
        public float supplyNodeReserve;

        /// <summary>Units a fresh iron node can pay out.</summary>
        public float ironNodeReserve;

        /// <summary>Units an outcrop holds when nothing else gave it a reserve
        /// (veilstone keeps its own fast-and-finite rule).</summary>
        public float veilstoneNodeReserve;

        /// <summary>Reserve multiplier per purity (Territory_Claims.md § 11.3):
        /// Pure must outlast its higher yield, Poor runs out first.</summary>
        public float pureReserveMultiplier;
        public float normalReserveMultiplier;
        public float poorReserveMultiplier;

        /// <summary>Yield floor as a fraction of a node's fresh rate. 0 = a
        /// worked node runs dry; above 0 it keeps that trickle forever.</summary>
        public float depletionFloor;

        /// <summary>
        /// THE LATE GAME (2026-10-08): every node — supply, iron, veilstone —
        /// is spent by this many minutes of SIMULATED match time. A node's
        /// reserve is capped at Initial × (1 − matchMinutes / this), so its
        /// pay falls to nothing by then whether or not it was worked. 0 or
        /// less turns the lifetime off.
        /// </summary>
        public float nodeLifetimeMinutes;

        // ── slot rates ───────────────────────────────────────────────────

        /// <summary>What an EMPTY slot pays (supply, iron or uncursed
        /// veilstone node in held ground, no extractor on it).</summary>
        public float emptySlotPerMinute;

        /// <summary>Flat multiplier on every iron slot, empty or Mined
        /// (Veilstone_Economy.md § 6). 1 = the SO ladder as authored.</summary>
        public float ironYieldMultiplier;

        /// <summary>The Mine research on Mine-worked iron slots: Deep Shafts,
        /// then Rich Seams (replaces it, does not stack).</summary>
        public float deepShaftsMultiplier;
        public float richSeamsMultiplier;

        /// <summary>Feraldis Veilstone Mines pay (and drain) this much more
        /// (Veilstone_Economy.md § 3.2).</summary>
        public float feraldisVeilstoneMultiplier;

        // ── the capital ──────────────────────────────────────────────────

        /// <summary>
        /// Multiplier on a Fortress's OWN supplies income (its SO's
        /// SuppliesIncome) by its level: entry 0 = L1, 1 = L2, 2 = L3. It
        /// scales nothing else — extractor slots, claims and surveys are not
        /// Fortress income (2026-10-08). The share above ×1 is paid by the
        /// territory tick and booked as FortressLevel.
        /// </summary>
        public float[] fortressLevelIncomeMultipliers;

        // ── Guild surveys (Veilstone_Economy.md § 5.1) ───────────────────

        /// <summary>Per Guild, per completed Iron Surveying tier, iron a minute.</summary>
        public float guildSurveyIronPerTier;

        /// <summary>Per Guild, per completed Veilstone Survey tier, veilstone a minute.</summary>
        public float guildSurveyVeilstonePerTier;

        /// <summary>Per Guild, with Veilsteel Survey done, veilsteel a minute.</summary>
        public float guildSurveyVeilsteelPerTier;

        /// <summary>Survey output scale by the Guild's level: entry 0 = L1,
        /// 1 = L2, 2 = L3.</summary>
        public float[] guildSurveyLevelScale;

        // ── territory claims ─────────────────────────────────────────────

        /// <summary>
        /// What every held territory pays its holder a minute, nodes or not,
        /// once the holder has aged up (claims open at age-up, so the Age 0
        /// opening is untouched). The late-game income that does not run dry.
        /// </summary>
        public float claimSuppliesPerMinute;
        public float claimIronPerMinute;
        public float claimVeilstonePerMinute;
    }
}
