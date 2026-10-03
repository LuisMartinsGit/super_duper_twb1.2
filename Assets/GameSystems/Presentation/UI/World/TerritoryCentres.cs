// TerritoryCentres.cs
// Where a world-space overlay about a TERRITORY floats: the centroid of its
// ground, sampled once per partition from RegionMap — not its Voronoi seed,
// which can sit near an edge. A concave territory whose centroid falls outside
// it anchors on its own sample nearest the centroid instead.
//
// Shared by TerritoryClaimBars (takeovers) and IncomeOverlay (income), so the
// two overlays about one territory stand on the same point. Presentation only.

using UnityEngine;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.World
{
    public static class TerritoryCentres
    {
        /// <summary>Metres above the ground the anchor floats.</summary>
        private const float AnchorHeight = 6f;
        /// <summary>Region sampling step, metres.</summary>
        private const float SampleStep = 4f;

        private static Vector3[] _centres;
        private static int _version = int.MinValue;

        /// <summary>The anchor over territory <paramref name="t"/>, or false
        /// before the regions exist.</summary>
        public static bool TryGet(int t, out Vector3 centre)
        {
            centre = default;
            Ensure();
            if (_centres == null || t < 0 || t >= _centres.Length) return false;
            centre = _centres[t];
            return true;
        }

        private static void Ensure()
        {
            if (!RegionMap.Ready) return;
            if (_centres != null && _version == RegionMap.Version) return;
            _version = RegionMap.Version;

            int n = RegionMap.Count;
            var sum = new Vector2[n];
            var count = new int[n];

            TerrainUtility.GetPlayableBounds(out var min, out var max);
            for (float z = min.y + SampleStep * 0.5f; z < max.y; z += SampleStep)
                for (float x = min.x + SampleStep * 0.5f; x < max.x; x += SampleStep)
                {
                    int r = RegionMap.RegionAt(x, z);
                    if (r < 0 || r >= n) continue;
                    sum[r] += new Vector2(x, z);
                    count[r]++;
                }

            var best = new Vector2[n];
            var bestD = new float[n];
            var mean = new Vector2[n];
            for (int r = 0; r < n; r++)
            {
                mean[r] = count[r] > 0 ? sum[r] / count[r] : RegionMap.SeedOf(r);
                best[r] = mean[r];
                bestD[r] = float.MaxValue;
            }
            for (float z = min.y + SampleStep * 0.5f; z < max.y; z += SampleStep)
                for (float x = min.x + SampleStep * 0.5f; x < max.x; x += SampleStep)
                {
                    int r = RegionMap.RegionAt(x, z);
                    if (r < 0 || r >= n) continue;
                    float d = (new Vector2(x, z) - mean[r]).sqrMagnitude;
                    if (d < bestD[r]) { bestD[r] = d; best[r] = new Vector2(x, z); }
                }

            _centres = new Vector3[n];
            for (int r = 0; r < n; r++)
            {
                Vector2 c = RegionMap.RegionAt(mean[r].x, mean[r].y) == r ? mean[r] : best[r];
                _centres[r] = new Vector3(c.x, TerrainUtility.GetHeight(c.x, c.y) + AnchorHeight, c.y);
            }
        }
    }
}
