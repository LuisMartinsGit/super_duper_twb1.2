// IncrementalRebuildTests.cs
// task-112 M4 -- demonstrates that the incremental rebuild produces
// a different portal-graph blob when the underlying cost field changes
// across a tile boundary. We can't easily exercise
// IncrementalPortalRebuildSystem standalone (it needs a full ECS world
// + singletons), so the test runs PortalDetectionJob against two cost
// slabs and asserts the second pass discovers different portals at
// the modified boundary.
//
// This mirrors the chunk of incremental rebuild that actually CHANGES
// when a wall placement dirties tiles -- the detection pass over the
// dirty tiles' boundaries -- without depending on the surrounding
// ECS / blob plumbing.
//
// Location: Assets/Tests/EditMode/NavStack/M4/IncrementalRebuildTests.cs

using NUnit.Framework;
using Unity.Collections;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Tests.EditMode.NavStack.M4
{
    public class IncrementalRebuildTests
    {
        // Use a 32x32 grid with TileSize = 16 -- yields a 2x2 tile grid.
        private const int Width = 32;
        private const int Height = 32;
        private const int TileSize = 16;
        private const int TilesX = 2;
        private const int TilesZ = 2;

        [Test]
        public void WallPlacedAcrossBoundary_ReducesPortalCount_OnSecondPass()
        {
            // Pass 1: empty grid -- every boundary cell is walkable, so
            // each of the 4 boundary segments (east of tile 0, east of
            // tile 2, north of tile 0, north of tile 1) emits ONE portal
            // spec (a single contiguous span the full length of the
            // tile-side). 4 portal specs total.
            var cost = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var beforePortals = new NativeList<PortalSpec>(64, Allocator.Temp);

            var detect1 = new PortalDetectionJob
            {
                Cost = cost,
                Width = Width,
                Height = Height,
                TileSize = TileSize,
                TilesX = TilesX,
                TilesZ = TilesZ,
                Portals = beforePortals,
            };
            detect1.Execute();
            int beforeCount = beforePortals.Length;

            // Pass 2: stamp a wall across the WHOLE east boundary of tile 0
            // (x = 15 column, z = 0..15). This cuts the span into nothing.
            for (int z = 0; z < TileSize; z++)
                cost[z * Width + (TileSize - 1)] = NavCostField.CostImpassable;

            var afterPortals = new NativeList<PortalSpec>(64, Allocator.Temp);
            var detect2 = new PortalDetectionJob
            {
                Cost = cost,
                Width = Width,
                Height = Height,
                TileSize = TileSize,
                TilesX = TilesX,
                TilesZ = TilesZ,
                Portals = afterPortals,
            };
            detect2.Execute();
            int afterCount = afterPortals.Length;

            // The walled boundary (east of tile 0) lost its portal entirely
            // (every boundary cell on the lower-tile side is now impassable).
            // The other 3 boundaries are unchanged.
            Assert.AreEqual(beforeCount - 1, afterCount,
                "stamping a full wall across the east boundary of tile 0 should lose exactly 1 portal");

            cost.Dispose();
            beforePortals.Dispose();
            afterPortals.Dispose();
        }

        [Test]
        public void WallRemoved_RestoresPortalCount_OnThirdPass()
        {
            // The full cycle exercised by Phase4ScriptedWallController:
            // pass 1 = empty -> 4 portals; pass 2 = wall placed ->
            // 3 portals; pass 3 = wall removed -> 4 portals again.
            // This is what the M4 incremental rebuild + cache invalidation
            // does end-to-end (the only difference is the cache eviction
            // side, covered in CacheInvalidationTests).
            var cost = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            int countPass1 = RunDetect(cost);

            // Stamp wall.
            for (int z = 0; z < TileSize; z++)
                cost[z * Width + (TileSize - 1)] = NavCostField.CostImpassable;
            int countPass2 = RunDetect(cost);

            // Clear wall.
            for (int z = 0; z < TileSize; z++)
                cost[z * Width + (TileSize - 1)] = 0;
            int countPass3 = RunDetect(cost);

            Assert.AreEqual(countPass1, countPass3,
                "removing the wall must restore the original portal count");
            Assert.Less(countPass2, countPass1,
                "placing the wall must reduce the portal count");

            cost.Dispose();
        }

        private static int RunDetect(NativeArray<byte> cost)
        {
            var portals = new NativeList<PortalSpec>(64, Allocator.Temp);
            var detect = new PortalDetectionJob
            {
                Cost = cost,
                Width = Width,
                Height = Height,
                TileSize = TileSize,
                TilesX = TilesX,
                TilesZ = TilesZ,
                Portals = portals,
            };
            detect.Execute();
            int count = portals.Length;
            portals.Dispose();
            return count;
        }
    }
}
