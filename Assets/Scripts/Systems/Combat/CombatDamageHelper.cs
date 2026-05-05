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
        /// </summary>
        public static void TrackLastDamager(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target)
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
        }

        // task-063 phase 1: ApplySectOnHitDebuffs deleted with the old
        // FactionSectState multiplier bridge. Panic / control chance are sect
        // levers that Phase 2 will reintroduce per-sect, per-lever.
    }
}
