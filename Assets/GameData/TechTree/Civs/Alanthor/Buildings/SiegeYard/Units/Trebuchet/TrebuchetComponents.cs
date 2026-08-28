// TrebuchetComponents.cs
// All types are in the global namespace (single assembly), so location is
// organizational only — co-located with the unit that introduced them.

using Unity.Entities;

/// <summary>
/// Trebuchet pack/unpack state. The engine fires ONLY while deployed:
/// TrebuchetDeploySystem (co-located) packs it the moment it moves or gains
/// an active DesiredDestination (Deployed = 0, Timer = 0) and accumulates
/// Timer while it stands with a live target, flipping Deployed at 3 s.
/// RangedCombatSystem's fire path skips shooters with Deployed == 0.
/// </summary>
public struct TrebuchetState : IComponentData
{
    /// <summary>1 once the 3 s set-up completed; 0 while packed / setting up.</summary>
    public byte Deployed;

    /// <summary>Seconds spent standing with a live target since last move.</summary>
    public float Timer;
}
