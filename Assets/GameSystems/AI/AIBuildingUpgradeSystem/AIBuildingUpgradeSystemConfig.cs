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
        /// much iron remains banked for the military line. The four
        /// production lines are exempt (2026-10-04, Game_AI.md 5f): their
        /// levels are the army's output.</summary>
        public int upgradeIronReserve;

        // Reserve buffer the AI keeps untouched before queueing an upgrade —
        // upgrades are expensive and we don't want them to starve military /
        // research lines.
        public int reserveSupplies;

        public int reserveIron;

        public int reserveVeilstone;

        /// <summary>The HOME capital is levelled ahead of the rotation and
        /// past the reserves until it reaches this level (King Lexor trains
        /// only at a Lv3 capital; every capital level raises the territory
        /// limit). While the bank cannot pay for it, only Gatherer's Huts
        /// level meanwhile. 0 = no priority.</summary>
        public int capitalPriorityLevel;

        /// <summary>Capital levels up to this one are ESSENTIAL: bought and
        /// saved for ahead of everything (L2 doubles the home territory's
        /// income). Levels above it, up to capitalPriorityLevel, are economy
        /// drive: on a tier with unitsBeforeEconomy they wait while the money
        /// could still become units (docs/Design/Game_AI.md 5h).</summary>
        public int capitalEssentialLevel;

        /// <summary>SURPLUS (docs/Design/Game_AI.md 5e): while the bank is
        /// overflowing (AIBudget surplusSupplies / surplusIron) up to this
        /// many level-ups are queued per think instead of one — the capital
        /// first, then the rotation. While the army is short of veilstone a
        /// level whose price includes veilstone must leave the army's
        /// earmark in the bank.</summary>
        public int surplusUpgradesPerThink;

        /// <summary>Huts are levelled only while free housing (popMax - pop)
        /// is at most this. A Hut level buys population and nothing else.</summary>
        public int hutUpgradeHeadroomMax;
    }
}
