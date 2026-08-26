// CombatDamageHelper.cs
// Shared on-hit pipeline used by MeleeCombatSystem and RangedCombatSystem.
// Location: Assets/Scripts/Systems/Combat/CombatDamageHelper.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Fix #226: central place for the on-hit effect pipeline so Melee and
    /// Ranged combat systems don't have to maintain their own 80-line copies
    /// of the same logic.
    ///
    /// Covers the pieces that were 100% duplicated between the two systems:
    ///   - Condemned mark bonus damage
    ///   - IgniteBuff consumption and bonus damage
    ///   - VoidStrikeBuff consumption and bonus damage
    ///   - SpellBuff.DamageMultiplier on attacker
    ///   - DamageReflect from target SpellBuff
    ///   - LastDamagedByFaction and LastAttackerEntity tracking
    ///   - Sect panic/control chance debuff application
    ///
    /// The main damage calculation (damage-type x armor-type matrix, defense,
    /// height modifier, sect multipliers) is intentionally NOT extracted
    /// because it is deeply coupled with the caller's local cooldown and
    /// sect-multiplier state. Callers should fold <see cref="GetSpellBuffArmorBonus"/>
    /// into the defense value BEFORE invoking the matrix calculation.
    /// </summary>
    public static class CombatDamageHelper
    {
        /// <summary>
        /// Returns extra armor on the target — sums SpellBuff.ArmorBonus and
        /// SilenceVigilArmor.Bonus. Callers MUST add this to the defender's
        /// defense value before running the damage-type x armor-type matrix.
        /// Wired into Melee/Ranged combat so abilities like StoneheartBastion's
        /// +3 armor aura and Silence's "Steadfast Vigil" stance bonus actually
        /// fire. (task-062 C-1, task-063 phase 2e)
        /// </summary>
        public static int GetSpellBuffArmorBonus(EntityManager em, Entity target)
        {
            int bonus = 0;
            if (em.HasComponent<SpellBuff>(target))
                bonus += (int)em.GetComponentData<SpellBuff>(target).ArmorBonus;
            if (em.HasComponent<SilenceVigilArmor>(target))
                bonus += em.GetComponentData<SilenceVigilArmor>(target).Bonus;
            return bonus;
        }

        /// <summary>
        /// Feraldis fire-and-blood attack multiplier for one attacker
        /// (docs/Design/Age_1_Feraldis.md). Two independent sources, applied
        /// multiplicatively:
        ///   - <see cref="BloodFrenzy"/>: the culture signature — any Feraldis
        ///     unit fighting on bloodsoaked ground. Stamped by BloodFrenzySystem.
        ///   - <see cref="DeathFrenzyState"/>: the Berserker's last stand.
        /// Returns 1.0 for everyone else, so non-Feraldis combat is untouched.
        /// </summary>
        public static float GetFrenzyDamageMult(EntityManager em, Entity attacker)
        {
            float mult = 1f;
            if (em.HasComponent<BloodFrenzy>(attacker))
                mult *= TheWaningBorder.Core.Config.FeraldisConstants.FrenzyDamageMult;
            if (em.HasComponent<DeathFrenzyState>(attacker))
                mult *= TheWaningBorder.Core.Config.FeraldisConstants.DeathFrenzyDamageMult;
            return mult;
        }

        /// <summary>
        /// Attack-cooldown multiplier from Feraldis blood frenzy (&lt; 1 means
        /// swings come faster). Death Frenzy deliberately does NOT shorten the
        /// cooldown — its bonus is raw damage and speed, so the two frenzies
        /// read differently in play.
        /// </summary>
        public static float GetFrenzyCooldownMult(EntityManager em, Entity attacker)
        {
            return em.HasComponent<BloodFrenzy>(attacker)
                ? TheWaningBorder.Core.Config.FeraldisConstants.FrenzyCooldownMult
                : 1f;
        }

        /// <summary>
        /// Attack-cooldown multiplier from timed haste (SectHaste, i.e. Blood
        /// Rain). Returns 1 when the unit is not hasted, so callers can multiply
        /// it in unconditionally.
        ///
        /// SectHaste.Multiplier is a SPEED multiplier, so the cooldown is
        /// DIVIDED by it: +15% attack speed means the unit swings on 1/1.15 of
        /// its normal cooldown. A zero multiplier means the component was
        /// stamped without one — that is "no effect", not "infinite cooldown",
        /// hence the &lt;= 0 guard.
        /// </summary>
        public static float GetHasteCooldownMult(EntityManager em, Entity attacker)
        {
            if (!em.HasComponent<SectHaste>(attacker)) return 1f;
            var haste = em.GetComponentData<SectHaste>(attacker);
            if (haste.TimeRemaining <= 0f) return 1f;
            return haste.Multiplier <= 0f ? 1f : 1f / haste.Multiplier;
        }

        /// <summary>
        /// Merge a new SpellBuff onto an entity. If the entity already has one,
        /// the per-field max wins (so a shorter Safeguard doesn't wipe a longer
        /// Aura's reflect). Without this merge, `ecb.AddComponent` overwrites
        /// the existing buff and silently drops fields it didn't set —
        /// stacking Safeguard onto a unit already inside a Sanctuary aura
        /// discarded the aura's reflect/armor. (task-062 C-3)
        /// </summary>
        public static void MergeSpellBuff(EntityManager em, EntityCommandBuffer ecb,
            Entity target, SpellBuff incoming)
        {
            if (em.HasComponent<SpellBuff>(target))
            {
                var existing = em.GetComponentData<SpellBuff>(target);
                existing.ArmorBonus       = Unity.Mathematics.math.max(existing.ArmorBonus, incoming.ArmorBonus);
                existing.DamageMultiplier = Unity.Mathematics.math.max(existing.DamageMultiplier, incoming.DamageMultiplier);
                existing.SpeedMultiplier  = Unity.Mathematics.math.max(existing.SpeedMultiplier, incoming.SpeedMultiplier);
                existing.DamageReflect    = Unity.Mathematics.math.max(existing.DamageReflect, incoming.DamageReflect);
                existing.TimeRemaining    = Unity.Mathematics.math.max(existing.TimeRemaining, incoming.TimeRemaining);
                em.SetComponentData(target, existing);
            }
            else
            {
                ecb.AddComponent(target, incoming);
            }
        }

        /// <summary>
        /// Applies on-hit bonus damage from Condemned, IgniteBuff, VoidStrikeBuff,
        /// and SpellBuff.DamageMultiplier on the attacker. Returns the modified
        /// damage. Consumes IgniteBuff / VoidStrikeBuff charges via ECB.
        ///
        /// Order: matrix damage → SpellBuff.DamageMultiplier (attacker buff) →
        /// Condemned (target debuff) → Ignite/VoidStrike one-shot bonuses. The
        /// multiplier is applied before flat add-ons so timed Empower-style
        /// buffs scale with base damage, not with one-shot proc damage.
        /// (task-062 C-1)
        /// </summary>
        /// <summary>
        /// True when <paramref name="attacker"/> may actually take HP off
        /// <paramref name="target"/> — the same rule
        /// <see cref="ApplyBonusDamageOnHit"/> enforces by returning 0, exposed
        /// so a caller can skip the hit ENTIRELY.
        ///
        /// Callers need this because every damage site ends with a
        /// minimum-damage floor (`math.max(1, finalDamage)`, or the ranged
        /// building-chip clamp). A floor applied AFTER the ally gate turns the
        /// gate's 0 straight back into 1, and teammates chipped each other for
        /// a point a swing — the "allies are attacking each other" report of
        /// 2026-08-15. The gate and the floor have to agree, so the floor asks
        /// this first. docs/Design/Teams.md
        ///
        /// Entities without a FactionTag (neutral props, unowned wreckage) are
        /// damageable as before — this only speaks about faction relations.
        /// </summary>
        public static bool CanDamage(EntityManager em, Entity attacker, Entity target)
        {
            if (!em.HasComponent<FactionTag>(attacker) || !em.HasComponent<FactionTag>(target))
                return true;

            return Alliances.AreHostile(
                em.GetComponentData<FactionTag>(attacker).Value,
                em.GetComponentData<FactionTag>(target).Value);
        }

        public static int ApplyBonusDamageOnHit(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, int baseDamage)
        {
            // Last line of defence for the no-allied-damage rule. Hostility is
            // decided upstream (target acquisition, attack orders, AoE filters)
            // and MeleeCombatSystem / RangedCombatSystem deliberately do no
            // faction check of their own — they damage whatever is in Target.
            // A zero here means any path that slips through, now or later,
            // still cannot hurt a teammate. docs/Design/Teams.md
            if (em.HasComponent<FactionTag>(attacker) && em.HasComponent<FactionTag>(target))
            {
                var af = em.GetComponentData<FactionTag>(attacker).Value;
                var tf = em.GetComponentData<FactionTag>(target).Value;
                if (!Alliances.AreHostile(af, tf)) return 0;
            }

            int final = baseDamage;

            // SpellBuff.DamageMultiplier on attacker (Empower-style timed buff)
            if (em.HasComponent<SpellBuff>(attacker))
            {
                float dmgMult = em.GetComponentData<SpellBuff>(attacker).DamageMultiplier;
                if (dmgMult > 0f && !Unity.Mathematics.math.abs(dmgMult - 1f).Equals(0f))
                    final = (int)(final * dmgMult);
            }

            // Feraldis War Totem aura: a fractional attack bonus while the
            // attacker stands in a friendly totem's radius. Added/removed by
            // WarTotemAuraSystem as units enter and leave, so this is a flat
            // component read rather than a per-attack totem scan.
            if (em.HasComponent<TotemAuraBuff>(attacker))
            {
                float bonus = em.GetComponentData<TotemAuraBuff>(attacker).AttackBonus;
                if (bonus > 0f) final = (int)(final * (1f + bonus));
            }

            // Charge payoff. Percentages first (the unit's own innate charge plus a
            // one-shot War Horn window), then King's Call's flat bonus on top.
            if (em.HasComponent<TheWaningBorder.Abilities.Charging>(attacker))
            {
                float chargePct = 0f;
                if (em.HasComponent<TheWaningBorder.Abilities.InnateChargePct>(attacker))
                    chargePct += em.GetComponentData<TheWaningBorder.Abilities.InnateChargePct>(attacker).Pct;
                if (em.HasComponent<TheWaningBorder.Abilities.NextChargePct>(attacker))
                {
                    chargePct += em.GetComponentData<TheWaningBorder.Abilities.NextChargePct>(attacker).Pct;
                    // War Horn is a NEXT-charge window: spend it on this hit.
                    ecb.RemoveComponent<TheWaningBorder.Abilities.NextChargePct>(attacker);
                }

                if (chargePct > 0f) final = (int)(final * (1f + chargePct / 100f));

                // Ability: flat charge bonus while the attacker is charging (King's
                // Call grants ChargeDamageBonus to allied cavalry; King Lexor gains
                // it from his own aura).
                if (em.HasComponent<TheWaningBorder.Abilities.ChargeDamageBonus>(attacker))
                    final += em.GetComponentData<TheWaningBorder.Abilities.ChargeDamageBonus>(attacker).Bonus;
            }
            // (Liquid Courage's incoming-damage reduction is applied uniformly at
            // every HP-application site via AbilityDamageHooks.ScaleIncoming, not here.)

            // Condemned mark: target takes bonus damage
            if (em.HasComponent<Condemned>(target))
            {
                var condemned = em.GetComponentData<Condemned>(target);
                final = (int)(final * condemned.DamageMultiplier);
            }

            // MarkedForSentence (Justice Lv I passive): if the target was marked
            // by the attacker's faction, the marker faction's units deal bonus
            // damage. Other factions attacking the same target don't get the
            // bonus — the mark is per-marker. (task-063 phase 2c)
            if (em.HasComponent<MarkedForSentence>(target)
                && em.HasComponent<FactionTag>(attacker))
            {
                var mark = em.GetComponentData<MarkedForSentence>(target);
                if (mark.MarkerFaction == em.GetComponentData<FactionTag>(attacker).Value
                    && mark.DamageBonus > 0f)
                {
                    final = (int)(final * (1f + mark.DamageBonus));
                }
            }

            // Ruin "Profane Hands": Ruin-adopted attackers deal +25/40/60%
            // damage to enemy buildings. Refund-on-destroy half lives in
            // SectRuinRefundSystem. Friendly fire is excluded. (task-063
            // phase 2d / phase 4 scaling)
            if (em.HasComponent<BuildingTag>(target)
                && em.HasComponent<FactionTag>(attacker)
                && em.HasComponent<FactionTag>(target))
            {
                var atkFac = em.GetComponentData<FactionTag>(attacker).Value;
                var tgtFac = em.GetComponentData<FactionTag>(target).Value;
                if (atkFac != tgtFac)
                {
                    byte ruinLevel = SectQuery.LevelOf(em, atkFac,
                        SectConfig.Ruin, SectLeverKind.Passive);
                    if (ruinLevel > 0)
                    {
                        float ruinMult = ruinLevel switch
                        {
                            2 => 1.40f,
                            3 => 1.60f,
                            _ => 1.25f,
                        };
                        final = (int)(final * ruinMult);
                    }
                }
            }

            // Antiquity "Tally of the Lost": +N% per logged kill of the
            // target's UnitClass; per-kill bonus scales with lever level.
            // (task-063 phase 2e / phase 4 scaling)
            if (em.HasComponent<AntiquityKills>(attacker)
                && em.HasComponent<UnitTag>(target)
                && em.HasComponent<FactionTag>(attacker))
            {
                byte antiqLevel = SectQuery.LevelOf(em,
                    em.GetComponentData<FactionTag>(attacker).Value,
                    SectConfig.Antiquity, SectLeverKind.Passive);
                if (antiqLevel > 0)
                {
                    var kills = em.GetComponentData<AntiquityKills>(attacker);
                    var tgtClass = em.GetComponentData<UnitTag>(target).Class;
                    byte n = SectAntiquityTallySystem.KillsAgainst(in kills, tgtClass);
                    if (n > 0)
                    {
                        // Spec: +0.5% / +1% / +1.5% per logged kill, which
                        // with the 10-kill tally cap yields the spec's
                        // +5% / +10% / +15% per-class ceilings.
                        float perKill = antiqLevel switch
                        {
                            2 => 0.010f,
                            3 => 0.015f,
                            _ => 0.005f,
                        };
                        final = (int)(final * (1f + perKill * n));
                    }
                }
            }

            // Wrath "Spite of the Forsaken": +N% per 5% HP missing on the
            // attacker. Lv I 0.5% per 5% (max +9.5%). Lv II 1% (max +19%).
            // Lv III 1.5% (max +28.5%). Stacks multiplicatively with the
            // blood-pool bonus when the attacker is standing in a Feraldis
            // pool (phase 3): +10/+15/+20% by lever level.
            // (task-063 phase 2c / phase 4 scaling / phase 3)
            if (em.HasComponent<FactionTag>(attacker)
                && em.HasComponent<Health>(attacker))
            {
                byte wrathLevel = SectQuery.LevelOf(em,
                    em.GetComponentData<FactionTag>(attacker).Value,
                    SectConfig.Wrath, SectLeverKind.Passive);
                if (wrathLevel > 0)
                {
                    var hp = em.GetComponentData<Health>(attacker);
                    if (hp.Max > 0 && hp.Value < hp.Max)
                    {
                        float fractionMissing = 1f - (float)hp.Value / hp.Max;
                        float scalar = wrathLevel switch
                        {
                            2 => 0.20f,
                            3 => 0.30f,
                            _ => 0.10f,
                        };
                        final = (int)(final * (1f + fractionMissing * scalar));
                    }

                    if (em.HasComponent<InBloodPool>(attacker))
                    {
                        float poolMult = wrathLevel switch
                        {
                            2 => 1.15f,
                            3 => 1.20f,
                            _ => 1.10f,
                        };
                        final = (int)(final * poolMult);
                    }
                }
            }

            // IgniteBuff: attacker's next attacks deal bonus fire damage
            if (em.HasComponent<IgniteBuff>(attacker))
            {
                var ignite = em.GetComponentData<IgniteBuff>(attacker);
                if (ignite.AttacksRemaining > 0)
                {
                    final += (int)ignite.BonusDamage;
                    ignite.AttacksRemaining--;
                    if (ignite.AttacksRemaining <= 0)
                        ecb.RemoveComponent<IgniteBuff>(attacker);
                    else
                        em.SetComponentData(attacker, ignite);
                }
            }

            // VoidStrikeBuff: attacker's next attack deals bonus damage
            if (em.HasComponent<VoidStrikeBuff>(attacker))
            {
                var voidStrike = em.GetComponentData<VoidStrikeBuff>(attacker);
                float bonus = em.HasComponent<BorderTag>(target)
                    ? voidStrike.BonusVsBorder
                    : voidStrike.BonusDamage;
                final += (int)bonus;
                ecb.RemoveComponent<VoidStrikeBuff>(attacker);
            }

            // ---- Alanthor tech passives -------------------------------------
            // Attacker side: the Garrison "Charge" opening blow and the Siege Yard
            // "Ranging Shot" aimed shot. Both are one-shot windows spent here.
            if (em.HasComponent<TheWaningBorder.Abilities.FirstStrike>(attacker))
            {
                var fs = em.GetComponentData<TheWaningBorder.Abilities.FirstStrike>(attacker);
                if (fs.Ready != 0)
                {
                    final = (int)(final * (1f + fs.Pct / 100f));
                    fs.Ready = 0;
                    fs.OutOfCombatTimer = 0f;
                    ecb.SetComponent(attacker, fs);
                }
            }
            if (em.HasComponent<TheWaningBorder.Abilities.NextShotBonus>(attacker))
            {
                final = (int)(final * (1f + em.GetComponentData<TheWaningBorder.Abilities.NextShotBonus>(attacker).Pct / 100f));
                ecb.RemoveComponent<TheWaningBorder.Abilities.NextShotBonus>(attacker);
            }

            // Defender side. Shield Wall eats the first hit while planted; Deploy
            // Stakes only answers a CHARGING attacker; Siege Screens is continuous
            // but ranged-only. Each reduction comes off the post-bonus number.
            if (em.HasComponent<TheWaningBorder.Abilities.ShieldWallState>(target))
            {
                var sw = em.GetComponentData<TheWaningBorder.Abilities.ShieldWallState>(target);
                if (sw.Ready != 0)
                {
                    final = (int)(final * (1f - sw.Pct / 100f));
                    sw.Ready = 0;
                    sw.StillTimer = 0f;
                    ecb.SetComponent(target, sw);
                }
            }
            if (em.HasComponent<TheWaningBorder.Abilities.StakesState>(target)
                && em.HasComponent<TheWaningBorder.Abilities.Charging>(attacker))
            {
                var st = em.GetComponentData<TheWaningBorder.Abilities.StakesState>(target);
                if (st.Ready != 0)
                {
                    final = (int)(final * (1f - st.Pct / 100f));
                    st.Ready = 0;
                    st.StillTimer = 0f;
                    ecb.SetComponent(target, st);
                }
            }
            if (em.HasComponent<TheWaningBorder.Abilities.SiegeScreens>(target)
                && em.HasComponent<DamageTypeData>(attacker)
                && em.GetComponentData<DamageTypeData>(attacker).Value == DamageType.Ranged)
            {
                var ss = em.GetComponentData<TheWaningBorder.Abilities.SiegeScreens>(target);
                if (ss.Ready != 0) final = (int)(final * (1f - ss.Pct / 100f));
            }
            if (final < 1) final = 1;

            // Reclamation "Border-Hardened" (combat half): defender takes -25/35/50%
            // damage from Veilstone-faction PvE attackers. Applied last so the
            // reduction comes off the final post-bonus number — same intent as
            // a flat resistance. The border-ground DoT half is in
            // BorderGroundDamageSystem. (task-063 phase 2d / phase 4 scaling)
            if (em.HasComponent<BorderTag>(attacker)
                && em.HasComponent<FactionTag>(target))
            {
                byte reclLevel = SectQuery.LevelOf(em,
                    em.GetComponentData<FactionTag>(target).Value,
                    SectConfig.Reclamation, SectLeverKind.Passive);
                if (reclLevel > 0)
                {
                    float reclMult = reclLevel switch
                    {
                        2 => 0.65f,
                        3 => 0.50f,
                        _ => 0.75f,
                    };
                    final = (int)(final * reclMult);
                    if (final < 1) final = 1;
                }
            }

            return final;
        }

        /// <summary>
        /// Reflects a fraction of dealt damage back to the attacker if the
        /// target has a SpellBuff with DamageReflect > 0.
        /// </summary>
        public static void ApplyDamageReflect(EntityManager em,
            Entity attacker, Entity target, int finalDamage)
        {
            if (!em.HasComponent<SpellBuff>(target)) return;

            var tgtBuff = em.GetComponentData<SpellBuff>(target);
            if (tgtBuff.DamageReflect <= 0f) return;

            int reflected = math.max(1, (int)(finalDamage * tgtBuff.DamageReflect));
            if (!em.HasComponent<Health>(attacker)) return;
            var attackerHealth = em.GetComponentData<Health>(attacker);
            attackerHealth.Value -= reflected;
            em.SetComponentData(attacker, attackerHealth);
        }

        /// <summary>
        /// Updates LastDamagedByFaction and LastAttackerEntity on the target.
        /// Used by PillageSystem, CaravanDeathSystem, and defensive-stance
        /// return-fire logic.
        ///
        /// If <paramref name="elapsedTime"/> is non-zero AND the target is a
        /// building, also stamps <see cref="BuildingDamageState.LastDamagedAt"/>
        /// so out-of-combat readers (Renewal's auto-repair Lv I, etc.) can
        /// gate repair ticks on a quiet-window threshold. Pass 0 from callers
        /// that don't have the time handy. (task-063 phase 2c)
        /// </summary>
        /// <summary>
        /// Add <paramref name="damage"/> to the attacker's match-long damage
        /// ledger (<see cref="DamageDealtTotal"/>), which the Wrath sect's
        /// Spite pools and pays back. Call this wherever damage is actually
        /// subtracted from a target's Health, with the FINAL amount that
        /// landed — Spite's whole premise is that a unit answers for what it
        /// really did, not what it rolled before mitigation.
        ///
        /// Silently ignores non-positive amounts and dead references, so it is
        /// safe to call unconditionally from a combat hot path.
        /// </summary>
        public static void RecordDamageDealt(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, int damage)
        {
            if (damage <= 0) return;
            if (attacker == Entity.Null || !em.Exists(attacker)) return;

            if (em.HasComponent<DamageDealtTotal>(attacker))
            {
                var led = em.GetComponentData<DamageDealtTotal>(attacker);
                led.Value += damage;
                em.SetComponentData(attacker, led);
            }
            else
            {
                // Structural add during query iteration — must go through the
                // command buffer.
                ecb.AddComponent(attacker, new DamageDealtTotal { Value = damage });
            }
        }

        public static void TrackLastDamager(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, double elapsedTime = 0)
        {
            if (em.HasComponent<FactionTag>(attacker))
            {
                var lastDamaged = new LastDamagedByFaction
                {
                    Value = em.GetComponentData<FactionTag>(attacker).Value
                };
                if (em.HasComponent<LastDamagedByFaction>(target))
                    em.SetComponentData(target, lastDamaged);
                    else
                        ecb.AddComponent(target, lastDamaged);
            }

            // Use ECB for structural add (required during query iteration),
            // but immediate write for existing component to ensure latest attacker wins.
            if (em.HasComponent<LastAttackerEntity>(target))
                em.SetComponentData(target, new LastAttackerEntity { Value = attacker });
                else
                    ecb.AddComponent(target, new LastAttackerEntity { Value = attacker });

            // Building-only damage timestamp for the out-of-combat repair window.
            if (elapsedTime > 0 && em.HasComponent<BuildingTag>(target))
            {
                var stamp = new BuildingDamageState { LastDamagedAt = elapsedTime };
                if (em.HasComponent<BuildingDamageState>(target))
                    em.SetComponentData(target, stamp);
                else
                    ecb.AddComponent(target, stamp);
            }
        }
    }
}
