// File: Assets/GameData/TechTree/Abilities/Sect/SectLeverEffects.Alanthor.cs
// The four Alanthor sects, authored against docs/Design/Sects.md (canon).
//
// This file is the first cut-over to the canon shape. Two things change from
// the legacy table in SectLeverEffects.cs:
//
//   1. A sect has THREE named actives, not one power at three tiers. The
//      second argument is therefore a SLOT (which of the three), and the
//      third is a LEVEL (I / II / III, earned by adopting before a Temple
//      upgrade — see docs/Design/Sects.md section 3).
//
//   2. Every radius is one of the four in SectRadii. No bespoke metres.
//
// The remaining eight sects still resolve through the legacy table until
// their own pass lands; ActiveOf falls through to it when CanonActive
// returns Kind = None.

namespace TheWaningBorder.Economy
{
    public static partial class SectLeverEffects
    {
        /// <summary>
        /// Canon actives for the four Alanthor sects. Returns default —
        /// Kind = None — for every other sect, which is the signal for
        /// <see cref="CanonActive"/> to try the next cluster's table.
        /// </summary>
        internal static SectActivePowerSpec CanonActiveAlanthor(string sectId, int slot, int level)
        {
            switch (sectId)
            {
                case SectConfig.Antiquity:   return Antiquity(slot, level);
                case SectConfig.Renewal:     return Renewal(slot, level);
                case SectConfig.Fortitude:   return Fortitude(slot, level);
                case SectConfig.Reclamation: return Reclamation(slot, level);
                default: return default;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF ANTIQUITY — the holy librarians. Intel and enemy shutdown.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Antiquity(int slot, int level)
        {
            switch (slot)
            {
                case 1: // Scour the Registry — reveal.
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.RevealCircle, SectRadius.Medium, "Scour the Registry",
                                  "Reveal a medium area for 15s.", duration: 15f, cooldown: 75f),
                        2 => Spec(SectActivePowerKind.RevealCircle, SectRadius.Large, "Scour the Registry",
                                  "Reveal a large area for 15s.", duration: 15f, cooldown: 70f),
                        _ => Spec(SectActivePowerKind.RevealCircle, SectRadius.Large, "Scour the Registry",
                                  "Reveal a large area for 35s.", duration: 35f, cooldown: 60f),
                    };

                case 2: // Heavy Bureaucracy — building shutdown.
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.BuildingShutdown, SectRadius.Single, "Heavy Bureaucracy",
                                  "One building stops training, research and resource output for 30s.",
                                  duration: 30f, cooldown: 150f),
                        2 => Spec(SectActivePowerKind.BuildingShutdown, SectRadius.Small, "Heavy Bureaucracy",
                                  "All buildings in a small area stop for 30s.", duration: 30f, cooldown: 135f),
                        _ => Spec(SectActivePowerKind.BuildingShutdown, SectRadius.Large, "Heavy Bureaucracy",
                                  "All buildings in a large area stop for 30s.", duration: 30f, cooldown: 120f),
                    };

                default: // Sew Disorder — turn units hostile to everything.
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.HostileConversion, SectRadius.Small, "Sew Disorder",
                                  "Units in a small area turn hostile to all other units for 8s.",
                                  duration: 8f, cooldown: 300f),
                        2 => Spec(SectActivePowerKind.HostileConversion, SectRadius.Medium, "Sew Disorder",
                                  "Units in a medium area turn hostile for 20s.", duration: 20f, cooldown: 270f),
                        _ => Spec(SectActivePowerKind.HostileConversion, SectRadius.Large, "Sew Disorder",
                                  "Units in a large area turn hostile until killed.",
                                  duration: Permanent, cooldown: 240f),
                    };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF RENEWAL — the menders. Repair and sustain.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Renewal(int slot, int level)
        {
            switch (slot)
            {
                case 1: // Hands of Plenty — Magnitude is a FRACTION of max HP.
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.HealCirclePercent, SectRadius.Small, "Hands of Plenty",
                                  "Restore 30% HP to units and buildings in a small area.",
                                  magnitude: 0.30f, cooldown: 90f),
                        2 => Spec(SectActivePowerKind.HealCirclePercent, SectRadius.Medium, "Hands of Plenty",
                                  "Restore 50% HP in a medium area.", magnitude: 0.50f, cooldown: 85f),
                        // Duration is the regen tail: the burst lands, then healing
                        // continues for 10s. 80%, not 100% — a full heal made every
                        // other Renewal power redundant.
                        _ => Spec(SectActivePowerKind.HealCirclePercent, SectRadius.Medium, "Hands of Plenty",
                                  "Restore 80% HP in a medium area, and healing continues for 10s.",
                                  magnitude: 0.80f, duration: 10f, cooldown: 80f),
                    };

                case 2: // Raise Anew — Magnitude is the tower LEVEL, Duration its lifetime.
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.RaiseTower, SectRadius.Single, "Raise Anew",
                                  "Raise one free Lv 1 Watch Tower. It crumbles after 30s.",
                                  magnitude: 1f, duration: 30f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.RaiseTower, SectRadius.Small, "Raise Anew",
                                  "Raise Lv 2 Watch Towers across a small area. They crumble after 60s.",
                                  magnitude: 2f, duration: 60f, cooldown: 150f),
                        // III returns to Single Target on purpose: a permanent free
                        // Lv 3 tower is the payoff, and several of them at once would
                        // out-value every other level-III power in the game.
                        _ => Spec(SectActivePowerKind.RaiseTower, SectRadius.Single, "Raise Anew",
                                  "Raise a permanent Lv 3 Watch Tower. It stays until destroyed.",
                                  magnitude: 3f, duration: Permanent, cooldown: 180f),
                    };

                default: // Second Wind — Magnitude is the heal-on-expiry fraction.
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.DeathWard, SectRadius.Small, "Second Wind",
                                  "Units in a small area cannot drop below 1 HP for 6s.",
                                  duration: 6f, cooldown: 150f),
                        2 => Spec(SectActivePowerKind.DeathWard, SectRadius.Small, "Second Wind",
                                  "Units in a small area cannot drop below 1 HP for 12s.",
                                  duration: 12f, cooldown: 140f),
                        _ => Spec(SectActivePowerKind.DeathWard, SectRadius.Medium, "Second Wind",
                                  "Medium area, 12s; survivors heal 25% when it ends.",
                                  magnitude: 0.25f, duration: 12f, cooldown: 130f),
                    };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF FORTITUDE — the wall-keepers. Static defense.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Fortitude(int slot, int level)
        {
            switch (slot)
            {
                // Stoneveil — veiled units MOVE (faster, in fact) but are invisible,
                // untargetable and cannot interact with anything. Sect powers still
                // reach them. Magnitude carries the post-veil damage bonus at Lv III.
                case 1:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.Veil, SectRadius.Small, "Stoneveil",
                                  "Veil a small area for 8s: invisible, untargetable, faster, but unable to act.",
                                  duration: 8f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.Veil, SectRadius.Small, "Stoneveil",
                                  "Veil a small area for 15s.", duration: 15f, cooldown: 110f),
                        _ => Spec(SectActivePowerKind.Veil, SectRadius.Medium, "Stoneveil",
                                  "Veil a medium area for 15s; on expiry they gain +25% damage for 10s.",
                                  magnitude: 0.25f, duration: 15f, cooldown: 100f),
                    };

                // Bulwark — Magnitude is the bonus HP fraction; Lv III adds reflect.
                case 2:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.BuildingHpBuff, SectRadius.Single, "Bulwark",
                                  "One building gains +100% HP for 30s.",
                                  magnitude: 1.0f, duration: 30f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.BuildingHpBuff, SectRadius.Small, "Bulwark",
                                  "Buildings in a small area gain +100% HP for 30s.",
                                  magnitude: 1.0f, duration: 30f, cooldown: 110f),
                        _ => Spec(SectActivePowerKind.BuildingHpBuff, SectRadius.Medium, "Bulwark",
                                  "Buildings in a medium area gain +100% HP for 30s and reflect 20% of melee damage.",
                                  magnitude: 1.0f, duration: 30f, cooldown: 100f),
                    };

                // Immovable — flat armor, then outright invulnerability. Replaces the
                // earlier crowd-control version: the game has no pushback system for
                // it to negate.
                default:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.ArmorCircle, SectRadius.Small, "Immovable",
                                  "Units in a small area gain +5 armor for 10s.",
                                  magnitude: 5f, duration: 10f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.ArmorCircle, SectRadius.Medium, "Immovable",
                                  "Units in a medium area gain +8 armor for 15s.",
                                  magnitude: 8f, duration: 15f, cooldown: 130f),
                        // Balance flag (docs/Design/Sects.md): a 25m army-wide 20s
                        // invulnerability is the strongest defensive effect in the
                        // game. On-theme, but the first number to revisit if
                        // Fortitude dominates — hence the long cooldown.
                        _ => Spec(SectActivePowerKind.Invulnerable, SectRadius.Large, "Immovable",
                                  "Units in a large area become invulnerable for 20s.",
                                  duration: 20f, cooldown: 240f),
                    };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF RECLAMATION — the curse-harvesters. Curse exploitation.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Reclamation(int slot, int level)
        {
            switch (slot)
            {
                // Harvest the Veil — always single-target on a resource node; the
                // escalation is entirely in what comes out of it. Magnitude carries
                // Supplies per tick; the other three resources are read off the
                // level at dispatch (see HarvestYield).
                case 1:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.NodeOverYield, SectRadius.Single, "Harvest the Veil",
                                  "Target a resource node: 50 Supplies every 5s for 30s (300 total).",
                                  magnitude: 50f, duration: 30f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.NodeOverYield, SectRadius.Single, "Harvest the Veil",
                                  "Target a resource node: 75 Supplies + 20 Iron every 5s for 30s.",
                                  magnitude: 75f, duration: 30f, cooldown: 120f),
                        _ => Spec(SectActivePowerKind.NodeOverYield, SectRadius.Single, "Harvest the Veil",
                                  "Target a resource node: 150 Supplies + 60 Iron + 35 Veilstone + 5 Veilsteel every 5s for 30s.",
                                  magnitude: 150f, duration: 30f, cooldown: 120f),
                    };

                // Cleanse — drives the existing influence map rather than inventing a
                // suppression system, so it pushes the curse back and claims ground
                // in one motion. Magnitude is the per-second influence deposit.
                case 2:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.InfluenceBurst, SectRadius.Small, "Cleanse",
                                  "Pump heavy player influence into a small area for 20s.",
                                  magnitude: 6f, duration: 20f, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.InfluenceBurst, SectRadius.Medium, "Cleanse",
                                  "Pump heavy player influence into a medium area for 40s.",
                                  magnitude: 6f, duration: 40f, cooldown: 120f),
                        _ => Spec(SectActivePowerKind.InfluenceBurst, SectRadius.Large, "Cleanse",
                                  "Pump heavy player influence into a large area for 40s; allies inside regenerate.",
                                  magnitude: 8f, duration: 40f, cooldown: 120f),
                    };

                // Veil-Touched — Magnitude is the cursed-ground speed bonus at Lv III.
                default:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.CurseWard, SectRadius.Small, "Veil-Touched",
                                  "Units in a small area take no curse damage for 15s.",
                                  duration: 15f, cooldown: 100f),
                        2 => Spec(SectActivePowerKind.CurseWard, SectRadius.Medium, "Veil-Touched",
                                  "Units in a medium area take no curse damage for 30s.",
                                  duration: 30f, cooldown: 100f),
                        _ => Spec(SectActivePowerKind.CurseWard, SectRadius.Large, "Veil-Touched",
                                  "Large area, 30s, and they move 20% faster on cursed ground.",
                                  magnitude: 0.20f, duration: 30f, cooldown: 100f),
                    };
            }
        }

        /// <summary>
        /// Per-tick yield for Harvest the Veil, by power level. Supplies also
        /// live on Spec.Magnitude; this is the full basket so the dispatcher
        /// does not have to carry four magnitudes on the spec struct.
        /// </summary>
        public static void HarvestYield(int level, out int supplies, out int iron,
                                        out int veilstone, out int veilsteel)
        {
            switch (level)
            {
                case 1:  supplies = 50;  iron = 0;  veilstone = 0;  veilsteel = 0; break;
                case 2:  supplies = 75;  iron = 20; veilstone = 0;  veilsteel = 0; break;
                default: supplies = 150; iron = 60; veilstone = 35; veilsteel = 5; break;
            }
        }

        /// <summary>Harvest the Veil pays out on this cadence for its duration.</summary>
        public const float HarvestTickSeconds = 5f;
    }
}
