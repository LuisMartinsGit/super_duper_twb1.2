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
            // The Ram Rig blows wherever it dies (docs/Design/Age_1_Feraldis.md §7.5).
            if (em.HasComponent<RamRigTag>(e) && !em.HasComponent<RamRigSpent>(e))
                return true;
            // The Berserker's Altered Self-Perception (2026-10-12, replaces the
            // Death Frenzy): an open window, or one about to open.
            return em.HasComponent<BerserkerTag>(e)
                && TheWaningBorder.Feraldis.FeraldisCombatSystems.WantsInterceptBerserker(em, e);
        }

        /// <summary>
        /// Apply the rule. Returns TRUE when the entity must NOT die this
        /// frame (Altered Self-Perception is holding the Berserker at 1 HP);
        /// FALSE when it should proceed to die normally.
        ///
        /// Call only from DeathSystem's post-loop pass — this makes
        /// structural changes.
        /// </summary>
        public static bool Apply(EntityManager em, Entity e)
        {
            // --- Suicidal: detonate, then die normally. ---
            if (em.HasComponent<SuicidalTag>(e) && !em.HasComponent<SuicideSpent>(e))
            {
                SuicidalDetonationSystem.Detonate(em, e);
                return false;
            }
            // --- Ram Rig: the blast, then die. ---
            if (em.HasComponent<RamRigTag>(e) && !em.HasComponent<RamRigSpent>(e))
            {
                TheWaningBorder.Feraldis.RamRigSystem.Detonate(em, e);
                return false;
            }
            if (em.HasComponent<BerserkerTag>(e))
                return TheWaningBorder.Feraldis.FeraldisCombatSystems.InterceptBerserker(em, e);
            return false;
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
