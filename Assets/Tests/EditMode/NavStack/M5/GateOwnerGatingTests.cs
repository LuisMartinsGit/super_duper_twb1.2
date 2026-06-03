// GateOwnerGatingTests.cs
// task-112 M5 -- hand-authors a tiny portal graph blob with one
// gate-ground portal owned by Blue (owner id 0). Calls
// AbstractPathfinder.SolveGated with (a) a Blue unit owner -> path
// crosses; (b) a Red unit owner -> path rejected at the gate (returns
// unreachable because the gate is the only crossing).
//
// Validates R4: gate gating consults the owner-bits mirror per query.
//
// Location: Assets/Tests/EditMode/NavStack/M5/GateOwnerGatingTests.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Tests.EditMode.NavStack.M5
{
    public class GateOwnerGatingTests
    {
        private const int Width = 32;
        private const int Height = 32;
        private const int TileSize = 16;

        [Test]
        public void OwnerMatch_CrossesGate_OwnerMismatch_Rejected()
        {
            // Build a minimal blob:
            //   * 1 inter-tile portal pair on the boundary x=15 z=8
            //     (nodes 0, 1) -- the "free" crossing.
            //   * 1 gate-ground portal pair (nodes 2, 3) owned by Blue.
            //   * No path through node 0/1 (we mark them KindInterTile
            //     but break the graph by setting them as IsolatedPortalIds
            //     -- we deliberately omit edges between them).
            //
            // The test exercises the gate ADMISSION path: with Blue owner
            // gating returns success (gate admits Blue), with Red gating
            // returns unreachable.
            const int BlueOwner = 0;
            const int RedOwner = 1;

            // Build the blob.
            BlobAssetReference<PortalGraphBlob> blob;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<PortalGraphBlob>();
                root.TileSize = TileSize;
                root.TilesX = 2;
                root.TilesZ = 2;

                var nodes = builder.Allocate(ref root.Nodes, 2);
                // Gate-ground portal pair at cells (14, 8) and (16, 8).
                nodes[0] = new PortalNode
                {
                    Id = 0,
                    CellIndex = 8 * Width + 14,
                    TileIndex = 0, // tile 0 (tileX 0, tileZ 0)
                    PortalKind = PortalNode.KindGateGround,
                    OwnerId = BlueOwner,
                    Layer = 0,
                };
                nodes[1] = new PortalNode
                {
                    Id = 1,
                    CellIndex = 8 * Width + 16,
                    TileIndex = 1, // tile 1 (tileX 1, tileZ 0)
                    PortalKind = PortalNode.KindGateGround,
                    OwnerId = BlueOwner,
                    Layer = 0,
                };

                var edges = builder.Allocate(ref root.Edges, 2);
                edges[0] = new PortalEdge
                {
                    FromPortalId = 0,
                    ToPortalId = 1,
                    Cost = 10,
                    ProfileMask = 0xFF,
                };
                edges[1] = new PortalEdge
                {
                    FromPortalId = 1,
                    ToPortalId = 0,
                    Cost = 10,
                    ProfileMask = 0xFF,
                };

                var firstEdge = builder.Allocate(ref root.NodeFirstEdge, 3);
                firstEdge[0] = 0; // node 0 -> edge 0..0
                firstEdge[1] = 1; // node 1 -> edge 1..1
                firstEdge[2] = 2; // sentinel

                blob = builder.CreateBlobAssetReference<PortalGraphBlob>(Allocator.Temp);
            }

            try
            {
                var grid = new NavGridSingleton
                {
                    Width = Width,
                    Height = Height,
                    CellSize = 1f,
                    Origin = new float3(0, 0, 0),
                    LayerCount = 2,
                };

                // Mirror with the gate OPEN to Blue.
                var mirror = new NativeArray<ushort>(2, Allocator.Temp);
                mirror[0] = PortalOwnerBitsMirror.Pack(BlueOwner, true);
                mirror[1] = PortalOwnerBitsMirror.Pack(BlueOwner, true);

                // Solve as Blue -- starts on tile 0 (cell 0,0), goal on
                // tile 1 (cell 31,15).
                ref var graph = ref blob.Value;
                var bluePath = new NativeList<int>(8, Allocator.Temp);
                byte blueStatus = AbstractPathfinder.SolveGated(ref graph, grid,
                    new int2(0, 0), new int2(31, 15), bluePath,
                    mirror, BlueOwner);
                Assert.AreEqual(NavPathRequest.StatusSuccess, blueStatus,
                    "Blue should cross its own gate");
                Assert.IsTrue(bluePath.Length >= 2,
                    "Blue path must include at least start/goal virtuals");
                bluePath.Dispose();

                // Solve as Red -- gate owner mismatch, no other crossing,
                // expect Unreachable.
                var redPath = new NativeList<int>(8, Allocator.Temp);
                byte redStatus = AbstractPathfinder.SolveGated(ref graph, grid,
                    new int2(0, 0), new int2(31, 15), redPath,
                    mirror, RedOwner);
                Assert.AreEqual(NavPathRequest.StatusUnreachable, redStatus,
                    "Red must be rejected at Blue's gate");
                redPath.Dispose();

                mirror.Dispose();
            }
            finally
            {
                blob.Dispose();
            }
        }
    }
}
