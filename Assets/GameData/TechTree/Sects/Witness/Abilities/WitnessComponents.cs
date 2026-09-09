// WitnessComponents.cs
// ECS components for the Sect of Witness's canon kit
// (docs/Design/Sects.md §"Sect of Witness"):
//   * Spy Network     — WitnessSpy on the compromised enemy, WitnessEye on the
//                       invisible vision source that follows it, SpyExposure
//                       on the enemies standing close enough to catch it.
//   * Blinding Glare  — SectBlinded.
// Nowhere to Hide needs no component: it resolves entirely at cast time.
//
// Global namespace per project convention — SectWitnessSpySystem reads them
// from TheWaningBorder.Systems.Sect, and AbilityLifecycleSystem reads
// SectBlinded from TheWaningBorder.Abilities.

using Unity.Entities;

/// <summary>
/// Spy Network. This ENEMY unit is unwittingly reporting to
/// <see cref="Owner"/>: everything it can see, the owner can see.
///
/// The unit is not converted and does not change sides — it fights for its
/// own faction exactly as before, which is the whole point. It simply leaks.
/// </summary>
public struct WitnessSpy : IComponentData
{
    /// <summary>The Witness player this unit reports to.</summary>
    public Faction Owner;

    /// <summary><see cref="SectEffectDuration.Permanent"/> at Lv III.</summary>
    public float TimeRemaining;

    /// <summary>The invisible vision entity following this unit.</summary>
    public Entity Eye;

    /// <summary>
    /// Power level that planted this spy. The cascade rule is read back from
    /// SectLeverEffects.WitnessCascade rather than copied here, so a balance
    /// edit reaches spies already standing on the map.
    /// </summary>
    public byte Level;
}

/// <summary>
/// The vision source for one spy: FactionTag + LocalTransform + LineOfSight
/// is exactly the trio FogOfWarSystem stamps vision from, so the owner sees
/// through the spy with no special support in the fog at all. It is the same
/// device SpawnReveal uses for a timed circle — this one just moves.
/// </summary>
public struct WitnessEye : IComponentData
{
    /// <summary>The spy this eye rides. Destroyed with it.</summary>
    public Entity Host;
}

/// <summary>
/// How long this enemy unit has been standing near a spy. At the cascade
/// threshold it becomes a spy itself.
///
/// <see cref="LastTouch"/> is what makes proximity have to be CONTINUOUS:
/// a unit that walks out of range and back later starts its count again,
/// rather than accumulating three seconds over a whole match.
/// </summary>
public struct SpyExposure : IComponentData
{
    public Faction Owner;
    public float Seconds;
    public float LastTouch;
}

/// <summary>
/// Blinding Glare. The unit sees nothing at all for the duration —
/// its LineOfSight radius is zeroed and <see cref="OriginalRadius"/> holds
/// what to give back.
/// </summary>
public struct SectBlinded : IComponentData
{
    public float TimeRemaining;
    public float OriginalRadius;

    /// <summary>Non-zero at Lv III: the unit also cannot start an ability.</summary>
    public byte LocksAbilities;
}
