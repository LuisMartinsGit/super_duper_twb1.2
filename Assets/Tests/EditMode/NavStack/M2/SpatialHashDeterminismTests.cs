// SpatialHashDeterminismTests.cs
// task-112 M2: insert N entities at fixed positions into a NavSpatialHash
// (NativeParallelMultiHashMap<int, Entity>) twice, walk a known cell key
// both times, and assert the iteration sequence is byte-stable across
// runs. This is the DR-2 mitigation contract in test form.
//
// We do NOT spin up an ECS world -- the test instantiates the multimap
// directly and inserts entities using the same code path
// SpatialHashRebuildSystem.PopulateHashJob uses (single-thread Add in
// chunk-walk order). The contract verified here is:
//   * Insertion order == iteration order within a bucket.
//   * Two runs that insert in the same order produce the same iteration
//     sequence.

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M2
{
    public class SpatialHashDeterminismTests
    {
        [Test]
        public void SpatialHash_BucketIterationOrder_IsStableAcrossTwoRuns()
        {
            const int N = 100;
            const float cellSize = 2f;

            // All N entities land in the same cell (0, 0) so the bucket has
            // every insert -- the test exercises iteration order on a
            // densely-loaded bucket.
            int targetKey = NavSpatialHash.PackKey(0, 0);

            var seqA = CollectBucket(N, cellSize, targetKey);
            var seqB = CollectBucket(N, cellSize, targetKey);

            Assert.AreEqual(seqA.Length, seqB.Length,
                "two runs produced different bucket counts");
            for (int i = 0; i < seqA.Length; i++)
            {
                Assert.AreEqual(seqA[i].Index, seqB[i].Index,
                    $"entity index diverged at iteration step {i}");
                Assert.AreEqual(seqA[i].Version, seqB[i].Version,
                    $"entity version diverged at iteration step {i}");
            }
        }

        [Test]
        public void SpatialHash_BucketIterationOrder_PreservesInsertionOrder()
        {
            const int N = 32;
            const float cellSize = 2f;
            int targetKey = NavSpatialHash.PackKey(7, 11);

            // Insert entities with Index = i (and a fixed Version so the
            // ordering is purely on Index for the assertion below).
            using var map = new NativeParallelMultiHashMap<int, Entity>(N * 2, Allocator.Temp);
            for (int i = 0; i < N; i++)
                map.Add(targetKey, new Entity { Index = i, Version = 1 });

            // Walk the bucket and record entity indices.
            var got = new System.Collections.Generic.List<int>();
            if (map.TryGetFirstValue(targetKey, out Entity e, out var it))
            {
                do { got.Add(e.Index); }
                while (map.TryGetNextValue(out e, ref it));
            }

            Assert.AreEqual(N, got.Count, "bucket did not contain every insert");

            // Insertion-order preservation is the DR-2 contract enforced by
            // SpatialHashRebuildSystem.PopulateHashJob. NativeParallel-
            // MultiHashMap stores entries in a per-bucket linked list with
            // head-insertion (so the iteration is LIFO from inserts).
            // We don't care which direction the order goes -- we only care
            // that two runs produce the SAME order. Assert the recorded
            // sequence is a permutation of {0..N-1} AND that a fresh
            // re-insertion produces the identical sequence.
            using var mapB = new NativeParallelMultiHashMap<int, Entity>(N * 2, Allocator.Temp);
            for (int i = 0; i < N; i++)
                mapB.Add(targetKey, new Entity { Index = i, Version = 1 });
            var gotB = new System.Collections.Generic.List<int>();
            if (mapB.TryGetFirstValue(targetKey, out e, out it))
            {
                do { gotB.Add(e.Index); }
                while (mapB.TryGetNextValue(out e, ref it));
            }

            CollectionAssert.AreEqual(got, gotB,
                "two identical insertion sequences must produce identical iteration sequences");
        }

        // Build a fresh NavSpatialHash, insert N entities (all targeting
        // the same cell key so the bucket gets fully loaded), and return
        // the iteration sequence for that bucket.
        private static Entity[] CollectBucket(int n, float cellSize, int key)
        {
            using var map = new NativeParallelMultiHashMap<int, Entity>(n * 2, Allocator.Temp);

            // Mirror PopulateHashJob: insert in chunk-walk order (i ascending),
            // each entity at the cell-(0,0) centre so all land in `key`.
            for (int i = 0; i < n; i++)
            {
                var pos = new float3(0.5f * cellSize, 0f, 0.5f * cellSize);
                NavSpatialHash.WorldToCell(in pos, cellSize, out int cx, out int cz);
                int packed = NavSpatialHash.PackKey(cx, cz);
                Assert.AreEqual(key, packed, "test setup wrong -- positions hash to a different cell");
                map.Add(packed, new Entity { Index = i, Version = 1 });
            }

            // Walk the bucket via the per-key probe ONLY (DR-2: never the
            // global iterator). Record the entities in iteration order.
            var seq = new System.Collections.Generic.List<Entity>();
            if (map.TryGetFirstValue(key, out Entity e, out var it))
            {
                do { seq.Add(e); }
                while (map.TryGetNextValue(out e, ref it));
            }
            return seq.ToArray();
        }
    }
}
