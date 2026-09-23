using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="CurseBarrierVfx"/> — the veil at the edge of
    /// cursed ground (Art_Direction.md §6.4). The asset is
    /// CurseBarrierVfx.asset, beside CurseBarrierVfx.cs. No field
    /// initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Curse Barrier Vfx",
                     fileName = "CurseBarrierVfx")]
    public sealed class CurseBarrierVfxConfig : ScriptableObject, IComponentConfig
    {
        // -- Ribbon --
        /// <summary>Height of the standing veil above the ground, metres.</summary>
        public float ribbonHeight;
        /// <summary>The ribbon's base sits this far above the terrain, metres.</summary>
        public float groundOffset;
        /// <summary>Ribbon vertex spacing along the boundary, metres.</summary>
        public float ribbonSpacing;
        /// <summary>The veil is an arch in cross-section: this is the distance
        /// from the boundary line to each foot, metres.</summary>
        public float archHalfWidth;

        // -- Wisps (particles) --
        /// <summary>Wisps spawned per metre of boundary per second.</summary>
        public float wispsPerMetrePerSecond;
        /// <summary>Hard cap on wisps per territory, whatever its perimeter.</summary>
        public int maxWispsPerTerritory;
        public float wispLifetimeMin;
        public float wispLifetimeMax;
        public float wispSizeMin;
        public float wispSizeMax;
        /// <summary>Upward drift, metres per second.</summary>
        public float wispRiseSpeed;
        /// <summary>Turbulence strength (ParticleSystem noise module).</summary>
        public float wispNoiseStrength;
        public float wispNoiseFrequency;
        /// <summary>HDR wisp colours: the veil is where veilstone and curse meet.</summary>
        public Color wispCyan;
        public Color wispPurple;
        /// <summary>Peak alpha of a wisp over its life.</summary>
        public float wispPeakAlpha;
    }
}
