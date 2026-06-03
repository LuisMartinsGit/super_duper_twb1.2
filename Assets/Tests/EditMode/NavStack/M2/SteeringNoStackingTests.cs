// SteeringNoStackingTests.cs
// task-112 M2: assert that two units at the *identical* XZ position
// produce a non-zero separation push that drives them apart in opposite
// directions. This is the lower bound of the "no stacking" guarantee
// from AC-P2; without it, two units that spawn on the same cell would
// stay there forever and the steering layer would be useless.
//
// We mirror the AccumulateSteeringForcesJob's stacked-pair branch
// (CompareEntities-based deterministic side pick) and assert:
//   1. Both units receive a non-zero separation force.
//   2. The forces point in OPPOSITE directions (sum to ~zero), proving
//      the deterministic side pick is reciprocal.
//
// Location: Assets/Tests/EditMode/NavStack/M2/SteeringNoStackingTests.cs

using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M2
{
    public class SteeringNoStackingTests
    {
        // Mirror constants from AccumulateSteeringForcesJob.
        private const float SeparationWeight = 2.0f;
        private const float SeparationRadius = 1.5f;

        [Test]
        public void StackedPair_ProducesNonZeroSeparation_OnBothUnits()
        {
            // Two units sharing the exact same XZ position. Entity values
            // are arbitrary but distinct; the CompareEntities tie-break
            // pushes them to opposite directions deterministically.
            var unitA = new Entity { Index = 5, Version = 1 };
            var unitB = new Entity { Index = 9, Version = 1 };

            // Run the stacked-pair branch for A (other = B).
            float3 forceA = StackedSeparation(unitA, unitB);
            // ... and for B (other = A).
            float3 forceB = StackedSeparation(unitB, unitA);

            // 1. Non-zero forces.
            Assert.That(math.lengthsq(forceA), Is.GreaterThan(1e-8f),
                "unit A must receive a non-zero separation push");
            Assert.That(math.lengthsq(forceB), Is.GreaterThan(1e-8f),
                "unit B must receive a non-zero separation push");

            // 2. Opposite directions: forceA + forceB ~= zero.
            float3 sum = forceA + forceB;
            Assert.That(math.lengthsq(sum), Is.LessThan(1e-8f),
                "stacked-pair forces should cancel (units push opposite ways)");

            // 3. Sanity: the X component flips sign between A and B,
            //    proving the deterministic-side pick actually picked
            //    opposite signs.
            Assert.That(math.sign(forceA.x), Is.Not.EqualTo(math.sign(forceB.x)),
                "X-axis push direction must differ between the two units");
        }

        [Test]
        public void StackedPair_PickIsDeterministic_AcrossTwoRuns()
        {
            var unitA = new Entity { Index = 5, Version = 1 };
            var unitB = new Entity { Index = 9, Version = 1 };

            float3 first  = StackedSeparation(unitA, unitB);
            float3 second = StackedSeparation(unitA, unitB);

            Assert.AreEqual(first.x, second.x, "X-axis push must repeat exactly");
            Assert.AreEqual(first.z, second.z, "Z-axis push must repeat exactly");
        }

        // Mirror of the stacked-pair branch in
        // AccumulateSteeringForcesJob.Execute. Keep in sync with the
        // production job's separation math.
        private static float3 StackedSeparation(Entity self, Entity other)
        {
            // self and other share position -> distSq == 0 branch fires.
            int cmp = CompareEntities(self, other);
            float3 toMe = new float3(cmp >= 0 ? 1f : -1f, 0f, 0f);
            float dist = 1f;

            // Separation force: triggers because dist (1) < SeparationRadius (1.5).
            float overlap = SeparationRadius - dist;
            float3 separation = (toMe / dist) * overlap;

            // Single-neighbour case: separation count == 1 -> no division.
            float3 force = float3.zero;
            force += separation * SeparationWeight;
            return force;
        }

        private static int CompareEntities(Entity a, Entity b)
        {
            if (a.Index != b.Index) return a.Index - b.Index;
            return a.Version - b.Version;
        }
    }
}
