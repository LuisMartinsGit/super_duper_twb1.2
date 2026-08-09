// NavTileRegions.cs
// Tile-local walkable-region flood fill over the layer-0 cost slab.
//
// Why this exists: the portal graph's intra-tile edges (portal <-> portal)
// and the A*'s virtual start/goal edges used to be pure Manhattan-distance
// links between everything sharing a tile — they never looked at the cost
// cells INSIDE the tile. Any blocker that cuts a tile in two (hand-painted
// NoWalk terrain, a long wall, a building row) split the tile into disjoint
// walkable regions the graph could not see: A* returned "paths" through the
// wall, the flow slabs (which integrate REAL costs) could not reach the
// unit's side, and units degraded to the direct-to-goal fallback — grinding
// circles against the obstacle.
//
// The fix: a bounded flood fill (4-connected, tile-local, max TileSize^2
// cells) that answers "which cells of this tile are walkable-reachable from
// this seed cell". Both edge emitters filter their links through it.
//
// Determinism: fixed scan order (explicit stack, neighbours pushed in a
// constant order), integer math only, reads the lockstep-identical cost
// slab — safe for multiplayer.
//
// Location: Assets/Scripts/Systems/Navigation/NavTileRegions.cs

using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    internal static class NavTileRegions
    {
        /// <summary>
        /// Flood-fill the walkable region containing <paramref name="seedCell"/>
        /// (grid coordinates), constrained to the seed's tile. Writes 1 into
        /// <paramref name="mask"/> (length &gt;= tileSize*tileSize, indexed
        /// localZ*tileSize+localX, caller-cleared) for every reachable cell.
        /// Walkable = any cost except CostImpassable (255) — conditional gate
        /// cells count as walkable here; per-unit gate admission is handled
        /// at the portal level.
        /// Returns false when the seed itself is off-tile or impassable
        /// (callers should fall back to permissive behaviour).
        /// </summary>
        public static bool FloodFromCell(
            in NativeArray<byte> cost,
            int gridWidth,
            int gridHeight,
            int tileSize,
            int2 seedCell,
            NativeArray<byte> mask)
        {
            int tileX = seedCell.x / tileSize;
            int tileZ = seedCell.y / tileSize;
            int baseX = tileX * tileSize;
            int baseZ = tileZ * tileSize;
            // Edge tiles can be cropped by the grid bounds.
            int w = math.min(tileSize, gridWidth - baseX);
            int h = math.min(tileSize, gridHeight - baseZ);

            int lx = seedCell.x - baseX;
            int lz = seedCell.y - baseZ;
            if (lx < 0 || lx >= w || lz < 0 || lz >= h) return false;
            if (cost[seedCell.y * gridWidth + seedCell.x] == NavCostField.CostImpassable)
                return false;

            // Explicit stack, worst case one entry per tile cell.
            var stack = new NativeList<int>(tileSize * tileSize, Allocator.Temp);
            mask[lz * tileSize + lx] = 1;
            stack.Add(lz * tileSize + lx);

            while (stack.Length > 0)
            {
                int local = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                int cx = local % tileSize;
                int cz = local / tileSize;

                // Neighbours in fixed order: -x, +x, -z, +z (determinism).
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

                    int nLocal = nz * tileSize + nx;
                    if (mask[nLocal] != 0) continue;

                    int gIdx = (baseZ + nz) * gridWidth + (baseX + nx);
                    if (cost[gIdx] == NavCostField.CostImpassable) continue;

                    mask[nLocal] = 1;
                    stack.Add(nLocal);
                }
            }

            stack.Dispose();
            return true;
        }

        /// <summary>True when <paramref name="cell"/> (grid coords) lies in the
        /// tile the mask was filled for AND is marked reachable.</summary>
        public static bool CellInMask(
            in NativeArray<byte> mask,
            int tileSize,
            int2 maskSeedCell,
            int2 cell)
        {
            int tileX = maskSeedCell.x / tileSize;
            int tileZ = maskSeedCell.y / tileSize;
            int lx = cell.x - tileX * tileSize;
            int lz = cell.y - tileZ * tileSize;
            if (lx < 0 || lx >= tileSize || lz < 0 || lz >= tileSize) return false;
            return mask[lz * tileSize + lx] != 0;
        }
    }
}
