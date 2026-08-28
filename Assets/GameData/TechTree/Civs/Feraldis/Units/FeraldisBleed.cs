// One place that applies Bleeding, and the component that declares a unit
// inflicts it. Canon: docs/Design/Age_1_Feraldis.md.
//
// Bleeding reaches victims by three routes and they must behave identically:
//   * Bloodletter whirl  -> FeraldisWhirl
//   * Axe Thrower shots  -> ProjectileSystem, via InflictsBleed copied onto
//                           the projectile at fire time
//   * any future melee   -> MeleeCombatSystem, via InflictsBleed
//
// Rules: refresh, never stack (a pack of bleeders must not multiply a
// victim into a delete); units only (buildings do not bleed).

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Combat
{
    public static class FeraldisBleed
    {
        /// <summary>
        /// Apply or refresh bleeding on one victim. Safe to call from a
        /// query iteration: the add goes through the ECB.
        /// </summary>
        public static void Apply(EntityManager em, EntityCommandBuffer ecb,
            Entity victim, float dps, float duration, Faction source)
        {
            if (victim == Entity.Null || !em.Exists(victim)) return;
            if (dps <= 0f || duration <= 0f) return;
            if (!em.HasComponent<Health>(victim)) return;
            if (em.HasComponent<DeathAnimationState>(victim)) return;
            // Buildings don't bleed.
            if (!em.HasComponent<UnitTag>(victim)) return;

            if (em.HasComponent<Bleeding>(victim))
            {
                var b = em.GetComponentData<Bleeding>(victim);
                b.DamagePerSecond = math.max(b.DamagePerSecond, dps);
                b.Remaining = math.max(b.Remaining, duration);
                b.Source = source;
                em.SetComponentData(victim, b);
            }
            else
            {
                ecb.AddComponent(victim, new Bleeding
                {
                    DamagePerSecond = dps,
                    Remaining = duration,
                    Source = source,
                    Accumulator = 0f,
                });
            }
        }

        /// <summary>
        /// Apply bleeding declared by an attacker's <see cref="InflictsBleed"/>.
        /// No-op for attackers without it, so the shared combat paths pay one
        /// HasComponent check for every other unit in the game.
        /// </summary>
        public static void ApplyFrom(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity victim, Faction source)
        {
            if (!em.HasComponent<InflictsBleed>(attacker)) return;
            var spec = em.GetComponentData<InflictsBleed>(attacker);
            Apply(em, ecb, victim, spec.DamagePerSecond, spec.Duration, source);
        }
    }
}
