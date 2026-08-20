// HexGrid.cs
// Hex-ring slot generation, shared by the resource-patch bootstraps.
//
// Single-sourced 2026-08-12: identical copies of HexDirs + GenerateHexSlots
// (and the SQRT3_OVER_2 literal) lived in IronDepositBootstrap,
// VeilstoneOutcroppingBootstrap and ScenarioSetup. They never drifted, but
// three copies of a coordinate transform is three places to get it wrong.
//
// Location: Assets/Scripts/Core/Types/HexGrid.cs

using Unity.Collections;
using Unity.Mathematics;

namespace TheWaningBorder.Core
{
    public static class HexGrid
    {
        /// <summary>sin(60°) — the vertical compression of an axial hex row.</summary>
        public const float Sqrt3Over2 = 0.8660254f;

        // Axial-coordinate neighbour directions. Walked in this order they
        // trace each ring exactly once around the centre.
        private static readonly int[,] Dirs =
        {
            {  1,  0 }, {  1, -1 }, {  0, -1 },
            { -1,  0 }, { -1,  1 }, {  0,  1 }
        };

        /// <summary>
        /// Fills <paramref name="output"/> with the cartesian positions of every
        /// cell in a hex grid of <paramref name="maxRings"/> rings, starting at
        /// the centre cell (ring 0). Output is centred on the origin — the
        /// caller offsets to the patch position.
        /// </summary>
        public static void GenerateSlots(int maxRings, float spacing, NativeList<float2> output)
        {
            output.Add(float2.zero);

            for (int ring = 1; ring <= maxRings; ring++)
            {
                int q = -ring;
                int r = ring;
                for (int side = 0; side < 6; side++)
                {
                    for (int step = 0; step < ring; step++)
                    {
                        float x = spacing * (q + r * 0.5f);
                        float z = spacing * r * Sqrt3Over2;
                        output.Add(new float2(x, z));
                        q += Dirs[side, 0];
                        r += Dirs[side, 1];
                    }
                }
            }
        }
    }
}
