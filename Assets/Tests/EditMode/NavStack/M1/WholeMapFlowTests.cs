// WholeMapFlowTests.cs
// task-112 M1: hand-authored 8x8 grid, single obstacle, run the
// IntegrationDijkstraJob + FlowDirectionJob directly and assert known
// integration values + direction codes at named cells.
//
// Layout (W=8, H=8). Goal at (7,7). Single impassable wall column at x=4
// spanning rows 0..6 (row 7 left open as the gate):
//
//     z=0  . . . . # . . .
//     z=1  . . . . # . . .
//     z=2  . . . . # . . .
//     z=3  . . . . # . . .
//     z=4  . . . . # . . .
//     z=5  . . . . # . . .
//     z=6  . . . . # . . .
//     z=7  . . . . . . . G
//
// Asserts:
//   * Integration at (7,7) == 0 (goal).
//   * Integration at (4,7) == 30 (3 cardinal hops west of G).
//   * Integration at (0,0) is reachable (< UnreachableIntegration).
//   * Integration at (4,3) == UnreachableIntegration (it's the wall).
//   * Direction at the goal == NoDirection.
//   * Direction at (5,7) points toward +x (byte 0).
//
// Location: Assets/Tests/EditMode/NavStack/M1/WholeMapFlowTests.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M1
{
    public class WholeMapFlowTests
    {
        private const int W = 8;
        private const int H = 8;

        [Test]
        public void IntegrationDijkstra_GoalIsZero_AndUnreachableIsSentinel()
        {
            var cost = NewCostField();
            var integration = new NativeArray<uint>(W * H, Allocator.Temp);
            var fa = new NativeQueue<int>(Allocator.TempJob);
            var fb = new NativeQueue<int>(Allocator.TempJob);

            var job = new IntegrationDijkstraJob
            {
                Cost = cost,
                Integration = integration,
                Width = W,
                Height = H,
                Goal = new int2(7, 7),
                FrontierA = fa,
                FrontierB = fb,
            };
            // Run in-line: IJob.Execute is plain on the struct.
            job.Execute();

            // Goal cell is zero.
            Assert.AreEqual((uint)0, integration[7 * W + 7]);

            // (4,7) is 3 cardinal hops west of (7,7): 7→6→5→4. Cost 3*10 = 30.
            Assert.AreEqual(NavFlowConstants.StepCardinal * 3, integration[7 * W + 4]);

            // (4,3) is the wall itself — must remain at the unreachable sentinel.
            Assert.AreEqual(NavFlowConstants.UnreachableIntegration, integration[3 * W + 4]);

            // (0,0) is reachable (we go around the wall via row 7). Just
            // assert it's strictly less than the sentinel.
            Assert.That(integration[0 * W + 0], Is.LessThan(NavFlowConstants.UnreachableIntegration));
        }

        [Test]
        public void FlowDirection_GoalCellHasNoDirection_AndAdjacentPointsToGoal()
        {
            var cost = NewCostField();
            var integration = new NativeArray<uint>(W * H, Allocator.Temp);
            var dir = new NativeArray<byte>(W * H, Allocator.Temp);
            var fa = new NativeQueue<int>(Allocator.TempJob);
            var fb = new NativeQueue<int>(Allocator.TempJob);

            new IntegrationDijkstraJob
            {
                Cost = cost,
                Integration = integration,
                Width = W,
                Height = H,
                Goal = new int2(7, 7),
                FrontierA = fa,
                FrontierB = fb,
            }.Execute();

            var dirJob = new FlowDirectionJob
            {
                Integration = integration,
                Cost = cost,
                Dir = dir,
                Width = W,
                Height = H,
                Goal = new int2(7, 7),
            };
            for (int i = 0; i < W * H; i++)
                dirJob.Execute(i);

            // Goal cell: no direction.
            Assert.AreEqual(NavFlowConstants.NoDirection, dir[7 * W + 7]);

            // Cell (6,7) sits one cardinal step west of the goal. Its
            // smallest-integration neighbour is (7,7), direction code 0
            // (+x).
            Assert.AreEqual((byte)0, dir[7 * W + 6]);

            // Cell (4,3) is the wall. It must carry NoDirection (the
            // direction job short-circuits impassable cells).
            Assert.AreEqual(NavFlowConstants.NoDirection, dir[3 * W + 4]);
        }

        [Test]
        public void FlowDirection_DirectsAroundObstacle()
        {
            var cost = NewCostField();
            var integration = new NativeArray<uint>(W * H, Allocator.Temp);
            var dir = new NativeArray<byte>(W * H, Allocator.Temp);
            var fa = new NativeQueue<int>(Allocator.TempJob);
            var fb = new NativeQueue<int>(Allocator.TempJob);

            new IntegrationDijkstraJob
            {
                Cost = cost,
                Integration = integration,
                Width = W,
                Height = H,
                Goal = new int2(7, 7),
                FrontierA = fa,
                FrontierB = fb,
            }.Execute();

            var dirJob = new FlowDirectionJob
            {
                Integration = integration,
                Cost = cost,
                Dir = dir,
                Width = W,
                Height = H,
                Goal = new int2(7, 7),
            };
            for (int i = 0; i < W * H; i++)
                dirJob.Execute(i);

            // Cell (3,0) — left of the wall, top row. The wall blocks the
            // straight +x route to the goal. Direction must NOT point
            // straight +x (code 0) into a higher-integration cell. We
            // assert the direction's pick has strictly smaller integration
            // than the cell itself, which by construction is the property
            // FlowDirectionJob enforces. This guards against regressions
            // that would cause units to bump the wall.
            uint here = integration[0 * W + 3];
            byte d = dir[0 * W + 3];

            Assert.That(d, Is.Not.EqualTo(NavFlowConstants.NoDirection),
                "cell (3,0) should be reachable and carry a direction");

            // Sanity-check by looking up the neighbour the direction code
            // points to and confirming it has strictly lower integration.
            (int dx, int dz) = DecodeDir(d);
            int nx = 3 + dx;
            int nz = 0 + dz;
            Assert.That(nx, Is.InRange(0, W - 1));
            Assert.That(nz, Is.InRange(0, H - 1));
            Assert.That(integration[nz * W + nx], Is.LessThan(here),
                $"flow at (3,0) points to ({nx},{nz}) which must improve integration");
        }

        // Helper: build the 8x8 cost field with the vertical wall described
        // in the file header.
        private static NativeArray<byte> NewCostField()
        {
            var cost = new NativeArray<byte>(W * H, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            // Wall at x=4 for z=0..6.
            for (int z = 0; z <= 6; z++)
                cost[z * W + 4] = NavCostField.CostImpassable;
            return cost;
        }

        // Inverse of the lookup in FlowDirectionJob.
        private static (int dx, int dz) DecodeDir(byte d) => d switch
        {
            0   => (+1,  0),
            32  => (+1, +1),
            64  => ( 0, +1),
            96  => (-1, +1),
            128 => (-1,  0),
            160 => (-1, -1),
            192 => ( 0, -1),
            224 => (+1, -1),
            _   => ( 0,  0),
        };
    }
}
