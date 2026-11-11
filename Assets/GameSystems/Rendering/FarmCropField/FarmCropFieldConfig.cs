using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="FarmCropField"/> — the cereal on a Farm's crop
    /// ring and the tilled soil under it (docs/Design/Age_1_Alanthor.md § The
    /// Farm, "The field's look"). The asset is FarmCropField.asset, beside
    /// FarmCropField.cs. No field initialisers: the values live in the asset.
    /// Colours are sRGB; the field's GEOMETRY (where the ring is) is not here,
    /// it is FarmRing's, read from Farm.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Farm Crop Field",
                     fileName = "FarmCropField")]
    public sealed class FarmCropFieldConfig : ScriptableObject, IComponentConfig
    {
        // -- Shaders (referenced here so they ship in the build) --
        /// <summary>TheWaningBorder/FarmCrop — the instanced cereal.</summary>
        public Shader cropShader;
        /// <summary>TheWaningBorder/FarmSoil — the tilled ground under it.</summary>
        public Shader soilShader;

        // -- Layout --
        /// <summary>Distance between tufts on the jittered grid, metres.</summary>
        public float tuftSpacing;
        /// <summary>Grid jitter as a share of the spacing (0..0.5).</summary>
        public float tuftJitter;
        /// <summary>Stalks in one tuft mesh.</summary>
        public int stalksPerTuft;
        /// <summary>Leaf blades at the foot of one tuft.</summary>
        public int leavesPerTuft;
        /// <summary>Radius the tuft's stalks stand in, metres.</summary>
        public float tuftRadius;
        /// <summary>Full-grown stalk height range, metres.</summary>
        public float stalkHeightMin;
        public float stalkHeightMax;
        /// <summary>Stalk ribbon width at the foot and under the ear, metres.</summary>
        public float stalkWidthBase;
        public float stalkWidthTop;
        /// <summary>Ear length and half-thickness, metres.</summary>
        public float earLength;
        public float earRadius;
        /// <summary>Leaf blade length and width, metres.</summary>
        public float leafLength;
        public float leafWidth;
        /// <summary>The last metres inside the ring's outer circle thin out.</summary>
        public float edgeFadeMeters;

        // -- Growth --
        /// <summary>Seconds from bare soil to ripe with nobody in the Farm.</summary>
        public float growSeconds;
        /// <summary>A full Farm grows (1 + this) times as fast; scales with Workers inside.</summary>
        public float workerGrowthBonus;
        /// <summary>Height share of the tiny sprouts at growth 0.</summary>
        public float sproutHeight;
        /// <summary>Growth at which the ears start to show.</summary>
        public float earAppearAt;
        /// <summary>Growth at which the green starts to turn to straw and gold.</summary>
        public float ripenStart;

        // -- Colour (sRGB) --
        public Color colorBase;
        public Color colorYoung;
        public Color colorStalkRipe;
        public Color colorEarYoung;
        public Color colorEarRipe;
        /// <summary>Per-tuft brightness spread, ± share.</summary>
        public float colorVariation;
        /// <summary>Sunlight through the blades when the sun is behind them.</summary>
        public float translucency;
        public float smoothness;
        /// <summary>Light reaching the foot of the stalks (fake self-shadow), 0..1.</summary>
        public float footOcclusion;
        /// <summary>How far the lighting normal leans toward up (0..1): softer field shading.</summary>
        public float normalUpBias;

        // -- Wind --
        public float windStrength;
        public float windSpeed;
        /// <summary>Waves per metre across the field.</summary>
        public float windScale;
        public float gustStrength;
        public Vector3 windDirection;

        // -- Roads (the road mask's coverage where stalks start / stop) --
        public float roadCutLow;
        public float roadCutHigh;

        // -- Cost --
        /// <summary>Within this camera distance every tuft draws, metres.</summary>
        public float lodNearDistance;
        /// <summary>At and beyond this distance only lodFarFraction of them do.</summary>
        public float lodFarDistance;
        public float lodFarFraction;
        public bool castShadows;
        /// <summary>Seconds between checks for a changed blocked set around each Farm.</summary>
        public float rebuildInterval;

        // -- The level-2 fence (L3's low stone wall: FarmStoneWallSystem.asset) --
        /// <summary>Synty fence pieces (one picked at random per span).</summary>
        public GameObject[] fencePieces;
        /// <summary>Span of one piece along the edge, metres.</summary>
        public float fencePieceLength;
        /// <summary>Height each piece is fitted to, metres.</summary>
        public float fenceHeight;
        /// <summary>The outer fence stands this far inside the ring's rim, metres.</summary>
        public float fenceInset;
        /// <summary>How far behind a piece the "does crop grow here" probe looks, metres.</summary>
        public float fenceCropProbe;
        /// <summary>Road-mask coverage the road-edge fence follows (just off the road).</summary>
        public float roadFenceCoverage;
        /// <summary>A piece touching more road coverage than this is dropped (never across a road).</summary>
        public float roadBlockCoverage;
        /// <summary>Grid step of the road contour trace, metres.</summary>
        public float fenceGridMeters;
        /// <summary>Width of the fence's opening facing the Farm's door, metres
        /// (the door's direction: FarmStoneWall.DoorDirection).</summary>
        public float doorOpeningMeters;

        // -- Soil --
        public bool soilEnabled;
        public Color soilColor;
        public Color soilFurrowColor;
        public float soilFurrowSpacing;
        public float soilOpacity;
        /// <summary>Soil mesh grid step, metres.</summary>
        public float soilGridMeters;
        /// <summary>The soil mesh floats this far above the terrain, metres.</summary>
        public float soilLift;
    }
}
