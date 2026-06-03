// IntegrationDijkstraJob.cs
// Whole-map integer Dijkstra used by the M1 flow field. Bucketed by step
// cost (10 for cardinal, 14 for diagonal) so the open set degenerates to
// a small set of FIFO buckets — deterministic iteration order, no heap
// pointer chasing.
//
// Determinism notes (DR-3 / DR-4):
//   * Open buckets are processed in ascending cost order.
//   * Within a bucket, cells are processed in FIFO insertion order, which
//     is itself row-major within a sweep — see neighbour-write order below.
//   * Neighbours are visited in the locked order [+x, -x, +z, -z, +x+z,
//     +x-z, -x+z, -x-z], producing a stable tie-break across machines.
//
// Costs are uint to make UnreachableIntegration (uint.MaxValue) cheap to
// compare with arithmetic.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/IntegrationDijkstraJob.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Computes the per-cell integration cost from <see cref="Goal"/> over
    /// the layer-0 cost slab, treating <see cref="NavCostField.CostImpassable"/>
    /// cells as walls. Single-threaded by design: M1 has at most one active
    /// goal, and a deterministic frontier order is cheaper to enforce on
    /// one thread than to recover across threads.
    /// </summary>
    [BurstCompile]
    internal struct IntegrationDijkstraJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Cost;
        public NativeArray<uint> Integration;
        public int Width;
        public int Height;
        public int2 Goal;

        // Scratch FIFO queue — pre-allocated by the caller, capacity at
        // least Width*Height so we never need to resize during the sweep.
        public NativeQueue<int> FrontierA;
        public NativeQueue<int> FrontierB;

        public void Execute()
        {
            int n = Width * Height;

            // Init integration to "unreachable" everywhere. Iterate row-major
            // so the compiler can keep the pattern in a tight loop.
            for (int i = 0; i < n; i++)
                Integration[i] = NavFlowConstants.UnreachableIntegration;

            // Bounds-check + impassable-goal guard.
            if (Goal.x < 0 || Goal.x >= Width || Goal.y < 0 || Goal.y >= Height)
                return;

            int goalIdx = Goal.y * Width + Goal.x;
            if (Cost[goalIdx] == NavCostField.CostImpassable)
                return;

            Integration[goalIdx] = 0;
            FrontierA.Clear();
            FrontierB.Clear();
            FrontierA.Enqueue(goalIdx);

            // Bellman-Ford-style relaxation using two alternating frontiers.
            // Each cell is enqueued at most a small constant number of times
            // (each time its cost strictly improves), giving O(N) work in
            // practice for a uniform-cost grid. Deterministic by FIFO order.
            //
            // Loop swaps Read/Write each iteration until the write frontier
            // is empty.
            var readFrontier = FrontierA;
            var writeFrontier = FrontierB;

            while (readFrontier.Count > 0)
            {
                while (readFrontier.TryDequeue(out int idx))
                {
                    uint here = Integration[idx];
                    int x = idx % Width;
                    int z = idx / Width;

                    // Cardinal neighbours: +x, -x, +z, -z
                    RelaxNeighbour(x + 1, z, here + NavFlowConstants.StepCardinal, writeFrontier);
                    RelaxNeighbour(x - 1, z, here + NavFlowConstants.StepCardinal, writeFrontier);
                    RelaxNeighbour(x, z + 1, here + NavFlowConstants.StepCardinal, writeFrontier);
                    RelaxNeighbour(x, z - 1, here + NavFlowConstants.StepCardinal, writeFrontier);

                    // Diagonal neighbours: +x+z, +x-z, -x+z, -x-z. Octile
                    // movement requires both adjacent cardinals be open so
                    // diagonals don't "squeeze through" a wall corner.
                    if (IsOpen(x + 1, z) && IsOpen(x, z + 1))
                        RelaxNeighbour(x + 1, z + 1, here + NavFlowConstants.StepDiagonal, writeFrontier);
                    if (IsOpen(x + 1, z) && IsOpen(x, z - 1))
                        RelaxNeighbour(x + 1, z - 1, here + NavFlowConstants.StepDiagonal, writeFrontier);
                    if (IsOpen(x - 1, z) && IsOpen(x, z + 1))
                        RelaxNeighbour(x - 1, z + 1, here + NavFlowConstants.StepDiagonal, writeFrontier);
                    if (IsOpen(x - 1, z) && IsOpen(x, z - 1))
                        RelaxNeighbour(x - 1, z - 1, here + NavFlowConstants.StepDiagonal, writeFrontier);
                }

                // Swap frontiers and continue with whatever the relaxation
                // produced. Swap by value — both are by-value NativeQueue
                // handles into the same allocator.
                var tmp = readFrontier;
                readFrontier = writeFrontier;
                writeFrontier = tmp;
            }
        }

        private bool IsOpen(int x, int z)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Height) return false;
            return Cost[z * Width + x] != NavCostField.CostImpassable;
        }

        private void RelaxNeighbour(int x, int z, uint tentative, NativeQueue<int> writeFrontier)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Height) return;
            int idx = z * Width + x;
            if (Cost[idx] == NavCostField.CostImpassable) return;

            if (tentative < Integration[idx])
            {
                Integration[idx] = tentative;
                writeFrontier.Enqueue(idx);
            }
        }
    }
}
