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
        /// <summary>
        /// Label every 4-connected walkable component of one tile (cost !=
        /// CostImpassable, the same walkability and connectivity
        /// <see cref="NavTileRegions.FloodFromCell"/> uses) into
        /// <paramref name="labels"/>: 0.. per component in row-major seed
        /// order, -1 for impassable cells. Only equality within one tile is
        /// meaningful.
        /// </summary>
        public static void LabelTile(in NativeArray<byte> cost, int gridWidth, int gridHeight,
            int tileSize, int tileX, int tileZ, NativeArray<int> labels, NativeList<int> stack)
        {
            int baseX = tileX * tileSize, baseZ = tileZ * tileSize;
            int w = math.min(tileSize, gridWidth - baseX);
            int h = math.min(tileSize, gridHeight - baseZ);
            if (w <= 0 || h <= 0) return;

            for (int z = 0; z < h; z++)
            {
                int row = (baseZ + z) * gridWidth + baseX;
                for (int x = 0; x < w; x++)
                    labels[row + x] = cost[row + x] == NavCostField.CostImpassable ? -1 : -2;
            }

            int next = 0;
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    int g = (baseZ + z) * gridWidth + baseX + x;
                    if (labels[g] != -2) continue;
                    int id = next++;
                    labels[g] = id;
                    stack.Clear();
                    stack.Add(z * tileSize + x);
                    while (stack.Length > 0)
                    {
                        int local = stack[stack.Length - 1];
                        stack.RemoveAt(stack.Length - 1);
                        int cx = local % tileSize, cz = local / tileSize;
                        for (int n = 0; n < 4; n++)
                        {
                            int nx = cx, nz = cz;
                            switch (n)
                            {
                                case 0: nx--; break;
                                case 1: nx++; break;
                                case 2: nz--; break;
                                default: nz++; break;
                            }
                            if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;
                            int ng = (baseZ + nz) * gridWidth + baseX + nx;
                            if (labels[ng] != -2) continue;
                            labels[ng] = id;
                            stack.Add(nz * tileSize + nx);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// <see cref="Build"/> with the region test answered from cached
        /// <see cref="LabelTile"/> labels instead of a flood per region.
        /// Same edge set: two portals of a bucket share a region iff both
        /// cells are walkable, lie in the same tile and carry the same label
        /// (the flood's mask covered exactly the seed's tile component; an
        /// impassable portal cell was isolated).
        /// </summary>
        public static void BuildFromLabels(
            in NavGridSingleton grid,
            NativeArray<PortalNode> nodes,
            NativeList<PortalEdge> outEdges,
            in NativeArray<int> labels)
        {
            int n = nodes.Length;
            if (n < 2) return;

            int width = grid.Width;
            int tileSize = PortalGraphSingleton.TileSize;
            int tilesX = (grid.Width + tileSize - 1) / tileSize;

            var order = new NativeArray<ulong>(n, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++)
            {
                order[i] = ((ulong)(uint)nodes[i].TileIndex << 42)
                    | ((ulong)(uint)nodes[i].CellIndex << 21)
                    | (uint)i;
            }
            NativeSortExtension.Sort(order);

            // Region key per node: (geometric tile, label), or a unique
            // negative for an impassable / off-grid cell.
            var regionKey = new NativeArray<long>(n, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++)
            {
                int c = nodes[i].CellIndex;
                int lbl = (c >= 0 && c < labels.Length) ? labels[c] : -1;
                if (lbl < 0) { regionKey[i] = -1L - i; continue; }
                int gt = ((c / width) / tileSize) * tilesX + (c % width) / tileSize;
                regionKey[i] = ((long)gt << 20) | (uint)lbl;
            }

            int start = 0;
            while (start < n)
            {
                int tile = (int)(order[start] >> 42);
                int end = start + 1;
                while (end < n && (int)(order[end] >> 42) == tile) end++;

                for (int i = start; i < end; i++)
                {
                    int oi = (int)(order[i] & 0x1FFFFF);
                    var ni = nodes[oi];
                    int aX = ni.CellIndex % width;
                    int aZ = ni.CellIndex / width;
                    for (int j = i + 1; j < end; j++)
                    {
                        int oj = (int)(order[j] & 0x1FFFFF);
                        if (regionKey[oi] != regionKey[oj]) continue;

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

            regionKey.Dispose();
            order.Dispose();
        }

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
