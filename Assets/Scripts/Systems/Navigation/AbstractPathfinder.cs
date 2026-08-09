// AbstractPathfinder.cs
// task-112 M3 -- pure-function A* on a PortalGraphBlob. Extracted from
// AbstractPathfinderSystem so the algorithm can be unit-tested directly
// against a hand-authored blob in EditMode.
//
// The algorithm is the same A* shipped in the runtime system:
//   * f = g + octile heuristic to goal cell
//   * tie-break by ascending portal id (DR-3)
//   * synthesized virtual start/goal nodes that connect to every real
//     portal on the start/goal cell's tile via Manhattan-cost edges.
//
// All scratch allocations use Allocator.Temp so the caller doesn't have
// to clean up (Temp is freed at job-end / frame-end depending on
// caller).
//
// Lambda-free neighbour expansion: the EnumerateNeighbours work is
// inlined into Solve because BlobAsset types must be passed by ref
// (DOTS analyzer EA0009) and `ref` params can't be captured by lambdas.
//
// Location: Assets/Scripts/Systems/Navigation/AbstractPathfinder.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Pure-function A* over the portal graph. Use
    /// <see cref="Solve"/> with a blob reference and the start/goal
    /// cells; pass <paramref name="portals"/> as a fresh
    /// <see cref="NativeList{T}"/> the caller will dispose. Returns the
    /// path status (success / unreachable).
    /// </summary>
    public static class AbstractPathfinder
    {
        /// <summary>
        /// Bucket width for the spec's bucket-queue. Documentation only --
        /// the M3 implementation uses a flat (fScore, nodeId) open list
        /// sorted by (fScore asc, nodeId asc) on pop, which is
        /// observationally equivalent for tie-breaking but has no
        /// per-bucket structure. Keep the constant exposed so future
        /// switches to a real bucket queue remain bit-compatible.
        /// </summary>
        public const int BucketWidth = 4;

        /// <summary>
        /// Solve the path. Caller owns <paramref name="portals"/>;
        /// returns the trailing path as portal ids in execution order
        /// (head = start virtual, tail = goal virtual).
        ///
        /// task-112 M5: convenience overload (no profile / owner gating).
        /// Forwards to the gated <see cref="SolveGated"/> with the
        /// "any owner" sentinel + no profile mask.
        /// </summary>
        public static byte Solve(
            ref PortalGraphBlob graph,
            in NavGridSingleton grid,
            int2 startCell,
            int2 goalCell,
            NativeList<int> portals)
        {
            // Default empty mirror = no gate gating; -1 owner = any.
            return SolveGated(ref graph, in grid, startCell, goalCell, portals,
                ownerBitsMirror: default, unitOwnerId: -1);
        }

        /// <summary>
        /// Convenience overload with the layer-0 cost slab for region-aware
        /// virtual edges (see <see cref="SolveGated"/>).
        /// </summary>
        public static byte Solve(
            ref PortalGraphBlob graph,
            in NavGridSingleton grid,
            int2 startCell,
            int2 goalCell,
            NativeList<int> portals,
            in NativeArray<byte> cost)
        {
            return SolveGated(ref graph, in grid, startCell, goalCell, portals,
                ownerBitsMirror: default, unitOwnerId: -1, cost);
        }

        /// <summary>
        /// task-112 M5 -- profile + owner gated A*. Reads the per-portal
        /// owner-bits mirror to skip gate portals whose owner doesn't
        /// match or whose open bit is clear. Falls through to the
        /// existing edge-cost expansion when the portal is admissible.
        /// </summary>
        public static byte SolveGated(
            ref PortalGraphBlob graph,
            in NavGridSingleton grid,
            int2 startCell,
            int2 goalCell,
            NativeList<int> portals,
            NativeArray<ushort> ownerBitsMirror,
            int unitOwnerId,
            in NativeArray<byte> cost = default)
        {
            int realNodeCount = graph.Nodes.Length;
            int startVirtual = realNodeCount;
            int goalVirtual = realNodeCount + 1;
            int totalNodes = realNodeCount + 2;

            int startTileIndex = TileIndexOfCell(grid, ref graph, startCell);
            int goalTileIndex = TileIndexOfCell(grid, ref graph, goalCell);

            // ── Region masks (optional, needs the cost slab) ───────────────
            // Virtual start/goal edges only connect to portals in the SAME
            // walkable region of their tile. Without this, a blocker cutting
            // the tile in two (painted NoWalk terrain, a wall) is silently
            // bridged by Manhattan edges: A* "succeeds" straight through it,
            // the flow slab can't reach the unit, and the unit degrades to
            // direct-to-goal wall-grinding. A failed flood (seed cell itself
            // blocked — e.g. the click landed ON the blocker) falls back to
            // the permissive old behaviour for that side.
            int tileSize = graph.TileSize;
            bool startMaskValid = false;
            bool goalMaskValid = false;
            NativeArray<byte> startMask = default;
            NativeArray<byte> goalMask = default;
            if (cost.IsCreated)
            {
                startMask = new NativeArray<byte>(tileSize * tileSize, Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
                goalMask = new NativeArray<byte>(tileSize * tileSize, Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
                startMaskValid = NavTileRegions.FloodFromCell(cost, grid.Width, grid.Height,
                    tileSize, startCell, startMask);
                goalMaskValid = NavTileRegions.FloodFromCell(cost, grid.Width, grid.Height,
                    tileSize, goalCell, goalMask);
            }

            // Trivial case: same tile AND same walkable region -- direct
            // virtual hop (the tile-local flow slab handles the detail).
            // Same tile but DIFFERENT regions falls through to the full A*,
            // which routes out through a neighbouring tile and back in.
            if (startTileIndex == goalTileIndex)
            {
                bool sameRegion = !startMaskValid
                    || NavTileRegions.CellInMask(startMask, tileSize, startCell, goalCell);
                if (sameRegion)
                {
                    portals.Add(startVirtual);
                    portals.Add(goalVirtual);
                    if (startMask.IsCreated) startMask.Dispose();
                    if (goalMask.IsCreated) goalMask.Dispose();
                    return NavPathRequest.StatusSuccess;
                }
            }

            var g = new NativeArray<uint>(totalNodes, Allocator.Temp);
            for (int i = 0; i < totalNodes; i++) g[i] = uint.MaxValue;
            g[startVirtual] = 0;
            var cameFrom = new NativeArray<int>(totalNodes, Allocator.Temp);
            for (int i = 0; i < totalNodes; i++) cameFrom[i] = -1;
            var closed = new NativeArray<byte>(totalNodes, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var open = new NativeList<int2>(64, Allocator.Temp);

            open.Add(new int2((int)Heuristic(startCell, goalCell), startVirtual));

            bool found = false;
            while (open.Length > 0)
            {
                // Pop min by (fScore asc, nodeId asc).
                // BucketWidth = 4 is documented in the spec; this linear
                // scan is observationally equivalent for tie-break.
                int minIdx = 0;
                int2 minVal = open[0];
                for (int i = 1; i < open.Length; i++)
                {
                    var v = open[i];
                    if (v.x < minVal.x || (v.x == minVal.x && v.y < minVal.y))
                    {
                        minIdx = i;
                        minVal = v;
                    }
                }
                open[minIdx] = open[open.Length - 1];
                open.RemoveAt(open.Length - 1);

                int current = minVal.y;
                if (closed[current] != 0) continue;
                closed[current] = 1;

                if (current == goalVirtual)
                {
                    found = true;
                    break;
                }

                // ── Inline neighbour expansion. Each branch emits
                //    candidates with edge costs; we update g/cameFrom and
                //    push onto the open list.
                if (current == startVirtual)
                {
                    // Connect to every real portal that lives on the start
                    // cell's tile via Manhattan-cost synthetic edges —
                    // restricted to portals in the start cell's walkable
                    // region when a region mask is available.
                    for (int i = 0; i < realNodeCount; i++)
                    {
                        if (graph.Nodes[i].TileIndex != startTileIndex) continue;
                        int idxCell = graph.Nodes[i].CellIndex;
                        int cx = idxCell % grid.Width;
                        int cz = idxCell / grid.Width;
                        if (startMaskValid && !NavTileRegions.CellInMask(
                                startMask, tileSize, startCell, new int2(cx, cz)))
                            continue;
                        int dx = math.abs(cx - startCell.x);
                        int dz = math.abs(cz - startCell.y);
                        uint edgeCost = (uint)((dx + dz) * 10);
                        if (edgeCost > ushort.MaxValue) edgeCost = ushort.MaxValue;
                        RelaxOpen(ref graph, in grid, g, cameFrom, closed, open,
                            current, i, edgeCost, startVirtual, goalVirtual, startCell, goalCell);
                    }
                }
                else
                {
                    // Real portal node: walk its CSR run.
                    int runStart = graph.NodeFirstEdge[current];
                    int runEnd = graph.NodeFirstEdge[current + 1];
                    for (int e = runStart; e < runEnd; e++)
                    {
                        var edge = graph.Edges[e];
                        // task-112 M5: skip edges leading to portals the
                        // unit can't traverse (gate closed, wrong owner).
                        if (!IsPortalAdmissibleForA(ref graph, ownerBitsMirror,
                                edge.ToPortalId, unitOwnerId)) continue;
                        RelaxOpen(ref graph, in grid, g, cameFrom, closed, open,
                            current, edge.ToPortalId, edge.Cost,
                            startVirtual, goalVirtual, startCell, goalCell);
                    }

                    // Plus: if this real portal lives on the goal's tile
                    // (and, when a mask exists, in the goal cell's walkable
                    // region), emit an edge to the goal virtual node.
                    if (graph.Nodes[current].TileIndex == goalTileIndex)
                    {
                        int idxCell = graph.Nodes[current].CellIndex;
                        int cx = idxCell % grid.Width;
                        int cz = idxCell / grid.Width;
                        bool admissible = !goalMaskValid || NavTileRegions.CellInMask(
                            goalMask, tileSize, goalCell, new int2(cx, cz));
                        if (admissible)
                        {
                            int dx = math.abs(cx - goalCell.x);
                            int dz = math.abs(cz - goalCell.y);
                            uint edgeCost = (uint)((dx + dz) * 10);
                            if (edgeCost > ushort.MaxValue) edgeCost = ushort.MaxValue;
                            RelaxOpen(ref graph, in grid, g, cameFrom, closed, open,
                                current, goalVirtual, edgeCost,
                                startVirtual, goalVirtual, startCell, goalCell);
                        }
                    }
                }
            }

            byte status;
            if (found)
            {
                var reverse = new NativeList<int>(16, Allocator.Temp);
                int cur = goalVirtual;
                while (cur != -1)
                {
                    reverse.Add(cur);
                    cur = cameFrom[cur];
                }
                for (int i = reverse.Length - 1; i >= 0; i--)
                    portals.Add(reverse[i]);
                reverse.Dispose();
                status = NavPathRequest.StatusSuccess;
            }
            else
            {
                status = NavPathRequest.StatusUnreachable;
            }

            open.Dispose();
            g.Dispose();
            cameFrom.Dispose();
            closed.Dispose();
            if (startMask.IsCreated) startMask.Dispose();
            if (goalMask.IsCreated) goalMask.Dispose();

            return status;
        }

        // Octile distance in integer "octile units" (cardinal=10, diagonal=14).
        public static uint Heuristic(int2 from, int2 to)
        {
            int dx = math.abs(from.x - to.x);
            int dz = math.abs(from.y - to.y);
            int dMin = math.min(dx, dz);
            int dMax = math.max(dx, dz);
            return (uint)(14 * dMin + 10 * (dMax - dMin));
        }

        public static int TileIndexOfCell(in NavGridSingleton grid, ref PortalGraphBlob graph,
            int2 cell)
        {
            int tileSize = graph.TileSize;
            int tilesX = graph.TilesX;
            int tx = math.clamp(cell.x / tileSize, 0, tilesX - 1);
            int tz = math.clamp(cell.y / tileSize, 0, graph.TilesZ - 1);
            return tz * tilesX + tx;
        }

        /// <summary>
        /// task-112 M5 -- per-portal admissibility check. Gate portals
        /// with a closed open-bit or mismatched owner are skipped; non-
        /// gate portals are always admissible.
        /// </summary>
        private static bool IsPortalAdmissibleForA(
            ref PortalGraphBlob graph,
            NativeArray<ushort> ownerBitsMirror,
            int portalId,
            int unitOwnerId)
        {
            if (portalId < 0 || portalId >= graph.Nodes.Length) return true;
            var node = graph.Nodes[portalId];
            bool isGate = node.PortalKind == PortalNode.KindGateGround
                || node.PortalKind == PortalNode.KindGateRampart;
            if (!isGate) return true;

            // No mirror -> assume open + any-owner (legacy path).
            if (!ownerBitsMirror.IsCreated || portalId >= ownerBitsMirror.Length) return true;
            ushort slot = ownerBitsMirror[portalId];
            if (!PortalOwnerBitsMirror.UnpackOpen(slot)) return false;
            int portalOwner = PortalOwnerBitsMirror.UnpackOwner(slot);
            if (portalOwner >= 0 && unitOwnerId >= 0 && portalOwner != unitOwnerId) return false;
            return true;
        }

        // Relax a single edge candidate (current -> neighbour) with the
        // given edge cost. Updates g / cameFrom and pushes onto the open
        // list when the tentative cost beats the current best. Inlined
        // here to avoid lambda capture of the ref blob.
        private static void RelaxOpen(
            ref PortalGraphBlob graph,
            in NavGridSingleton grid,
            NativeArray<uint> g,
            NativeArray<int> cameFrom,
            NativeArray<byte> closed,
            NativeList<int2> open,
            int current,
            int neighbour,
            uint edgeCost,
            int startVirtual,
            int goalVirtual,
            int2 startCell,
            int2 goalCell)
        {
            if (closed[neighbour] != 0) return;
            uint tentative = g[current] + edgeCost;
            if (tentative >= g[neighbour]) return;
            g[neighbour] = tentative;
            cameFrom[neighbour] = current;

            int2 cell;
            if (neighbour == startVirtual) cell = startCell;
            else if (neighbour == goalVirtual) cell = goalCell;
            else
            {
                int idx = graph.Nodes[neighbour].CellIndex;
                cell = new int2(idx % grid.Width, idx / grid.Width);
            }
            uint h = Heuristic(cell, goalCell);
            uint f = tentative + h;
            int fInt = (int)math.min(f, (uint)int.MaxValue);
            open.Add(new int2(fInt, neighbour));
        }
    }
}
