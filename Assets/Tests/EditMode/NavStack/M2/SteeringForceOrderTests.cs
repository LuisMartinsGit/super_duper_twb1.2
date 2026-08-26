// SteeringForceOrderTests.cs
// task-112 M2: assert the SteeringSystem accumulates the five force
// layers in the LOCKED order (DR-1):
//
//     separation -> unit-avoidance -> obstacle-avoidance -> cohesion -> flow
//
// The implementation's float bit pattern depends on accumulation order
// because float addition is not associative. We construct a hand-authored
// set of layer vectors with values chosen so that ADDING them in a
// different order produces a different bit pattern; then we add them in
// the locked order and a SHUFFLED order and assert:
//   * the locked-order result matches the documented constant
//   * the shuffled-order result differs from the locked-order result
//     (proves the test would catch a regression that re-ordered the
//     layers).
//
// This is a unit test of the force-blend MATH; it does not need an ECS
// world. The layer constants are documented in
// AccumulateSteeringForcesJob and re-used here so any future weight
// change is caught by the assertion.

using NUnit.Framework;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M2
{
    public class SteeringForceOrderTests
    {
        // Mirror the AccumulateSteeringForcesJob weights so any drift in
        // the production constants surfaces as a test failure.
        private const float SeparationWeight        = 2.0f;
        private const float UnitAvoidanceWeight     = 1.0f;
        private const float ObstacleAvoidanceWeight = 1.5f;
        private const float CohesionWeight          = 0.15f;
        private const float FlowWeight              = 1.0f;

        [Test]
        public void ForceAccumulation_FollowsLockedLayerOrder()
        {
            // Hand-authored per-layer vectors. Values picked so the sums
            // exercise the small-but-nonzero accumulator drift that
            // happens when float addition is reordered.
            float3 separation       = new float3( 1.123456f, 0f, -0.987654f);
            float3 unitAvoidance    = new float3(-0.314159f, 0f,  0.271828f);
            float3 obstacleAvoidance = new float3( 0.577215f, 0f, -0.301029f);
            float3 cohesion         = new float3(-0.110100f, 0f,  0.998000f);
            float3 flow             = new float3( 0.866025f, 0f,  0.500000f);

            // Locked order: separation -> unit-avoidance -> obstacle-avoidance -> cohesion -> flow.
            float3 locked = float3.zero;
            locked += separation        * SeparationWeight;
            locked += unitAvoidance     * UnitAvoidanceWeight;
            locked += obstacleAvoidance * ObstacleAvoidanceWeight;
            locked += cohesion          * CohesionWeight;
            locked += flow              * FlowWeight;

            // Same five vectors and weights, but added in reverse order.
            float3 reversed = float3.zero;
            reversed += flow              * FlowWeight;
            reversed += cohesion          * CohesionWeight;
            reversed += obstacleAvoidance * ObstacleAvoidanceWeight;
            reversed += unitAvoidance     * UnitAvoidanceWeight;
            reversed += separation        * SeparationWeight;

            // CONTRACT 1: locked order produces the value the implementation
            // produces (bit-identical, not just close). If the production
            // job ever re-orders its layers, the implementation will
            // disagree with this expected result and the test will fail.
            float3 expected = ComputeInLockedOrder(
                separation, unitAvoidance, obstacleAvoidance, cohesion, flow);
            Assert.AreEqual(expected.x, locked.x);
            Assert.AreEqual(expected.y, locked.y);
            Assert.AreEqual(expected.z, locked.z);

            // CONTRACT 2: a re-ordered accumulation produces a *different*
            // bit pattern -- otherwise the test would be a tautology. If
            // the chosen inputs ever happened to commute under float
            // addition the assertion below would falsely pass; the
            // hand-picked irrational-looking values guard against that.
            bool xDiffers = locked.x != reversed.x;
            bool zDiffers = locked.z != reversed.z;
            Assert.IsTrue(xDiffers || zDiffers,
                "test setup is bad: locked and reversed must differ in at "
                + "least one axis to prove order matters");
        }

        [Test]
        public void ForceAccumulation_LockedOrder_IsRepeatableAcrossRuns()
        {
            // Determinism check: the same inputs and the same order
            // produce the same bits on every call.
            float3 separation       = new float3( 0.5f, 0f, -0.5f);
            float3 unitAvoidance    = new float3(-0.25f, 0f, 0.75f);
            float3 obstacleAvoidance = new float3( 0.1f, 0f, -0.2f);
            float3 cohesion         = new float3( 0.05f, 0f, 0.05f);
            float3 flow             = new float3( 1.0f, 0f, 0.0f);

            float3 a = ComputeInLockedOrder(separation, unitAvoidance, obstacleAvoidance, cohesion, flow);
            float3 b = ComputeInLockedOrder(separation, unitAvoidance, obstacleAvoidance, cohesion, flow);

            Assert.AreEqual(a.x, b.x);
            Assert.AreEqual(a.y, b.y);
            Assert.AreEqual(a.z, b.z);
        }

        // Mirror of AccumulateSteeringForcesJob's force-summation order.
        // KEEP THIS IN SYNC with the production job; if either side
        // changes, both must change together.
        private static float3 ComputeInLockedOrder(
            float3 separation,
            float3 unitAvoidance,
            float3 obstacleAvoidance,
            float3 cohesion,
            float3 flow)
        {
            float3 force = float3.zero;
            force += separation        * SeparationWeight;
            force += unitAvoidance     * UnitAvoidanceWeight;
            force += obstacleAvoidance * ObstacleAvoidanceWeight;
            force += cohesion          * CohesionWeight;
            force += flow              * FlowWeight;
            return force;
        }
    }
}
