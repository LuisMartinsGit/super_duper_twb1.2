// File: Assets/Scripts/Systems/Border/Jobs/VeilEnclosureJob.cs
// "Any area completely enclosed by crystal fills in instantly." As the tendrils
// wrap around a pocket of open ground, that pocket should snap to full crystal
// rather than trickle in.
//
// Method: a flood fill of OPEN cells (coverage below the crust threshold),
// seeded from every open cell on the map border. Whatever the flood cannot
// reach is a pocket sealed off by crust — set those to full coverage. Cells on
// a fresh break's cooldown are skipped, so punching a hole in the interior still
// holds open for its cooldown instead of snapping shut the same second.
//
// Single-threaded IJob (flood fill isn't parallel), but the result is
// order-independent — reachability is deterministic regardless of visit order.
//
// Location: Assets/Scripts/Systems/Border/Jobs/

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Border.Jobs
{
    [BurstCompile]
    public struct VeilEnclosureJob : IJob
    {
        public NativeArray<byte> Saturation;        // read + write (fills pockets)
        [ReadOnly] public NativeArray<byte> Cooldown;
        [ReadOnly] public NativeArray<byte> Influence; // §2.6 — never fill warded ground
        [ReadOnly] public NativeArray<byte> WorkerWard; // never fill the pocket a worker stands in (no sealing diggers)
        public NativeArray<byte> Visited;           // scratch, W*H
        public NativeList<int> Stack;               // scratch flood stack
        public int Width;
        public int Height;
        public byte OpenBelow;                      // "open" = coverage < this (crust threshold)

        public void Execute()
        {
            int n = Width * Height;
            for (int i = 0; i < n; i++) Visited[i] = 0;
            Stack.Clear();

            // Seed from open cells on all four borders.
            for (int x = 0; x < Width; x++)
            {
                Seed(x, 0);
                Seed(x, Height - 1);
            }
            for (int z = 0; z < Height; z++)
            {
                Seed(0, z);
                Seed(Width - 1, z);
            }

            // Flood outward-connected open space.
            while (Stack.Length > 0)
            {
                int idx = Stack[Stack.Length - 1];
                Stack.RemoveAt(Stack.Length - 1);
                int x = idx % Width;
                int z = idx / Width;
                if (x > 0) Seed(x - 1, z);
                if (x < Width - 1) Seed(x + 1, z);
                if (z > 0) Seed(x, z - 1);
                if (z < Height - 1) Seed(x, z + 1);
            }

            // Any open cell the flood never reached is enclosed → fill solid,
            // unless it's a fresh break still on cooldown.
            for (int i = 0; i < n; i++)
            {
                if (Saturation[i] < OpenBelow && Visited[i] == 0 && Cooldown[i] == 0
                    && Influence[i] != InfluenceSuppress
                    && !(WorkerWard.IsCreated && WorkerWard[i] != 0))
                    Saturation[i] = 255;
            }
        }

        private void Seed(int x, int z)
        {
            int idx = z * Width + x;
            if (Visited[idx] != 0) return;
            if (Saturation[idx] >= OpenBelow) return; // crust wall — not open
            Visited[idx] = 1;
            Stack.Add(idx);
        }
    }
}
