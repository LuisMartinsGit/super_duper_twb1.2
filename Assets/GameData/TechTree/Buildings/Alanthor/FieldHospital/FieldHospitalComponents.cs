// FieldHospitalComponents.cs
// State for the Litharch-deployed Field Hospital (Shrine of Ridan tech).

using Unity.Entities;

/// <summary>Marks a deployed Field Hospital.</summary>
public struct FieldHospitalTag : IComponentData { }

/// <summary>Countdown to self-demolition. When TimeToLive reaches zero the
/// hospital sets its own Health to 0 and lets DeathSystem remove it — never a
/// direct DestroyEntity, which would corrupt the EndSimulation ECB playback.</summary>
public struct FieldHospitalState : IComponentData
{
    public float TimeToLive;
}
