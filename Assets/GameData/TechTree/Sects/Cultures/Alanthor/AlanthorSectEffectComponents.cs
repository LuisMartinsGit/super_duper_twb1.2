// Runtime state for the Alanthor sect actives (docs/Design/Sects.md section 4).
//
// One component per effect that outlives its cast. Every one of them carries a
// TimeRemaining that AlanthorSectEffectSystem decrements; a component whose
// TimeRemaining is <= 0 at the start of a tick is removed and its expiry
// payload (if any) is paid out.
//
// Two effects are deliberately NOT here because the engine already has them:
//   * Immovable III uses the existing Invulnerable component (SpellComponents).
//   * The invisible half of Stoneveil uses the existing StealthTag.
// Veiled below layers the rest of Stoneveil on top of StealthTag.

using Unity.Entities;

// Global namespace: ECS components live there by project convention
// (CLAUDE.md), and these are read from systems in four different
// namespaces -- Combat, Border, Training, Research.
/// <summary>
/// Sentinel for "does not expire on a timer". Sew Disorder III lasts until
/// the unit is killed, and a Lv III Spy Network spy reports until it dies.
/// Both store this instead of a duration. (Raise Anew used to be the third
/// case; every level of it is permanent now, and a permanent BUILDING needs
/// no sentinel — it simply has no timer at all.)
/// </summary>
public static class SectEffectDuration
{
    public const float Permanent = -1f;
    public static bool IsPermanent(float t) => t < 0f;
}

// ── Antiquity ───────────────────────────────────────────────────────────

/// <summary>
/// Heavy Bureaucracy. On a BUILDING: it produces nothing at all — no
/// training, no research, no resource output — until this expires. Readers
/// gate on presence, not on the value.
/// </summary>
public struct SectShutdown : IComponentData
{
    public float TimeRemaining;
}

/// <summary>
/// Sew Disorder. On a UNIT: it is hostile to everything, including its own
/// faction, and everything is hostile to it. <c>OriginalFaction</c> is kept
/// so the unit can be handed back when the effect is not permanent.
/// </summary>
public struct SectDisordered : IComponentData
{
    public float TimeRemaining;   // SectEffectDuration.Permanent at Lv III
    public Faction OriginalFaction;
}

// ── Renewal ─────────────────────────────────────────────────────────────

/// <summary>
/// The regen tail on Hands of Plenty III: after the burst lands, healing
/// continues for 10 s. <c>FractionPerSecond</c> is of max HP.
/// </summary>
public struct SectRegenTail : IComponentData
{
    public float TimeRemaining;
    public float FractionPerSecond;
}

/// <summary>
/// Second Wind. While present the unit cannot drop below 1 HP. On expiry it
/// heals <c>HealOnExpiry</c> (a fraction of max HP; 0 below Lv III).
/// </summary>
public struct SectDeathWard : IComponentData
{
    public float TimeRemaining;
    public float HealOnExpiry;
}

// SectConjuredTower is GONE (docs/Design/Sects.md §4). Raise Anew now raises
// three permanent buildings of its own — RenewalTower / RenewalFortification /
// RenewalFortress — and a permanent building needs no marker to distinguish it
// from an ordinary one. Nothing in the sect set has a crumble timer any more.

// ── Fortitude ───────────────────────────────────────────────────────────

/// <summary>
/// Stoneveil. The unit is invisible (StealthTag rides alongside),
/// untargetable, and cannot interact with anything — no attacking,
/// gathering, building or capturing — but it MOVES, faster than normal.
/// Sect powers still reach it, friendly and hostile alike.
/// </summary>
public struct SectVeiled : IComponentData
{
    public float TimeRemaining;
    public float SpeedBonus;        // fraction added to move speed while veiled
    public float DamageOnExpiry;    // fraction, 10s of it, at Lv III only
}

/// <summary>
/// Bulwark's temporary HP grant. <c>GrantedHp</c> is recorded so expiry can
/// take back exactly what it gave rather than re-deriving a fraction of a
/// max that may have changed in the meantime.
/// </summary>
public struct SectBulwark : IComponentData
{
    public float TimeRemaining;
    public int   GrantedHp;
    public float MeleeReflect;      // Lv III only
}

// ── Reclamation ─────────────────────────────────────────────────────────

/// <summary>
/// Harvest the Veil, riding on the targeted resource node. Pays
/// <c>Level</c>'s basket (SectLeverEffects.HarvestYield) to
/// <c>Beneficiary</c> every HarvestTickSeconds until the duration runs out.
/// </summary>
public struct SectNodeOverYield : IComponentData
{
    public float   TimeRemaining;
    public float   TickTimer;
    public byte    Level;
    public Faction Beneficiary;
}

/// <summary>
/// Cleanse. A free-standing effect entity (not attached to a unit) that
/// pumps influence into a circle for its duration and, at Lv III, heals
/// allies standing in it.
/// </summary>
public struct SectInfluenceBurst : IComponentData
{
    public float   TimeRemaining;
    public float   Radius;
    public float   PerSecond;
    public bool    HealsAllies;     // Lv III
    public Faction Owner;
}

/// <summary>
/// Veil-Touched. Immunity to curse damage, plus a cursed-ground move bonus
/// at Lv III.
/// </summary>
public struct SectCurseWard : IComponentData
{
    public float TimeRemaining;
    public float CursedGroundSpeedBonus;
}

