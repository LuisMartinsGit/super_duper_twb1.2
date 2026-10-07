// AbilityRuntimeComponents.cs
// ECS components for the data-driven ability engine (see AbilityCard /
// AbilityCatalog). Kept separate from the legacy enum-based AbilityComponents.cs.
//
// Units carry UnitAbilities (up to 4 catalog-index slots). Active abilities run
// through AbilityCastState -> effects -> AbilityAftermath. Effects are expressed
// as short-lived buff components ticked by AbilityEffectTickSystem (plus the
// existing SpellBuff/SpellDebuff for attack/armor/speed).

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    // ==================== Assignment ====================

    /// <summary>Up to four abilities on a unit, stored as stable AbilityCatalog
    /// indices (-1 = empty). Passive abilities are applied continuously by the
    /// aura/passive systems; active ones are fired via AbilityActivated.</summary>
    public struct UnitAbilities : IComponentData
    {
        // Design rule (2026-08-02): non-hero units carry at most ONE Active
        // and ONE Passive ability. The four slots exist for future heroes;
        // UI and cast routing (first-ready-active) assume the 1+1 rule.
        public int S0, S1, S2, S3;

        public int Get(int i) => i == 0 ? S0 : i == 1 ? S1 : i == 2 ? S2 : S3;

        public static UnitAbilities From(int s0 = -1, int s1 = -1, int s2 = -1, int s3 = -1)
            => new UnitAbilities { S0 = s0, S1 = s1, S2 = s2, S3 = s3 };
    }

    /// <summary>Per-slot cooldown remaining (seconds). Prevents recast while an
    /// active ability is running / cooling.</summary>
    public struct AbilityCooldowns : IComponentData
    {
        public float C0, C1, C2, C3;
    }

    // ==================== Active-ability lifecycle ====================

    /// <summary>An active ability being channelled. Added when the ability fires;
    /// when CastRemaining hits 0 the effects are applied, the slot's cooldown
    /// is charged and it is removed. A new order, a stun (Launched) or death
    /// removes it first -- the cast is lost and NO cooldown is charged
    /// (docs/Design/Spells.md, "Cast and interrupt").</summary>
    public struct AbilityCastState : IComponentData, IEnableableComponent
    {
        public int AbilityIndex;   // AbilityCatalog index
        public float CastRemaining; // seconds of cast time left (0 = apply now)
        public Entity Target;       // for SingleTarget/Area

        /// <summary>UnitAbilities slot being cast; its cooldown is charged on completion.</summary>
        public int Slot;
        /// <summary>The card's full cast time, for the cast bar's progress.</summary>
        public float CastTotal;

        // Order snapshot taken when the channel began. A move or attack order
        // that differs from it is a NEW order, and interrupts the cast.
        public byte HadMove;
        public Unity.Mathematics.float3 MoveDestination;
        public byte HadAttack;
        public Entity AttackTarget;

        /// <summary>0 at the start of the channel, 1 when it lands.</summary>
        public float Progress => CastTotal > 0f
            ? Unity.Mathematics.math.saturate(1f - CastRemaining / CastTotal) : 1f;
    }

    /// <summary>Scheduled aftermath: when Remaining hits 0, each aftermath ability
    /// of AbilityIndex's card is cast on this entity. Fires the Liquid Courage ->
    /// Veilshift Withdrawal + Life Cling and Automate Facility -> Under Automation
    /// chains.</summary>
    public struct AbilityAftermath : IComponentData, IEnableableComponent
    {
        public int AbilityIndex;
        public float Remaining;
        public Entity Target; // aftermath applies to Target if set, else self
    }

    // ==================== Effect / buff components ====================

    /// <summary>Self damage-over-time (Veilshift Withdrawal). Damages the owner's
    /// own Health. Ticked by AbilityEffectTickSystem.</summary>
    public struct SelfDoT : IComponentData, IEnableableComponent
    {
        public float Dps;
        public float TimeRemaining;
        public float FractionalAccumulator; // whole-HP accumulator (avoids per-frame rounding)
    }

    /// <summary>Life Cling — while present, the owner's HP is clamped so it never
    /// drops below Floor. Read at the damage-application sites via
    /// AbilityDamageHooks.</summary>
    public struct LifeCling : IComponentData, IEnableableComponent
    {
        public int Floor;
        public float TimeRemaining;
    }

    /// <summary>A cavalry unit currently charging (closed distance fast toward its
    /// target). Set/cleared by AbilityChargeSystem; read on-hit for charge bonus.</summary>
    public struct Charging : IComponentData, IEnableableComponent
    {
        public float TimeRemaining;
    }

    /// <summary>Flat bonus damage this unit adds on a charge hit (granted by
    /// King's Call to allied cavalry). Added/removed by AbilityAuraSystem.</summary>
    public struct ChargeDamageBonus : IComponentData, IEnableableComponent
    {
        public int Bonus;
        public float TimeRemaining; // refreshed by the King's Call aura; fades when out of range
    }

    /// <summary>The unit's own charge bonus, expressed as a percentage of final
    /// damage (Outrider 30, Cataphract 50, King Lexor 50). Stamped once by the
    /// unit factory — permanent, never ticked. Distinct from ChargeDamageBonus,
    /// which is the temporary FLAT bonus King's Call grants.</summary>
    public struct InnateChargePct : IComponentData
    {
        public float Pct;
    }

    /// <summary>War Horn: the next charge hit deals +Pct% damage. Consumed at the
    /// damage site (removed the moment it lands), or expires with the window.</summary>
    public struct NextChargePct : IComponentData, IEnableableComponent
    {
        public float Pct;
        public float TimeRemaining;
    }

    /// <summary>Full Gallop: the unit is sprinting and cannot attack while the
    /// speed burst lasts. Checked at the fire gates; ticked down by
    /// AbilityLifecycleSystem.</summary>
    public struct TempDisarm : IComponentData, IEnableableComponent
    {
        public float TimeRemaining;
    }

    // ==================== Building effects (Automate Facility) ====================

    /// <summary>Temporary resource-yield multiplier on an economy building
    /// (Automate Facility → +30% for 30s). Read by income systems; ticked by
    /// AbilityEffectTickSystem.</summary>
    public struct AutoYieldBoost : IComponentData, IEnableableComponent
    {
        public float Mult;          // e.g. 1.30
        public float TimeRemaining;
    }

    /// <summary>Lockout marker (Under Automation): the building cannot be
    /// re-automated while present. Ticked by AbilityEffectTickSystem.</summary>
    public struct UnderAutomation : IComponentData, IEnableableComponent
    {
        public float TimeRemaining;
    }

    // Fog reveal (Use Celestar) reuses the sect RevealCircle power via
    // SectActivePowerHelper.SpawnReveal.

    /// <summary>
    /// Ground point an Area ability was aimed at, stamped on the CASTER when
    /// the order is issued and consumed when the effects run.
    ///
    /// The ability pipeline's target is an Entity, which cannot express "that
    /// patch of empty map" — so an aimed area ability had nowhere to put the
    /// player's chosen point and Use Celestar simply revealed around the scout
    /// itself. This carries the point through the cast (Celestar has a 5 s cast
    /// time, so it has to survive until the effects fire).
    /// </summary>
    public struct AbilityAimPoint : IComponentData
    {
        public Unity.Mathematics.float3 Position;
    }

    // ==================== Markers ====================

    /// <summary>Marks a hero / one-per-player unique unit. Kind lets multiple
    /// unique lines coexist.</summary>
    public struct UniqueUnitTag : IComponentData
    {
        public int Kind; // e.g. UniqueUnitKind.KingLexor
    }

    public static class UniqueUnitKind
    {
        public const int KingLexor = 1;
    }

    /// <summary>Marks the Ledger automaton so its auto-cast AI can find eligible
    /// eco buildings.</summary>
    public struct LedgerTag : IComponentData { }

    /// <summary>The destination the Ledger's auto-cast roaming last sent it
    /// to. A live move order pointing anywhere ELSE is the player's, and the
    /// roaming leaves it alone (AbilityAuraSystem.TickLedgerAutoCast).</summary>
    public struct LedgerAutoGoal : IComponentData
    {
        /// <summary>The (walkable-snapped) destination the roaming wrote.</summary>
        public Unity.Mathematics.float3 Position;
        /// <summary>The building position it was heading for.</summary>
        public Unity.Mathematics.float3 Target;
    }

    /// <summary>Passive Scout-Sight owner — three-level LOS driven by
    /// AbilityAuraSystem.TickScoutSight: a small moving LOS, ramping to the
    /// authored BaseLos while standing still and unharmed; moving or taking
    /// damage resets the ramp back to the moving LOS.</summary>
    public struct ScoutSightState : IComponentData
    {
        public float BaseLos;
        public float CurrentBonus;
        public float LastX, LastZ; // last sampled position, for stillness detection
        public int LastHealth;     // last sampled HP — a drop resets the ramp
    }
}
