// StampOverpassJob.cs
// Stamps overpass bridges into the nav cost field (see
// OverpassBridgeComponents.cs and docs/Design/Navigation_And_Formations.md §5):
//
//   * Deck strip  → layer-1 (rampart) walkable, cost 1. The ground cells
//     UNDERNEATH are deliberately untouched — through-traffic keeps its
//     terrain cost, which is the whole point of an overpass.
//   * Ramp discs at both deck ends → layer-1 walkable + FlagClimbAccess on
//     BOTH layers (mount/dismount cells, same flag walls use for stairs).
//
// Single-thread Burst IJob: overpass counts are tiny (scene furniture) and
// the write set of overlapping bridges is idempotent, matching the wall
// stamp jobs' tolerance.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/StampOverpassJob.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct StampOverpassJob : IJob
    {
        public NativeArray<byte> Cost;
        public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public int LayerArea;
        public float CellSize;
        public float3 Origin;
        [ReadOnly] public NativeArray<OverpassBridge> Bridges;

        public void Execute()
        {
            float rampSq = OverpassBridge.RampRadius * OverpassBridge.RampRadius;

            for (int b = 0; b < Bridges.Length; b++)
            {
                var br = Bridges[b];
                float2 s = new float2(br.Start.x, br.Start.z);
                float2 e = new float2(br.End.x, br.End.z);
                float2 span = e - s;
                float len = math.length(span);
                if (len < 1e-3f) continue;
                float2 axis = span / len;
                float2 perp = new float2(-axis.y, axis.x);
                float halfW = br.Width * 0.5f;

                // Cell bounds of the span + ramp discs.
                float pad = halfW + OverpassBridge.RampRadius + CellSize;
                float2 mn = math.min(s, e) - pad;
                float2 mx = math.max(s, e) + pad;
                int x0 = math.clamp((int)math.floor((mn.x - Origin.x) / CellSize), 0, Width - 1);
                int x1 = math.clamp((int)math.floor((mx.x - Origin.x) / CellSize), 0, Width - 1);
                int z0 = math.clamp((int)math.floor((mn.y - Origin.z) / CellSize), 0, Height - 1);
                int z1 = math.clamp((int)math.floor((mx.y - Origin.z) / CellSize), 0, Height - 1);

                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float2 c = new float2(
                            Origin.x + (x + 0.5f) * CellSize,
                            Origin.z + (z + 0.5f) * CellSize);
                        float2 dS = c - s;
                        float along = math.dot(dS, axis);
                        float side = math.dot(dS, perp);

                        int gIdx = z * Width + x;
                        int rIdx = LayerArea + gIdx;

                        // Deck strip: walkable on the rampart layer. Ground
                        // beneath is untouched — units walk under freely.
                        if (along >= 0f && along <= len && math.abs(side) <= halfW)
                            Cost[rIdx] = 1;

                        // Ramp discs at both ends: layer transition cells.
                        float2 dE = c - e;
                        if (math.lengthsq(dS) <= rampSq || math.lengthsq(dE) <= rampSq)
                        {
                            Cost[rIdx] = 1;
                            Flags[rIdx] |= NavCostField.FlagClimbAccess;
                            Flags[gIdx] |= NavCostField.FlagClimbAccess;
                        }
                    }
                }
            }
        }
    }
}
