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
