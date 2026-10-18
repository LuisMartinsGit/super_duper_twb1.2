// The four Alanthor sects' powers — docs/Design/Sects.md §4 (rewritten
// 2026-10-15). Slot is WHICH power: 1 = A (damage or heal), 2 = B (buff or
// debuff), 3 = ★ (the wildcard). Level is the chapel level (I / II / III,
// one higher under Concord). Every radius is one of the four in SectRadii;
// every damage and heal number comes off the SpellLadder (Spells.md §8).
//
// The non-Alanthor sects resolve through their own tables; ActiveOf falls
// through when CanonActive returns Kind = None.

namespace TheWaningBorder.Economy
{
    public static partial class SectLeverEffects
    {
        internal static SectActivePowerSpec CanonActiveAlanthor(string sectId, int slot, int level)
        {
            switch (sectId)
            {
                case SectConfig.Antiquity:  return Antiquity(slot, level);
                case SectConfig.Renewal:    return Renewal(slot, level);
                case SectConfig.Fortitude:  return Fortitude(slot, level);
                case SectConfig.Prosperity: return Prosperity(slot, level);
                default: return default;
            }
        }

        private static SectRadius Grow(int level)
            => level <= 1 ? SectRadius.Small : level == 2 ? SectRadius.Medium : SectRadius.Large;

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF ANTIQUITY — the Registry.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Antiquity(int slot, int level)
        {
            switch (slot)
            {
                // Trespass — full damage to enemies inside the caster's
                // territory, half outside. Reach grows, so the band holds.
                case 1:
                {
                    var reach = Grow(level);
                    float dmg = SpellLadder.Damage(reach, level);
                    return Spec(SectActivePowerKind.TrespassStrike, reach, "Trespass",
                        $"Enemies in a {SectRadii.Label(reach)} area take {dmg:0} damage inside your territory, {dmg * 0.5f:0} outside it.",
                        magnitude: dmg, cooldown: SpellLadder.SectDamageCooldown);
                }

                // Embargo — ALWAYS one building; the level buys time.
                case 2:
                {
                    float seconds = level <= 1 ? 20f : level == 2 ? 30f : 40f;
                    return Spec(SectActivePowerKind.BuildingShutdown, SectRadius.Single, "Embargo",
                        $"One enemy building stops training, research and income for {seconds:0}s. Queue progress is kept.",
                        duration: seconds, cooldown: SpellLadder.SectTacticalCooldown);
                }

                // Friendly Fire — units in the area turn on everyone around them.
                default:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.HostileConversion, SectRadius.Small, "Friendly Fire",
                                  "Enemy units in a small area go mad for 8s: their owner loses control and they attack everyone near them, their own side included.",
                                  duration: 8f, cooldown: SpellLadder.FriendlyFireCooldown),
                        2 => Spec(SectActivePowerKind.HostileConversion, SectRadius.Medium, "Friendly Fire",
                                  "Enemy units in a medium area go mad for 20s: their owner loses control and they attack everyone near them, their own side included.",
                                  duration: 20f, cooldown: SpellLadder.FriendlyFireCooldown),
                        _ => Spec(SectActivePowerKind.HostileConversion, SectRadius.Large, "Friendly Fire",
                                  "Enemy units in a large area go mad until killed: their owner loses control and they attack everyone near them, their own side included.",
                                  duration: Permanent, cooldown: SpellLadder.FriendlyFireCooldown),
                    };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF RENEWAL — the Hospices.
        // ═══════════════════════════════════════════════════════════════════
        private static SectActivePowerSpec Renewal(int slot, int level)
        {
            switch (slot)
            {
                // Heal — a fraction of max HP, units and buildings.
                case 1:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.HealCirclePercent, SectRadius.Small, "Heal",
                                  "Restore 30% of max HP to your units and buildings in a small area.",
                                  magnitude: 0.30f, cooldown: SpellLadder.SectTacticalCooldown),
                        2 => Spec(SectActivePowerKind.HealCirclePercent, SectRadius.Medium, "Heal",
                                  "Restore 40% of max HP in a medium area.",
                                  magnitude: 0.40f, cooldown: SpellLadder.SectTacticalCooldown),
                        _ => Spec(SectActivePowerKind.HealCirclePercent, SectRadius.Medium, "Heal",
                                  "Restore 60% of max HP in a medium area.",
                                  magnitude: 0.60f, cooldown: SpellLadder.SectTacticalCooldown),
                    };

                // Lifeline — cannot drop below 1 HP (Magnitude = heal on expiry).
                case 2:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.DeathWard, SectRadius.Small, "Lifeline",
                                  "Your units in a small area cannot drop below 1 HP for 6s.",
                                  duration: 6f, cooldown: SpellLadder.SectTacticalCooldown),
                        2 => Spec(SectActivePowerKind.DeathWard, SectRadius.Small, "Lifeline",
                                  "Your units in a small area cannot drop below 1 HP for 12s.",
                                  duration: 12f, cooldown: SpellLadder.SectTacticalCooldown),
                        _ => Spec(SectActivePowerKind.DeathWard, SectRadius.Medium, "Lifeline",
                                  "Medium area, 12s; survivors heal 25% when it ends.",
                                  magnitude: 0.25f, duration: 12f, cooldown: SpellLadder.SectTacticalCooldown),
                    };

                // Revive — the recently fallen return weakened.
                default:
                {
                    var reach = Grow(level);
                    float frac = level <= 1 ? 0.30f : level == 2 ? 0.40f : 0.50f;
                    float window = level <= 1 ? 10f : level == 2 ? 15f : 20f;
                    float most = level <= 1 ? 5f : level == 2 ? 10f : 15f;
                    return Spec(SectActivePowerKind.Revive, reach, "Revive",
                        $"Your units that died in a {SectRadii.Label(reach)} area in the last {window:0}s return at {frac * 100f:0}% HP, at most {most:0}. Heroes excluded.",
                        magnitude: frac, duration: window, secondary: most,
                        cooldown: SpellLadder.SectWildcardCooldown);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF FORTITUDE — the Wardens.
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Ward Discharge: how long an unspent charge waits.</summary>
        public const float WardChargeLifetime = 300f;

        /// <summary>Ward Discharge: how far from the cast point the armed
        /// structure may stand (the pick, not the trigger).</summary>
        public const float WardPickRadius = 8f;

        private static SectActivePowerSpec Fortitude(int slot, int level)
        {
            switch (slot)
            {
                // Ward Discharge — Radius is the TRIGGER range of the armed
                // structure; each pulse is the Small band.
                case 1:
                {
                    var reach = Grow(level);
                    float pulses = level <= 1 ? 1f : level == 2 ? 4f : 8f;
                    float interval = level <= 1 ? 3f : level == 2 ? 2f : 1f;
                    float dmg = SpellLadder.Damage(SectRadius.Small, 1);
                    return Spec(SectActivePowerKind.WardCharge, reach, "Ward Discharge",
                        $"Arm one of your structures for 5 min. When an enemy comes within {SectRadii.Metres(reach):0} m it fires {pulses:0} pulse(s), {interval:0}s apart, each {dmg:0} damage to every enemy in range.",
                        magnitude: dmg, duration: interval, secondary: pulses,
                        cooldown: SpellLadder.DeployableCooldown);
                }

                // Emergency Repairs — Magnitude is the bonus HP fraction; III reflects.
                case 2:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.BuildingHpBuff, SectRadius.Single, "Emergency Repairs",
                                  "One building gains +100% HP for 15s.",
                                  magnitude: 1.0f, duration: 15f, cooldown: SpellLadder.SectTacticalCooldown),
                        2 => Spec(SectActivePowerKind.BuildingHpBuff, SectRadius.Small, "Emergency Repairs",
                                  "Your buildings in a small area gain +100% HP for 15s.",
                                  magnitude: 1.0f, duration: 15f, cooldown: SpellLadder.SectTacticalCooldown),
                        _ => Spec(SectActivePowerKind.BuildingHpBuff, SectRadius.Medium, "Emergency Repairs",
                                  "Your buildings in a medium area gain +100% HP for 15s and reflect 20% of melee damage.",
                                  magnitude: 1.0f, duration: 15f, cooldown: SpellLadder.SectTacticalCooldown),
                    };

                // Fortify — a permanent, complete structure. Magnitude picks
                // Tower / Fortification / Fortress; the cooldown climbs
                // steeply with the level so a Fortress cannot be spammed.
                default:
                    return level switch
                    {
                        1 => Spec(SectActivePowerKind.RaiseTower, SectRadius.Single, "Fortify",
                                  "Raise a permanent, complete Tower.",
                                  magnitude: 1f, duration: Permanent, cooldown: 120f),
                        2 => Spec(SectActivePowerKind.RaiseTower, SectRadius.Single, "Fortify",
                                  "Raise a permanent, complete Fortification.",
                                  magnitude: 2f, duration: Permanent, cooldown: 300f),
                        _ => Spec(SectActivePowerKind.RaiseTower, SectRadius.Single, "Fortify",
                                  "Raise a permanent, complete Fortress.",
                                  magnitude: 3f, duration: Permanent, cooldown: 600f),
                    };
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT OF PROSPERITY — the Guild.
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Bounty: how long after the strike a death still pays.</summary>
        public const float BountyWindowSeconds = 10f;

        private static SectActivePowerSpec Prosperity(int slot, int level)
        {
            switch (slot)
            {
                // Bounty — one unit; a fixed reach hits harder each level.
                case 1:
                {
                    float dmg = SpellLadder.Damage(SectRadius.Single, level);
                    return Spec(SectActivePowerKind.Bounty, SectRadius.Single, "Bounty",
                        $"One enemy unit takes {dmg:0} damage. If it dies within {BountyWindowSeconds:0}s you are paid its cost.",
                        magnitude: dmg, duration: BountyWindowSeconds, cooldown: SpellLadder.SectDamageCooldown);
                }

                // Overtime — one of your buildings works faster.
                case 2:
                {
                    float mult = level <= 1 ? 1.5f : level == 2 ? 1.75f : 2f;
                    return Spec(SectActivePowerKind.Overtime, SectRadius.Single, "Overtime",
                        $"One of your buildings trains, researches and produces {(mult - 1f) * 100f:0}% faster for 20s.",
                        magnitude: mult, duration: 20f, cooldown: SpellLadder.SectTacticalCooldown);
                }

                // Plenty — a lump of income; no target.
                default:
                {
                    float seconds = level <= 1 ? 60f : level == 2 ? 90f : 120f;
                    return Spec(SectActivePowerKind.Plenty, SectRadius.Single, "Plenty",
                        $"Instantly gain {seconds:0}s of your current income.",
                        duration: seconds, cooldown: SpellLadder.DeployableCooldown);
                }
            }
        }

        // ── Retired with the Sect of Reclamation (2026-10-15) ─────────────
        // Harvest the Veil is no longer cast. Its ticker in
        // AlanthorSectEffectSystem still reads these for any NodeOverYield
        // left in an old world; delete both together.
        public static void HarvestYield(int level, out int supplies, out int iron,
                                        out int veilstone, out int veilsteel)
        {
            supplies = 0; iron = 0; veilstone = 0; veilsteel = 0;
        }

        public const float HarvestTickSeconds = 5f;
    }
}
