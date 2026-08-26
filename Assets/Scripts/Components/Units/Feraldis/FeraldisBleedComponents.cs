// FeraldisBleedComponents.cs
// ECS components lifted out of FeraldisBleed.cs so the simulation's
// vocabulary lives in one place. See CLAUDE.md.

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Declares that this unit's landed hits inflict <see cref="Bleeding"/>.
/// Copied onto projectiles at fire time so a shot that lands after its
/// shooter dies still bleeds the target.
/// </summary>
public struct InflictsBleed : IComponentData
{
    public float DamagePerSecond;
    public float Duration;
}
