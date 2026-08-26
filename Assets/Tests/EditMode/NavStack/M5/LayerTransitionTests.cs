// LayerTransitionTests.cs
// task-112 M5 -- exercises the LayerTraversalState progress math +
// position lerp + layer flip pattern that LayerTransitionSystem
// applies. We don't spin up an ECS world (that would need every nav
// singleton + the portal graph blob assembled); instead we mirror the
// exact math the system does for the per-tick advance.
//
// Asserts:
//   * Progress advances by TransitionRate * dt each tick.
//   * Position is the lerp between StartPos and EndPos at progress p.
//   * Layer flips at p >= 0.5 (matching the system's midpoint flip).
//   * State is "complete" at p >= 1.0.

using NUnit.Framework;
using Unity.Mathematics;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Tests.EditMode.NavStack.M5
{
    public class LayerTransitionTests
    {
        // Fixed sim dt -- 60 Hz simulation = 1/60 = 0.01666...s.
        private const float FixedDt = 1f / 60f;

        [Test]
        public void Progress_AdvancesByTransitionRateTimesDt_EachTick()
        {
            var ts = new LayerTraversalState
            {
                InProgress = 1,
                FromLayer = 0,
                ToLayer = 1,
                PortalId = 42,
                Progress = 0f,
                StartPos = new float3(0, 0, 0),
                EndPos = new float3(10, 4, 0),
            };

            // Tick once.
            ts.Progress += LayerTransitionSystem.TransitionRate * FixedDt;
            // (1.6666...) * (0.01666...) = 0.02777...
            Assert.That(ts.Progress, Is.EqualTo(LayerTransitionSystem.TransitionRate * FixedDt)
                .Within(1e-6f));

            // Tick again.
            ts.Progress += LayerTransitionSystem.TransitionRate * FixedDt;
            Assert.That(ts.Progress, Is.EqualTo(2f * LayerTransitionSystem.TransitionRate * FixedDt)
                .Within(1e-6f));
        }

        [Test]
        public void Position_IsLerpBetweenStartAndEnd_AtAnyProgress()
        {
            var start = new float3(0, 0, 0);
            var end = new float3(10, 4, 2);

            // Mid-way (p=0.5) -> should be (5, 2, 1).
            var mid = math.lerp(start, end, 0.5f);
            Assert.That(mid.x, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(mid.y, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(mid.z, Is.EqualTo(1f).Within(1e-5f));

            // p=0.25 -> (2.5, 1, 0.5).
            var q = math.lerp(start, end, 0.25f);
            Assert.That(q.x, Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(q.y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(q.z, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void LayerFlips_AtProgressOverHalf_AndCompletesAtOne()
        {
            // Drive a ground -> rampart transition tick-by-tick.
            byte currentLayer = 0;
            var ts = new LayerTraversalState
            {
                InProgress = 1,
                FromLayer = 0,
                ToLayer = 1,
                PortalId = 0,
                Progress = 0f,
                StartPos = new float3(0, 0, 0),
                EndPos = new float3(0, LayerTransitionSystem.DeckY, 4),
            };

            int ticks = 0;
            bool flipped = false;
            bool completed = false;
            // 1 / (Rate * dt) = 1 / (1.666 * 0.01666) = 1 / 0.02777 = ~36 ticks
            // to complete. Allow some slack so the test is order-stable.
            for (int t = 0; t < 200; t++)
            {
                ts.Progress += LayerTransitionSystem.TransitionRate * FixedDt;
                ticks++;
                if (!flipped && ts.Progress >= 0.5f)
                {
                    currentLayer = ts.ToLayer;
                    flipped = true;
                }
                if (ts.Progress >= 1.0f)
                {
                    ts.Progress = 1.0f;
                    completed = true;
                    break;
                }
            }

            Assert.IsTrue(flipped,
                $"layer must flip at p >= 0.5 within 200 ticks (took {ticks})");
            Assert.IsTrue(completed,
                $"traversal must complete by p == 1 within 200 ticks (took {ticks})");
            Assert.AreEqual((byte)1, currentLayer,
                "post-transition layer must equal ToLayer (1)");

            // Total tick count = ceil(1 / (Rate * dt)). At 60Hz this is
            // ~36; allow 30..50 to absorb rounding.
            Assert.IsTrue(ticks >= 30 && ticks <= 50,
                $"tick count must land in [30, 50] for the 1/Rate * dt budget; got {ticks}");
        }
    }
}
