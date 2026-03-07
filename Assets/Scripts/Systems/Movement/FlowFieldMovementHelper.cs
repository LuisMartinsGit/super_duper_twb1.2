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

            int w = field.Value.Width;
            int h = field.Value.Height;

            // Bilinear interpolation: sample the 4 nearest cell centers for smooth directions
            float fx = (position.x - grid.Origin.x) / grid.CellSize - 0.5f;
            float fz = (position.z - grid.Origin.z) / grid.CellSize - 0.5f;
            int x0 = (int)math.floor(fx);
            int z0 = (int)math.floor(fz);
            float tx = fx - x0;
            float tz = fz - z0;

            float2 d00 = SampleManaged(field.Value.DirectionField, x0, z0, w, h);
            float2 d10 = SampleManaged(field.Value.DirectionField, x0 + 1, z0, w, h);
            float2 d01 = SampleManaged(field.Value.DirectionField, x0, z0 + 1, w, h);
            float2 d11 = SampleManaged(field.Value.DirectionField, x0 + 1, z0 + 1, w, h);

            float2 flowDir2 = math.lerp(
                math.lerp(d00, d10, tx),
                math.lerp(d01, d11, tx),
                tz);

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
