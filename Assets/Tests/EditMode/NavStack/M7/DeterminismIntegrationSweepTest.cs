// DeterminismIntegrationSweepTest.cs
// task-112 M7 -- repeat-run determinism check for IntegrationDijkstraJob.
// Builds a hand-authored 16x16 cost field, runs the integration sweep
// 100 times, and asserts every run produces a byte-identical integration
// array. A regression here means somewhere in the integration loop
// picked up a non-deterministic source (threading, allocator-dependent
// ordering, float math).
//
// The grid is the same shape every iteration so any divergence isolates
// to the job itself.
//
// Location: Assets/Tests/EditMode/NavStack/M7/DeterminismIntegrationSweepTest.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M7
{
    public class DeterminismIntegrationSweepTest
    {
        private const int W = 16;
        private const int H = 16;
        private const int RepeatRuns = 100;

        [Test]
        public void IntegrationSweep_100Runs_ByteIdentical()
        {
            // Build the reference run.
            var reference = new NativeArray<uint>(W * H, Allocator.Temp);
            RunOnce(reference);

            // Now repeat 99 more times; each must equal reference[*].
            for (int run = 1; run < RepeatRuns; run++)
            {
                var actual = new NativeArray<uint>(W * H, Allocator.Temp);
                RunOnce(actual);
                for (int i = 0; i < W * H; i++)
                {
                    if (reference[i] != actual[i])
                    {
                        Assert.Fail(
                            "run " + run + " diverged at cell " + i
                            + " expected=" + reference[i] + " got=" + actual[i]);
                    }
                }
            }
        }

        private static void RunOnce(NativeArray<uint> integration)
        {
            var cost = BuildCostField();
            var fa = new NativeQueue<int>(Allocator.TempJob);
            var fb = new NativeQueue<int>(Allocator.TempJob);
            new IntegrationDijkstraJob
            {
                Cost = cost,
                Integration = integration,
                Width = W,
                Height = H,
                Goal = new int2(W - 1, H - 1),
                FrontierA = fa,
                FrontierB = fb,
            }.Execute();
        }

        // 16x16 grid with a vertical wall at x=8 (rows 0..14 blocked,
        // row 15 open as the gate). Same layout used by the M1 tests
        // but at 16x16 so the integration sweep does meaningful work.
        private static NativeArray<byte> BuildCostField()
        {
            var cost = new NativeArray<byte>(W * H, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            for (int z = 0; z <= 14; z++)
                cost[z * W + 8] = NavCostField.CostImpassable;
            return cost;
        }
    }
}
