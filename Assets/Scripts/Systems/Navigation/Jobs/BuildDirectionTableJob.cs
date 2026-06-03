// BuildDirectionTableJob.cs
// Builds the 256-entry unit-vector lookup table consumed by the flow
// follower (see CCD-3). Runs once at world init.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/BuildDirectionTableJob.cs

using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Fills a <see cref="BlobBuilderArray{T}"/> with
    /// <c>dir[i] = (cos(i * 2π / 256), sin(i * 2π / 256))</c>. Deterministic
    /// across machines at a pinned Burst version (DR-15).
    ///
    /// Note: the actual blob construction must run in a non-Burst path
    /// (BlobBuilder is a managed disposable). This job is kept as a thin
    /// Burst-friendly helper so the math is identical regardless of where
    /// the blob is ultimately assembled.
    /// </summary>
    [BurstCompile]
    internal struct BuildDirectionTableJob : IJob
    {
        public Unity.Collections.NativeArray<float2> Out;

        public void Execute()
        {
            int n = Out.Length;
            if (n <= 0) return;
            float twoPi = 2f * math.PI;
            float inv = twoPi / n;
            for (int i = 0; i < n; i++)
            {
                float a = i * inv;
                Out[i] = new float2(math.cos(a), math.sin(a));
            }
        }
    }
}
