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
    }
}
