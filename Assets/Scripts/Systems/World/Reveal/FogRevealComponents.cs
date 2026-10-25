// FogRevealComponents.cs
// A TEMPORARY REVEAL of one entity to chosen player factions, through the
// fog. Two rules grant it:
//   - a projectile that damages a unit reveals its SHOOTER to the victim's
//     owner (docs/Design/Combat_Pacing.md § A shot gives the shooter away);
//   - a claim blocked by a lock or by the curse reveals the structures
//     blocking it to the conquering side (docs/Design/Territory_Claims.md §3.1).
// FogRevealSystem converts the pending bits into per-faction timers and
// counts them down; FogOfWarSystem stamps a small circle of sight for every
// faction whose timer runs; EndgameMarkers marks the lock reveals.
//
// Global namespace, per the ECS component convention. Enableable and
// pre-added disabled on units (TransientState UnitSet6) and buildings
// (BuildingTransientSeedSystem), so granting it is never a structural change.

using Unity.Entities;
using Unity.Mathematics;

/// <summary>Seconds left of a reveal, per player faction (Blue 0 .. White 7),
/// by the rule that granted it. Disabled = revealed to nobody.</summary>
public struct FogReveal : IComponentData, IEnableableComponent
{
    /// <summary>Factions hit by this shooter since the last FogRevealSystem
    /// pass (bit = faction index).</summary>
    public byte PendingHit;
    /// <summary>Factions this structure blocked a claim for since the last pass.</summary>
    public byte PendingLock;
    /// <summary>Shot reveal, seconds left, factions 0..3 / 4..7.</summary>
    public float4 HitLo, HitHi;
    /// <summary>Claim-block reveal, seconds left, factions 0..3 / 4..7.</summary>
    public float4 LockLo, LockHi;

    public const int Players = 8;

    public static float Lane(float4 lo, float4 hi, int f) => f < 4 ? lo[f] : hi[f - 4];

    /// <summary>Is this entity revealed to faction <paramref name="f"/> by any rule?</summary>
    public bool RevealedTo(int f)
        => f >= 0 && f < Players && (Lane(HitLo, HitHi, f) > 0f || Lane(LockLo, LockHi, f) > 0f);

    /// <summary>Revealed to <paramref name="f"/> because it blocks that faction's claim?</summary>
    public bool LockRevealedTo(int f) => f >= 0 && f < Players && Lane(LockLo, LockHi, f) > 0f;
}

/// <summary>Granting a <see cref="FogReveal"/>. Sim-side callers only — the
/// reveal is simulation state (what each side may see), set on the lockstep
/// clock.</summary>
public static class FogRevealGrant
{
    /// <summary>Mark <paramref name="e"/> revealed to <paramref name="viewer"/>.
    /// <paramref name="byLock"/>: the claim-block rule, else the shot rule.
    /// Direct write when the entity carries the (pre-added) component; an
    /// entity that never got the pre-add (a scenario husk, a curse creature
    /// spawned outside the factories) takes it through the ECB — a one-time
    /// structural change, per the TransientState contract.</summary>
    public static void Mark(EntityManager em, EntityCommandBuffer ecb, Entity e, Faction viewer, bool byLock)
    {
        int f = (int)viewer;
        if (f < 0 || f >= FogReveal.Players) return;   // the curse has no fog
        byte bit = (byte)(1 << f);
        if (em.HasComponent<FogReveal>(e))
        {
            var r = em.IsComponentEnabled<FogReveal>(e) ? em.GetComponentData<FogReveal>(e) : default;
            if (byLock) r.PendingLock |= bit; else r.PendingHit |= bit;
            em.SetComponentData(e, r);
            em.SetComponentEnabled<FogReveal>(e, true);
        }
        else
        {
            var r = default(FogReveal);
            if (byLock) r.PendingLock = bit; else r.PendingHit = bit;
            TransientState.Set(ecb, e, r);
        }
    }

    /// <summary><see cref="Mark(EntityManager, EntityCommandBuffer, Entity, Faction, bool)"/>
    /// for a caller that is not iterating a query: a missing component is
    /// added at once.</summary>
    public static void Mark(EntityManager em, Entity e, Faction viewer, bool byLock)
    {
        int f = (int)viewer;
        if (f < 0 || f >= FogReveal.Players) return;
        byte bit = (byte)(1 << f);
        var r = TransientState.Active<FogReveal>(em, e) ? em.GetComponentData<FogReveal>(e) : default;
        if (byLock) r.PendingLock |= bit; else r.PendingHit |= bit;
        TransientState.Set(em, e, r);
    }
}
