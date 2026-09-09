// CurseAwakeningHelper.cs
// The Waking — the single entry point that wakes a dormant well.
// Canon: docs/Design/Curse_And_Shardroot.md §2.8.
//
// Called by all three verb systems the instant a ritualist STARTS channelling
// on a well (PurificationRitualSystem, ConversionRitualSystem,
// CorruptionRitualSystem). Idempotent per well — a well that is already awake
// ignores further calls, so the systems can call unconditionally on every
// channel start without guarding.

using Unity.Entities;
using TheWaningBorder.Core.Localization;

using TheWaningBorder.Core;
namespace TheWaningBorder.Systems.Border
{
    public static class CurseAwakeningHelper
    {
        /// <summary>
        /// Wake one well: it starts feeding the veil field from the next CA
        /// pulse and never sleeps again. No-op if the well is already awake,
        /// gone, or was never dormant.
        ///
        /// The waker is announced because a woken well is a THREAT ANNOUNCEMENT
        /// as much as a victory step — the curse spreading out of it will reach
        /// whoever is nearest, which may well not be the player who woke it.
        /// That asymmetry is the weapon: this notification is how the target
        /// finds out they need to answer.
        /// </summary>
        public static void Wake(EntityManager em, Entity well, Faction waker, double now)
        {
            if (well == Entity.Null || !em.Exists(well)) return;
            if (!em.HasComponent<WellDormant>(well)) return;   // already awake

            em.RemoveComponent<WellDormant>(well);

            // Reaching for a well is the primary act of provocation (§2.10).
            // Hooking it HERE rather than in the three ritual systems is what
            // keeps Purify, Pacify and Corrupt equal in the curse's eyes: all
            // three already funnel through this one call on channel start, so
            // no verb can quietly reach in for free, and none of them can
            // drift apart later.
            // Cap is TierCount, not TierCount-1: wrath level N maps to tier
            // index N-1, so the ladder needs a level per tier. Capping one
            // lower here would have made the top tier unreachable by waking
            // wells — the primary provocation, and the one that should be able
            // to reach it.
            var settings = TheWaningBorder.Data.Border.BorderSettings.Get();
            int cap = settings != null ? settings.TierCount : 0;
            CurseWrath.Provoke(waker, now, cap, "woke a well");

            SimSignals.Notify(
                string.Format(Loc.T("A well stirs — {0} has disturbed it!"), waker));

            if (em.HasComponent<Unity.Transforms.LocalTransform>(well))
            {
                var p = em.GetComponentData<Unity.Transforms.LocalTransform>(well).Position;
                SimSignals.Ping(p,
                    SimPingKind.Curse, 8f, big: true);
                TWBLog.Log($"[CurseAwakening] well at ({p.x:0},{p.z:0}) woken by {waker} " +
                           $"at {now:0}s — it now feeds the veil.");
            }
        }
    }
}
