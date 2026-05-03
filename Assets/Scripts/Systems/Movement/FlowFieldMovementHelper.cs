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
        /// <summary>
        /// Blend radius in world units. Within this distance of the goal, the
        /// helper blends to direct-line for precise arrival. Kept tight (2m) so
        /// units close to a building still consult the flow field — earlier 6m
        /// caused units to walk direct-line straight into a building when their
        /// goal was just past it. Aligned with FlowFieldLookup.BlendRadius.
        /// </summary>
        private const float BlendRadius = 2f;

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

            // Request flow field (also queues generation if not cached)
            var field = ffm.RequestFlowField(goal);
            if (field == null) return directDir;

            // Try NativeArray-based lookup first (Burst-compatible path)
            var lookup = ffm.CurrentLookup;
            if (lookup.IsValid)
            {
                // Use the manager's snapped destination to avoid lookup mismatch
                return lookup.GetDirection(position, field.Value.DestinationIndex, directDir, distToGoal);
            }

            var grid = PassabilityGrid.Instance;
            if (grid == null) return directDir;

            int w = field.Value.Width;
            int h = field.Value.Height;

            // Simple per-cell lookup (smoothing is per-unit in MovementSystem)
            int cx = (int)math.floor((position.x - grid.Origin.x) / grid.CellSize);
            int cz = (int)math.floor((position.z - grid.Origin.z) / grid.CellSize);
            cx = math.clamp(cx, 0, w - 1);
            cz = math.clamp(cz, 0, h - 1);

            float2 flowDir2 = field.Value.DirectionField[cz * w + cx];

            if (math.lengthsq(flowDir2) < 1e-6f)
                return directDir;

            float3 flowDir = math.normalizesafe(new float3(flowDir2.x, 0f, flowDir2.y));

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

        /// <summary>
        /// Sample direction field at a cell, clamping to grid bounds.
        /// Returns float2.zero for out-of-bounds cells.
        /// </summary>
        private static float2 SampleManaged(
            Unity.Collections.NativeArray<float2> directionField,
            int cx, int cy, int width, int height)
        {
            cx = math.clamp(cx, 0, width - 1);
            cy = math.clamp(cy, 0, height - 1);
            return directionField[cy * width + cx];
        }
    }
}
