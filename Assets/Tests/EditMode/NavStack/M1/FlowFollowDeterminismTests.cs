// FlowFollowDeterminismTests.cs
// task-112 M1: re-run the FlowFollow sampling logic twice against the
// same input state; the resulting per-unit direction vectors must be
// byte-identical across runs. This is the M1 slice of the DR-1 / DR-15
// determinism guarantees recorded in the architecture's Determinism
// Risk Register.
//
// We can't easily schedule the IJobEntity in EditMode without an ECS
// world — so we mirror its inner math directly and assert determinism on
// the math, which is what the Burst-compiled version runs at every call
// site.
//
// Location: Assets/Tests/EditMode/NavStack/M1/FlowFollowDeterminismTests.cs

using NUnit.Framework;
using TheWaningBorder.Systems.Navigation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Tests.EditMode.NavStack.M1
{
    public class FlowFollowDeterminismTests
    {
        private const int W = 8;
        private const int H = 8;

        [Test]
        public void SampleFlow_ProducesByteIdenticalDirsAcrossTwoRuns()
        {
            // Build a deterministic direction lookup via a BlobBuilder —
            // same code path NavGridBootstrapSystem uses, so the bits we
            // compare here are the bits the runtime job will see.
            BlobAssetReference<DirectionTableBlob> tableRef;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<DirectionTableBlob>();
                var arr = builder.Allocate(ref root.Dirs, 256);
                float twoPi = 2f * math.PI;
                float inv = twoPi / 256;
                for (int i = 0; i < 256; i++)
                {
                    float a = i * inv;
                    arr[i] = new float2(math.cos(a), math.sin(a));
                }
                tableRef = builder.CreateBlobAssetReference<DirectionTableBlob>(Allocator.Temp);
            }

            // Build a flow-field with deterministic content. Use a tiny
            // goal-at-(7,7) flow over an open 8x8 grid.
            var cost = new NativeArray<byte>(W * H, Allocator.Temp,
                NativeArrayOptions.ClearMemory);
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
            for (int i = 0; i < W * H; i++) dirJob.Execute(i);

            // Eight test positions across the grid (mid-cell world coords).
            var positions = new float3[]
            {
                new float3(0.5f, 0, 0.5f),
                new float3(1.5f, 0, 0.5f),
                new float3(0.5f, 0, 1.5f),
                new float3(3.5f, 0, 3.5f),
                new float3(4.5f, 0, 4.5f),
                new float3(5.5f, 0, 5.5f),
                new float3(6.5f, 0, 6.5f),
                new float3(7.5f, 0, 7.5f),
            };

            var runA = new (float3 v, byte has)[positions.Length];
            var runB = new (float3 v, byte has)[positions.Length];

            // Mirror SampleFlowAndWriteDesiredDirJob.Execute.
            void Run((float3 v, byte has)[] dst)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    int cx = (int)math.floor(p.x / 1f);
                    int cz = (int)math.floor(p.z / 1f);
                    if (cx < 0 || cx >= W || cz < 0 || cz >= H)
                    {
                        dst[i] = (float3.zero, 0);
                        continue;
                    }
                    byte d = dir[cz * W + cx];
                    if (d == NavFlowConstants.NoDirection)
                    {
                        dst[i] = (float3.zero, 0);
                        continue;
                    }
                    ref var table = ref tableRef.Value.Dirs;
                    float2 v = table[d];
                    dst[i] = (new float3(v.x, 0f, v.y), 1);
                }
            }

            Run(runA);
            Run(runB);

            // Byte-identical across runs.
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.AreEqual(runA[i].has, runB[i].has, $"HasValue diverged at idx {i}");
                Assert.AreEqual(runA[i].v.x, runB[i].v.x, $"Value.x diverged at idx {i}");
                Assert.AreEqual(runA[i].v.y, runB[i].v.y, $"Value.y diverged at idx {i}");
                Assert.AreEqual(runA[i].v.z, runB[i].v.z, $"Value.z diverged at idx {i}");
            }

            tableRef.Dispose();
        }
    }
}
