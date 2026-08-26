// WallPortalGraphAppender.cs
// task-112 M5 -- helper that appends wall-derived portal nodes / edges
// to the in-flight portal graph build. Used by BOTH
// PortalGraphBuildSystem (one-shot) and IncrementalPortalRebuildSystem
// (every dirty-tile rebuild) so the climb / gate portals appear in
// every graph generation.
//
// The wall portal pairs become TWO portal nodes (source side + target
// side) plus a bidirectional edge between them. Climb portals: source
// layer = 0, target layer = 1. Gate-ground: both at layer 0 but cells
// on opposite sides of the gatehouse. Gate-rampart: both at layer 1.
//
// Determinism: spec list is iterated in entity.Index ascending order
// (WallPortalDetectionSystem already sorted), so node id assignment is
// stable across machines / runs.

using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Helper that appends wall-derived portal nodes + edges to a
    /// growing portal-graph node/edge list. Caller passes the lists
    /// already populated with inter-tile portals from
    /// <see cref="PortalDetectionJob"/>; this routine appends the
    /// climb / gate kinds in a deterministic order.
    /// </summary>
    public static class WallPortalGraphAppender
    {
        /// <summary>
        /// Append wall-derived portal nodes + edges. The caller's
        /// <paramref name="nodes"/> already holds the M3 inter-tile
        /// nodes; this method assigns new ids starting at
        /// <c>nodes.Length</c>. Returns the number of node IDs
        /// added (one source + one target per spec).
        ///
        /// The portal edges added carry <see cref="PortalEdge.Cost"/>
        /// = 1 (cells are adjacent across the boundary) and
        /// <see cref="PortalEdge.ProfileMask"/> = 0xFF (all profiles
        /// admitted at build time; gate gating happens via the
        /// owner-bits mirror at query time).
        /// </summary>
        public static int Append(
            in NavGridSingleton grid,
            in NavCostField cost,
            NativeList<WallPortalSpec> specs,
            NativeList<PortalNode> nodes,
            NativeList<PortalEdge> edges,
            int tileSize,
            int tilesX)
        {
            int added = 0;
            for (int i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                int srcCellIdx = spec.SourceCell.y * grid.Width + spec.SourceCell.x;
                int tgtCellIdx = spec.TargetCell.y * grid.Width + spec.TargetCell.x;

                int srcTile = TileIndex(spec.SourceCell, tileSize, tilesX);
                int tgtTile = TileIndex(spec.TargetCell, tileSize, tilesX);

                int srcNodeId = nodes.Length;
                int tgtNodeId = nodes.Length + 1;

                nodes.Add(new PortalNode
                {
                    Id = srcNodeId,
                    CellIndex = srcCellIdx,
                    TileIndex = srcTile,
                    PortalKind = spec.Kind,
                    OwnerId = spec.OwnerId,
                    Layer = spec.SourceLayer,
                });
                nodes.Add(new PortalNode
                {
                    Id = tgtNodeId,
                    CellIndex = tgtCellIdx,
                    TileIndex = tgtTile,
                    PortalKind = spec.Kind,
                    OwnerId = spec.OwnerId,
                    Layer = spec.TargetLayer,
                });

                // Bidirectional edge across the portal. Cost = 1 cell
                // (the cells are adjacent across the wall / boundary).
                edges.Add(new PortalEdge
                {
                    FromPortalId = srcNodeId,
                    ToPortalId = tgtNodeId,
                    Cost = (ushort)NavFlowConstants.StepCardinal,
                    ProfileMask = 0xFF,
                });
                edges.Add(new PortalEdge
                {
                    FromPortalId = tgtNodeId,
                    ToPortalId = srcNodeId,
                    Cost = (ushort)NavFlowConstants.StepCardinal,
                    ProfileMask = 0xFF,
                });

                added += 2;
            }
            return added;
        }

        private static int TileIndex(int2 cell, int tileSize, int tilesX)
        {
            int tx = cell.x / tileSize;
            int tz = cell.y / tileSize;
            return tz * tilesX + tx;
        }
    }
}
