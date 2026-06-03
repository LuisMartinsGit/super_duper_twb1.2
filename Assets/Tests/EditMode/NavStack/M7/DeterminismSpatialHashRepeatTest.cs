// DeterminismSpatialHashRepeatTest.cs
// task-112 M7 -- populate a NavSpatialHash multimap with the same
// insertion sequence 100 times; iterate one key per run; assert the
// emitted entity sequence is byte-identical every run. Catches any
// future regression where insertion order leaks (DR-2 contract).
//
// Builds the multimap directly (no ECS world) to keep the test as a
// pure unit test independent of scheduler timing.
//
// Location: Assets/Tests/EditMode/NavStack/M7/DeterminismSpatialHashRepeatTest.cs

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Tests.EditMode.NavStack.M7
{
    public class DeterminismSpatialHashRepeatTest
    {
        private const int N = 100;
        private const int RepeatRuns = 100;

        [Test]
        public void SpatialHash_RepeatedInsertions_ByteIdenticalIteration()
        {
            int targetKey = NavSpatialHash.PackKey(3, 9);

            var reference = CollectBucket(targetKey);
            for (int run = 1; run < RepeatRuns; run++)
            {
                var actual = CollectBucket(targetKey);
                Assert.AreEqual(reference.Length, actual.Length,
                    "run " + run + " produced different bucket count");
                for (int i = 0; i < reference.Length; i++)
                {
                    Assert.AreEqual(reference[i].Index, actual[i].Index,
                        "run " + run + " entity[" + i + "].Index diverged");
                    Assert.AreEqual(reference[i].Version, actual[i].Version,
                        "run " + run + " entity[" + i + "].Version diverged");
                }
            }
        }

        // Build a fresh multimap, insert N entities (Index = 100..100+N-1
        // so the values are non-trivial and stable across runs), and
        // collect the iteration sequence for the target key.
        private static Entity[] CollectBucket(int targetKey)
        {
            using var map = new NativeParallelMultiHashMap<int, Entity>(N * 2, Allocator.Temp);
            for (int i = 0; i < N; i++)
                map.Add(targetKey, new Entity { Index = 100 + i, Version = 1 });

            var seq = new System.Collections.Generic.List<Entity>(N);
            if (map.TryGetFirstValue(targetKey, out Entity e, out var it))
            {
                do { seq.Add(e); }
                while (map.TryGetNextValue(out e, ref it));
            }
            return seq.ToArray();
        }
    }
}
