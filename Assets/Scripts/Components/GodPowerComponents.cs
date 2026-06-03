// GodPowerComponents.cs
// Per-faction god power infrastructure (spec §6.2 + refinement #6).
//
// Refinement #6: god powers DO NOT spend Glow. Each cast triggers a
// cooldown; the cooldown is reduced by Glow currently stored in the
// faction's Temple of Ridan. Formula: cooldown = base × 0.8^stored_glow.
// First Glow saves 20%, second saves another 16% of the original (20%
// of remaining 80%), third 12.8%, etc. — asymptotic to 0 but never
// reaching it.
//
// Place in: Assets/Scripts/Core/Components/

using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Per-faction god power cooldown state. One per faction bank entity.
/// Initialized in EconomyBootstrap.CreateFactionBank.
/// </summary>
public struct GodPowerState : IComponentData
{
    /// <summary>Cooldown duration (seconds) at zero stored Glow. Tunable per faction.</summary>
    public float BaseCooldown;

    /// <summary>Seconds remaining before the power can be cast again (0 = ready).</summary>
    public float CooldownRemaining;

    /// <summary>Total casts so far this match (analytics + balance).</summary>
    public int CastCount;
}

/// <summary>
/// Emitted by CommandRouter.IssueGodPower. Consumed by the cast resolver
/// (GodPowerCastSystem) — applies AOE effect at TargetPosition, sets the
/// faction's GodPowerState.CooldownRemaining to base × 0.8^stored_glow,
/// and removes itself.
///
/// One pending cast per faction at a time (the router rejects new requests
/// while CooldownRemaining > 0).
/// </summary>
public struct PendingGodPowerCast : IComponentData
{
    public Faction Caster;
    public float3 TargetPosition;
}
