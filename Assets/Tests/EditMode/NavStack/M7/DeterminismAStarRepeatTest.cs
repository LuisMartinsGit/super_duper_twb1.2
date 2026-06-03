// DeterminismAStarRepeatTest.cs
// task-112 M7 -- run AbstractPathfinder.Solve 100 times on the same
// hand-authored portal blob; assert byte-identical portal-id sequence
// each run. Catches A* tie-break drift (DR-3), comparator instability,
// and any hidden float math sneaking into the heuristic.
//
// The blob shape is borrowed from AbstractPathfinderTests (M3) so the
// regression surface is identical. The test re-uses the helper layout
// to keep the assertion shape obvious.
//
// Location: Assets/Tests/EditMode/NavStack/M7/DeterminismAStarRepeatTest.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M7
{
    public class DeterminismAStarRepeatTest
    {
        private const int W = 32;
        private const int H = 32;
        private const int TileSize = 16;
        private const int RepeatRuns = 100;

        [Test]
        public void AStarSolve_100Runs_ByteIdenticalPortalSequence()
        {
            var grid = new NavGridSingleton
            {
                Width = W,
                Height = H,
                CellSize = 1f,
                Origin = float3.zero,
                LayerCount = 1,
            };

            // Build the reference run.
            var refBlob = BuildBlob();
            using var refPortals = new NativeList<int>(16, Allocator.Temp);
            byte refStatus = AbstractPathfinder.Solve(
                ref refBlob.Value, grid, new int2(1, 1), new int2(30, 30), refPortals);
            refBlob.Dispose();
            Assert.AreEqual(NavPathRequest.StatusSuccess, refStatus,
                "reference run must succeed");
            var reference = new int[refPortals.Length];
            for (int i = 0; i < refPortals.Length; i++) reference[i] = refPortals[i];

            // Repeat 99 times against fresh blobs (so the blob's internal
            // allocator state doesn't smuggle in cross-run state).
            for (int run = 1; run < RepeatRuns; run++)
            {
                var blob = BuildBlob();
                using var portals = new NativeList<int>(16, Allocator.Temp);
                byte status = AbstractPathfinder.Solve(
                    ref blob.Value, grid, new int2(1, 1), new int2(30, 30), portals);
                blob.Dispose();
                Assert.AreEqual(NavPathRequest.StatusSuccess, status,
                    "run " + run + " must succeed");
                Assert.AreEqual(reference.Length, portals.Length,
                    "run " + run + " produced different path length");
                for (int i = 0; i < reference.Length; i++)
                {
                    Assert.AreEqual(reference[i], portals[i],
                        "run " + run + " portal[" + i + "] diverged");
                }
            }
        }

        // Mirror of AbstractPathfinderTests.BuildToyBlob (M3). Kept inline
        // so M7 tests are self-contained.
        private static BlobAssetReference<PortalGraphBlob> BuildBlob()
        {
            int realNodeCount = 8;
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
            var edges = new PortalEdge[]
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
            int[] nodeFirstEdge = new int[realNodeCount + 1];
            for (int i = 0; i < edges.Length; i++) nodeFirstEdge[edges[i].FromPortalId + 1]++;
            for (int i = 1; i <= realNodeCount; i++) nodeFirstEdge[i] += nodeFirstEdge[i - 1];

            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<PortalGraphBlob>();
            root.TileSize = TileSize;
            root.TilesX = 2;
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
