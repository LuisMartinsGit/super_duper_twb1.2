// VeilBlockedSampleJob.cs
// Rule G sampling for the Veil CA, as a Burst job: for every veil cell, is
// the nav cell under its centre impassable for a NON-crust reason (baked
// terrain, or a building / wall / gate footprint)?
//
// This was a 36,864-cell managed loop on the main thread inside the 1 s
// maintenance pulse (VeilFieldSystem.SampleBlocked). It is a pure per-cell
// function of two lockstep-identical nav arrays, so it parallelises with no
// ordering concerns: each index writes only its own Blocked slot. Same
// FloatMode as the other veil jobs, so every peer computes the same cells.

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Border.Jobs
{
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    public struct VeilBlockedSampleJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> NavTerrainCost; // may be uncreated
        [ReadOnly] public NativeArray<byte> NavFlags;
        public bool HasTerrainCost;
        public int NavWidth, NavHeight;
        public float NavCell;
        public float3 NavOrigin;
        public byte StructuralMask;
        public byte CostImpassable;

        public int Width;
        public float CellSize;
        public float2 Origin;

        [WriteOnly] public NativeArray<byte> Blocked;

        public void Execute(int index)
        {
            int z = index / Width;
            int x = index - z * Width;
            float wz = Origin.y + (z + 0.5f) * CellSize;
            int nz = (int)math.floor((wz - NavOrigin.z) / NavCell);
            float wx = Origin.x + (x + 0.5f) * CellSize;
            int nx = (int)math.floor((wx - NavOrigin.x) / NavCell);
            byte b = 0;
            if (nx >= 0 && nx < NavWidth && nz >= 0 && nz < NavHeight)
            {
                int nidx = nz * NavWidth + nx;
                bool terrainBlock = HasTerrainCost && NavTerrainCost[nidx] == CostImpassable;
                bool structBlock = (NavFlags[nidx] & StructuralMask) != 0;
                if (terrainBlock || structBlock) b = 1;
            }
            Blocked[index] = b;
        }
    }
}
