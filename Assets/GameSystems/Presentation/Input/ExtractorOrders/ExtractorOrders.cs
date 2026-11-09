// ExtractorOrders.cs
// STOP / RESUME EXTRACTING for the local player's selection
// (docs/Design/Veilstone_Economy.md §3.5, 2026-11-09). The toggle on the action
// panel sets EVERY selected extractor of ours — the Gatherer's Hut line, the Mine
// and the Veilstone Mine — not just the one whose panel is showing.
//
// Every order still travels through CommandRouter.IssueSetExtractorStopped, one
// command per building, so lockstep sees exactly what a click on each would have
// sent. (Replaces OutpostOrders, the retired trade-route fan-out.)

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;

namespace TheWaningBorder.Input
{
    public static class ExtractorOrders
    {
        private static bool IsOwnFinishedExtractor(EntityManager em, Entity e, Faction own)
            => CommandRouter.IsStoppableExtractor(em, e)
            && !em.HasComponent<UnderConstruction>(e)
            && em.HasComponent<FactionTag>(e)
            && em.GetComponentData<FactionTag>(e).Value == own;

        /// <summary>Stop (or resume) <paramref name="primary"/> and every other
        /// finished extractor of the local player in the selection.</summary>
        public static void SetForSelection(EntityManager em, Entity primary, bool stopped)
        {
            CommandRouter.IssueSetExtractorStopped(em, primary, stopped);
            var sel = SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count <= 1) return;
            var own = GameSettings.LocalPlayerFaction;
            for (int i = 0; i < sel.Count; i++)
            {
                var e = sel[i];
                if (e == primary || !IsOwnFinishedExtractor(em, e, own)) continue;
                CommandRouter.IssueSetExtractorStopped(em, e, stopped);
            }
        }
    }
}
