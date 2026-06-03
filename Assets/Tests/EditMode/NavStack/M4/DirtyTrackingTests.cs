// DirtyTrackingTests.cs
// task-112 M4 -- exercises the dirty-tile bookkeeping at the data
// level. Replicates the BuildingCostStampSystem diff logic against a
// hand-authored cost slab + shadow slab, asserting that the tile-index
// math matches the system's behaviour for both the "place" and the
// "swap to adjacent footprint" cases.
//
// We don't spin up an ECS world for these tests -- the diff is a pure
// function of (oldCost, newCost) so the test runs in pure NativeArray
// land and is byte-stable across machines.
//
// Location: Assets/Tests/EditMode/NavStack/M4/DirtyTrackingTests.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Tests.EditMode.NavStack.M4
{
    public class DirtyTrackingTests
    {
        // Tile size locked at 16 (CCD-4); use a small grid that crosses
        // exactly two tile boundaries.
        private const int Width = 32;
        private const int Height = 32;
        private const int TileSize = 16; // matches PortalGraphSingleton.TileSize

        [Test]
        public void Stamp_NewBuilding_PopulatesDirtyTiles()
        {
            var shadow = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var current = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var dirty = new NativeHashSet<int>(64, Allocator.Temp);

            try
            {
                // Stamp a 3x3 building centred at cell (8, 8) -- entirely
                // inside the upper-left tile (tile index 0 == tileZ 0,
                // tileX 0 on a 2x2 tile grid).
                StampBuilding(current, cx: 8, cz: 8, w: 3, h: 3);

                DiffAndMarkDirty(current, shadow, dirty);

                Assert.AreEqual(1, dirty.Count,
                    "stamping inside a single tile should mark exactly one tile dirty");
                Assert.IsTrue(dirty.Contains(0),
                    "the tile containing cells around (8, 8) is index 0");
            }
            finally
            {
                shadow.Dispose();
                current.Dispose();
                dirty.Dispose();
            }
        }

        [Test]
        public void Stamp_CornerBuilding_MarksFourAdjacentTilesDirty()
        {
            var shadow = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var current = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var dirty = new NativeHashSet<int>(64, Allocator.Temp);

            try
            {
                // Stamp a 3x3 building straddling the tile corner at
                // cell (16, 16). On a 2x2 tile grid, cells (15,15) ..
                // (17,17) belong to all 4 tiles.
                StampBuilding(current, cx: 16, cz: 16, w: 3, h: 3);

                DiffAndMarkDirty(current, shadow, dirty);

                Assert.AreEqual(4, dirty.Count,
                    "a footprint that straddles the centre corner must mark all 4 tiles");
                Assert.IsTrue(dirty.Contains(0), "tile 0 (NW) must be dirty");
                Assert.IsTrue(dirty.Contains(1), "tile 1 (NE) must be dirty");
                Assert.IsTrue(dirty.Contains(2), "tile 2 (SW) must be dirty");
                Assert.IsTrue(dirty.Contains(3), "tile 3 (SE) must be dirty");
            }
            finally
            {
                shadow.Dispose();
                current.Dispose();
                dirty.Dispose();
            }
        }

        [Test]
        public void Stamp_SecondStamp_OnDifferentFootprint_OnlyDirtyChangedTiles()
        {
            var shadow = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var current = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            var dirty = new NativeHashSet<int>(64, Allocator.Temp);

            try
            {
                // Stamp 1: building in tile 0 (cells around (8, 8)).
                StampBuilding(current, cx: 8, cz: 8, w: 3, h: 3);
                DiffAndMarkDirty(current, shadow, dirty);
                Assert.IsTrue(dirty.Contains(0));

                // Drain + bump generation (simulates IncrementalPortalRebuild
                // having processed this tick's dirty set).
                dirty.Clear();

                // Stamp 2: leave the first stamp in place but ADD a second
                // building in tile 3 (cells around (24, 24)). Now current
                // has both footprints; shadow has only the first. The diff
                // should ONLY mark tile 3 dirty (tile 0 didn't change).
                StampBuilding(current, cx: 24, cz: 24, w: 3, h: 3);
                DiffAndMarkDirty(current, shadow, dirty);

                Assert.AreEqual(1, dirty.Count,
                    "adding a building in tile 3 must only dirty tile 3");
                Assert.IsTrue(dirty.Contains(3),
                    "the new footprint sits in tile 3 (SE tile)");
                Assert.IsFalse(dirty.Contains(0),
                    "tile 0 was unchanged since the last drain");
            }
            finally
            {
                shadow.Dispose();
                current.Dispose();
                dirty.Dispose();
            }
        }

        // ── helpers ────────────────────────────────────────────────────

        private static void StampBuilding(NativeArray<byte> cost, int cx, int cz, int w, int h)
        {
            int halfW = w / 2;
            int halfH = h / 2;
            int x0 = math.max(0, cx - halfW);
            int z0 = math.max(0, cz - halfH);
            int x1 = math.min(Width - 1, cx + halfW);
            int z1 = math.min(Height - 1, cz + halfH);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    cost[z * Width + x] = NavCostField.CostImpassable;
        }

        // Mirrors BuildingCostStampSystem.OnUpdate's diff loop.
        private static void DiffAndMarkDirty(
            NativeArray<byte> current,
            NativeArray<byte> shadow,
            NativeHashSet<int> dirty)
        {
            int tilesX = (Width + TileSize - 1) / TileSize;
            int total = Width * Height;
            for (int i = 0; i < total; i++)
            {
                byte cur = current[i];
                byte prev = shadow[i];
                if (cur == prev) continue;

                int x = i % Width;
                int z = i / Width;
                int tileX = x / TileSize;
                int tileZ = z / TileSize;
                int tileIndex = tileZ * tilesX + tileX;
                dirty.Add(tileIndex);
                shadow[i] = cur;
            }
        }
    }
}
