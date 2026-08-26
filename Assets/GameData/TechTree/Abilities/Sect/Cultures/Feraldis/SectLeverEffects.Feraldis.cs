// The Feraldis sects, authored against docs/Design/Sects.md (canon).
//
// Only the Sect of War is cut over so far — Ash, Ruin and Wrath still resolve
// through the legacy tier table in SectLeverEffects.cs and land with their own
// pass. The file is named for the cluster, not the sect, so those three drop in
// beside War without another dispatch hop.
//
// War's canon shape (2026-08-18) replaced the old War March / Bloodfury /
// Annihilation kit wholesale, so nothing here is a rename of the legacy table:
//
//   Blood Rain  — the only MAP-WIDE power in the game. It leaves a local blood
//                 pool (real Feraldis terrain: Frenzy food and legal War Totem
//                 ground) and simultaneously hastes EVERY unit on the map while
//                 silencing EVERY caster on the map, both sides included. It is
//                 a "turn this match into a pure weapons fight" button, not a
//                 party buff, and it silences War itself for the duration.
//   Call to Arms — military buildings train at half cost; Lv III doubles their
//                 training speed on top. The old "trains free" version made an
//                 unanswerable army out of one cast.
//   Bloodfury   — attack DAMAGE (not attack speed, which is Blood Rain's axis
//                 now), plus flat armor at Lv III.

namespace TheWaningBorder.Economy
{
    public static partial class SectLeverEffects
    {
        /// <summary>Flat armor Bloodfury III grants alongside its damage buff.
        /// Flat, not a percentage, to match every other armor number in
        /// docs/Design/Sects.md (Immovable's +5/+8, Steadfast Vigil's +3).</summary>
        public const float BloodfuryArmorLv3 = 5f;

        /// <summary>Training-speed multiplier Call to Arms III adds on top of
        /// its cost cut. 2 = double speed, i.e. half the training time.</summary>
        public const float CallToArmsSpeedLv3 = 2f;

        /// <summary>
        /// Canon actives for the Feraldis sects. Returns default — Kind = None —
        /// for the three that have not been cut over, which is the signal for
        /// ActiveOf to use the legacy tier table.
        /// </summary>
        internal static SectActivePowerSpec CanonActiveFeraldis(string sectId, int slot, int level)
        {
            switch (sectId)
            {
                case SectConfig.War: return War(slot, level);
                default: return default;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF WAR — the muster. Mass and momentum.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec War(int slot, int level)
        {
            switch (slot)
            {
                // Blood Rain — Reach sizes the blood POOL it leaves; the haste
                // and the silence are map-wide regardless. Magnitude is the
                // attack-speed multiplier every unit on the map receives.
                case 1:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.BloodRain, SectRadius.Small, "Blood Rain",
                                  "Blood falls, leaving a small pool. For 10s every unit on the map attacks 5% faster and no ability or sect power can be cast anywhere.",
                                  magnitude: 1.05f, duration: 10f, cooldown: 240f),
                        2 => Spec(SectActivePowerKind.BloodRain, SectRadius.Medium, "Blood Rain",
                                  "A medium pool. For 20s every unit on the map attacks 10% faster and no ability or sect power can be cast anywhere.",
                                  magnitude: 1.10f, duration: 20f, cooldown: 220f),
                        _ => Spec(SectActivePowerKind.BloodRain, SectRadius.Large, "Blood Rain",
                                  "A large pool. For 30s every unit on the map attacks 15% faster and no ability or sect power can be cast anywhere.",
                                  magnitude: 1.15f, duration: 30f, cooldown: 200f),
                    };

                // Call to Arms — Magnitude is the training-COST multiplier.
                // Lv III additionally doubles training speed (CallToArmsSpeedLv3),
                // read off the level at dispatch rather than carried on the spec.
                case 2:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.TrainingBoon, SectRadius.Single, "Call to Arms",
                                  "One military building trains units 50% cheaper for 15s.",
                                  magnitude: 0.5f, duration: 15f, cooldown: 150f),
                        2 => Spec(SectActivePowerKind.TrainingBoon, SectRadius.Small, "Call to Arms",
                                  "Military buildings in a small area train 50% cheaper for 30s.",
                                  magnitude: 0.5f, duration: 30f, cooldown: 135f),
                        _ => Spec(SectActivePowerKind.TrainingBoon, SectRadius.Medium, "Call to Arms",
                                  "Military buildings in a medium area train 50% cheaper AND at double speed for 30s.",
                                  magnitude: 0.5f, duration: 30f, cooldown: 120f),
                    };

                // Bloodfury — Magnitude is the outgoing-damage multiplier.
                // Lv III also grants BloodfuryArmorLv3 flat armor.
                default:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.DamageCircle, SectRadius.Small, "Bloodfury",
                                  "Allies in a small area deal +25% attack damage for 8s.",
                                  magnitude: 1.25f, duration: 8f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.DamageCircle, SectRadius.Medium, "Bloodfury",
                                  "Allies in a medium area deal +25% attack damage for 12s.",
                                  magnitude: 1.25f, duration: 12f, cooldown: 110f),
                        _ => Spec(SectActivePowerKind.DamageArmorCircle, SectRadius.Large, "Bloodfury",
                                  "Allies in a large area deal +25% attack damage and gain +5 armor for 12s.",
                                  magnitude: 1.25f, duration: 12f, cooldown: 100f),
                    };
            }
        }
    }
}
