// PlundererComponents.cs
// ECS components lifted out of Plunderer.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

/// <summary>Marker for the free, uncontrollable Raider-Camp Plunderer.</summary>
public struct PlundererTag : IComponentData { }

/// <summary>
/// A Plunderer's unbanked take. Resources are drained in fractional amounts
/// each tick and banked as whole units, so a 5/s rate does not round to
/// nothing on a fast frame.
/// </summary>
public struct PlunderPurse : IComponentData
{
    public float Supplies;
    public float Iron;
    public float Veilstone;
    public float Veilsteel;
    public float TickTimer;
}

/// <summary>Back-reference to the camp that produced this Plunderer, so the
/// camp can count its own live raiders against the cap.</summary>
public struct PlundererOrigin : IComponentData
{
    public Entity Camp;
}
