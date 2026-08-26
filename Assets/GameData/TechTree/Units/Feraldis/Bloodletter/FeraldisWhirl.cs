// The Bloodletter's area strike, invoked from MeleeCombatSystem's hit path.
// Canon: docs/Design/Age_1_Feraldis.md.
//
// Rather than give the Bloodletter its own attack loop (which would mean
// duplicating targeting, chasing and cooldown handling), it fights as a
// normal melee unit and this helper widens each landed swing: everything
// hostile inside WhirlAttack.Radius of the primary target is struck for the
// same damage and left Bleeding. The primary target bleeds too.
//
// Damage here is deliberately flat (no armor matrix): the whirl's per-hit
// number is tiny by design and the unit's threat is the BLEED plus the
// sheer number of bodies it opens up.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Combat
{
    public static class FeraldisWhirl
    {
        private static readonly ComponentType[] VictimTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadWrite<Health>(),
        };

        // Static cache: this fires on EVERY Bloodletter swing, and
        // em.CreateEntityQuery permanently registers a new query per call.
        private static CachedEntityQuery _victimQuery;

        /// <summary>
        /// Apply the whirl for one landed swing. No-op for attackers without
        /// <see cref="WhirlAttack"/>, so the melee hot path pays one
        /// HasComponent check for every other unit in the game.
        /// </summary>
        public static void Strike(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity primaryTarget, float3 center, Faction attackerFaction, int damage)
        {
            if (!em.HasComponent<WhirlAttack>(attacker)) return;
            var whirl = em.GetComponentData<WhirlAttack>(attacker);

            // The unit the swing was aimed at always bleeds.
            ApplyBleed(em, ecb, primaryTarget, whirl, attackerFaction);

            float r2 = whirl.Radius * whirl.Radius;
            var query = _victimQuery.Get(em, VictimTypes);
            using var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e == attacker || e == primaryTarget) continue;
                // Whirlwind spares allies as well as own units.
                // docs/Design/Teams.md
                if (!Alliances.AreHostile(attackerFaction,
                        em.GetComponentData<FactionTag>(e).Value)) continue;
                if (em.HasComponent<Invulnerable>(e)) continue;
                if (em.HasComponent<DeathAnimationState>(e)) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                hp.Value = math.max(0, hp.Value - math.max(1, damage));
                em.SetComponentData(e, hp);

                if (em.HasComponent<LastDamagedByFaction>(e))
                    em.SetComponentData(e, new LastDamagedByFaction { Value = attackerFaction });

                ApplyBleed(em, ecb, e, whirl, attackerFaction);
            }
        }

        /// <summary>Routed through the shared applicator so whirl bleed,
        /// axe bleed and any future melee bleed behave identically
        /// (refresh-never-stack, units only).</summary>
        private static void ApplyBleed(EntityManager em, EntityCommandBuffer ecb,
            Entity victim, in WhirlAttack whirl, Faction source)
        {
            FeraldisBleed.Apply(em, ecb, victim,
                whirl.BleedDamagePerSecond, whirl.BleedDuration, source);
        }
    }
}
