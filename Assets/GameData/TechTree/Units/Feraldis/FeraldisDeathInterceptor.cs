// File: Assets/GameData/TechTree/Units/Feraldis/FeraldisDeathInterceptor.cs
// Feraldis "moment of death" rules, hooked into DeathSystem itself.
// Canon: docs/Design/Age_1_Feraldis.md.
//
// WHY THIS LIVES IN DeathSystem AND NOT IN ITS OWN [UpdateBefore] SYSTEM
// ---------------------------------------------------------------------
// Both of these effects must observe lethal damage from ANY source, and a
// separate system only carries [UpdateBefore(DeathSystem)] — which says
// nothing about its order relative to MeleeCombatSystem, ProjectileSystem,
// BleedingSystem or the other AoE systems that also run before DeathSystem.
// If the sort put a damage source AFTER the interceptor, the sequence in one
// frame was: clamp to 1 -> damage -> DeathSystem kills it. The Berserker
// would die inside its "unkillable" window and the Suicidal that was shot
// down would never detonate. Unity breaks those ties by system type hash, so
// it was a coin flip that could silently flip on a rename.
//
// DeathSystem's own pre-death pass is the ONE place that is by definition
// after every damage source in the frame. The existing LifeCling clamp
// (DeathSystem.cs, "source-agnostic, immediately before the death check")
// is the precedent this follows.
//
// Structural changes are made by the caller AFTER its collection loop, so
// iteration is never invalidated.

using Unity.Entities;

namespace TheWaningBorder.Systems.Combat
{
    public static class FeraldisDeathInterceptor
    {
        /// <summary>
        /// True if <paramref name="e"/> has a Feraldis rule that needs to act
        /// at the moment lethal damage lands. Pure query — no mutation, safe
        /// to call inside DeathSystem's collection loop.
        /// </summary>
        public static bool WantsIntercept(EntityManager em, Entity e)
        {
            if (em.HasComponent<SuicidalTag>(e) && !em.HasComponent<SuicideSpent>(e))
                return true;
            // Berserkers and Plunderers both hold at 1 HP before dying —
            // the Berserker for 5 s, the 1-HP Plunderer for 2 s.
            if (!em.HasComponent<BerserkerTag>(e) && !em.HasComponent<PlundererTag>(e))
                return false;
            // Sustaining an open window, or opening one for the first time.
            return em.HasComponent<DeathFrenzyState>(e)
                || !em.HasComponent<DeathFrenzySpent>(e);
        }

        /// <summary>
        /// Apply the rule. Returns TRUE when the entity must NOT die this
        /// frame (the Berserker's last stand is holding it at 1 HP); FALSE
        /// when it should proceed to die normally.
        ///
        /// Call only from DeathSystem's post-loop pass — this makes
        /// structural changes.
        /// </summary>
        public static bool Apply(EntityManager em, Entity e)
        {
            // --- Suicidal: detonate, then die normally. ---
            // Reaching here means it was brought down instead of arriving,
            // which is still a win for Feraldis: the blast and its blood
            // pool land either way.
            if (em.HasComponent<SuicidalTag>(e) && !em.HasComponent<SuicideSpent>(e))
            {
                SuicidalDetonationSystem.Detonate(em, e);
                return false;
            }

            bool isBerserker = em.HasComponent<BerserkerTag>(e);
            bool isPlunderer = em.HasComponent<PlundererTag>(e);
            if (!isBerserker && !isPlunderer) return false;

            // --- Hold at 1 HP for the frenzy window. ---
            if (em.HasComponent<DeathFrenzyState>(e))
            {
                ClampToOne(em, e);
                return true;
            }

            if (em.HasComponent<DeathFrenzySpent>(e)) return false;

            ClampToOne(em, e);
            em.AddComponentData(e, new DeathFrenzyState
            {
                // A Plunderer is a 1-HP tax collector, not a champion — it
                // thrashes for a moment, where a Berserker gets a real last
                // stand.
                Remaining = isPlunderer
                    ? TheWaningBorder.Core.Config.FeraldisConstants.PlundererFrenzySeconds
                    : TheWaningBorder.Core.Config.FeraldisConstants.DeathFrenzySeconds
            });
            em.AddComponent<DeathFrenzySpent>(e);

            // Faster on the way out. The damage half is read straight off
            // DeathFrenzyState by CombatDamageHelper.GetFrenzyDamageMult, so
            // only speed needs a write. Never restored — every exit from the
            // window force-kills the unit (see BerserkerDeathFrenzySystem).
            if (em.HasComponent<MoveSpeed>(e))
            {
                var ms = em.GetComponentData<MoveSpeed>(e);
                ms.Value *= TheWaningBorder.Core.Config.FeraldisConstants.DeathFrenzySpeedMult;
                em.SetComponentData(e, ms);
            }
            return true;
        }

        private static void ClampToOne(EntityManager em, Entity e)
        {
            var h = em.GetComponentData<Health>(e);
            if (h.Value < 1)
            {
                h.Value = 1;
                em.SetComponentData(e, h);
            }
        }
    }
}
