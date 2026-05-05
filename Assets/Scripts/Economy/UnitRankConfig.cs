// UnitRankConfig.cs
// Static tuning for the Lv 1..5 unit veteran-rank system.
//
// Costs (per single unit promotion):
//   Lv 1 → Lv 2 :  50 supplies
//   Lv 2 → Lv 3 :  25 crystal
//   Lv 3 → Lv 4 :  15 veilsteel
//   Lv 4 → Lv 5 :   5 glow
//
// Stat scalars (multiplier from base):
//   Lv 1 : 1.00 / 1.00 / 1.00  (atk / def / LOS)
//   Lv 2 : 1.10 / 1.10 / 1.00
//   Lv 3 : 1.15 / 1.15 / 1.20
//   Lv 4 : 1.20 / 1.20 / 1.20  + Lv 4 HP regen
//   Lv 5 : 1.25 / 1.25 / 1.25  + Lv 5 push-back AOE on death + Glow Ability
//
// Audit fix #1.
//
// Location: Assets/Scripts/Economy/UnitRankConfig.cs

using TheWaningBorder.Core;

namespace TheWaningBorder.Economy
{
    public static class UnitRankConfig
    {
        public const byte MaxRank = 5;

        // Lv4 / Lv5 features
        public const float Lv4HpRegenPerSecond = 1f;
        public const int   Lv4DeathAoeDamage = 25;
        public const float Lv4DeathAoeRadius = 4f;

        public const int   Lv5DeathAoeDamage = 50;
        public const float Lv5DeathAoeRadius = 6f;
        public const float Lv5PushDistance   = 4f;

        public const float GlowAbilityActiveDuration = 6f;
        public const float GlowAbilityCooldown       = 60f;
        public const int   GlowAbilityRegenPerSec    = 5;

        public static Cost CostFor(byte targetRank) => targetRank switch
        {
            2 => Cost.Of(supplies: 50),
            3 => Cost.Of(crystal: 25),
            4 => Cost.Of(veilsteel: 15),
            5 => Cost.Of(glow: 5),
            _ => default,
        };

        public static float AttackMultiplierFor(byte rank) => rank switch
        {
            2 => 1.10f,
            3 => 1.15f,
            4 => 1.20f,
            5 => 1.25f,
            _ => 1.00f,
        };

        public static float DefenseMultiplierFor(byte rank) => AttackMultiplierFor(rank);

        public static float LineOfSightMultiplierFor(byte rank) => rank switch
        {
            3 => 1.20f,
            4 => 1.20f,
            5 => 1.25f,
            _ => 1.00f,
        };
    }
}
