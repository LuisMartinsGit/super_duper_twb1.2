// PortalIntraTileEdges.cs
// Shared helper that emits intra-tile portal-to-portal edges (Manhattan
// distance cost between every pair of portals that share a tile).
//
// Used by both PortalGraphBuildSystem (one-shot build) and
// IncrementalPortalRebuildSystem (rebuild on dirty signal). Bucketing the
// portals by TileIndex makes this O(n log n + Σ kᵢ²) instead of the naive
// O(n²) all-pairs scan. That matters now the nav grid is sized to the whole
// map: the portal count scales with the map's tile-boundary length, so an
// O(n²) scan turned every building placement (which triggers a full portal
// rebuild) into a multi-second main-thread stall.
//
// The set of edges produced is identical to the old all-pairs version; the
// callers re-sort the full edge list by (FromPortalId, ToPortalId) before
// building the CSR blob, so insertion order here does not affect determinism.
//
// Location: Assets/Scripts/Systems/Navigation/PortalIntraTileEdges.cs

using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    internal static class PortalIntraTileEdges
    {
        public static void Build(
            in NavGridSingleton grid,
            NativeArray<PortalNode> nodes,
            NativeList<PortalEdge> outEdges)
        {
            int n = nodes.Length;
            if (n < 2) return;

            int width = grid.Width;

            // Order node-array indices by TileIndex so same-tile portals are
            // contiguous. Managed arrays + Array.Sort are fine here — these
            // callers are not Burst-compiled (they use BlobBuilder/Array.Sort
            // already).
            var order = new int[n];
            var tileOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                tileOf[i] = nodes[i].TileIndex;
            }
            System.Array.Sort(order, (a, b) => tileOf[a] - tileOf[b]);

            int start = 0;
            while (start < n)
            {
                int tile = tileOf[order[start]];
                int end = start + 1;
                while (end < n && tileOf[order[end]] == tile) end++;

                // Portals [start, end) all live on `tile` — O(k²) pairs.
                for (int i = start; i < end; i++)
                {
                    var ni = nodes[order[i]];
                    int aX = ni.CellIndex % width;
                    int aZ = ni.CellIndex / width;
                    for (int j = i + 1; j < end; j++)
                    {
                        var nj = nodes[order[j]];
                        int bX = nj.CellIndex % width;
                        int bZ = nj.CellIndex / width;
                        int manhattan = math.abs(aX - bX) + math.abs(aZ - bZ);
                        ushort cost = (ushort)math.min(manhattan * 10, ushort.MaxValue);

                        outEdges.Add(new PortalEdge
                        {
                            FromPortalId = ni.Id,
                            ToPortalId = nj.Id,
                            Cost = cost,
                            ProfileMask = 0xFF,
                        });
                        outEdges.Add(new PortalEdge
                        {
                            FromPortalId = nj.Id,
                            ToPortalId = ni.Id,
                            Cost = cost,
                            ProfileMask = 0xFF,
                        });
                    }
                }

                start = end;
            }
        }
    }
}
