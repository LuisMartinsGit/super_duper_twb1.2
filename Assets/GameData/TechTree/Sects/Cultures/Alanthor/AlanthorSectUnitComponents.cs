// AlanthorSectUnitComponents.cs
// State for the Alanthor sect UNITS and HEROES (docs/Design/Sects.md §4,
// 2026-10-15). Global namespace per project convention.
//
// Unit traits are PLAIN markers / counters added by each factory (a unit
// carries its trait for life). Everything that lands on OTHER units for a
// while is IEnableableComponent and rides the unit pre-add set
// (TransientState.UnitSet5) — toggle it with TransientState, never
// Add/Remove (see TransientState.cs for why).

using Unity.Entities;
using Unity.Mathematics;

// ── Unit traits ──────────────────────────────────────────────────────────

/// <summary>Artificer — Stasis Bolt: a target it hits is slowed in time
/// (move and attack speed) for a few seconds.</summary>
public struct StasisBoltTrait : IComponentData
{
    public float MoveSlow;      // fraction, 0.30 = -30%
    public float AttackSlow;    // attack-speed multiplier while slowed (0.75)
    public float Seconds;
}

/// <summary>Scar Guard — Borrowed Breath: more damage the lower its HP.
/// Damage x (1 + MaxBonus x missing-HP fraction).</summary>
public struct BorrowedBreathTrait : IComponentData
{
    public float MaxBonus;      // 0.6 = up to +60% at the edge of death
}

/// <summary>Sallyguard — Stored Strike: the halberd charges while out of
/// combat; the first blow afterwards deals Multiplier x damage.</summary>
public struct StoredStrike : IComponentData
{
    public float Charge;            // seconds banked
    public float ChargeSeconds;     // full charge
    public float Multiplier;        // 2 = double
    public float OutOfCombatDelay;  // seconds without a target before charging starts
    public float IdleTimer;
}

/// <summary>Collector — Run Down: bonus damage against slowed enemies and
/// enemies under half HP.</summary>
public struct RunDownTrait : IComponentData
{
    public float Bonus;     // 0.5 = +50%
}

/// <summary>Marks a sect HERO (Religion.md §4): which sect it serves.
/// Unique per faction; carries HeroLevel / HeroExperience like King Lexor.</summary>
public struct SectHeroTag : IComponentData
{
    public byte SectIndex;
}

// ── Hero state ───────────────────────────────────────────────────────────

/// <summary>Archivist of Hours — where it stood over the last few seconds,
/// sampled every half second, for Recall (L6).</summary>
public struct ArchivistTrail : IComponentData
{
    public float3 P0, P1, P2, P3, P4, P5, P6, P7, P8, P9;
    public byte Head;
    public byte Count;
    public float Sample;
}

/// <summary>Guildmaster — Stake (L3): a channel on the node under the hero.</summary>
public struct StakeChannel : IComponentData   // active while TimeRemaining > 0
{
    public Entity Node;
    public float TimeRemaining;
    public int HealthAtStart;
}

// ── Timed effects on others (unit pre-add set) ───────────────────────────

/// <summary>Catalogued (Archivist L1 aura): a slowed enemy near the Archivist
/// takes Bonus more damage. Refreshed every pulse while it qualifies.</summary>
public struct CatalogedMark : IComponentData, IEnableableComponent
{
    public float Bonus;
    public float TimeRemaining;
    public Faction ByFaction;
}

/// <summary>Lancet (Surgeon L6): bleeding, and no healing or repair while it lasts.</summary>
public struct Lanced : IComponentData, IEnableableComponent
{
    public int LastHp;          // healing above this is taken back while it lasts
    public float DamagePerSecond;
    public float TimeRemaining;
    public float Accum;
    public Faction Source;
}

/// <summary>Cauterize (Surgeon L1 aura): immune to damage over time.</summary>
public struct Cauterized : IComponentData, IEnableableComponent
{
    public float TimeRemaining;
}

/// <summary>Challenge (Warden L6): must attack the Taunter until it lapses.</summary>
public struct Taunted : IComponentData, IEnableableComponent
{
    public Entity Taunter;
    public float TimeRemaining;
}

/// <summary>Bounty (Prosperity A): if this unit dies before the window
/// closes, its cost is paid to <see cref="ByFaction"/>.</summary>
public struct BountyMarked : IComponentData, IEnableableComponent
{
    public Faction ByFaction;
    public float TimeRemaining;
}

// ── Alanthor sect powers (2026-10-15) ────────────────────────────────────

/// <summary>Overtime (Prosperity B): this building trains, researches and
/// produces Multiplier x faster. Building transient (pre-added disabled by
/// BuildingTransientSeedSystem).</summary>
public struct OvertimeBoost : IComponentData, IEnableableComponent
{
    public float Multiplier;
    public float TimeRemaining;
}

/// <summary>Ward Discharge (Fortitude A): an armed charge on one of the
/// caster's structures. Waits up to Lifetime; when a hostile unit enters
/// TriggerRadius it fires its pulses, Interval apart, each Damage to every
/// hostile unit in range. Unspent pulses stay armed. Building transient.</summary>
public struct WardDischargeCharge : IComponentData, IEnableableComponent
{
    public float Damage;
    public float TriggerRadius;
    public float Interval;
    public int PulsesLeft;
    public float Lifetime;
    public float Timer;
    public byte Firing;
}

/// <summary>One recent death, for Revive (Renewal wildcard). Kept on the
/// DeathLog singleton for a short window; heroes are never logged.</summary>
public struct RecentDeath : IBufferElementData
{
    public Unity.Collections.FixedString64Bytes UnitId;
    public float3 Position;
    public double Time;
    public Faction Faction;
}

/// <summary>The singleton that holds the RecentDeath buffer.</summary>
public struct DeathLogTag : IComponentData { }
