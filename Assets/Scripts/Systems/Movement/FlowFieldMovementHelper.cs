// FlowFieldMovementHelper.cs
// Static helper providing flow-field-aware direction lookup with graceful fallback.
// Fallback API for non-Burst callers (e.g., CaravanMovementSystem, managed code).
// MovementSystem uses FlowFieldLookup directly for Burst-compatible direction lookup.
// Location: Assets/Scripts/Systems/Movement/FlowFieldMovementHelper.cs

using Unity.Mathematics;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Managed-code fallback for flow-field direction lookup.
    /// Delegates to FlowFieldLookup (NativeArray-based) when available,
    /// falling back to direct managed FlowField access otherwise.
    ///
    /// MovementSystem reads FlowFieldLookup directly for Burst compatibility.
    /// This helper remains for non-ECS callers that cannot access the lookup struct.
    ///
    /// Given a unit position and goal, returns the best movement direction:
    /// - Far from goal: flow field direction (obstacle-aware pathfinding)
    /// - Near goal: blended flow + direct-line (precise arrival)
    /// - No flow field available: direct-line fallback (graceful degradation)
    /// </summary>
    public static class FlowFieldMovementHelper
    {
        /// <summary>Blend radius in world units (approx 3 cells at cellSize=2).</summary>
        private const float BlendRadius = 6f;

        /// <summary>
        /// Given a unit position and its goal, return the movement direction
        /// using flow fields when available, with smooth blending near the goal.
        /// Returns the direct-line direction as fallback.
        ///
        /// Prefers FlowFieldLookup (NativeArray-based) when FlowFieldManager is
        /// initialized. Falls back to direct FlowField access if lookup is unavailable.
        /// </summary>
        /// <param name="position">Unit's current world position.</param>
        /// <param name="goal">Unit's destination world position.</param>
        /// <param name="directDir">Pre-computed normalized direct-line direction (goal - pos).</param>
        /// <param name="distToGoal">Pre-computed distance to goal (horizontal).</param>
        /// <returns>Normalized movement direction to use this frame.</returns>
        public static float3 GetDirection(float3 position, float3 goal,
                                           float3 directDir, float distToGoal)
        {
            var ffm = FlowFieldManager.Instance;
            if (ffm == null) return directDir;

            // Try NativeArray-based lookup first (Burst-compatible path)
            var lookup = ffm.CurrentLookup;
            if (lookup.IsValid)
            {
                // Ensure flow field is queued for generation
                ffm.RequestFlowField(goal);

                // Delegate to FlowFieldLookup (same logic, NativeArray-only)
                return lookup.GetDirection(position, goal, directDir, distToGoal);
            }

            // Fallback: direct managed FlowField access (pre-initialization)
            var field = ffm.RequestFlowField(goal);
            if (field == null) return directDir;

            var grid = PassabilityGrid.Instance;
            if (grid == null) return directDir;

            // Convert unit position to cell index
            int2 cell = grid.WorldToCell(position);
            if (cell.x < 0 || cell.x >= field.Value.Width ||
                cell.y < 0 || cell.y >= field.Value.Height)
                return directDir;

            int cellIndex = cell.y * field.Value.Width + cell.x;
            float2 flowDir2 = field.Value.DirectionField[cellIndex];

            // If cell has no valid direction (unreachable or destination), use direct
            if (math.lengthsq(flowDir2) < 1e-6f)
                return directDir;

            float3 flowDir = new float3(flowDir2.x, 0f, flowDir2.y);
            flowDir = math.normalizesafe(flowDir);

            // Blend: near destination use direct-line for precise arrival,
            // far from destination use flow field for obstacle avoidance.
            if (distToGoal < BlendRadius)
            {
                float t = distToGoal / BlendRadius; // 0 at goal, 1 at blend edge
                float3 blended = math.lerp(directDir, flowDir, t);
                return math.normalizesafe(blended);
            }

            return flowDir;
        }
    }
}
