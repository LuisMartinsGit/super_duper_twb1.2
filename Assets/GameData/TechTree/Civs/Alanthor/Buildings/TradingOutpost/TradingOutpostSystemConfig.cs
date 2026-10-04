using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Numbers for <see cref="TradingOutpostSystem"/> and the Outpost's
    /// placement reach (docs/Design/Veilstone_Economy.md §3.1). The asset is
    /// TradingOutpostSystem.asset, beside the class. Every rate is PER MINUTE,
    /// the unit the player is shown; the system converts to its cycle.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Economy/TradingOutpostSystem",
                     fileName = "TradingOutpostSystem")]
    public sealed class TradingOutpostSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Seconds between trade cycles (how lumpy the bank looks,
        /// not how much is traded).</summary>
        public float cycleSeconds;

        /// <summary>Buy Veilstone (default): supplies and iron spent per minute.</summary>
        public float buySupplies;
        public float buyIron;
        /// <summary>Veilstone bought per minute.</summary>
        public float buyVeilstone;

        /// <summary>Forge Veilsteel: veilstone spent per minute.</summary>
        public float forgeVeilstone;
        /// <summary>Veilsteel forged per minute.</summary>
        public float forgeVeilsteel;

        /// <summary>Sell Veilsteel: veilsteel spent per minute.</summary>
        public float sellVeilsteel;
        /// <summary>Iron and supplies earned per minute.</summary>
        public float sellIron;
        public float sellSupplies;

        /// <summary>Research that unlocks Forge Veilsteel.</summary>
        public string forgeTech;
        /// <summary>Research that unlocks Sell Veilsteel.</summary>
        public string sellTech;

        /// <summary>The discount research ladder, cheapest first.</summary>
        public string[] discountTechs;
        /// <summary>Percent off every recipe's INPUTS at each discount tier.</summary>
        public float[] discountPercent;

        /// <summary>Research that speeds every trade up (Swift Caravans). Each
        /// one's effectsList carries a "TradeSpeed" Pct entry (target
        /// building:Alanthor_TradingOutpost): the recipe's spend AND earn per
        /// minute are multiplied by 1 + percent/100.</summary>
        public string[] speedTechs;

        /// <summary>Placement snap reach: metres from the cursor (or a
        /// candidate site) to an outcrop's centre within which the Outpost
        /// snaps to the nearest free side of that outcrop.</summary>
        public float nodeReach;

        /// <summary>
        /// THE PER-OUTCROP COST RAMP (2026-10-04, Veilstone_Economy.md §3.1).
        /// Up to four Outposts stand around one outcrop, one per side; entry
        /// N multiplies the build price AND every level-up price of the
        /// outcrop's (N+1)-th post. Index = how many posts already stand
        /// beside that outcrop (plus the builder's own plans there). A new
        /// outcrop starts again at entry 0. Past the end the last entry holds.
        /// </summary>
        public float[] outcropRampMultipliers;
    }
}
