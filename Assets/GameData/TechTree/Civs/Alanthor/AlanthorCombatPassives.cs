// AlanthorCombatPassives.cs
// Components for the Alanthor tech-tree combat passives (Garrison Charge and
// Shield Wall, Archery Range Deploy Stakes, Siege Yard Ranging Shot and Siege
// Screens). Each is granted by its research tech and read at the single damage
// choke point in CombatDamageHelper.ApplyBonusDamageOnHit.
//
// They share one shape: a readiness flag armed by a condition (out of combat,
// or standing still) and spent by the first hit that qualifies. The arming is
// ticked by AlanthorCombatPassiveSystem; the spending happens at the damage
// site via ECB, exactly like the Ignite / VoidStrike one-shot charges.

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    /// <summary>
    /// Garrison "Charge" tech (and the Royal Stable's cavalry version).
    ///
    /// Three phases, not one. The unit ARMS after ChargeRearmSeconds with no
    /// target; arming alone does nothing. It ACTIVATES the moment it engages —
    /// gaining +ChargeSpeedPct% move speed for a ChargeWindowSeconds window —
    /// and it SPENDS the window on the first blow it lands, which deals +Pct%.
    /// A charge that closes on nothing expires: the speed comes off and the
    /// unit must spend another ChargeRearmSeconds out of combat to arm again.
    ///
    /// Ready alone used to be the whole model — armed forever once out of
    /// combat, with no speed and no window — so the tech read as a flat damage
    /// bonus on the opening blow rather than as a charge.
    /// </summary>
    public struct FirstStrike : IComponentData, IEnableableComponent
    {
        public float Pct;              // 30 = +30% on the opening blow
        public byte Ready;             // 1 = armed or mid-charge (see Window)
        public float OutOfCombatTimer; // seconds since this unit last dealt damage
        public float WindowRemaining;  // >0 = charging; the blow must land inside it
        public float SpeedBonus;       // move speed ADDED while charging, for exact removal
    }

    /// <summary>Garrison "Shield Wall" tech. While stationary, the first incoming
    /// attack is reduced by Pct%; rearms after StillRequired seconds standing.</summary>
    public struct ShieldWallState : IComponentData
    {
        public float Pct;        // 30 = the hit lands at 70%
        public byte Ready;
        public float StillTimer;
        public float LastX, LastZ; // last sampled position, for stillness detection
    }

    /// <summary>
    /// Archery Range "Deploy Stakes" tech. The first CHARGING cavalry attacker
    /// is reduced by Pct%, and ReflectPct% of the damage that still lands is
    /// paid straight back into the horse. It then needs RefreshSeconds to
    /// re-plant.
    ///
    /// The gate is a COOLDOWN, not stillness. Stakes that armed by standing
    /// still and disarmed on any movement meant a line that had just been
    /// repositioned — the exact moment cavalry arrives — was defenceless, and
    /// there was no refresh rule at all: one charge disarmed the archer until
    /// it stood again.
    /// </summary>
    public struct StakesState : IComponentData
    {
        public float Pct;               // 50 = the charge lands at half
        public byte Ready;              // 1 = stakes planted
        public float CooldownRemaining; // seconds until they re-plant
        public float ReflectPct;        // 50 = half of what lands is paid back
    }

    /// <summary>Siege Yard "Siege Screens" tech. Continuous while the engine is
    /// stationary: incoming RANGED damage is reduced by Pct%. Not a one-shot —
    /// Ready simply tracks whether the engine is currently planted.</summary>
    public struct SiegeScreens : IComponentData
    {
        public float Pct;        // 50
        public byte Ready;
        public float StillTimer;
        public float LastX, LastZ; // last sampled position, for stillness detection
    }

    /// <summary>Siege Yard "Ranging Shot" active. The next shot deals +Pct% damage.
    /// Applied by the ability cast (which requires the engine to have been
    /// stationary), consumed by the shot that lands.</summary>
    public struct NextShotBonus : IComponentData
    {
        public float Pct;              // 100 = double damage
        public float TimeRemaining;
    }

    /// <summary>Choreographed Volleys — faction-wide archer fire-rate buff. While
    /// present the unit's attack cooldown is divided by Mult.</summary>
    public struct VolleyBuff : IComponentData
    {
        public float Mult;             // 2 = double fire rate
        public float TimeRemaining;
    }

    /// <summary>Shared tuning for the passives above.</summary>
    public static class AlanthorPassiveTuning
    {
        public const float ChargeRearmSeconds = 5f;      // out of combat before it arms
        public const float ChargeWindowSeconds = 2f;     // to land the blow once activated
        public const float ChargeSpeedPct = 50f;         // move speed while charging
        public const float ShieldWallStillSeconds = 3f;  // stationary
        public const float StakesRefreshSeconds = 20f;   // between charges answered
        public const float StakesReflectPct = 50f;       // of what lands, paid back
        public const float SiegeScreensStillSeconds = 1f;
        public const float StillEpsilonSq = 0.01f;       // squared XZ movement tolerance
    }
}
