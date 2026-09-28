// AbilityDamageHooks.cs
// Central incoming-damage scaling for the ability system. Called at every site
// that subtracts damage from a target's Health, so it applies uniformly
// regardless of the damage source (melee, ranged, ability AoE, DoT, ...).
//
// Liquid Courage sets SpellBuff.DamageTakenMultiplier = 0.10 (90% reduction):
//   150 total damage * 0.10 = 15 applied to HP.
//
// A REDUCED hit is never erased: the scaled amount is floored AFTER scaling
// and held at a minimum of 1. The old `(int)(damage * m)` ran after the
// callers' own max(1, ...) floor, so under Liquid Courage every hit or tick
// below 10 became 0 — burning ground's 4-DPS pyre did literally nothing.
// Damage-over-time paths that can carry a fraction use IncomingMultiplier
// directly instead (docs/Design/Fire.md, "The damage-over-time contract").

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Abilities
{
    public static class AbilityDamageHooks
    {
        /// <summary>The target's incoming-damage multiplier from its SpellBuff:
        /// DamageTakenMultiplier when it is a reduction in (0, 1), otherwise 1.
        /// (0 on the buff means "no effect", not "immune" — immunity is
        /// Invulnerable / FireImmune, checked by the caller.)</summary>
        public static float IncomingMultiplier(EntityManager em, Entity target)
        {
            if (target == Entity.Null || !em.Exists(target)) return 1f;
            if (!TransientState.Active<SpellBuff>(em, target)) return 1f;
            float m = em.GetComponentData<SpellBuff>(target).DamageTakenMultiplier;
            return (m > 0f && m < 1f) ? m : 1f;
        }

        /// <summary>Scale a computed damage amount by the target's incoming-damage
        /// multiplier (SpellBuff.DamageTakenMultiplier; 0 = no effect). Apply this
        /// to the final damage right before subtracting it from Health. A
        /// positive hit stays at least 1 after scaling.</summary>
        public static int ScaleIncoming(EntityManager em, Entity target, int damage)
        {
            if (damage <= 0) return damage;
            float m = IncomingMultiplier(em, target);
            if (m >= 1f) return damage;
            return math.max(1, (int)(damage * m));
        }
    }
}
