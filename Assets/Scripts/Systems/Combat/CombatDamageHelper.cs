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
        public static int ApplyBonusDamageOnHit(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, int baseDamage)
        {
            int final = baseDamage;

            // SpellBuff.DamageMultiplier on attacker (Empower-style timed buff)
            if (em.HasComponent<SpellBuff>(attacker))
            {
                float dmgMult = em.GetComponentData<SpellBuff>(attacker).DamageMultiplier;
                if (dmgMult > 0f && !Unity.Mathematics.math.abs(dmgMult - 1f).Equals(0f))
                    final = (int)(final * dmgMult);
            }

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
                        float perKill = antiqLevel switch
                        {
                            2 => 0.015f,
                            3 => 0.020f,
                            _ => 0.010f,
                        };
                        final = (int)(final * (1f + perKill * n));
                    }
                }
            }

            // Wrath "Spite of the Forsaken": +N% per 5% HP missing on the
            // attacker. Lv I 0.5% per 5% (max +9.5%). Lv II 1% (max +19%).
            // Lv III 1.5% (max +28.5%). Blood-pool half lives in Phase 3.
            // (task-063 phase 2c / phase 4 scaling)
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
                        // Per-level scalar on the fraction-missing.
                        float scalar = wrathLevel switch
                        {
                            2 => 0.20f,
                            3 => 0.30f,
                            _ => 0.10f,
                        };
                        final = (int)(final * (1f + fractionMissing * scalar));
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
                float bonus = em.HasComponent<CrystalTag>(target)
                    ? voidStrike.BonusVsCrystal
                    : voidStrike.BonusDamage;
                final += (int)bonus;
                ecb.RemoveComponent<VoidStrikeBuff>(attacker);
            }

            // Reclamation "Curse-Hardened" (combat half): defender takes -25/35/50%
            // damage from Crystal-faction PvE attackers. Applied last so the
            // reduction comes off the final post-bonus number — same intent as
            // a flat resistance. The cursed-ground DoT half is in
            // CursedGroundDamageSystem. (task-063 phase 2d / phase 4 scaling)
            if (em.HasComponent<CrystalTag>(attacker)
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

        // task-063 phase 1: ApplySectOnHitDebuffs deleted with the old
        // FactionSectState multiplier bridge. Panic / control chance are sect
        // levers that Phase 2 will reintroduce per-sect, per-lever.
    }
}
