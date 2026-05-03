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
    ///   - DamageReflect from target SpellBuff
    ///   - LastDamagedByFaction and LastAttackerEntity tracking
    ///   - Sect panic/control chance debuff application
    ///
    /// The main damage calculation (damage-type x armor-type matrix, defense,
    /// height modifier, sect multipliers) is intentionally NOT extracted
    /// because it is deeply coupled with the caller's local cooldown and
    /// sect-multiplier state.
    /// </summary>
    public static class CombatDamageHelper
    {
        /// <summary>
        /// Applies on-hit bonus damage from Condemned, IgniteBuff, and
        /// VoidStrikeBuff. Returns the modified damage. Consumes IgniteBuff /
        /// VoidStrikeBuff charges via ECB.
        /// </summary>
        public static int ApplyBonusDamageOnHit(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, int baseDamage)
        {
            int final = baseDamage;

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

        /// <summary>
        /// Applies sect panic and control chance debuffs on hit. Uses a
        /// deterministic hash of (attacker, target) so the outcome is
        /// reproducible inside a single frame.
        /// </summary>
        public static void ApplySectOnHitDebuffs(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, FactionSectState.SectMultipliers sectMults)
        {
            // Panic chance: 50% speed reduction for 2 seconds
            if (sectMults.PanicChance > 0f)
            {
                int hash = attacker.Index ^ (target.Index * 397);
                if ((math.abs(hash) % 100) < (int)(sectMults.PanicChance * 100f))
                {
                    var panic = new SpellDebuff { SpeedReduction = 0.5f, TimeRemaining = 2f };
                    if (!em.HasComponent<SpellDebuff>(target))
                        ecb.AddComponent(target, panic);
                        else
                            ecb.SetComponent(target, panic);
                }
            }

            // Control chance: full root for 1 second
            if (sectMults.ControlChance > 0f)
            {
                int hash = attacker.Index ^ (target.Index * 631);
                if ((math.abs(hash) % 100) < (int)(sectMults.ControlChance * 100f))
                {
                    var root = new SpellDebuff { SpeedReduction = 1.0f, TimeRemaining = 1f };
                    if (!em.HasComponent<SpellDebuff>(target))
                        ecb.AddComponent(target, root);
                        else
                            ecb.SetComponent(target, root);
                }
            }
        }
    }
}
