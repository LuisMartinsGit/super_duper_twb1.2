// FlowFieldGenerator.cs
// Static utility class containing Burst-compiled BFS integration field generation
// and direction field derivation for flow field pathfinding.
// Provides both synchronous (Generate) and async (ScheduleAsync/CompleteAsync) APIs.
// Location: Assets/Scripts/Systems/Movement/FlowFieldGenerator.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Generates flow fields from passability grid data using BFS.
    /// Contains two Burst-compiled IJob structs:
    /// - IntegrationFieldJob: BFS outward from destination, computing cost-to-goal per cell.
    /// - DirectionFieldJob: derives a unit direction vector per cell pointing toward lowest cost neighbor.
    ///
    /// Two generation paths:
    /// - Generate(): synchronous via .Run() for immediate results on the main thread.
    /// - ScheduleAsync()/CompleteAsync(): async 2-frame path — BFS runs on worker thread
    ///   via .Schedule(), completed next frame. Direction derivation runs via .Run() on completion.
    /// </summary>
    public static class FlowFieldGenerator
    {
        // =====================================================================
        // CONSTANTS
        // =====================================================================

        /// <summary>Cardinal movement cost (10 = 1.0 scaled by 10 for integer math).</summary>
        private const ushort CardinalCost = 10;

        /// <summary>Diagonal movement cost (14 ~ sqrt(2) * 10).</summary>
        private const ushort DiagonalCost = 14;

        /// <summary>Sentinel value for unreachable / unvisited cells.</summary>
        private const ushort Unreachable = ushort.MaxValue;

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        /// <summary>
        /// Synchronously generates a flow field from the given passability grid data.
        /// Called from FlowFieldManager on the main thread (jobs complete immediately via .Run()).
        /// Uses FlowFieldArrayPool for array allocation to reduce churn.
        /// </summary>
        /// <param name="passabilityCells">Raw cell data from PassabilityGrid (0=passable, 1=terrain-blocked, 2=building-blocked).</param>
        /// <param name="gridWidth">Grid width in cells.</param>
        /// <param name="gridHeight">Grid height in cells.</param>
        /// <param name="destinationIndex">Flat index (y * width + x) of the destination cell.</param>
        /// <param name="gridVersion">Current grid version for staleness detection.</param>
        /// <returns>A FlowField with pooled NativeArrays. Caller must Dispose() when done.</returns>
        public static FlowField Generate(
            NativeArray<byte> passabilityCells,
            int gridWidth,
            int gridHeight,
            int destinationIndex,
            int gridVersion = 0)
        {
            // Rent arrays from pool instead of allocating
            var integrationField = FlowFieldArrayPool.RentIntegration();
            var directionField = FlowFieldArrayPool.RentDirection();

            // Allocate BFS queue (TempJob allocator — freed after job completes)
            var bfsQueue = new NativeQueue<int>(Allocator.TempJob);

            // Step 1: BFS integration field
            var integrationJob = new IntegrationFieldJob
            {
                Cells = passabilityCells,
                Width = gridWidth,
                Height = gridHeight,
                DestinationIndex = destinationIndex,
                IntegrationField = integrationField,
                BfsQueue = bfsQueue,
            };
            integrationJob.Run();

            bfsQueue.Dispose();

            // Step 2: Direction field derivation
            var directionJob = new DirectionFieldJob
            {
                IntegrationField = integrationField,
                Cells = passabilityCells,
                Width = gridWidth,
                Height = gridHeight,
                DirectionField = directionField,
            };
            directionJob.Run();

            return new FlowField
            {
                IntegrationField = integrationField,
                DirectionField = directionField,
                DestinationIndex = destinationIndex,
                Width = gridWidth,
                Height = gridHeight,
                GridVersion = gridVersion,
            };
        }

        // =====================================================================
        // ASYNC GENERATION API
        // =====================================================================

        /// <summary>
        /// Handle for an in-flight async flow field generation.
        /// Holds all resources needed to complete the generation on a later frame.
        /// </summary>
        public struct AsyncFlowFieldHandle
        {
            /// <summary>JobHandle for the scheduled IntegrationFieldJob.</summary>
            public JobHandle IntegrationHandle;

            /// <summary>Rented integration field array (written by BFS job).</summary>
            public NativeArray<ushort> IntegrationField;

            /// <summary>Rented direction field array (written on completion).</summary>
            public NativeArray<float2> DirectionField;

            /// <summary>BFS queue allocated for the job (disposed on completion).</summary>
            public NativeQueue<int> BfsQueue;

            /// <summary>Copy of passability data for job safety (disposed on completion).</summary>
            public NativeArray<byte> PassabilityCopy;

            /// <summary>Destination cell flat index.</summary>
            public int DestinationIndex;

            /// <summary>Grid width in cells.</summary>
            public int Width;

            /// <summary>Grid height in cells.</summary>
            public int Height;

            /// <summary>Grid version at schedule time.</summary>
            public int GridVersion;
        }

        /// <summary>
        /// Schedule async flow field generation. BFS runs on a worker thread
        /// via .Schedule(). Call CompleteAsync() on a later frame when
        /// handle.IntegrationHandle.IsCompleted is true.
        /// </summary>
        /// <param name="passabilityCells">Source passability data (will be copied for job safety).</param>
        /// <param name="gridWidth">Grid width in cells.</param>
        /// <param name="gridHeight">Grid height in cells.</param>
        /// <param name="destinationIndex">Destination cell flat index.</param>
        /// <param name="gridVersion">Current grid version for staleness detection.</param>
        /// <returns>Handle to track and complete the async generation.</returns>
        public static AsyncFlowFieldHandle ScheduleAsync(
            NativeArray<byte> passabilityCells,
            int gridWidth,
            int gridHeight,
            int destinationIndex,
            int gridVersion)
        {
            // Rent arrays from pool
            var integrationField = FlowFieldArrayPool.RentIntegration();
            var directionField = FlowFieldArrayPool.RentDirection();

            // Copy passability data — the source lives on a managed MonoBehaviour
            // and may change between schedule and complete frames
            var passabilityCopy = new NativeArray<byte>(passabilityCells.Length, Allocator.TempJob);
            passabilityCopy.CopyFrom(passabilityCells);

            // Allocate BFS queue
            var bfsQueue = new NativeQueue<int>(Allocator.TempJob);

            // Schedule BFS on worker thread
            var integrationJob = new IntegrationFieldJob
            {
                Cells = passabilityCopy,
                Width = gridWidth,
                Height = gridHeight,
                DestinationIndex = destinationIndex,
                IntegrationField = integrationField,
                BfsQueue = bfsQueue,
            };

            var handle = integrationJob.Schedule();

            return new AsyncFlowFieldHandle
            {
                IntegrationHandle = handle,
                IntegrationField = integrationField,
                DirectionField = directionField,
                BfsQueue = bfsQueue,
                PassabilityCopy = passabilityCopy,
                DestinationIndex = destinationIndex,
                Width = gridWidth,
                Height = gridHeight,
                GridVersion = gridVersion,
            };
        }

        /// <summary>
        /// Complete an async flow field generation. Calls Complete() on the BFS job handle,
        /// runs the direction field derivation synchronously (it is fast — pure lookups),
        /// disposes temporary resources, and returns the finished FlowField.
        /// </summary>
        /// <param name="handle">Handle from ScheduleAsync().</param>
        /// <returns>Completed FlowField with pooled NativeArrays.</returns>
        public static FlowField CompleteAsync(AsyncFlowFieldHandle handle)
        {
            // Complete the BFS job
            handle.IntegrationHandle.Complete();

            // Run direction field derivation synchronously (fast pass)
            var directionJob = new DirectionFieldJob
            {
                IntegrationField = handle.IntegrationField,
                Cells = handle.PassabilityCopy,
                Width = handle.Width,
                Height = handle.Height,
                DirectionField = handle.DirectionField,
            };
            directionJob.Run();

            // Dispose temporary resources
            handle.BfsQueue.Dispose();
            handle.PassabilityCopy.Dispose();

            return new FlowField
            {
                IntegrationField = handle.IntegrationField,
                DirectionField = handle.DirectionField,
                DestinationIndex = handle.DestinationIndex,
                Width = handle.Width,
                Height = handle.Height,
                GridVersion = handle.GridVersion,
            };
        }

        /// <summary>
        /// Cancel an in-flight async generation, disposing all resources.
        /// Use when a handle becomes stale (e.g., grid version changed).
        /// </summary>
        public static void CancelAsync(AsyncFlowFieldHandle handle)
        {
            handle.IntegrationHandle.Complete(); // Must complete before disposing
            handle.BfsQueue.Dispose();
            handle.PassabilityCopy.Dispose();
            FlowFieldArrayPool.Return(handle.IntegrationField);
            FlowFieldArrayPool.Return(handle.DirectionField);
        }

        // =====================================================================
        // INTEGRATION FIELD JOB (BFS)
        // =====================================================================

        /// <summary>
        /// BFS from destination cell outward through passable cells.
        /// Computes cost-to-goal for every reachable cell using 8-directional movement
        /// with cardinal cost 10 and diagonal cost 14.
        /// Diagonal movement is blocked (corner-cutting prevention) if either adjacent
        /// cardinal cell is impassable.
        /// </summary>
        [BurstCompile]
        private struct IntegrationFieldJob : IJob
        {
            [ReadOnly] public NativeArray<byte> Cells;
            public int Width;
            public int Height;
            public int DestinationIndex;

            public NativeArray<ushort> IntegrationField;
            public NativeQueue<int> BfsQueue;

            public void Execute()
            {
                // Initialize all cells to unreachable
                for (int i = 0; i < IntegrationField.Length; i++)
                    IntegrationField[i] = Unreachable;

                // Validate destination is in bounds and passable
                if (DestinationIndex < 0 || DestinationIndex >= Cells.Length)
                    return;
                if (Cells[DestinationIndex] != 0)
                    return;

                // Seed BFS with destination
                IntegrationField[DestinationIndex] = 0;
                BfsQueue.Enqueue(DestinationIndex);

                while (BfsQueue.Count > 0)
                {
                    int currentIndex = BfsQueue.Dequeue();
                    int cx = currentIndex % Width;
                    int cy = currentIndex / Width;
                    ushort currentCost = IntegrationField[currentIndex];

                    // --- Cardinals (N, E, S, W) ---
                    ProcessCardinal(cx, cy, 0, 1, currentCost);   // N
                    ProcessCardinal(cx, cy, 1, 0, currentCost);   // E
                    ProcessCardinal(cx, cy, 0, -1, currentCost);  // S
                    ProcessCardinal(cx, cy, -1, 0, currentCost);  // W

                    // --- Diagonals (NE, SE, SW, NW) with corner-cutting prevention ---
                    ProcessDiagonal(cx, cy, 1, 1, currentCost);   // NE (check N, E)
                    ProcessDiagonal(cx, cy, 1, -1, currentCost);  // SE (check E, S)
                    ProcessDiagonal(cx, cy, -1, -1, currentCost); // SW (check S, W)
                    ProcessDiagonal(cx, cy, -1, 1, currentCost);  // NW (check W, N)
                }
            }

            private void ProcessCardinal(int cx, int cy, int dx, int dy, ushort currentCost)
            {
                int nx = cx + dx;
                int ny = cy + dy;

                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height)
                    return;

                int neighborIndex = ny * Width + nx;

                if (Cells[neighborIndex] != 0)
                    return;

                ushort newCost = (ushort)(currentCost + CardinalCost);
                if (newCost < IntegrationField[neighborIndex])
                {
                    IntegrationField[neighborIndex] = newCost;
                    BfsQueue.Enqueue(neighborIndex);
                }
            }

            private void ProcessDiagonal(int cx, int cy, int dx, int dy, ushort currentCost)
            {
                int nx = cx + dx;
                int ny = cy + dy;

                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height)
                    return;

                int neighborIndex = ny * Width + nx;

                if (Cells[neighborIndex] != 0)
                    return;

                // Corner-cutting prevention: both adjacent cardinals must be passable
                // For diagonal (dx, dy), the two adjacent cardinals are (dx, 0) and (0, dy)
                int adj1x = cx + dx;
                int adj1y = cy;
                int adj2x = cx;
                int adj2y = cy + dy;

                // Check cardinal 1: (cx+dx, cy)
                if (adj1x < 0 || adj1x >= Width || adj1y < 0 || adj1y >= Height)
                    return;
                if (Cells[adj1y * Width + adj1x] != 0)
                    return;

                // Check cardinal 2: (cx, cy+dy)
                if (adj2x < 0 || adj2x >= Width || adj2y < 0 || adj2y >= Height)
                    return;
                if (Cells[adj2y * Width + adj2x] != 0)
                    return;

                ushort newCost = (ushort)(currentCost + DiagonalCost);
                if (newCost < IntegrationField[neighborIndex])
                {
                    IntegrationField[neighborIndex] = newCost;
                    BfsQueue.Enqueue(neighborIndex);
                }
            }
        }

        // =====================================================================
        // DIRECTION FIELD JOB
        // =====================================================================

        /// <summary>
        /// Derives a direction vector per cell by finding the neighbor with the lowest
        /// integration cost. Uses the same 8-directional neighbor check with corner-cutting
        /// prevention as the integration field.
        /// </summary>
        [BurstCompile]
        private struct DirectionFieldJob : IJob
        {
            [ReadOnly] public NativeArray<ushort> IntegrationField;
            [ReadOnly] public NativeArray<byte> Cells;
            public int Width;
            public int Height;

            public NativeArray<float2> DirectionField;

            public void Execute()
            {
                for (int i = 0; i < IntegrationField.Length; i++)
                {
                    ushort cost = IntegrationField[i];

                    // Unreachable or destination cell: no direction
                    if (cost == Unreachable || cost == 0)
                    {
                        DirectionField[i] = float2.zero;
                        continue;
                    }

                    int cx = i % Width;
                    int cy = i / Width;

                    ushort bestCost = cost;
                    int bestNx = cx;
                    int bestNy = cy;

                    // Check 4 cardinal neighbors
                    CheckCardinalNeighbor(cx, cy, 0, 1, ref bestCost, ref bestNx, ref bestNy);   // N
                    CheckCardinalNeighbor(cx, cy, 1, 0, ref bestCost, ref bestNx, ref bestNy);   // E
                    CheckCardinalNeighbor(cx, cy, 0, -1, ref bestCost, ref bestNx, ref bestNy);  // S
                    CheckCardinalNeighbor(cx, cy, -1, 0, ref bestCost, ref bestNx, ref bestNy);  // W

                    // Check 4 diagonal neighbors with corner-cutting prevention
                    CheckDiagonalNeighbor(cx, cy, 1, 1, ref bestCost, ref bestNx, ref bestNy);   // NE
                    CheckDiagonalNeighbor(cx, cy, 1, -1, ref bestCost, ref bestNx, ref bestNy);  // SE
                    CheckDiagonalNeighbor(cx, cy, -1, -1, ref bestCost, ref bestNx, ref bestNy); // SW
                    CheckDiagonalNeighbor(cx, cy, -1, 1, ref bestCost, ref bestNx, ref bestNy);  // NW

                    // Compute direction toward best neighbor
                    if (bestNx == cx && bestNy == cy)
                    {
                        // No better neighbor found (shouldn't happen for reachable cells, but safety)
                        DirectionField[i] = float2.zero;
                    }
                    else
                    {
                        float2 dir = new float2(bestNx - cx, bestNy - cy);
                        DirectionField[i] = math.normalizesafe(dir);
                    }
                }
            }

            private void CheckCardinalNeighbor(int cx, int cy, int dx, int dy,
                ref ushort bestCost, ref int bestNx, ref int bestNy)
            {
                int nx = cx + dx;
                int ny = cy + dy;

                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height)
                    return;

                int neighborIndex = ny * Width + nx;
                ushort neighborCost = IntegrationField[neighborIndex];

                if (neighborCost < bestCost)
                {
                    bestCost = neighborCost;
                    bestNx = nx;
                    bestNy = ny;
                }
            }

            private void CheckDiagonalNeighbor(int cx, int cy, int dx, int dy,
                ref ushort bestCost, ref int bestNx, ref int bestNy)
            {
                int nx = cx + dx;
                int ny = cy + dy;

                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height)
                    return;

                int neighborIndex = ny * Width + nx;
                ushort neighborCost = IntegrationField[neighborIndex];

                if (neighborCost >= bestCost)
                    return;

                // Corner-cutting prevention: both adjacent cardinals must be passable
                int adj1x = cx + dx;
                int adj1y = cy;
                int adj2x = cx;
                int adj2y = cy + dy;

                if (adj1x < 0 || adj1x >= Width || adj1y < 0 || adj1y >= Height)
                    return;
                if (Cells[adj1y * Width + adj1x] != 0)
                    return;

                if (adj2x < 0 || adj2x >= Width || adj2y < 0 || adj2y >= Height)
                    return;
                if (Cells[adj2y * Width + adj2x] != 0)
                    return;

                bestCost = neighborCost;
                bestNx = nx;
                bestNy = ny;
            }
        }
    }
}
