using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Numbers for <see cref="TradingOutpostSystem"/> and the Outpost's
    /// placement reach (docs/Design/Veilstone_Economy.md §3.1, rewritten
    /// 2026-11-09). The asset is TradingOutpostSystem.asset, beside the class.
    /// Every rate is PER MINUTE, the unit the player is shown.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Economy/TradingOutpostSystem",
                     fileName = "TradingOutpostSystem")]
    public sealed class TradingOutpostSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Seconds between payment cycles (how lumpy the bank looks,
        /// not how much is paid).</summary>
        public float cycleSeconds;

        /// <summary>What one post pays per minute beside a SUPPLY node, before
        /// the node's purity and the post's level.</summary>
        public float trickleSuppliesPerMinute;
        /// <summary>... beside an IRON node.</summary>
        public float trickleIronPerMinute;
        /// <summary>... beside an uncursed VEILSTONE outcrop.</summary>
        public float trickleVeilstonePerMinute;

        /// <summary>The research that unlocks building the Outpost (Trade
        /// Charters, Fortress L3).</summary>
        public string unlockTech;

        /// <summary>Placement snap reach: metres from the cursor (or a
        /// candidate site) to a node's centre within which the Outpost
        /// snaps to the nearest free side of that node.</summary>
        public float nodeReach;

        /// <summary>
        /// THE PER-NODE COST RAMP (Veilstone_Economy.md §3.1). Up to four
        /// Outposts stand around one node, one per side; entry N multiplies the
        /// build price of the node's (N+1)-th post. Index = how many posts
        /// already stand beside that node (plus the builder's own plans there).
        /// A new node starts again at entry 0. Past the end the last entry holds.
        /// </summary>
        public float[] outcropRampMultipliers;
    }
}
