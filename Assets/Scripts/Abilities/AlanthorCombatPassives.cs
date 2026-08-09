// AlanthorCombatPassives.cs
// Components for the Alanthor tech-tree combat passives (Garrison Charge and
// Shield Wall, Practice Range Deploy Stakes, Siege Yard Ranging Shot and Siege
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
    /// <summary>Garrison "Charge" tech. The unit's first strike deals +Pct% damage;
    /// rearms after StillRequired seconds without dealing damage.</summary>
    public struct FirstStrike : IComponentData
    {
        public float Pct;              // 30 = +30% on the opening blow
        public byte Ready;             // 1 = the next hit is the first strike
        public float OutOfCombatTimer; // seconds since this unit last dealt damage
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

    /// <summary>Practice Range "Deploy Stakes" tech. Once the archer has stood for
    /// StillRequired seconds, the first CHARGING cavalry attacker is reduced by
    /// Pct%. Moving disarms it.</summary>
    public struct StakesState : IComponentData
    {
        public float Pct;        // 50 = the charge lands at half
        public byte Ready;
        public float StillTimer;
        public float LastX, LastZ; // last sampled position, for stillness detection
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
        public const float ChargeRearmSeconds = 2f;      // out of combat
        public const float ShieldWallStillSeconds = 3f;  // stationary
        public const float StakesStillSeconds = 3f;      // stationary
        public const float SiegeScreensStillSeconds = 1f;
        public const float StillEpsilonSq = 0.01f;       // squared XZ movement tolerance
    }
}
