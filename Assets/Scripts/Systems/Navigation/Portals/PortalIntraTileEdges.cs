// PortalIntraTileEdges.cs
// Shared helper that emits intra-tile portal-to-portal edges (Manhattan
// distance cost between pairs of portals that share a tile AND sit in the
// same walkable region of that tile — see NavTileRegions for why blockers
// that cut a tile in two must not be bridged by abstract edges).
//
// Used by both PortalGraphBuildSystem (one-shot build) and
// IncrementalPortalRebuildSystem (rebuild on dirty signal). Bucketing the
// portals by TileIndex makes this O(n log n + Σ kᵢ²) instead of the naive
// O(n²) all-pairs scan. That matters now the nav grid is sized to the whole
// map: the portal count scales with the map's tile-boundary length, so an
// O(n²) scan turned every building placement (which triggers a full portal
// rebuild) into a multi-second main-thread stall.
//
// The callers re-sort the full edge list by (FromPortalId, ToPortalId) before
// building the CSR blob, so insertion order here does not affect determinism.

using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    internal static class PortalIntraTileEdges
    {
        public static void Build(
            in NavGridSingleton grid,
            NativeArray<PortalNode> nodes,
            NativeList<PortalEdge> outEdges,
            in NativeArray<byte> cost)
        {
            int n = nodes.Length;
            if (n < 2) return;

            int width = grid.Width;
            int tileSize = PortalGraphSingleton.TileSize;

            // Region labelling scratch: -1 = unlabelled. Portals sharing a
            // tile only get an edge when they sit in the SAME walkable
            // region of that tile — a blocker that cuts the tile in two
            // (painted NoWalk terrain, a wall line) must not be bridged by
            // an abstract edge the flow layer can't actually walk.
            var regionOf = new NativeArray<int>(n, Allocator.Temp);
            for (int i = 0; i < n; i++) regionOf[i] = -1;
            bool haveCost = cost.IsCreated;
            var mask = haveCost
                ? new NativeArray<byte>(tileSize * tileSize, Allocator.Temp)
                : default;

            // Order node-array indices by (TileIndex, CellIndex) so same-tile
            // portals are contiguous — a TOTAL order (the trailing index
            // uniquifies the key, so the unstable sort cannot reorder equal
            // entries). Packed ulong keys instead of a managed comparator:
            // this helper must be Burst-callable (the incremental rebuild
            // runs it inside a Burst job) and a managed Array.Sort with a
            // lambda was both a Burst blocker and the slow path.
            // Bit budget: TileIndex < 2^22, CellIndex < 2^21 (grids to
            // 1448x1448), i < 2^21 — see the assemble job's node counts.
            var order = new NativeArray<ulong>(n, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++)
            {
                order[i] = ((ulong)(uint)nodes[i].TileIndex << 42)
                    | ((ulong)(uint)nodes[i].CellIndex << 21)
                    | (uint)i;
            }
            NativeSortExtension.Sort(order);

            int start = 0;
            while (start < n)
            {
                int tile = (int)(order[start] >> 42);
                int end = start + 1;
                while (end < n && (int)(order[end] >> 42) == tile) end++;

                // ── Label walkable regions inside this tile ────────────────
                // Flood from each still-unlabelled portal cell; every portal
                // of the bucket whose cell lands in the flooded mask shares
                // that region id. Bucket order is (TileIndex, CellIndex) asc
                // (DR-4 node order), so labelling is deterministic.
                if (haveCost)
                {
                    int nextRegion = 0;
                    for (int i = start; i < end; i++)
                    {
                        int oi = (int)(order[i] & 0x1FFFFF);
                        if (regionOf[oi] >= 0) continue;

                        var ni = nodes[oi];
                        int2 seed = new int2(ni.CellIndex % width, ni.CellIndex / width);

                        for (int c = 0; c < mask.Length; c++) mask[c] = 0;
                        if (!NavTileRegions.FloodFromCell(cost, grid.Width, grid.Height,
                                tileSize, seed, mask))
                        {
                            // Portal cell impassable (stale portal on freshly
                            // blocked ground) — isolate it in its own region.
                            regionOf[oi] = nextRegion++;
                            continue;
                        }

                        int r = nextRegion++;
                        for (int j = i; j < end; j++)
                        {
                            int oj = (int)(order[j] & 0x1FFFFF);
                            if (regionOf[oj] >= 0) continue;
                            var njNode = nodes[oj];
                            int2 cj = new int2(njNode.CellIndex % width, njNode.CellIndex / width);
                            if (NavTileRegions.CellInMask(mask, tileSize, seed, cj))
                                regionOf[oj] = r;
                        }
                    }
                }

                // Portals [start, end) all live on `tile` — O(k²) pairs.
                for (int i = start; i < end; i++)
                {
                    int oi = (int)(order[i] & 0x1FFFFF);
                    var ni = nodes[oi];
                    int aX = ni.CellIndex % width;
                    int aZ = ni.CellIndex / width;
                    for (int j = i + 1; j < end; j++)
                    {
                        int oj = (int)(order[j] & 0x1FFFFF);
                        // Different walkable regions of the tile — no edge.
                        if (haveCost && regionOf[oi] != regionOf[oj])
                            continue;

                        var nj = nodes[oj];
                        int bX = nj.CellIndex % width;
                        int bZ = nj.CellIndex / width;
                        int manhattan = math.abs(aX - bX) + math.abs(aZ - bZ);
                        ushort edgeCost = (ushort)math.min(manhattan * 10, ushort.MaxValue);

                        outEdges.Add(new PortalEdge
                        {
                            FromPortalId = ni.Id,
                            ToPortalId = nj.Id,
                            Cost = edgeCost,
                            ProfileMask = 0xFF,
                        });
                        outEdges.Add(new PortalEdge
                        {
                            FromPortalId = nj.Id,
                            ToPortalId = ni.Id,
                            Cost = edgeCost,
                            ProfileMask = 0xFF,
                        });
                    }
                }

                start = end;
            }

            if (mask.IsCreated) mask.Dispose();
            regionOf.Dispose();
            order.Dispose();
        }
    }
}
