// CombatDamageHelper.cs
// Shared on-hit pipeline used by MeleeCombatSystem and RangedCombatSystem.
// Location: Assets/Scripts/Systems/Combat/CombatDamageHelper.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

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
        /// Returns the SpellBuff.ArmorBonus on the target (0 if no SpellBuff).
        /// Callers MUST add this to the defender's defense value before running
        /// the damage-type x armor-type matrix. Wired into Melee/Ranged combat
        /// so abilities like StoneheartBastion's +3 armor aura actually fire.
        /// (task-062 C-1)
        /// </summary>
        public static int GetSpellBuffArmorBonus(EntityManager em, Entity target)
        {
            if (!em.HasComponent<SpellBuff>(target)) return 0;
            return (int)em.GetComponentData<SpellBuff>(target).ArmorBonus;
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

            // Ruin Lv I "Profane Hands" passive: Ruin-adopted attackers deal
            // +25% damage to enemy buildings. The 12% cost-refund-on-destruction
            // half lives in SectRuinRefundSystem. Friendly-fire on own buildings
            // is rare but explicitly excluded — the bonus only applies when
            // attacker faction != target faction. Phase 4 scales the multiplier
            // to 1.40× / 1.60× for Lv II / Lv III. (task-063 phase 2d)
            if (em.HasComponent<BuildingTag>(target)
                && em.HasComponent<FactionTag>(attacker)
                && em.HasComponent<FactionTag>(target))
            {
                var atkFac = em.GetComponentData<FactionTag>(attacker).Value;
                var tgtFac = em.GetComponentData<FactionTag>(target).Value;
                if (atkFac != tgtFac
                    && SectQuery.IsAdoptedAtLeast(em, atkFac,
                        SectConfig.Ruin, SectLeverKind.Passive))
                {
                    final = (int)(final * 1.25f);
                }
            }

            // Wrath Lv I "Spite of the Forsaken" passive: attacker deals
            // +0.5% damage per 5% HP missing (max +9.5% at 1 HP). The blood-
            // pool half (+10% in pools at Lv I) lives behind Phase 3 and is
            // not applied here. Phase 4 scales the per-stack bonus to 1% / 1.5%
            // for Lv II / Lv III. (task-063 phase 2c)
            if (em.HasComponent<FactionTag>(attacker)
                && em.HasComponent<Health>(attacker)
                && SectQuery.IsAdoptedAtLeast(em,
                    em.GetComponentData<FactionTag>(attacker).Value,
                    SectConfig.Wrath, SectLeverKind.Passive))
            {
                var hp = em.GetComponentData<Health>(attacker);
                if (hp.Max > 0 && hp.Value < hp.Max)
                {
                    float fractionMissing = 1f - (float)hp.Value / hp.Max;
                    // 0.5% per 5% missing == 0.1× the missing fraction.
                    float bonus = fractionMissing * 0.10f;
                    final = (int)(final * (1f + bonus));
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

            // Reclamation Lv I "Curse-Hardened" passive (combat half): defender
            // takes -25% damage from Crystal-faction PvE attackers. Applied last
            // so the reduction comes off the final post-bonus number — same
            // intent as a flat resistance. The cursed-ground DoT half is hooked
            // separately in CursedGroundDamageSystem (it bypasses this helper).
            // Phase 4 scales reduction to 35% / 50% for Lv II / Lv III.
            // (task-063 phase 2d)
            if (em.HasComponent<CrystalTag>(attacker)
                && em.HasComponent<FactionTag>(target)
                && SectQuery.IsAdoptedAtLeast(em,
                    em.GetComponentData<FactionTag>(target).Value,
                    SectConfig.Reclamation, SectLeverKind.Passive))
            {
                final = (int)(final * 0.75f);
                if (final < 1) final = 1;
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
