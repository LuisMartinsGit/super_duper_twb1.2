// LayerCostFieldTests.cs
// task-112 M5 -- exercises the per-layer cost-field stamping. Builds
// a synthetic two-layer cost slab + stamps a wall footprint using the
// same pattern StampWallLayersJob writes. Asserts:
//   * Ground layer cells under the wall = 255 (impassable) except gate
//     cells which become 254 (conditional).
//   * Rampart layer cells under the wall = 1 (walkable wall-top).
//   * Rampart layer cells AWAY from the wall stay at 255 (the empty-
//     air default).
//
// Hand-authored to stay independent of the ECS world / scheduling,
// matching the pattern the M4 tests use.

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M5
{
    public class LayerCostFieldTests
    {
        private const int Width = 16;
        private const int Height = 16;
        private const int LayerCount = 2;

        [Test]
        public void WallStamp_GroundImpassable_RampartWalkable()
        {
            int layerArea = Width * Height;
            var cost = new NativeArray<byte>(layerArea * LayerCount, Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            try
            {
                // Initialise the rampart layer to impassable (matches
                // ClearLayerImpassableJob behaviour).
                for (int i = 0; i < layerArea; i++)
                    cost[layerArea + i] = NavCostField.CostImpassable;

                // Stamp a 3x3 wall footprint centred at (8, 8).
                StampWallAt(cost, cx: 8, cz: 8, w: 3, h: 3,
                    isGate: false, isClimb: false, layerArea: layerArea);

                // Ground cells under the wall = 255.
                for (int z = 7; z <= 9; z++)
                    for (int x = 7; x <= 9; x++)
                        Assert.AreEqual(NavCostField.CostImpassable, cost[z * Width + x],
                            $"ground cell ({x},{z}) under wall must be 255");

                // Rampart cells under the wall = 1.
                for (int z = 7; z <= 9; z++)
                    for (int x = 7; x <= 9; x++)
                        Assert.AreEqual((byte)1, cost[layerArea + z * Width + x],
                            $"rampart cell ({x},{z}) on wall top must be 1");

                // Rampart cells AWAY from the wall stay impassable (empty air).
                Assert.AreEqual(NavCostField.CostImpassable, cost[layerArea + 0 * Width + 0],
                    "rampart cell (0,0) off the wall stays impassable");
                Assert.AreEqual(NavCostField.CostImpassable, cost[layerArea + 15 * Width + 15],
                    "rampart cell (15,15) off the wall stays impassable");

                // Ground cells AWAY from the wall stay walkable (default 0).
                Assert.AreEqual((byte)0, cost[0 * Width + 0],
                    "ground cell (0,0) off the wall stays 0");
            }
            finally
            {
                cost.Dispose();
            }
        }

        [Test]
        public void GateStamp_GroundConditional254_RampartWalkable()
        {
            int layerArea = Width * Height;
            var cost = new NativeArray<byte>(layerArea * LayerCount, Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            try
            {
                for (int i = 0; i < layerArea; i++)
                    cost[layerArea + i] = NavCostField.CostImpassable;

                // Gate stamp at (6, 8) -- 3x3 footprint.
                StampWallAt(cost, cx: 6, cz: 8, w: 3, h: 3,
                    isGate: true, isClimb: false, layerArea: layerArea);

                // Ground cells under the gate = 254 (conditional).
                for (int z = 7; z <= 9; z++)
                    for (int x = 5; x <= 7; x++)
                        Assert.AreEqual(NavCostField.CostConditional, cost[z * Width + x],
                            $"ground cell ({x},{z}) under gate must be 254 (conditional)");

                // Rampart still walkable above the gate (gatehouse roof).
                for (int z = 7; z <= 9; z++)
                    for (int x = 5; x <= 7; x++)
                        Assert.AreEqual((byte)1, cost[layerArea + z * Width + x],
                            $"rampart cell ({x},{z}) on gate roof must be 1");
            }
            finally
            {
                cost.Dispose();
            }
        }

        [Test]
        public void ClimbStamp_GroundWalkable_RampartWalkable()
        {
            int layerArea = Width * Height;
            var cost = new NativeArray<byte>(layerArea * LayerCount, Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            try
            {
                for (int i = 0; i < layerArea; i++)
                    cost[layerArea + i] = NavCostField.CostImpassable;

                // Climb stamp at (10, 8).
                StampWallAt(cost, cx: 10, cz: 8, w: 3, h: 3,
                    isGate: false, isClimb: true, layerArea: layerArea);

                // Ground cells under the stair stay walkable (cost 1) so
                // units can approach without being blocked.
                for (int z = 7; z <= 9; z++)
                    for (int x = 9; x <= 11; x++)
                        Assert.AreEqual((byte)1, cost[z * Width + x],
                            $"ground cell ({x},{z}) under climb access must be 1");

                // Rampart cells walkable too.
                for (int z = 7; z <= 9; z++)
                    for (int x = 9; x <= 11; x++)
                        Assert.AreEqual((byte)1, cost[layerArea + z * Width + x],
                            $"rampart cell ({x},{z}) on stair top must be 1");
            }
            finally
            {
                cost.Dispose();
            }
        }

        // Replicates StampWallLayersJob.StampFootprint behaviour for
        // the test (no ECS scheduling needed).
        private static void StampWallAt(NativeArray<byte> cost,
            int cx, int cz, int w, int h, bool isGate, bool isClimb, int layerArea)
        {
            int halfW = w / 2;
            int halfH = h / 2;
            int x0 = math.max(0, cx - halfW);
            int z0 = math.max(0, cz - halfH);
            int x1 = math.min(Width - 1, cx + halfW);
            int z1 = math.min(Height - 1, cz + halfH);

            byte groundCost = isGate
                ? NavCostField.CostConditional
                : NavCostField.CostImpassable;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int idxG = z * Width + x;
                    int idxR = layerArea + z * Width + x;
                    if (isClimb) cost[idxG] = 1;
                    else cost[idxG] = groundCost;
                    cost[idxR] = 1;
                }
            }
        }
    }
}
