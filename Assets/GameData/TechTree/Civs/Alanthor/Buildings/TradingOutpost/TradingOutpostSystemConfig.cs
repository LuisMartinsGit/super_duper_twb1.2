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

        /// <summary>Metres from the Outpost's centre to the outcrop it serves.</summary>
        public float nodeReach;
    }
}
