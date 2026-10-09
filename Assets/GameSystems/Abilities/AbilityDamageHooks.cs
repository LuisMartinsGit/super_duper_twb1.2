// AbilityDamageHooks.cs
// Central incoming-damage scaling for the ability system. Called at every site
// that subtracts damage from a target's Health, so it applies uniformly
// regardless of the damage source (melee, ranged, ability AoE, DoT, ...).
//
// Liquid Courage sets SpellBuff.DamageTakenMultiplier = 0.10 (90% reduction):
//   150 total damage * 0.10 = 15 applied to HP.
//
// The Shardroot rides the same hook (Curse_And_Shardroot.md § 3.1b): the
// Shardbound King takes only ShardboundKing.asset's fraction, an enshrining
// civilization's units take less and (attacker overload) deal more.
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
        /// <summary>The target's incoming-damage multiplier: its SpellBuff
        /// DamageTakenMultiplier when that is a reduction in (0, 1) (0 on the
        /// buff means "no effect", not "immune" — immunity is Invulnerable /
        /// FireImmune, checked by the caller), times the Shardroot's victim
        /// side (Curse_And_Shardroot.md § 3.1b): the Shardbound King's damage
        /// fraction and the enshrining civilization's protection.</summary>
        public static float IncomingMultiplier(EntityManager em, Entity target)
        {
            if (target == Entity.Null || !em.Exists(target)) return 1f;
            float m = 1f;
            if (TransientState.Active<SpellBuff>(em, target))
            {
                float b = em.GetComponentData<SpellBuff>(target).DamageTakenMultiplier;
                if (b > 0f && b < 1f) m = b;
            }
            m *= TheWaningBorder.Entities.ShardboundKingRules.IncomingMultiplier(em, target);
            m *= TheWaningBorder.Entities.ShardrootEmpowerment.IncomingMultiplier(em, target);
            return m;
        }

        /// <summary>Scale a computed damage amount by the target's incoming-damage
        /// multiplier (see <see cref="IncomingMultiplier"/>). Apply this to the
        /// final damage right before subtracting it from Health. A positive
        /// hit stays at least 1 after scaling.</summary>
        public static int ScaleIncoming(EntityManager em, Entity target, int damage)
        {
            if (damage <= 0) return damage;
            float m = IncomingMultiplier(em, target);
            int scaled = m >= 1f ? damage : math.max(1, (int)(damage * m));
            // The Shardbound King's swarm cap (Curse_And_Shardroot.md § 3.1b), spent once per hit.
            return TheWaningBorder.Entities.ShardboundKingRules.CapIncoming(em, target, scaled);
        }

        /// <summary>Weapon-hit form: the target's incoming multiplier times
        /// the ATTACKER's outgoing one — the enshrining civilization's damage
        /// bonus (§ 3.1b). Melee and projectile hits (direct and splash) call
        /// this; an attacker that no longer exists contributes 1.</summary>
        public static int ScaleIncoming(EntityManager em, Entity target, int damage, Entity attacker)
        {
            if (damage <= 0) return damage;
            float m = IncomingMultiplier(em, target)
                * TheWaningBorder.Entities.ShardrootEmpowerment.OutgoingMultiplier(em, attacker);
            int scaled = m == 1f ? damage : math.max(1, (int)(damage * m));
            return TheWaningBorder.Entities.ShardboundKingRules.CapIncoming(em, target, scaled);
        }
    }
}
