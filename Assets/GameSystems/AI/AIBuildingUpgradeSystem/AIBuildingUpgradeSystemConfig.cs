using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIBuildingUpgradeSystem"/>. The asset is AIBuildingUpgradeSystem.asset,
    /// beside AIBuildingUpgradeSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIBuildingUpgradeSystem",
                     fileName = "AIBuildingUpgradeSystem")]
    public sealed class AIBuildingUpgradeSystemConfig : ScriptableObject, IComponentConfig
    {
        static AIBuildingUpgradeSystemConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIBuildingUpgradeSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIBuildingUpgradeSystemConfig>());

        // Slow strategic loop. Upgrades take 20-45s to complete; checking
        // every 6 s is plenty of cadence and keeps query churn low.
        public float thinkInterval;

        /// <summary>ARMY-FIRST gate (2026-08-04, log-proven: six hut
        /// level-ups drained iron to ~20 exactly while the army needed
        /// rebuilding after a wipe — unit production starved while
        /// cosmetics were bought). Building upgrades only proceed when this
        /// much iron remains banked for the military line.</summary>
        public int upgradeIronReserve;

        // Reserve buffer the AI keeps untouched before queueing an upgrade —
        // upgrades are expensive and we don't want them to starve military /
        // research lines.
        public int reserveSupplies;

        public int reserveIron;

        public int reserveVeilstone;

        /// <summary>Veilsteel kept banked for Smelter levels while the faction's
        /// Smelter is below max: L2 costs 30, L3 costs 60. Without this the
        /// 5-veilsteel hut upgrades eat the entire L1 drip (6/min) forever and
        /// the engine never grows — the exact famine the 47-min log shows
        /// (veilsteel oscillating 0-11 with 12,700 veilstone banked).</summary>
        public int smelterVeilsteelReserve;
    }
}
