// StampTerrainCostJob.cs
// One-time terrain stamp for the cost field. M1 keeps the terrain layer
// flat (cost = 0 everywhere) since the Phase1Test scenario runs on a flat
// 64x64 grid. Later phases will fill in slope/water blends here.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/StampTerrainCostJob.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Initialises every cell in the layer-0 cost slab to <c>0</c>
    /// (nominal walkable) and clears the flag byte. Parallel over rows
    /// to keep the inner loop cache-friendly.
    /// </summary>
    [BurstCompile]
    internal struct StampTerrainCostJob : IJobParallelFor
    {
        [WriteOnly] public NativeArray<byte> Cost;
        [WriteOnly] public NativeArray<byte> Flags;
        public int Width;

        // index = row index (0..Height-1). Each Execute fills one row of
        // the layer-0 slab.
        public void Execute(int row)
        {
            int rowStart = row * Width;
            for (int x = 0; x < Width; x++)
            {
                Cost[rowStart + x] = 0;
                Flags[rowStart + x] = 0;
            }
        }
    }
}
