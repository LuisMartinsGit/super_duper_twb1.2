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
        /// saved for ahead of everything (L2 raises the home territory's
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

        /// <summary>FACTION-WIDE LEVELS (2026-10-19): a level is priced
        /// against soldiers. The unit whose SO cost (all four resources
        /// summed) is "one soldier". Empty = the value gate is off.</summary>
        public string referenceUnitId;

        /// <summary>Soldiers one production building's level is worth (a
        /// Barracks, Archery Range, Stable, Siege Yard and their Runai /
        /// Feraldis twins). A level is bought only while buildings-of-the-type
        /// x this >= its price in soldiers — more Barracks, a level worth
        /// more. 0 = no gate for the category.</summary>
        public float soldiersPerProductionBuilding;

        /// <summary>The same, for an economy building (Gatherer's Hut, Trading
        /// Outpost).</summary>
        public float soldiersPerEconomyBuilding;

        /// <summary>The same, for everything else in the rotation (Watch
        /// Tower, Vault, Sanctuary, ...). Not the capital: its priority levels
        /// are bought on their own rule, and not the housing Hut (need-gated).</summary>
        public float soldiersPerOtherBuilding;

        /// <summary>While the bank overflows (AIBudget.BankOverflowing) the
        /// value gate is skipped: the basics cap leaves nothing to train.</summary>
        public bool surplusIgnoresValue;
    }
}
