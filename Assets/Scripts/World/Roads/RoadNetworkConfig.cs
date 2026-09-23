using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World.Roads
{
    /// <summary>
    /// Every tunable the procedural road network uses (docs/Design/Roads.md
    /// §4, §7). The asset is RoadNetwork.asset, beside RoadNetwork.cs. No
    /// field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Road Network",
                     fileName = "RoadNetwork")]
    public sealed class RoadNetworkConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Texels per side of the road mask. 512 ≈ 0.4 m on a 192 m map.</summary>
        public int maskResolution;

        /// <summary>Added to half a building's longer footprint side (metres).</summary>
        public float plazaMargin;
        /// <summary>Disc around a resource / curse node, metres.</summary>
        public float nodeDiscRadius;
        /// <summary>Full width of a road in a network that has buildings, metres.</summary>
        public float roadWidth;
        /// <summary>Full width of a trail in a network of nodes only, metres.</summary>
        public float trailWidth;

        /// <summary>A* step cost multiplier per metre of height change.</summary>
        public float slopePenalty;
        /// <summary>A* expansion budget per edge; beyond it the edge is dropped.</summary>
        public int maxRouteExpansions;
        /// <summary>Chaikin passes over the cell path - the FALLBACK when the
        /// spline cannot stay on passable ground.</summary>
        public int smoothingPasses;

        // -- Curves (Roads.md 3.3) --
        /// <summary>Douglas-Peucker tolerance, metres: cells that bend the
        /// route by less than this are not knots.</summary>
        public float simplifyTolerance;
        /// <summary>How far a road wanders sideways from its routed line, metres.</summary>
        public float meanderAmplitude;
        /// <summary>Distance between spline knots along the road, metres - the
        /// meander's wavelength.</summary>
        public float meanderWavelength;
        /// <summary>Spline sampling step, metres.</summary>
        public float curveSampleStep;

        // -- Plazas (Roads.md 4) --
        /// <summary>Rim radius variation, as a fraction of the radius. 0.3 = +-30 %.</summary>
        public float plazaEdgeNoise;
        /// <summary>Coverage is full out to this fraction of the radius and fades to the rim.</summary>
        public float plazaFadeStart;
        /// <summary>Road coverage is full out to this fraction of the half width and fades to the rim.</summary>
        public float roadEdgeFade;

        // -- Carriage tracks (shader) --
        /// <summary>Where the wheel ruts sit across the road, 0 = centre, 1 = rim.</summary>
        public float rutOffset;
        /// <summary>Rut width, in the same 0..1 lateral units.</summary>
        public float rutWidth;
        /// <summary>How much darker the rut floor is, 0..1.</summary>
        public float rutDepth;
        /// <summary>How much of the original ground shows through between the ruts, 0..1.</summary>
        public float centreStrip;

        /// <summary>Feather of the noise erosion at a road's edge (shader).</summary>
        public float edgeFeather;
        /// <summary>Multiplier on the earth texture at a road's worn core, 1 =
        /// none. Above 1 lifts the path ABOVE the moss, which is how a worn dry
        /// path reads; the first cut at 0.8 sank it to near-black (shader).</summary>
        public float earthenDarken;

        /// <summary>How often the site set is re-read, seconds.</summary>
        public float pollSeconds;
        /// <summary>Edges routed (A* + spline) per poll at most; the rest wait
        /// for the next poll. Spreads the match-start burst.</summary>
        public int maxRoutesPerPoll;

        // -- Fades (Roads.md 6) --
        /// <summary>A new plaza or road wears in over this many seconds.</summary>
        public float fadeInSeconds;
        /// <summary>A plaza or road whose building is gone is reclaimed over
        /// this many seconds - deliberately slow, the grass takes its time.</summary>
        public float fadeOutSeconds;
        /// <summary>Re-raster interval while something is wearing IN, seconds.</summary>
        public float fastRasterSeconds;
        /// <summary>Re-raster interval while only reclaims are running, seconds.</summary>
        public float slowRasterSeconds;
    }
}
