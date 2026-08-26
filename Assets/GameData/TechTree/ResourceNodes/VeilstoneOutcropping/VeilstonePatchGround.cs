// VeilstonePatchGround.cs
// Where the veilstone patches are, for the terrain painter.
//
// A patch is ore-bearing GROUND, not just a cluster of props: the ground it
// sits on gets its own terrain layer ("VeilstonePatch") so a patch reads as a
// mineral seam from across the map instead of gems dropped on plain grass.
// Deliberately its own layer and NOT the curse's — a patch is a resource, and
// borrowing CurseInfluence made ordinary mining ground look cursed.
//
// The bootstrap registers one disc per patch as it spawns; InfluenceTerrainPainter
// samples CoverageAt per alphamap texel, exactly like it samples the influence
// and blood maps. Discs rather than per-cell sets because the patch fill is
// already a compact block of cells (see VeilstoneOutcroppingBootstrap), so a
// radial falloff matches its shape while staying O(patches) per texel instead
// of a hash lookup with no soft edge.

using System.Collections.Generic;
using Unity.Mathematics;

namespace TheWaningBorder.Entities
{
    public static class VeilstonePatchGround
    {
        /// <summary>How far the painted ground fades out past the last node's
        /// cell. Wide enough to read as a seam the patch sits in rather than a
        /// disc stamped under it.</summary>
        private const float EdgeFade = 4f;

        private struct Patch
        {
            public float X, Z;
            public float Core;    // full-strength radius
            public float Outer;   // Core + EdgeFade
        }

        private static readonly List<Patch> _patches = new();

        /// <summary>Drop every registered patch. Called at the start of each
        /// match's veilstone bootstrap so a reload doesn't inherit the previous
        /// map's seams.</summary>
        public static void Clear() => _patches.Clear();

        /// <summary>True when at least one patch is registered — lets the
        /// painter skip the sample entirely on maps with no veilstone.</summary>
        public static bool Any => _patches.Count > 0;

        /// <summary>
        /// Register a spawned patch. <paramref name="nodesPlaced"/> is the
        /// actual node count, so a patch that lost cells to bad terrain paints
        /// the ground it really covers.
        /// </summary>
        public static void Register(float3 centre, int nodesPlaced)
        {
            if (nodesPlaced <= 0) return;

            // Radius of the disc that holds `nodesPlaced` build cells, plus
            // half a cell so the outermost cells are covered corner to corner.
            float core = math.sqrt(nodesPlaced / math.PI) * BuildGrid.CellSize + BuildGrid.HalfCell;
            _patches.Add(new Patch
            {
                X = centre.x,
                Z = centre.z,
                Core = core,
                Outer = core + EdgeFade,
            });
        }

        /// <summary>
        /// Painted weight at a world XZ: 1 inside a patch, easing to 0 across
        /// EdgeFade, max across overlapping patches. Smoothstep rather than a
        /// linear ramp so the seam edge doesn't read as a drawn circle.
        /// </summary>
        public static float CoverageAt(float wx, float wz)
        {
            float best = 0f;
            for (int i = 0; i < _patches.Count; i++)
            {
                var p = _patches[i];

                // Cheap AABB reject before the sqrt.
                float dx = wx - p.X;
                if (dx > p.Outer || dx < -p.Outer) continue;
                float dz = wz - p.Z;
                if (dz > p.Outer || dz < -p.Outer) continue;

                float d2 = dx * dx + dz * dz;
                if (d2 >= p.Outer * p.Outer) continue;
                if (d2 <= p.Core * p.Core) return 1f;

                float t = 1f - (math.sqrt(d2) - p.Core) / EdgeFade;
                t = math.smoothstep(0f, 1f, math.saturate(t));
                if (t > best) best = t;
            }
            return best;
        }
    }
}
