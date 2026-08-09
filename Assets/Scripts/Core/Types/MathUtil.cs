// MathUtil.cs
// Shared Burst-compatible math helpers used across systems.
// Location: Assets/Scripts/Core/Types/MathUtil.cs

using Unity.Mathematics;

namespace TheWaningBorder.Core
{
    /// <summary>
    /// Small Burst-compatible math helpers. Centralizes formulas that were
    /// previously copy-pasted into many systems (e.g. horizontal distance).
    /// </summary>
    public static class MathUtil
    {
        /// <summary>Horizontal (XZ-plane) distance between two world positions.</summary>
        public static float DistXZ(float3 a, float3 b)
            => math.distance(new float2(a.x, a.z), new float2(b.x, b.z));

        /// <summary>Squared horizontal (XZ-plane) distance — cheaper when only comparing.</summary>
        public static float DistXZsq(float3 a, float3 b)
            => math.distancesq(new float2(a.x, a.z), new float2(b.x, b.z));

        /// <summary>
        /// Default turn rate, rad/s (~460 deg/s). Matches the cosmetic turn rate
        /// UnitIntegratorSystem applies while moving, so a unit that stops to work
        /// keeps rotating at the same speed it was rotating a frame earlier.
        /// </summary>
        public const float DefaultTurnSpeed = 8f;

        /// <summary>
        /// Rotate <paramref name="current"/> toward the XZ bearing from
        /// <paramref name="fromPos"/> to <paramref name="targetPos"/>, by at most
        /// turnSpeed*dt radians. Yaw only — units stay upright.
        ///
        /// Nothing rotated a unit that had STOPPED: UnitIntegratorSystem's
        /// rotation block is inside its "has a destination" branch, so a unit that
        /// stopped to mine, build, repair, heal or swing kept whatever heading it
        /// happened to hold on its last moving frame — frequently sideways or
        /// fully turned away from what it was working on.
        /// </summary>
        public static quaternion TurnTowardXZ(quaternion current, float3 fromPos, float3 targetPos,
            float dt, float turnSpeed = DefaultTurnSpeed)
        {
            float3 to = targetPos - fromPos;
            to.y = 0f;
            if (math.lengthsq(to) < 1e-6f) return current;

            quaternion desired = quaternion.RotateY(math.atan2(to.x, to.z));
            return SlerpClamped(current, desired, turnSpeed * dt);
        }

        /// <summary>Slerp from→to, never turning more than maxAngle radians.</summary>
        public static quaternion SlerpClamped(quaternion from, quaternion to, float maxAngle)
        {
            if (maxAngle <= 0f) return from;

            // |dot| — quaternions q and -q are the same rotation, so the absolute
            // value picks the short way round.
            float dot = math.clamp(math.abs(math.dot(from.value, to.value)), -1f, 1f);
            float angle = 2f * math.acos(dot);
            if (angle <= maxAngle || angle < 1e-5f) return to;
            return math.slerp(from, to, maxAngle / angle);
        }
    }
}
