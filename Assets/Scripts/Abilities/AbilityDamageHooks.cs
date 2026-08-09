// AbilityDamageHooks.cs
// Central incoming-damage scaling for the ability system. Called at every site
// that subtracts damage from a target's Health, so it applies uniformly
// regardless of the damage source (melee, ranged, ability AoE, DoT, ...).
//
// Liquid Courage sets SpellBuff.DamageTakenMultiplier = 0.10 (90% reduction):
//   150 total damage * 0.10 = 15 applied to HP.

using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class AbilityDamageHooks
    {
        /// <summary>Scale a computed damage amount by the target's incoming-damage
        /// multiplier (SpellBuff.DamageTakenMultiplier; 0 = no effect). Apply this
        /// to the final damage right before subtracting it from Health.</summary>
        public static int ScaleIncoming(EntityManager em, Entity target, int damage)
        {
            if (damage <= 0 || target == Entity.Null || !em.Exists(target)) return damage;
            if (!em.HasComponent<SpellBuff>(target)) return damage;
            float m = em.GetComponentData<SpellBuff>(target).DamageTakenMultiplier;
            if (m > 0f && m < 1f) return (int)(damage * m);
            return damage;
        }
    }
}
