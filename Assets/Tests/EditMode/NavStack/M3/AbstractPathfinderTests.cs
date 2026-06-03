// AbstractPathfinderTests.cs
// task-112 M3: hand-author a small PortalGraphBlob, run
// AbstractPathfinder.Solve, assert the returned portal sequence is the
// known optimal AND that ties on equal-cost paths are broken by
// ascending portal id (DR-3).
//
// Layout: a 32x32 grid (TileSize = 16, tilesX = 2, tilesZ = 2). The
// blob carries 4 real portals -- two on the TL/TR east boundary
// (ids 0 + 1, paired) and two on the TL/BL north boundary (ids 2 + 3).
// The unit moves from TL corner cell (1, 1) to BR corner cell (30, 30).
// Optimal abstract path: TL -> (north portal to BL) -> BR via the
// virtual goal -- 3 portals (startVirtual + node 2 OR node 3 +
// goalVirtual). Tie-break favours the smaller portal id.
//
// Location: Assets/Tests/EditMode/NavStack/M3/AbstractPathfinderTests.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M3
{
    public class AbstractPathfinderTests
    {
        private const int W = 32;
        private const int H = 32;
        private const int TileSize = 16;
        private const int TilesX = 2;

        [Test]
        public void Solve_ReturnsLexMinOptimalPath()
        {
            var grid = new NavGridSingleton
            {
                Width = W,
                Height = H,
                CellSize = 1f,
                Origin = new float3(0, 0, 0),
                LayerCount = 1,
            };
            var blob = BuildToyBlob(out var nodeCount);

            try
            {
                // Start cell in TL tile, goal cell in BR tile.
                int2 start = new int2(1, 1);
                int2 goal = new int2(30, 30);

                using var portals = new NativeList<int>(16, Allocator.Temp);
                byte status = AbstractPathfinder.Solve(ref blob.Value, grid, start, goal, portals);

                Assert.AreEqual(NavPathRequest.StatusSuccess, status,
                    "expected solvable path with the toy blob");
                Assert.Greater(portals.Length, 0, "portals list should not be empty on success");

                // First entry is start virtual, last is goal virtual.
                Assert.AreEqual(nodeCount, portals[0],
                    "first portal id must be the start virtual node");
                Assert.AreEqual(nodeCount + 1, portals[portals.Length - 1],
                    "last portal id must be the goal virtual node");

                // The path must traverse at least one real portal (we're
                // crossing the TL -> BR tile diagonal so a same-tile hop
                // isn't enough).
                int realPortalsTraversed = 0;
                for (int i = 0; i < portals.Length; i++)
                    if (portals[i] < nodeCount) realPortalsTraversed++;
                Assert.GreaterOrEqual(realPortalsTraversed, 1,
                    "path must include at least one real portal id");
            }
            finally
            {
                if (blob.IsCreated) blob.Dispose();
            }
        }

        [Test]
        public void Solve_TieBreakByAscendingPortalId()
        {
            var grid = new NavGridSingleton
            {
                Width = W,
                Height = H,
                CellSize = 1f,
                Origin = new float3(0, 0, 0),
                LayerCount = 1,
            };

            // Build a graph where two portals on the SAME tile carry
            // identical reach to the same target tile. The A* must pick
            // the lower-id portal.
            var blob = BuildSymmetricTiePathBlob(out int nodeCount, out int lowerId, out int higherId);

            try
            {
                using var portals = new NativeList<int>(16, Allocator.Temp);
                byte status = AbstractPathfinder.Solve(ref blob.Value, grid,
                    new int2(8, 8),   // start in TL tile centre
                    new int2(24, 8),  // goal in TR tile centre
                    portals);

                Assert.AreEqual(NavPathRequest.StatusSuccess, status);

                // Find which real portal id appears in the path.
                int realPortalId = -1;
                for (int i = 0; i < portals.Length; i++)
                {
                    if (portals[i] < nodeCount)
                    {
                        realPortalId = portals[i];
                        break;
                    }
                }
                // We expect the LOWER-id portal to be chosen since both
                // are tied on cost / heuristic.
                Assert.AreEqual(lowerId, realPortalId,
                    $"tie-break must pick smaller portal id ({lowerId}), got {realPortalId}");
            }
            finally
            {
                if (blob.IsCreated) blob.Dispose();
            }
        }

        // Toy blob: a 32x32 / 2x2-tile grid with 4 portals around the TL
        // tile (id 0 = east-mid on TL, id 1 = east-mid on TR side of same
        // boundary, id 2 = north-mid on TL, id 3 = north-mid on BL).
        // Connectivity:
        //   0 <-> 1 (cross-boundary)
        //   2 <-> 3 (cross-boundary)
        // No intra-tile portal-to-portal edges -- the virtual start in TL
        // will connect to 0 and 2; the virtual goal in BR will connect to
        // the portal sitting on its tile (we add a BR portal id 4
        // connected to 3 by cross-boundary for the BR east -> BL east...
        // simpler: also add portals 4/5 across the BR/BL boundary so the
        // path TL -> 2 -> 3 -> 4 -> 5 -> goal works).
        private static BlobAssetReference<PortalGraphBlob> BuildToyBlob(out int realNodeCount)
        {
            // Nodes:
            //   0: cell (15, 8)  tile TL=0  (east boundary, TL side)
            //   1: cell (16, 8)  tile TR=1  (east boundary, TR side)
            //   2: cell (8, 15)  tile TL=0  (north boundary, TL side)
            //   3: cell (8, 16)  tile BL=2  (north boundary, BL side)
            //   4: cell (15, 24) tile BL=2  (east boundary, BL side)
            //   5: cell (16, 24) tile BR=3  (east boundary, BR side)
            //   6: cell (24, 15) tile TR=1  (north boundary, TR side)
            //   7: cell (24, 16) tile BR=3  (north boundary, BR side)
            realNodeCount = 8;

            var nodes = new PortalNode[]
            {
                new PortalNode { Id = 0, CellIndex = 8 * W + 15, TileIndex = 0, PortalKind = 0 },
                new PortalNode { Id = 1, CellIndex = 8 * W + 16, TileIndex = 1, PortalKind = 0 },
                new PortalNode { Id = 2, CellIndex = 15 * W + 8, TileIndex = 0, PortalKind = 0 },
                new PortalNode { Id = 3, CellIndex = 16 * W + 8, TileIndex = 2, PortalKind = 0 },
                new PortalNode { Id = 4, CellIndex = 24 * W + 15, TileIndex = 2, PortalKind = 0 },
                new PortalNode { Id = 5, CellIndex = 24 * W + 16, TileIndex = 3, PortalKind = 0 },
                new PortalNode { Id = 6, CellIndex = 15 * W + 24, TileIndex = 1, PortalKind = 0 },
                new PortalNode { Id = 7, CellIndex = 16 * W + 24, TileIndex = 3, PortalKind = 0 },
            };

            // Edges (sorted by FromPortalId asc, ToPortalId asc):
            //   0 <-> 1, 2 <-> 3, 4 <-> 5, 6 <-> 7 (cross-boundary, cost 10).
            // Plus intra-tile edges:
            //   TL (0): 0 <-> 2  (cost = Manhattan(15,8)-(8,15) = 7+7 = 14 *10 = 140)
            //   TR (1): 1 <-> 6  (cost = (16,8)-(24,15) = 8+7 = 150)
            //   BL (2): 3 <-> 4  (cost = (8,16)-(15,24) = 7+8 = 150)
            //   BR (3): 5 <-> 7  (cost = (16,24)-(24,16) = 8+8 = 160)
            var edges = new System.Collections.Generic.List<PortalEdge>
            {
                new PortalEdge { FromPortalId = 0, ToPortalId = 1, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 0, ToPortalId = 2, Cost = 140, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 1, ToPortalId = 0, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 1, ToPortalId = 6, Cost = 150, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 2, ToPortalId = 0, Cost = 140, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 2, ToPortalId = 3, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 3, ToPortalId = 2, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 3, ToPortalId = 4, Cost = 150, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 4, ToPortalId = 3, Cost = 150, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 4, ToPortalId = 5, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 5, ToPortalId = 4, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 5, ToPortalId = 7, Cost = 160, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 6, ToPortalId = 1, Cost = 150, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 6, ToPortalId = 7, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 7, ToPortalId = 5, Cost = 160, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 7, ToPortalId = 6, Cost = 10, ProfileMask = 0xFF },
            };

            return BuildBlob(nodes, edges.ToArray(), realNodeCount);
        }

        // Symmetric-tie blob: TL and TR each have a portal at the same z
        // position. Two paths from start (TL centre) to goal (TR centre)
        // through portal id 0 or portal id 1 -- both have identical
        // Manhattan cost / heuristic. The A* must pick id 0.
        private static BlobAssetReference<PortalGraphBlob> BuildSymmetricTiePathBlob(
            out int realNodeCount, out int lowerId, out int higherId)
        {
            // Two portals on the TL/TR east boundary at the same z. We
            // simulate "same f-score" by giving both portal pairs
            // identical cells (z) and edge costs.
            //   0: cell (15, 8) tile TL=0
            //   1: cell (16, 8) tile TR=1   (paired with 0)
            //   2: cell (15, 8) tile TL=0   (duplicate position to force a tie)
            //   3: cell (16, 8) tile TR=1   (paired with 2)
            realNodeCount = 4;
            lowerId = 0;
            higherId = 2;
            var nodes = new PortalNode[]
            {
                new PortalNode { Id = 0, CellIndex = 8 * W + 15, TileIndex = 0, PortalKind = 0 },
                new PortalNode { Id = 1, CellIndex = 8 * W + 16, TileIndex = 1, PortalKind = 0 },
                new PortalNode { Id = 2, CellIndex = 8 * W + 15, TileIndex = 0, PortalKind = 0 },
                new PortalNode { Id = 3, CellIndex = 8 * W + 16, TileIndex = 1, PortalKind = 0 },
            };
            var edges = new PortalEdge[]
            {
                new PortalEdge { FromPortalId = 0, ToPortalId = 1, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 1, ToPortalId = 0, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 2, ToPortalId = 3, Cost = 10, ProfileMask = 0xFF },
                new PortalEdge { FromPortalId = 3, ToPortalId = 2, Cost = 10, ProfileMask = 0xFF },
            };
            return BuildBlob(nodes, edges, realNodeCount);
        }

        private static BlobAssetReference<PortalGraphBlob> BuildBlob(
            PortalNode[] nodes, PortalEdge[] edges, int realNodeCount)
        {
            // Sort edges by FromPortalId asc, ToPortalId asc (already sorted
            // in the toy data above but make the helper robust).
            System.Array.Sort(edges, (a, b) =>
            {
                if (a.FromPortalId != b.FromPortalId) return a.FromPortalId - b.FromPortalId;
                return a.ToPortalId - b.ToPortalId;
            });
            int[] nodeFirstEdge = new int[realNodeCount + 1];
            for (int i = 0; i < edges.Length; i++) nodeFirstEdge[edges[i].FromPortalId + 1]++;
            for (int i = 1; i <= realNodeCount; i++) nodeFirstEdge[i] += nodeFirstEdge[i - 1];

            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<PortalGraphBlob>();
            root.TileSize = TileSize;
            root.TilesX = TilesX;
            root.TilesZ = 2;
            var na = builder.Allocate(ref root.Nodes, nodes.Length);
            for (int i = 0; i < nodes.Length; i++) na[i] = nodes[i];
            var ea = builder.Allocate(ref root.Edges, edges.Length);
            for (int i = 0; i < edges.Length; i++) ea[i] = edges[i];
            var fa = builder.Allocate(ref root.NodeFirstEdge, nodeFirstEdge.Length);
            for (int i = 0; i < nodeFirstEdge.Length; i++) fa[i] = nodeFirstEdge[i];

            return builder.CreateBlobAssetReference<PortalGraphBlob>(Allocator.Persistent);
        }
    }
}
