// BloodTrailComponents.cs
// ECS components lifted out of BloodTrailSystem.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;

/// <summary>A unit that paints blood on the ground as it moves.</summary>
public struct BloodTrail : IComponentData
{
    /// <summary>Blood laid down per second of movement (BloodMap amounts).</summary>
    public float BloodPerSecond;

    /// <summary>Minimum metres between splats.</summary>
    public float MinStep;

    public float3 LastPos;
    public byte HasLast;
}
