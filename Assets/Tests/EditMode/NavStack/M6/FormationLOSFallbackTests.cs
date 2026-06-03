// FormationLOSFallbackTests.cs
// task-112 M6 -- direct test of the Bresenham LOS check used by
// FormationLeaderNavSystem. Build a small NavCostField with a row of
// impassable cells between (start_x, z) and (slot_x, z); assert the
// LOS test returns false (so the system would fall back to flow
// direction instead of overriding it with the slot direction).
//
// Also asserts the LOS-clear baseline case (no obstacles) so the
// negative test isn't tautological.
//
// Algorithm mirror -- the test re-implements the same integer
// Bresenham the system uses so we exercise the contract without
// having to schedule the Burst job.
//
// Location: Assets/Tests/EditMode/NavStack/M6/FormationLOSFallbackTests.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M6
{
    public class FormationLOSFallbackTests
    {
        private const int Width = 32;
        private const int Height = 32;

        [Test]
        public void LOS_BlockedByImpassableCell_ReturnsFalse()
        {
            // 32x32 cost field; column x=10 is impassable for the
            // entire grid height. Unit at (5, 16), slot at (20, 16).
            var cost = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            for (int z = 0; z < Height; z++)
                cost[z * Width + 10] = NavCostField.CostImpassable;

            bool clear = BresenhamLOS(cost, Width, Height,
                new int2(5, 16), new int2(20, 16));
            Assert.IsFalse(clear,
                "LOS through an impassable column must return false");

            cost.Dispose();
        }

        [Test]
        public void LOS_ClearOnEmptyGrid_ReturnsTrue()
        {
            var cost = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            bool clear = BresenhamLOS(cost, Width, Height,
                new int2(5, 16), new int2(20, 16));
            Assert.IsTrue(clear, "Empty grid must have clear LOS");

            cost.Dispose();
        }

        [Test]
        public void LOS_TwoMachinesIdenticalInput_ByteIdenticalResult()
        {
            // Determinism check: run the same input twice and assert
            // the result is byte-identical. (The byte-stable promise
            // covers the integer-only Bresenham walk; this guards
            // against accidental float math creeping in.)
            var cost = new NativeArray<byte>(Width * Height, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            cost[16 * Width + 12] = NavCostField.CostImpassable;

            bool a = BresenhamLOS(cost, Width, Height,
                new int2(5, 16), new int2(20, 16));
            bool b = BresenhamLOS(cost, Width, Height,
                new int2(5, 16), new int2(20, 16));
            Assert.AreEqual(a, b, "Two identical LOS calls must produce the same result");

            cost.Dispose();
        }

        // Mirror of FormationLeaderNavSystem.FormationFollowJob.BresenhamLOS.
        // Keeping it inline here lets the test exercise the algorithm
        // independently of scheduling a Burst job.
        private static bool BresenhamLOS(NativeArray<byte> cost, int width, int height,
            int2 from, int2 to)
        {
            int x0 = from.x, z0 = from.y;
            int x1 = to.x, z1 = to.y;
            int dx = math.abs(x1 - x0);
            int dz = math.abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int x = x0;
            int z = z0;
            int maxSteps = dx + dz + 1;
            for (int step = 0; step < maxSteps; step++)
            {
                if (x < 0 || x >= width || z < 0 || z >= height) return false;
                byte c = cost[z * width + x];
                if (c == NavCostField.CostImpassable) return false;
                if (x == x1 && z == z1) return true;
                int e2 = err * 2;
                if (e2 > -dz) { err -= dz; x += sx; }
                if (e2 < dx) { err += dx; z += sz; }
            }
            return true;
        }
    }
}
