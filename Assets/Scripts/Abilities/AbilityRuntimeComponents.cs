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
    /// when CastRemaining hits 0 the effects are applied and it is removed.</summary>
    public struct AbilityCastState : IComponentData
    {
        public int AbilityIndex;   // AbilityCatalog index
        public float CastRemaining; // seconds of cast time left (0 = apply now)
        public Entity Target;       // for SingleTarget/Area
    }

    /// <summary>Scheduled aftermath: when Remaining hits 0, each aftermath ability
    /// of AbilityIndex's card is cast on this entity. Fires the Liquid Courage ->
    /// Veilshift Withdrawal + Life Cling and Automate Facility -> Under Automation
    /// chains.</summary>
    public struct AbilityAftermath : IComponentData
    {
        public int AbilityIndex;
        public float Remaining;
        public Entity Target; // aftermath applies to Target if set, else self
    }

    // ==================== Effect / buff components ====================

    /// <summary>Self damage-over-time (Veilshift Withdrawal). Damages the owner's
    /// own Health. Ticked by AbilityEffectTickSystem.</summary>
    public struct SelfDoT : IComponentData
    {
        public float Dps;
        public float TimeRemaining;
        public float FractionalAccumulator; // whole-HP accumulator (avoids per-frame rounding)
    }

    /// <summary>Life Cling — while present, the owner's HP is clamped so it never
    /// drops below Floor. Read at the damage-application sites via
    /// AbilityDamageHooks.</summary>
    public struct LifeCling : IComponentData
    {
        public int Floor;
        public float TimeRemaining;
    }

    /// <summary>A cavalry unit currently charging (closed distance fast toward its
    /// target). Set/cleared by AbilityChargeSystem; read on-hit for charge bonus.</summary>
    public struct Charging : IComponentData
    {
        public float TimeRemaining;
    }

    /// <summary>Flat bonus damage this unit adds on a charge hit (granted by
    /// King's Call to allied cavalry). Added/removed by AbilityAuraSystem.</summary>
    public struct ChargeDamageBonus : IComponentData
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
    public struct NextChargePct : IComponentData
    {
        public float Pct;
        public float TimeRemaining;
    }

    /// <summary>Full Gallop: the unit is sprinting and cannot attack while the
    /// speed burst lasts. Checked at the fire gates; ticked down by
    /// AbilityLifecycleSystem.</summary>
    public struct TempDisarm : IComponentData
    {
        public float TimeRemaining;
    }

    // ==================== Building effects (Automate Facility) ====================

    /// <summary>Temporary resource-yield multiplier on an economy building
    /// (Automate Facility → +30% for 30s). Read by income systems; ticked by
    /// AbilityEffectTickSystem.</summary>
    public struct AutoYieldBoost : IComponentData
    {
        public float Mult;          // e.g. 1.30
        public float TimeRemaining;
    }

    /// <summary>Lockout marker (Under Automation): the building cannot be
    /// re-automated while present. Ticked by AbilityEffectTickSystem.</summary>
    public struct UnderAutomation : IComponentData
    {
        public float TimeRemaining;
    }

    // Fog reveal (Use Celestar) reuses the sect RevealCircle power via
    // SectActivePowerHelper.SpawnReveal — no dedicated component needed.

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
