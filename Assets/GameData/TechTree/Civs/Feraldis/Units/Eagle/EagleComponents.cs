// EagleComponents.cs
// ECS components lifted out of Eagle.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>An eagle circling its owner, carrying its own vision.</summary>
public struct EagleCompanion : IComponentData
{
    public Entity Owner;

    /// <summary>Current orbit angle, radians. Seeded per-eagle so a group of
    /// scouts doesn't fly its birds in lockstep formation.</summary>
    public float Angle;

    /// <summary>Phase offset for the radius wobble, same reasoning.</summary>
    public float WobblePhase;
}

/// <summary>Stamped on a scout that already has its eagle, so the retrofit
/// sweep never issues a second one.</summary>
public struct HasEagle : IComponentData
{
    public Entity Eagle;
}
