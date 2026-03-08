// AStarPathfinder.cs
// Static A* pathfinding utility using PassabilityGrid.
// 8-directional movement with same corner-cutting rules as FlowFieldGenerator.
// Location: Assets/Scripts/Systems/Movement/AStarPathfinder.cs

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
        /// </summary>
        public static List<float3> FindPath(float3 startWorld, float3 goalWorld, PassabilityGrid grid)
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

            // g costs (ushort.MaxValue = unvisited)
            var gCost = new int[totalCells];
            var parent = new int[totalCells];
            var closed = new bool[totalCells];

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
                    if (grid.Cells[ni] != PassabilityGrid.Passable) continue;

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
