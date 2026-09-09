// The Runai sects, authored against docs/Design/Sects.md (canon).
//
// Only the Sect of Witness is cut over so far — Silence, Justice and
// Veneration still resolve through the legacy tier table in
// SectLeverEffects.cs and land with their own pass. The file is named for the
// CLUSTER, not the sect, so those three drop in beside Witness without another
// dispatch hop, exactly as War sits alone in the Feraldis file today.
//
// Witness's canon shape (2026-09-08) is not a rename of the legacy table. The
// old kit was three reveals in a row — All-Seeing Gaze, Foresight and
// Watcher's Mark all lit up an area and did nothing else — so the sect paid
// three slots for one verb and could not touch an enemy at any level. What
// replaces it:
//
//   Spy Network    — the ENGINE. One enemy unit becomes an unwitting eye and
//                    the network spreads on its own through the enemy's own
//                    movement. Seeded into a marching army it can quietly
//                    become total vision; seeded into a lone scout it dies
//                    with the scout. The information is earned by the
//                    opponent's behaviour rather than bought with a cast.
//   Blinding Glare — the counterpart-B debuff. Vision is what this sect
//                    trades in, so taking it away is its natural debuff.
//   Nowhere to Hide— the WILDCARD, and the reason Spy Network is worth
//                    planting: it hits every enemy you can currently SEE,
//                    anywhere on the map. A Witness player who has done
//                    nothing gets a tickle; one whose network has cascaded
//                    through a whole army deletes it from across the map. It
//                    is a multiplier on preparation, which is what makes it a
//                    pivot rather than a damage spell.

namespace TheWaningBorder.Economy
{
    public static partial class SectLeverEffects
    {
        /// <summary>
        /// Canon actives for the Runai sects. Returns default — Kind = None —
        /// for the three that have not been cut over, which is the signal for
        /// ActiveOf to use the legacy tier table.
        /// </summary>
        internal static SectActivePowerSpec CanonActiveRunai(string sectId, int slot, int level)
        {
            switch (sectId)
            {
                case SectConfig.Witness: return Witness(slot, level);
                default: return default;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF WITNESS — the open eye. Information, and what it is worth.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Witness(int slot, int level)
        {
            switch (slot)
            {
                // Spy Network — Single Target: the cast picks ONE enemy unit.
                // Duration is how long a spy lasts (Permanent at III),
                // Magnitude the seconds of proximity the cascade needs, and
                // Secondary the radius that proximity is measured in.
                case 1:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.SpyNetwork, SectRadius.Single, "Spy Network",
                                  "One enemy unit becomes your spy for 45s. Enemies that spend 3s near a spy become spies too.",
                                  magnitude: 3f, duration: 45f, cooldown: 120f, secondary: SectRadii.Small),
                        2 => Spec(SectActivePowerKind.SpyNetwork, SectRadius.Single, "Spy Network",
                                  "One enemy unit becomes your spy for 90s, and the cascade reaches further.",
                                  magnitude: 3f, duration: 90f, cooldown: 105f, secondary: SectRadii.Medium),
                        // III is the level the network stops decaying: spies
                        // last until they die, so a cascade that gets going
                        // never unwinds on its own.
                        _ => Spec(SectActivePowerKind.SpyNetwork, SectRadius.Single, "Spy Network",
                                  "One enemy unit spies for you until it dies, and the cascade takes only 1.5s.",
                                  magnitude: 1.5f, duration: Permanent, cooldown: 90f, secondary: SectRadii.Medium),
                    };

                // Blinding Glare — Magnitude >= 1 also locks abilities (Lv III).
                case 2:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.Blind, SectRadius.Small, "Blinding Glare",
                                  "Enemies in a small area lose all vision for 8s.",
                                  duration: 8f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.Blind, SectRadius.Medium, "Blinding Glare",
                                  "Enemies in a medium area lose all vision for 12s.",
                                  duration: 12f, cooldown: 110f),
                        _ => Spec(SectActivePowerKind.Blind, SectRadius.Large, "Blinding Glare",
                                  "Enemies in a large area lose all vision for 12s and cannot use abilities.",
                                  magnitude: 1f, duration: 12f, cooldown: 100f),
                    };

                // Nowhere to Hide — MAP-WIDE. Reach is nominal here, the same
                // concession Blood Rain makes: the power's true reach is
                // however much of the enemy army you have managed to reveal,
                // which is not expressible as a radius. Magnitude is the
                // damage each revealed enemy takes; Secondary >= 1 lets it
                // reach revealed BUILDINGS as well.
                default:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.RevealedStrike, SectRadius.Large, "Nowhere to Hide",
                                  "Every enemy unit you can see takes 60 damage, anywhere on the map.",
                                  magnitude: 60f, cooldown: 300f),
                        2 => Spec(SectActivePowerKind.RevealedStrike, SectRadius.Large, "Nowhere to Hide",
                                  "Every revealed enemy takes 110 damage, buildings included.",
                                  magnitude: 110f, cooldown: 270f, secondary: 1f),
                        _ => Spec(SectActivePowerKind.RevealedStrike, SectRadius.Large, "Nowhere to Hide",
                                  "Every revealed enemy takes 200 damage, buildings included — enough to finish a wounded army.",
                                  magnitude: 200f, cooldown: 240f, secondary: 1f),
                    };
            }
        }

        /// <summary>
        /// The cascade rule a LIVE spy runs on, read back from the spec that
        /// created it. The spy carries its level rather than its numbers, so a
        /// balance edit to the table above reaches spies already on the map —
        /// and there is exactly one place these numbers live.
        /// </summary>
        public static void WitnessCascade(byte level, out float seconds, out float radius)
        {
            var spec = Witness(1, level);
            seconds = spec.Magnitude;
            radius  = spec.Secondary;
        }
    }
}
