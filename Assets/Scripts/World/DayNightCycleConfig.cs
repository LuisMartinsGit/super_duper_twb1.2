using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World
{
    /// <summary>
    /// Every tunable the match atmosphere uses — sun, ambient, fog, the void
    /// beyond the map, the post-processing grade, shadows and cloud shadows.
    /// The asset is DayNightCycle.asset, beside DayNightCycle.cs, and its
    /// values are the "starting recipe" in docs/Design/Art_Direction.md §3.2.
    ///
    /// There are DELIBERATELY no field initialisers here. The values live in
    /// the asset and nowhere else — a C# initialiser would be a second source
    /// of truth that silently wins whenever the asset is missing a value.
    ///
    /// DayNightCycle re-reads this every frame, so editing the asset in Play
    /// mode is live — and, because it is an asset, the edit PERSISTS when Play
    /// mode ends. That is the intended tuning loop: tune in Play, keep.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Day Night Cycle",
                     fileName = "DayNightCycle")]
    public sealed class DayNightCycleConfig : ScriptableObject, IComponentConfig
    {
        // ── Sun ──
        public float sunPitch;
        public float sunHeading;
        /// <summary>Slightly cool, never saturated blue: colour temperature
        /// comes from the ambient and the shadow split-tone (the one-tint
        /// rule, Art_Direction.md §3.1).</summary>
        public Color sunColor;
        /// <summary>The sun is fill; the lanterns are key.</summary>
        public float sunIntensity;
        public float shadowStrength;

        // ── Ambient (Trilight — no bake needed) ──
        public Color ambientSkyColor;
        public Color ambientEquatorColor;
        public Color ambientGroundColor;

        // ── Fog ──
        public Color fogColor;
        /// <summary>Exponential-squared density. 0 disables fog. 0.005 ate the
        /// map at RTS zoom-out (89% fog at 300 m); 0.0015 is 2% at 120 m and
        /// 12% at 300 m — a horizon fade only.</summary>
        public float fogDensity;

        // ── The void beyond the map ──
        /// <summary>Camera clear colour. Unexplored fog and the map edge fade
        /// into this, so it reads as mist rather than a hole.</summary>
        public Color voidColor;

        // ── Post-processing grade ──
        /// <summary>
        /// The tonemapping curve. 0 None, 1 Neutral, 2 ACES.
        ///
        /// This used to be a hard-coded ACES in DayNightCycle, which is the
        /// single biggest thing a stylised look fights: ACES is a filmic curve
        /// built for photographic realism and it desaturates bright colours
        /// toward white. Saturation pushed below it is eaten in the
        /// highlights. Neutral keeps colour true, which is what a cartoon
        /// palette needs. docs/Design/Art_Direction.md § Stylised look.
        /// </summary>
        public int tonemapping;

        /// <summary>
        /// An authored colour LUT (a 1024 x 32 unrolled strip) and how much of
        /// it to apply. THE stylisation tool: one bake carries the hue shifts,
        /// the shadow lift and the saturation that would otherwise be a pile
        /// of separate grade tweaks, and swapping the texture A/Bs a whole
        /// look. Null = no lookup, the rest of the grade stands alone.
        /// </summary>
        public Texture lutTexture;
        public float lutContribution;

        public float vignetteIntensity;
        public Color vignetteColor;
        public float vignetteSmoothness;
        public float bloomIntensity;
        /// <summary>Only HDR (> 1) surfaces cross the threshold: the emissive
        /// ladder in Art_Direction.md §3.4 is the only thing that glows.</summary>
        public float bloomThreshold;
        public float bloomScatter;
        /// <summary>Never used to set mood — brightness comes from emissives
        /// and ambient, not exposure (§3.1).</summary>
        public float postExposure;
        public float saturation;
        public float contrast;
        public Color smhShadowsTint;
        public Color smhHighlightsTint;
        public float filmGrainIntensity;
        public float filmGrainResponse;

        // ── Shadows ──
        /// <summary>The AUTHORITY for shadow distance — pushed over the pipeline
        /// asset at startup, so tuning the asset alone does not stick.</summary>
        public float shadowDistance;
        public int shadowCascadeCount;

        // ── Cloud shadows ──
        public bool cloudShadows;
        public float cloudOpacity;
        public float cloudSpeed;
        public float cloudScale;
        public float cloudProjectorSize;
    }
}
