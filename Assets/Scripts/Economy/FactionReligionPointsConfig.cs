using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Every number of the religion economy (docs/Design/Religion.md,
    /// 2026-09-29): what a curse kill pays, how many points make a Religion
    /// Point, what the Tithe sells one for, and what RP buys. The asset is
    /// FactionReligionPoints.asset, beside FactionReligionPoints.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Read by the lockstep executors, so every peer loads the same asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Faction Religion Points",
                     fileName = "FactionReligionPoints")]
    public sealed class FactionReligionPointsConfig : ScriptableObject, IComponentConfig
    {
        // ── Points per curse kill (§1) ──
        public int ptsCrystalling;
        public int ptsVeilstinger;
        public int ptsGodsplinter;

        // ── The Temple's slow trickle (§2): one point every N seconds ──
        public int templeSecondsPerPoint;

        // ── A standing Temple boosts curse-kill points by this percent (§2) ──
        public int templeKillBonusPct;

        // ── Points per Religion Point: min(base + step x (n - 1), cap) ──
        public int ptsBase;
        public int ptsStep;
        public int ptsCap;

        // ── Tithe (§1.1): RP for resources, dearer each purchase ──
        public int titheSupplies;
        public int titheIron;
        public int titheVeilstone;
        public float titheStep;

        // ── What RP buys (§2-§4) ──
        public int templeRp;
        public int unlockSecondRp;
        public int unlockWildcardRp;
        public int chapelLevel2Rp;
        public int chapelLevel3Rp;
        public int heroRp;
    }
}
