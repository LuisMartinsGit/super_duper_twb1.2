// ContextSteer.cs
// Tier D — context-steering local avoidance helper.
//
// When a unit's desired movement direction is blocked by the radius-aware
// passability check, this samples a small fan of alternative directions
// around the desired heading and returns the most-aligned one that the
// unit's body can actually traverse. This converts the existing "perpendicular
// nudge" stuck recovery (Tier 2 in MovementSystem) into a smoother,
// monotonically-improving direction selection.
//
// Inspired by Andrew Fray's "Context Behaviours Know How to Share" (Game AI
// Pro 2013, GDC 2018). We only score by (alignment * passability) — no
// danger map, no neighbour repulsion — because the global flow field
// (Tier B, radius-aware) already handles obstacle avoidance at the macro
// scale, and UnitSeparationSystem handles ally-vs-ally separation
// post-step. This helper exists purely to refine the "I want to go this
// way but a wall is in front of me" case.
//
// Location: Assets/Scripts/Systems/Movement/ContextSteer.cs

using Unity.Mathematics;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    public static class ContextSteer
    {
        // Candidate offsets in radians from the desired direction. Symmetric
        // around 0, ordered nearest-first so ties go to "go more or less the
        // way I wanted." The 0 entry is intentionally absent — caller has
        // already determined that direction is blocked.
        private static readonly float[] CandidateOffsets =
        {
             0.3927f, -0.3927f, // +/- 22.5°
             0.7854f, -0.7854f, // +/- 45°
             1.1781f, -1.1781f, // +/- 67.5°
             1.5708f, -1.5708f, // +/- 90°
        };

        /// <summary>
        /// Try to find a passable steering direction near <paramref name="desiredDir"/>
        /// when the unit's body would clip an obstacle on the desired step.
        ///
        /// Returns true and sets <paramref name="steeredDir"/> to the best
        /// candidate direction (highest alignment with desired) whose
        /// projected next-position passes the radius-aware passability check.
        /// Returns false if every candidate is blocked — in that case the
        /// caller should fall through to the existing stuck-escalation
        /// (perpendicular nudge / spiral escape / cancel order).
        ///
        /// <paramref name="desiredDir"/> must be normalised. <paramref name="step"/>
        /// is the per-frame movement magnitude. <paramref name="position"/>
        /// is the unit's current world position. <paramref name="radius"/>
        /// is its collision radius for the Minkowski check.
        /// </summary>
        public static bool TrySteerAround(
            float3 position,
            float3 desiredDir,
            float step,
            float radius,
            PassabilityGrid grid,
            out float3 steeredDir)
        {
            steeredDir = desiredDir;
            if (grid == null) return false;

            // 2D heading (XZ plane); preserve y from the input direction.
            float baseAngle = math.atan2(desiredDir.x, desiredDir.z);

            // The candidate list is already ordered nearest-first, so the
            // first match is the best.
            for (int i = 0; i < CandidateOffsets.Length; i++)
            {
                float angle = baseAngle + CandidateOffsets[i];
                float3 candidate = new float3(math.sin(angle), 0f, math.cos(angle));
                float3 candidatePos = position + candidate * step;
                if (!grid.IsPassableForRadius(candidatePos, radius)) continue;

                steeredDir = candidate;
                return true;
            }

            return false;
        }
    }
}
