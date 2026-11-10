using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Tunables for the Counting House (docs/Design/Sects.md § Sect of
    /// Prosperity, Plenty). The asset is CountingHouse.asset, beside
    /// CountingHouse.cs. No field initialisers. The per-level arrays are
    /// indexed by Plenty level - 1 (I, II, III).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Counting House",
                     fileName = "CountingHouse")]
    public sealed class CountingHouseConfig : ScriptableObject, IComponentConfig
    {
        static CountingHouseConfig _i;
        public static CountingHouseConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<CountingHouseConfig>());

        /// <summary>Most Counting Houses one player may hold; Plenty raises no
        /// more past it (2026-11-10, developer: "a cap of 5 per player").</summary>
        public int maxPerPlayer;

        /// <summary>The capture ring's radius, in metres.</summary>
        public float captureRadius;
        /// <summary>Seconds a side must stay inside the ring alone to take it.</summary>
        public int captureSeconds;

        /// <summary>Footprint edge in build cells (square).</summary>
        public int footprintCells;
        /// <summary>Metres round the aimed point searched for valid ground.</summary>
        public float placementSearchRadius;
        /// <summary>Metres between searched offsets (at least one build cell).</summary>
        public float placementSearchStep;

        public int[] suppliesPerMinute;
        public int[] ironPerMinute;
        public int[] veilstonePerMinute;
        /// <summary>Seconds per religion point (RP) at each level; 0 = none.</summary>
        public float[] secondsPerReligionPoint;

        public float selectionRadius;

        public GameObject modelPrefab;
        public float modelScale;
        public float lightHeight;
        public float lightRange;
        public float lightIntensity;
        /// <summary>Ring and light colour when no faction holds it.</summary>
        public Color neutralColor;
        /// <summary>The always-visible outer edge, as a share of the radius.</summary>
        public float circleEdge;
        public int circleResolution;
    }
}
