// AStarPathfinder.cs
// Static A* pathfinding utility using PassabilityGrid.
// 8-directional movement with same corner-cutting rules as FlowFieldGenerator.
// Location: Assets/Scripts/Systems/Movement/AStarPathfinder.cs

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// A* pathfinder operating on the passability grid.
    /// Produces a list of world-space waypoints from start to goal.
    /// Uses octile distance heuristic and 8-directional movement
    /// with corner-cutting prevention (same rules as FlowFieldGenerator).
    /// </summary>
    public static class AStarPathfinder
    {
        private const int CardinalCost = 10;
        private const int DiagonalCost = 14;
        private const int MaxSnapSearch = 25;

        // Fix #208: pooled scratch buffers for A* state. Previously every
        // FindPath call allocated three fresh managed arrays (gCost, parent,
        // closed) sized width*height. On a 200x200 grid that was ~320KB of
        // GC pressure per call; with up to 20 paths/frame = 6.4MB/frame.
        //
        // These pools are sized lazily to the largest grid seen and cleared
        // (not reallocated) on each call. Main-thread-only by contract —
        // AStarPathStore only calls FindPath from Update().
        private static int[] s_gCostPool;
        private static int[] s_parentPool;
        private static bool[] s_closedPool;

        /// <summary>
        /// Ensure the pooled scratch arrays are at least `minSize` in length.
        /// Grows (reallocates) only when the grid has gotten bigger.
        /// </summary>
        private static void EnsurePools(int minSize)
        {
            if (s_gCostPool == null || s_gCostPool.Length < minSize)
            {
                s_gCostPool = new int[minSize];
                s_parentPool = new int[minSize];
                s_closedPool = new bool[minSize];
            }
        }

        // 8 neighbors: 4 cardinal + 4 diagonal
        // dx, dy, cost, adj1dx, adj1dy, adj2dx, adj2dy (for corner-cutting check)
        private static readonly int[][] Neighbors =
        {
            // Cardinals (no corner-cutting check needed)
            new[] {  0,  1, CardinalCost, 0, 0, 0, 0 }, // N
            new[] {  1,  0, CardinalCost, 0, 0, 0, 0 }, // E
            new[] {  0, -1, CardinalCost, 0, 0, 0, 0 }, // S
            new[] { -1,  0, CardinalCost, 0, 0, 0, 0 }, // W
            // Diagonals (check adjacent cardinals)
            new[] {  1,  1, DiagonalCost, 1, 0, 0, 1 }, // NE: check E, N
            new[] {  1, -1, DiagonalCost, 1, 0, 0,-1 }, // SE: check E, S
            new[] { -1, -1, DiagonalCost,-1, 0, 0,-1 }, // SW: check W, S
            new[] { -1,  1, DiagonalCost,-1, 0, 0, 1 }, // NW: check W, N
        };

        /// <summary>
        /// Find a path from start to goal using A*.
        /// Returns a list of world-space waypoints (cell centers), or null if unreachable.
        /// Convenience overload — assumes the typical 0.5m unit radius for the
        /// string-pull LOS check. Callers with bigger units should pass agentRadius.
        /// </summary>
        public static List<float3> FindPath(float3 startWorld, float3 goalWorld, PassabilityGrid grid)
            => FindPath(startWorld, goalWorld, grid, agentRadius: 0.5f);

        /// <summary>
        /// Find a path with a configurable agent radius (world units).
        /// Inflation in cells = ceil((agentRadius - cellSize/2) / cellSize),
        /// requiring every traversed cell to have an N-cell ring of passable
        /// neighbours (Minkowski sum of obstacles by agent footprint).
        /// If no path is found at the requested inflation, falls back to 0
        /// inflation so units already wedged in tight spots can still escape.
        /// String-pull post-process uses the geometric, sampled LOS so it
        /// won't drop a waypoint whose shortcut clips a building corner.
        /// (Nav-clearance fix.)
        /// </summary>
        public static List<float3> FindPath(float3 startWorld, float3 goalWorld,
            PassabilityGrid grid, float agentRadius)
        {
            int agentRadiusCells = grid != null
                ? math.max(0, (int)math.ceil((agentRadius - grid.CellSize * 0.5f) / grid.CellSize))
                : 0;
            var path = FindPathInternal(startWorld, goalWorld, grid, agentRadiusCells);
            if (path == null && agentRadiusCells > 0)
                path = FindPathInternal(startWorld, goalWorld, grid, 0);
            if (path != null && path.Count >= 3)
                StringPull(path, grid, agentRadius);
            return path;
        }

        private static List<float3> FindPathInternal(float3 startWorld, float3 goalWorld,
            PassabilityGrid grid, int agentRadiusCells)
        {
            if (grid == null) return null;

            int2 startCell = grid.WorldToCell(startWorld);
            int2 goalCell = grid.WorldToCell(goalWorld);

            int w = grid.Width;
            int h = grid.Height;

            // Clamp start to grid
            startCell.x = math.clamp(startCell.x, 0, w - 1);
            startCell.y = math.clamp(startCell.y, 0, h - 1);

            // If goal is out of bounds, clamp
            goalCell.x = math.clamp(goalCell.x, 0, w - 1);
            goalCell.y = math.clamp(goalCell.y, 0, h - 1);

            int goalIndex = goalCell.y * w + goalCell.x;

            // Snap goal to passable if it's blocked
            if (grid.Cells[goalIndex] != PassabilityGrid.Passable)
            {
                goalIndex = SnapToPassable(grid, goalIndex);
                if (goalIndex < 0) return null;
                goalCell = new int2(goalIndex % w, goalIndex / w);
            }

            int startIndex = startCell.y * w + startCell.x;

            // Same cell — no path needed
            if (startIndex == goalIndex) return null;

            int totalCells = w * h;

            // Rent pooled scratch buffers (Fix #208) instead of allocating.
            EnsurePools(totalCells);
            var gCost  = s_gCostPool;
            var parent = s_parentPool;
            var closed = s_closedPool;

            // Clear the slice we're about to use.
            // Array.Clear is ~one memset and far cheaper than new int[].
            Array.Clear(closed, 0, totalCells);
            for (int i = 0; i < totalCells; i++)
            {
                gCost[i] = int.MaxValue;
                parent[i] = -1;
            }

            gCost[startIndex] = 0;

            // Priority queue: (f-cost, cell-index)
            var open = new SortedSet<(int f, int idx)>(Comparer<(int f, int idx)>.Create(
                (a, b) => a.f != b.f ? a.f.CompareTo(b.f) : a.idx.CompareTo(b.idx)));

            open.Add((OctileHeuristic(startCell, goalCell), startIndex));

            while (open.Count > 0)
            {
                var current = open.Min;
                open.Remove(current);

                int ci = current.idx;
                if (ci == goalIndex)
                    return ReconstructPath(parent, startIndex, goalIndex, w, grid);

                if (closed[ci]) continue;
                closed[ci] = true;

                int cx = ci % w;
                int cy = ci / w;

                for (int n = 0; n < 8; n++)
                {
                    int dx = Neighbors[n][0];
                    int dy = Neighbors[n][1];
                    int cost = Neighbors[n][2];

                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                    int ni = ny * w + nx;
                    if (closed[ni]) continue;
                    // Radius-aware passability: requires every cell within
                    // <agentRadiusCells> of (nx,ny) to be Passable. Falls back
                    // to plain centre-cell passability when inflation==0
                    // (used as the second-chance pass for stuck units).
                    if (agentRadiusCells <= 0)
                    {
                        if (grid.Cells[ni] != PassabilityGrid.Passable) continue;
                    }
                    else
                    {
                        if (!grid.IsCellPassableForRadius(new int2(nx, ny), agentRadiusCells)) continue;
                    }

                    // Corner-cutting prevention for diagonals
                    if (n >= 4)
                    {
                        int a1x = cx + Neighbors[n][3];
                        int a1y = cy + Neighbors[n][4];
                        int a2x = cx + Neighbors[n][5];
                        int a2y = cy + Neighbors[n][6];

                        if (a1x < 0 || a1x >= w || a1y < 0 || a1y >= h) continue;
                        if (a2x < 0 || a2x >= w || a2y < 0 || a2y >= h) continue;
                        if (grid.Cells[a1y * w + a1x] != PassabilityGrid.Passable) continue;
                        if (grid.Cells[a2y * w + a2x] != PassabilityGrid.Passable) continue;
                    }

                    int tentativeG = gCost[ci] + cost;
                    if (tentativeG >= gCost[ni]) continue;

                    gCost[ni] = tentativeG;
                    parent[ni] = ci;
                    int fCost = tentativeG + OctileHeuristic(new int2(nx, ny), goalCell);
                    open.Add((fCost, ni));
                }
            }

            // No path found
            return null;
        }

        /// <summary>Octile distance heuristic (consistent with 8-dir movement costs).</summary>
        private static int OctileHeuristic(int2 a, int2 b)
        {
            int dx = math.abs(a.x - b.x);
            int dy = math.abs(a.y - b.y);
            return CardinalCost * math.max(dx, dy) + (DiagonalCost - CardinalCost) * math.min(dx, dy);
        }

        /// <summary>
        /// Reconstruct path from parent pointers, convert to world positions,
        /// and simplify by removing collinear intermediate waypoints.
        /// </summary>
        private static List<float3> ReconstructPath(int[] parent, int startIdx, int goalIdx,
            int width, PassabilityGrid grid)
        {
            // Walk backwards from goal to start
            var rawPath = new List<int>();
            int current = goalIdx;
            while (current != startIdx)
            {
                rawPath.Add(current);
                current = parent[current];
                if (current < 0) return null; // broken chain
            }
            rawPath.Add(startIdx);
            rawPath.Reverse();

            // Convert to world positions and simplify
            if (rawPath.Count <= 2)
            {
                var result = new List<float3>(rawPath.Count);
                foreach (int idx in rawPath)
                    result.Add(grid.CellToWorld(new int2(idx % width, idx / width)));
                return result;
            }

            // Simplify: remove intermediate points with same direction
            var simplified = new List<float3> { grid.CellToWorld(new int2(rawPath[0] % width, rawPath[0] / width)) };

            int prevDx = (rawPath[1] % width) - (rawPath[0] % width);
            int prevDy = (rawPath[1] / width) - (rawPath[0] / width);

            for (int i = 2; i < rawPath.Count; i++)
            {
                int dx = (rawPath[i] % width) - (rawPath[i - 1] % width);
                int dy = (rawPath[i] / width) - (rawPath[i - 1] / width);

                if (dx != prevDx || dy != prevDy)
                {
                    // Direction changed — add the turning point
                    simplified.Add(grid.CellToWorld(new int2(rawPath[i - 1] % width, rawPath[i - 1] / width)));
                    prevDx = dx;
                    prevDy = dy;
                }
            }

            // Always add the goal
            int lastIdx = rawPath[rawPath.Count - 1];
            simplified.Add(grid.CellToWorld(new int2(lastIdx % width, lastIdx / width)));

            return simplified;
        }

        /// <summary>
        /// String-pulling smoothing pass — drops any waypoint whose
        /// predecessor and successor have a clear, radius-aware line of sight.
        /// Uses the sampled geometric LOS so it correctly rejects shortcuts
        /// that clip a building corner (which a Bresenham-on-cells scan would
        /// miss when the line passes through the corner of a blocked cell).
        /// Operates in place.
        /// </summary>
        private static void StringPull(List<float3> path, PassabilityGrid grid, float agentRadius)
        {
            if (path == null || path.Count < 3) return;
            int i = 0;
            while (i < path.Count - 2)
            {
                if (grid.HasClearLineOfSight(path[i], path[i + 2], agentRadius))
                {
                    path.RemoveAt(i + 1);
                    if (i > 0) i--; // re-test the prior segment, the new neighbour might be reachable too
                }
                else
                {
                    i++;
                }
            }
        }

        /// <summary>
        /// Spiral search for nearest passable cell (same logic as FlowFieldManager).
        /// </summary>
        private static int SnapToPassable(PassabilityGrid grid, int cellIndex)
        {
            int cx = cellIndex % grid.Width;
            int cy = cellIndex / grid.Width;
            int cellsChecked = 0;

            for (int ring = 1; cellsChecked < MaxSnapSearch; ring++)
            {
                for (int dx = -ring; dx <= ring && cellsChecked < MaxSnapSearch; dx++)
                {
                    for (int dy = -ring; dy <= ring && cellsChecked < MaxSnapSearch; dy++)
                    {
                        if (math.abs(dx) != ring && math.abs(dy) != ring) continue;

                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height)
                        {
                            cellsChecked++;
                            continue;
                        }

                        int ni = ny * grid.Width + nx;
                        if (grid.Cells[ni] == PassabilityGrid.Passable) return ni;
                        cellsChecked++;
                    }
                }
            }

            return -1;
        }
    }
}
