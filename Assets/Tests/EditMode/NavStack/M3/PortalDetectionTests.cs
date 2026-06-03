// PortalDetectionTests.cs
// task-112 M3: hand-authored 32x32 cost field split into 2x2 tiles
// (TileSize = 16). A vertical wall down the middle of the central
// boundary leaves a known gap; assert PortalDetectionJob emits exactly
// one portal per expected boundary opening at the correct cells.
//
// Layout (32x32 grid, tile size 16, so 2 tiles per axis = 4 tiles):
//   * Tiles: TL(0)=(tx=0,tz=0), TR(1)=(1,0), BL(2)=(0,1), BR(3)=(1,1).
//     (tileIndex = tz * tilesX + tx with tilesX=2).
//   * All cells walkable EXCEPT a column at x=15 (the last column of
//     the western half / the east-most cell of the left tiles) which
//     is impassable on rows z=0..6 and z=10..15, leaving an open span
//     at z=7..9 (3 cells).
//
// The east-tile-boundary detector walks from tile-pair (TL,TR) and
// (BL,BR). For (TL,TR): boundaryX=15, neighbourX=16, z range 0..15.
//   * Row z=0..6: cell (15,z) is impassable -> no portal.
//   * Row z=7..9: open span of length 3 -> ONE portal at midpoint
//     (15, 8) on TL (CellIndex = 8 * 32 + 15 = 271). Neighbour
//     CellIndex = 8 * 32 + 16 = 272.
//   * Row z=10..15: (15,z) impassable on z=10..15 -> no further portals.
// For (BL,BR): boundaryX=15, z range 16..31. The wall cells are at
// z=0..6 and 10..15 so on the BL/BR boundary every cell is open
// (z=16..31 are not part of the wall). That's one big span of length
// 16 -> ONE portal at midpoint z = 16 + 16/2 = 24. CellIndex = 24*32+15.
//
// North boundary: TL/BL and TR/BR. The wall doesn't intersect these
// boundaries -- every cell is open -> ONE portal each at midpoint
// x = 0 + 16/2 = 8. CellIndices = 15*32+8 and 15*32+24.
//
// Expected total portal SPECS = 4 (one per (TL/TR east), (BL/BR east),
// (TL/BL north), (TR/BR north)).
//
// Location: Assets/Tests/EditMode/NavStack/M3/PortalDetectionTests.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M3
{
    public class PortalDetectionTests
    {
        private const int W = 32;
        private const int H = 32;
        private const int TileSize = 16;
        private const int TilesX = 2;
        private const int TilesZ = 2;

        [Test]
        public void Detect_OneEastPortalPerBoundary_AndOneNorthPortal()
        {
            var cost = NewCostField();
            var portals = new NativeList<PortalSpec>(64, Allocator.TempJob);

            var job = new PortalDetectionJob
            {
                Cost = cost,
                Width = W,
                Height = H,
                TileSize = TileSize,
                TilesX = TilesX,
                TilesZ = TilesZ,
                Portals = portals,
            };
            job.Execute();

            // Expected: 4 specs total.
            // (TL/TR east), (BL/BR east), (TL/BL north), (TR/BR north).
            Assert.AreEqual(4, portals.Length,
                "Expected exactly 4 portal specs (2 east boundaries + 2 north boundaries).");

            // Sort specs by TileIndex asc, CellIndex asc for stable assertions.
            var sorted = new System.Collections.Generic.List<PortalSpec>();
            for (int i = 0; i < portals.Length; i++) sorted.Add(portals[i]);
            sorted.Sort((a, b) =>
            {
                if (a.TileIndex != b.TileIndex) return a.TileIndex - b.TileIndex;
                return a.CellIndex - b.CellIndex;
            });

            // TileIndex 0 (TL): two outgoing portals -- east (to TR=1) and
            // north (to BL=2).
            //   East: midpoint of z=7..9 span -> z=8. Cell (15, 8) idx 271.
            //   North: the NewCostField helper walls x=15 at z=0..6 and
            //         z=10..15, which truncates the TL-north walkable span
            //         to x=0..14 (15 cells, not 16 -- the x=15 cell at the
            //         TL's north row is walled). Midpoint = 0 + 15/2 = 7.
            //         Cell (7, 15) idx 487.
            Assert.AreEqual(0, sorted[0].TileIndex);
            Assert.AreEqual(271, sorted[0].CellIndex,
                $"TL east portal cell expected (15, 8) idx 271, got {sorted[0].CellIndex}");
            Assert.AreEqual(272, sorted[0].NeighbourCellIndex);
            Assert.AreEqual(1, sorted[0].NeighbourTileIndex);

            Assert.AreEqual(0, sorted[1].TileIndex);
            Assert.AreEqual(15 * W + 7, sorted[1].CellIndex,
                $"TL north portal cell expected (7, 15) idx 487, got {sorted[1].CellIndex}");
            Assert.AreEqual(16 * W + 7, sorted[1].NeighbourCellIndex);
            Assert.AreEqual(2, sorted[1].NeighbourTileIndex);

            // TileIndex 1 (TR): north portal (to BR=3). No east portal
            // (TR is at the map edge in x).
            Assert.AreEqual(1, sorted[2].TileIndex);
            Assert.AreEqual(15 * W + 24, sorted[2].CellIndex,
                $"TR north portal cell expected (24, 15) idx {15*32+24}, got {sorted[2].CellIndex}");
            Assert.AreEqual(3, sorted[2].NeighbourTileIndex);

            // TileIndex 2 (BL): east portal (to BR=3). North impossible
            // (BL is at the map edge in z).
            //   East: open span z=16..31 (length 16), mid = 16 + 16/2 = 24.
            //   Cell (15, 24) idx 24*32 + 15 = 783.
            Assert.AreEqual(2, sorted[3].TileIndex);
            Assert.AreEqual(24 * W + 15, sorted[3].CellIndex,
                $"BL east portal cell expected (15, 24) idx 783, got {sorted[3].CellIndex}");
            Assert.AreEqual(3, sorted[3].NeighbourTileIndex);
        }

        [Test]
        public void Detect_NoPortalsWhenBoundaryFullyWalled()
        {
            // Build a cost field with x=15 column COMPLETELY blocked.
            var cost = new NativeArray<byte>(W * H, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            for (int z = 0; z < H; z++)
                cost[z * W + 15] = NavCostField.CostImpassable;

            var portals = new NativeList<PortalSpec>(64, Allocator.TempJob);
            new PortalDetectionJob
            {
                Cost = cost,
                Width = W,
                Height = H,
                TileSize = TileSize,
                TilesX = TilesX,
                TilesZ = TilesZ,
                Portals = portals,
            }.Execute();

            // East boundaries (TL/TR and BL/BR) are fully walled -> 0 portals.
            // North boundaries still emit 1 each.
            int eastCount = 0, northCount = 0;
            for (int i = 0; i < portals.Length; i++)
            {
                var p = portals[i];
                if (p.NeighbourCellIndex == p.CellIndex + 1) eastCount++;
                else northCount++;
            }
            Assert.AreEqual(0, eastCount, "fully-walled east boundary must emit no portals");
            Assert.AreEqual(2, northCount, "north boundaries should still emit one portal each");
        }

        // Build the 32x32 grid described in the file header. Wall at x=15
        // for z in [0..6] and [10..15].
        private static NativeArray<byte> NewCostField()
        {
            var cost = new NativeArray<byte>(W * H, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            for (int z = 0; z <= 6; z++)
                cost[z * W + 15] = NavCostField.CostImpassable;
            for (int z = 10; z <= 15; z++)
                cost[z * W + 15] = NavCostField.CostImpassable;
            return cost;
        }
    }
}
