// PortalDetectionJob.cs
// task-112 M3 -- detect inter-tile portals on the layer-0 cost field.
//
// For each tile boundary edge (both vertical and horizontal), walk the
// boundary cell-by-cell looking for CONTIGUOUS WALKABLE SPANS that
// cross from one tile to the next. Each contiguous span becomes one
// portal at its midpoint cell (M3 simplification -- M5 may emit
// multiple portals per long span). The portal lives on the boundary
// cell that is INSIDE the lower-indexed tile, with an implicit edge to
// the adjacent cell in the higher-indexed tile.
//
// Determinism notes (DR-4 / DR-10):
//   * Tiles iterated tileZ asc, tileX asc (row-major).
//   * For each tile, vertical (east) boundary processed first, then
//     horizontal (north). Within a boundary, cells scanned in the
//     "along-boundary" axis ascending (z asc for vertical, x asc for
//     horizontal). This produces a stable per-tile portal sequence.
//   * Span midpoint chosen as the cell at index (start + length / 2).
//     For even-length spans, integer division picks the lower-index
//     centre -- byte-stable across machines.
//
// Single-thread IJob -- M3 has one one-shot graph build, parallelism
// across tiles would force a post-merge sort to recover the
// deterministic order described above. Cheaper to do it sequentially.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/PortalDetectionJob.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// A single emitted portal candidate. Consumed by
    /// <see cref="PortalGraphAssembleJob"/> which sorts + de-duplicates
    /// them into the CSR blob.
    /// </summary>
    public struct PortalSpec
    {
        /// <summary>Cell on the lower-indexed tile side of the boundary.</summary>
        public int CellIndex;
        /// <summary>Cell on the higher-indexed tile side of the boundary
        /// (always one step east or north).</summary>
        public int NeighbourCellIndex;
        /// <summary>Index of the tile this portal sits on (lower-indexed
        /// side of the boundary).</summary>
        public int TileIndex;
        /// <summary>Index of the tile across the boundary (always a
        /// strictly larger tile index).</summary>
        public int NeighbourTileIndex;
    }

    /// <summary>
    /// Walks every tile boundary on the layer-0 cost slab and emits one
    /// <see cref="PortalSpec"/> per contiguous walkable span. The list
    /// it writes is sorted in tile-row-major / cell-axis-asc order by
    /// construction (see file header), so the assemble job downstream
    /// only needs a stable sort by (TileIndex, CellIndex).
    /// </summary>
    [BurstCompile]
    internal struct PortalDetectionJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Cost;
        public int Width;
        public int Height;
        public int TileSize;
        public int TilesX;
        public int TilesZ;

        /// <summary>Output. Caller pre-allocates with a worst-case capacity
        /// (every boundary cell could be a 1-wide span). Capacity grows
        /// automatically via <c>NativeList.Add</c> if needed.</summary>
        public NativeList<PortalSpec> Portals;

        public void Execute()
        {
            // Tiles are numbered row-major: tileIndex = tileZ * TilesX + tileX.
            // Sweep tile rows, then tile cols within each row. Per tile,
            // emit east-boundary portals first, then north-boundary.
            // The (TilesX-1) / (TilesZ-1) boundaries between map edges
            // are skipped (no tile beyond).

            for (int tileZ = 0; tileZ < TilesZ; tileZ++)
            {
                for (int tileX = 0; tileX < TilesX; tileX++)
                {
                    int tileIndex = tileZ * TilesX + tileX;

                    // ── East boundary (between tileX and tileX + 1) ─────
                    if (tileX + 1 < TilesX)
                    {
                        int boundaryX = (tileX + 1) * TileSize - 1; // last cell of this tile in x
                        int neighbourX = boundaryX + 1;             // first cell of next tile in x
                        int neighbourTileIndex = tileZ * TilesX + (tileX + 1);

                        int z0 = tileZ * TileSize;
                        int z1 = math.min(z0 + TileSize, Height);

                        DetectSpansAlong(
                            isVertical: true,
                            fixedAxis: boundaryX,
                            neighbourFixedAxis: neighbourX,
                            varAxisStart: z0,
                            varAxisEnd: z1,
                            tileIndex: tileIndex,
                            neighbourTileIndex: neighbourTileIndex);
                    }

                    // ── North boundary (between tileZ and tileZ + 1) ────
                    if (tileZ + 1 < TilesZ)
                    {
                        int boundaryZ = (tileZ + 1) * TileSize - 1; // last cell of this tile in z
                        int neighbourZ = boundaryZ + 1;
                        int neighbourTileIndex = (tileZ + 1) * TilesX + tileX;

                        int x0 = tileX * TileSize;
                        int x1 = math.min(x0 + TileSize, Width);

                        DetectSpansAlong(
                            isVertical: false,
                            fixedAxis: boundaryZ,
                            neighbourFixedAxis: neighbourZ,
                            varAxisStart: x0,
                            varAxisEnd: x1,
                            tileIndex: tileIndex,
                            neighbourTileIndex: neighbourTileIndex);
                    }
                }
            }
        }

        // Sweeps the varying axis along a tile-tile boundary, accumulating
        // contiguous walkable spans (both cells in the pair must be
        // walkable). Each span emits one portal at its midpoint.
        //
        // When isVertical==true, fixedAxis is an x value and varAxis is z.
        // When isVertical==false, fixedAxis is a z value and varAxis is x.
        private void DetectSpansAlong(
            bool isVertical,
            int fixedAxis,
            int neighbourFixedAxis,
            int varAxisStart,
            int varAxisEnd,
            int tileIndex,
            int neighbourTileIndex)
        {
            int spanStart = -1;

            for (int v = varAxisStart; v < varAxisEnd; v++)
            {
                int hereIdx, thereIdx;
                if (isVertical)
                {
                    hereIdx = v * Width + fixedAxis;
                    thereIdx = v * Width + neighbourFixedAxis;
                }
                else
                {
                    hereIdx = fixedAxis * Width + v;
                    thereIdx = neighbourFixedAxis * Width + v;
                }

                bool open = Cost[hereIdx] != NavCostField.CostImpassable
                            && Cost[thereIdx] != NavCostField.CostImpassable;

                if (open)
                {
                    if (spanStart < 0) spanStart = v;
                }
                else if (spanStart >= 0)
                {
                    EmitSpan(isVertical, fixedAxis, neighbourFixedAxis,
                        spanStart, v - 1, tileIndex, neighbourTileIndex);
                    spanStart = -1;
                }
            }

            if (spanStart >= 0)
                EmitSpan(isVertical, fixedAxis, neighbourFixedAxis,
                    spanStart, varAxisEnd - 1, tileIndex, neighbourTileIndex);
        }

        private void EmitSpan(
            bool isVertical,
            int fixedAxis,
            int neighbourFixedAxis,
            int varStart,
            int varEnd,
            int tileIndex,
            int neighbourTileIndex)
        {
            int length = varEnd - varStart + 1;
            int mid = varStart + length / 2;

            int hereIdx, thereIdx;
            if (isVertical)
            {
                hereIdx = mid * Width + fixedAxis;
                thereIdx = mid * Width + neighbourFixedAxis;
            }
            else
            {
                hereIdx = fixedAxis * Width + mid;
                thereIdx = neighbourFixedAxis * Width + mid;
            }

            Portals.Add(new PortalSpec
            {
                CellIndex = hereIdx,
                NeighbourCellIndex = thereIdx,
                TileIndex = tileIndex,
                NeighbourTileIndex = neighbourTileIndex,
            });
        }
    }
}
