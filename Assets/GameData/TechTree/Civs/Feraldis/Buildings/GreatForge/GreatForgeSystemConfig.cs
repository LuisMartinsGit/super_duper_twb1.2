using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Feraldis
{
    /// <summary>
    /// Every number of the Great Forge (docs/Design/Age_1_Feraldis.md §4). The
    /// asset is GreatForgeSystem.asset, beside <see cref="GreatForgeSystem"/>.
    /// Rates are per minute at bay level 1 with no crew and no research; the
    /// Forge loop is tuned to match the Alanthor Trading Outpost loop at equal
    /// investment (TradingOutpostSystem.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Feraldis/GreatForgeSystem",
                     fileName = "GreatForgeSystem")]
    public sealed class GreatForgeSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Seconds between production cycles.</summary>
        public float cycleSeconds;
        /// <summary>Most bays one Forge may hold.</summary>
        public int maxBays;
        /// <summary>Bays a new Forge starts with (free).</summary>
        public int startingBays;

        /// <summary>Price of a new bay before the ramp (supplies / iron).</summary>
        public int baySupplies;
        public int bayIron;
        /// <summary>Price multiplier for the 1st, 2nd, 3rd, 4th bay bought.</summary>
        public float[] bayCostRamp;

        /// <summary>Upgrade price of a bay to level 2 and to level 3 (index 0 = L2).</summary>
        public int[] bayUpgradeSupplies;
        public int[] bayUpgradeIron;
        public int[] bayUpgradeVeilstone;
        /// <summary>Rate multiplier at bay level 1, 2, 3.</summary>
        public float[] bayLevelRate;

        /// <summary>Soulforge: slaves consumed a minute, and veilstone made from each.</summary>
        public float soulforgeSlavesPerMinute;
        public int soulforgeVeilstonePerSlave;

        /// <summary>Veilsteel recipe per minute: inputs, output.</summary>
        public float veilsteelInVeilstone;
        public float veilsteelInIron;
        public float veilsteelOut;

        /// <summary>Arms Export per minute: veilsteel in, iron out.</summary>
        public float exportInVeilsteel;
        public float exportOutIron;

        /// <summary>Extra speed from a FULL stoking crew (0.5 = +50 %), scaled by fill and work multiplier.</summary>
        public float crewSpeedAtFull;

        /// <summary>Bellows I-III ids and the input discount each gives (percent, cumulative = the highest held).</summary>
        public string[] bellowsTechs;
        public float[] bellowsDiscountPercent;
        /// <summary>Draught Furnaces id and its speed-up (percent).</summary>
        public string draughtTech;
        public float draughtSpeedPercent;
    }
}
