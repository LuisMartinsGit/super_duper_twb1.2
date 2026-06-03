// SpatialHashBucketTests.cs
// task-112 M2: assert entities at known world positions hash to the
// expected cell buckets, and assert NavSpatialHash.PackKey wraps
// deterministically at high coordinate magnitudes (DR-2 ancillary
// contract -- the steering job's neighbour ring relies on adjacent
// cells producing adjacent keys, but the *absolute* key value doesn't
// have to fit any particular convention as long as it's a pure function
// of (cellX, cellZ)).
//
// Location: Assets/Tests/EditMode/NavStack/M2/SpatialHashBucketTests.cs

using NUnit.Framework;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M2
{
    public class SpatialHashBucketTests
    {
        [Test]
        public void WorldToCell_FloorsXZ_ByCellSize()
        {
            const float cellSize = 2f;

            // (0, 0, 0) -> (0, 0)
            var p0 = new float3(0f, 0f, 0f);
            NavSpatialHash.WorldToCell(in p0, cellSize, out int x0, out int z0);
            Assert.AreEqual(0, x0);
            Assert.AreEqual(0, z0);

            // (1.99, 0, 1.99) -> (0, 0)  -- still within cell-(0,0) bounds.
            var p1 = new float3(1.99f, 0f, 1.99f);
            NavSpatialHash.WorldToCell(in p1, cellSize, out int x1, out int z1);
            Assert.AreEqual(0, x1);
            Assert.AreEqual(0, z1);

            // (2.0, 0, 2.0) -> (1, 1)  -- snaps to the next cell.
            var p2 = new float3(2f, 0f, 2f);
            NavSpatialHash.WorldToCell(in p2, cellSize, out int x2, out int z2);
            Assert.AreEqual(1, x2);
            Assert.AreEqual(1, z2);

            // (-0.1, 0, -0.1) -> (-1, -1) -- floor of negative is one less.
            var p3 = new float3(-0.1f, 0f, -0.1f);
            NavSpatialHash.WorldToCell(in p3, cellSize, out int x3, out int z3);
            Assert.AreEqual(-1, x3);
            Assert.AreEqual(-1, z3);

            // (-2.0, 0, -2.0) -> (-1, -1) -- on the boundary, floor stays at -1.
            var p4 = new float3(-2f, 0f, -2f);
            NavSpatialHash.WorldToCell(in p4, cellSize, out int x4, out int z4);
            Assert.AreEqual(-1, x4);
            Assert.AreEqual(-1, z4);

            // (-2.01, 0, -2.01) -> (-2, -2).
            var p5 = new float3(-2.01f, 0f, -2.01f);
            NavSpatialHash.WorldToCell(in p5, cellSize, out int x5, out int z5);
            Assert.AreEqual(-2, x5);
            Assert.AreEqual(-2, z5);
        }

        [Test]
        public void PackKey_IsPureFunction_OfCellCoordinates()
        {
            // Same inputs -> same output across repeated calls.
            int a1 = NavSpatialHash.PackKey(0, 0);
            int a2 = NavSpatialHash.PackKey(0, 0);
            Assert.AreEqual(a1, a2);

            int b1 = NavSpatialHash.PackKey(-5, 7);
            int b2 = NavSpatialHash.PackKey(-5, 7);
            Assert.AreEqual(b1, b2);

            // Different cells produce different keys for the values used
            // in the M2 unit tests. (We don't prove zero collisions across
            // the whole int domain -- only that the immediate 3x3 ring
            // around the origin is collision-free, which is what the
            // SteeringSystem neighbour walk relies on.)
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int k = NavSpatialHash.PackKey(dx, dz);
                Assert.IsTrue(seen.Add(k),
                    $"collision in 3x3 ring at offset ({dx}, {dz}) -- key {k}");
            }
        }

        [Test]
        public void PackKey_WrapsDeterministically_AtIntBoundary()
        {
            // High-magnitude cells: the multiplier overflows int, but C# /
            // unchecked semantics guarantee the bit pattern is well-defined
            // and identical across machines / .NET runtimes (DR-15).
            // We assert the pure-function contract still holds.
            int k1 = NavSpatialHash.PackKey(int.MaxValue, int.MinValue);
            int k2 = NavSpatialHash.PackKey(int.MaxValue, int.MinValue);
            Assert.AreEqual(k1, k2, "PackKey must be deterministic at int boundary");

            // Two distinct very-large cells with different X should still
            // produce distinct keys (collision probability is low even at
            // boundaries because both multipliers are odd primes).
            int k3 = NavSpatialHash.PackKey(int.MaxValue - 1, int.MinValue);
            Assert.AreNotEqual(k1, k3,
                "adjacent extreme cells should not collide on PackKey");
        }

        [Test]
        public void WorldToCell_Then_PackKey_Roundtrip_Matches_DirectPackKey()
        {
            const float cellSize = 2f;

            // Picking a few sample positions across the grid and asserting
            // the full pipeline (WorldToCell -> PackKey) matches what a
            // direct PackKey(cx, cz) produces.
            var samples = new (float3 pos, int cx, int cz)[]
            {
                (new float3( 0f,    0f,  0f),    0,  0),
                (new float3( 4f,    0f,  4f),    2,  2),
                (new float3( 5.99f, 0f,  7.99f), 2,  3),
                (new float3(-3f,    0f, -3f),   -2, -2),
                (new float3(63.5f,  0f, -1f),   31, -1),
            };

            foreach (var s in samples)
            {
                NavSpatialHash.WorldToCell(in s.pos, cellSize, out int cx, out int cz);
                Assert.AreEqual(s.cx, cx, $"cellX wrong for {s.pos}");
                Assert.AreEqual(s.cz, cz, $"cellZ wrong for {s.pos}");

                int pipelineKey = NavSpatialHash.PackKey(cx, cz);
                int directKey   = NavSpatialHash.PackKey(s.cx, s.cz);
                Assert.AreEqual(directKey, pipelineKey,
                    $"pipeline key mismatch for {s.pos}");
            }
        }
    }
}
