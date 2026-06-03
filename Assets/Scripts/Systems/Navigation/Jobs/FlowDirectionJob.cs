// FlowDirectionJob.cs
// Converts the per-cell integration field into a per-cell direction byte
// indexing the 256-entry DirectionTableBlob (CCD-3). Parallel-for over
// cells — each Execute writes exactly one Dir[i], reads only Integration
// (read-only), so the parallel-for restriction is naturally satisfied.
//
// Determinism notes:
//   * The chosen neighbour is the one with the strictly smallest
//     integration value; on a tie, the lexicographically earlier neighbour
//     in the locked order [+x, -x, +z, -z, +x+z, +x-z, -x+z, -x-z] wins.
//   * No float math anywhere in the hot loop — the resulting direction
//     byte is computed by integer index lookup.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/FlowDirectionJob.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Writes the direction byte for one cell index per <see cref="Execute"/>
    /// call. Looks at the 8 neighbours; picks the one with the smallest
    /// integration cost; converts the (dx, dz) delta to a 0..255 angle
    /// index using a small lookup of the eight neighbour offsets.
    /// </summary>
    [BurstCompile]
    internal struct FlowDirectionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint> Integration;
        [ReadOnly] public NativeArray<byte> Cost;
        [WriteOnly] public NativeArray<byte> Dir;
        public int Width;
        public int Height;
        public int2 Goal;

        // Locked neighbour order (dx, dz, direction-byte). The dir bytes
        // pre-compute (atan2(dz, dx) / 2π * 256) so consumers can pull the
        // matching unit vector straight out of DirectionTableBlob.Dirs.
        // Order matters for tie-break determinism (DR-3 family).
        //
        //  +x      → angle 0        →   0
        //  +x+z    → angle π/4      →  32
        //  +z      → angle π/2      →  64
        //  -x+z    → angle 3π/4     →  96
        //  -x      → angle π        → 128
        //  -x-z    → angle 5π/4     → 160
        //  -z      → angle 3π/2     → 192
        //  +x-z    → angle 7π/4     → 224
        //
        // Visit order [+x, -x, +z, -z, +x+z, +x-z, -x+z, -x-z] is preserved
        // by the explicit ladder below.

        public void Execute(int idx)
        {
            int x = idx % Width;
            int z = idx / Width;
            uint here = Integration[idx];

            // Unreachable cells / impassable cells get the no-direction sentinel.
            if (here == NavFlowConstants.UnreachableIntegration
                || Cost[idx] == NavCostField.CostImpassable)
            {
                Dir[idx] = NavFlowConstants.NoDirection;
                return;
            }

            // Goal cell: no direction needed; the follower will treat
            // NoDirection as "stop".
            if (x == Goal.x && z == Goal.y)
            {
                Dir[idx] = NavFlowConstants.NoDirection;
                return;
            }

            // Compute a smooth gradient by weighted-sum over all 8 walkable
            // neighbours: each neighbour pulls the cell toward itself with
            // weight proportional to (here - neighbourCost). The resulting
            // (gx, gz) vector points toward lower integration. atan2 -> 256-
            // bin angle byte -- so flat-terrain Dijkstra produces a true
            // bearing-to-goal field instead of the 8-direction "best neighbour"
            // quantization that made units zig-zag on open ground.
            //
            // Determinism: float math here is permitted (Unity.Mathematics
            // math.* only). The accumulation order is the fixed neighbour
            // visit order [-z row, 0 row, +z row, left-to-right] so two
            // machines on the same Burst version produce identical gx/gz.
            float gx = 0f, gz = 0f;
            for (int dzz = -1; dzz <= 1; dzz++)
            for (int dxx = -1; dxx <= 1; dxx++)
            {
                if (dxx == 0 && dzz == 0) continue;
                int nx = x + dxx;
                int nz = z + dzz;
                if (nx < 0 || nx >= Width || nz < 0 || nz >= Height) continue;
                int nIdx = nz * Width + nx;
                if (Cost[nIdx] == NavCostField.CostImpassable) continue;
                if (Integration[nIdx] == NavFlowConstants.UnreachableIntegration) continue;

                // Diagonals require both adjacent cardinals walkable so the
                // gradient doesn't tunnel through wall corners.
                if (dxx != 0 && dzz != 0)
                {
                    if (Cost[idx + dxx] == NavCostField.CostImpassable) continue;
                    if (Cost[idx + dzz * Width] == NavCostField.CostImpassable) continue;
                }

                uint nCost = Integration[nIdx];
                if (nCost >= here) continue;
                float weight = (float)(here - nCost);
                // 1/sqrt(2) for diagonals so the unit vectors contribute
                // their orthogonal components equally.
                float inv = (dxx != 0 && dzz != 0) ? 0.70710678f : 1f;
                gx += dxx * weight * inv;
                gz += dzz * weight * inv;
            }

            if (gx == 0f && gz == 0f)
            {
                Dir[idx] = NavFlowConstants.NoDirection;
                return;
            }

            // Convert (gx, gz) to 0..255 angle byte.
            // angle = atan2(gz, gx) maps to (-π, π]; shift to [0, 2π) and
            // scale to 256. 0=+x, 64=+z, 128=-x, 192=-z. Matches the
            // BuildDirectionTableJob convention so Dir[idx] indexes the
            // correct unit vector in DirectionTableBlob.Dirs.
            float angle = math.atan2(gz, gx);
            if (angle < 0f) angle += 2f * math.PI;
            int dirByte = (int)math.round(angle / (2f * math.PI) * 256f);
            Dir[idx] = (byte)(dirByte & 0xFF);
        }
    }
}
