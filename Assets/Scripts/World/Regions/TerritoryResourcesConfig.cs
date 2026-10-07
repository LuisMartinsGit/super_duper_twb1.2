using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World.Regions
{
    /// <summary>
    /// The territory-type DISTRIBUTION <see cref="TerritoryResources"/> deals
    /// out of the match seed (docs/Design/Territory_Claims.md §11). The asset
    /// is TerritoryResources.asset, beside TerritoryResources.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Read while the map is generated on every lockstep peer, so every peer
    /// must load the SAME asset — it ships with the build like every other
    /// config.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Territory Resources",
                     fileName = "TerritoryResources")]
    public sealed class TerritoryResourcesConfig : ScriptableObject, IComponentConfig
    {
        // ── The filler deal (§11): every Auto territory left after the
        // special types is Normal, Normal + iron or Normal + veilstone, in
        // these proportions (weights, not percentages — 1 / 2 / 4 deals
        // 1 Normal, 2 + iron and 4 + veilstone in every 7). ──
        public int normalWeight;
        public int normalIronWeight;
        public int normalVeilstoneWeight;

        // ── Outcrops in a Normal + veilstone territory: a seeded draw per
        // territory, inclusive range. ──
        public int normalVeilstoneOutcropsMin;
        public int normalVeilstoneOutcropsMax;

        /// <summary>HOME NODES ALONG THE EDGE (2026-10-07): in a home
        /// territory, a node's centre stands at least this far from the start
        /// on either axis (a square, like the territory), so the AI's main
        /// camp and its wall ring stay clear and the nodes line the
        /// territory's edge. Metres; 0 = anywhere outside the start clearing.</summary>
        public float homeNodeMinOffset;

        /// <summary>THE CURSE STARTS SOMEWHERE NEW EVERY MATCH (2026-10-07):
        /// the authored curse territories (Veilstone rich) swap types with
        /// territories drawn from the match seed — on a mirrored map one per
        /// quadrant, mirrored, so the map stays fair; never a home or a
        /// territory bordering one.</summary>
        public bool randomizeCurseTerritories;

        /// <summary>NODE PURITY (2026-10-07, Territory_Claims.md § 11.2): what a
        /// Pure / Normal / Poor node's slot pays, as a multiplier on its rate.</summary>
        public float purityPureMultiplier;
        public float purityNormalMultiplier;
        public float purityPoorMultiplier;

        /// <summary>The bag every node outside a start territory is dealt from:
        /// this many Pure, Normal and Poor per bag, shuffled, refilled when
        /// empty. Start territories' nodes are all Pure.</summary>
        public int purityBagPure;
        public int purityBagNormal;
        public int purityBagPoor;
    }
}
