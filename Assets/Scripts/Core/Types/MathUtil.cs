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
    }
}
