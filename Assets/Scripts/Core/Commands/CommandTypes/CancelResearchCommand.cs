// CancelResearchCommand.cs
// Cancel-research command component and execution logic.
// Location: Assets/Scripts/Core/Commands/CommandTypes/CancelResearchCommand.cs
//
// Cancels a research queue slot on a building. Mirrors CancelTrainCommand:
// refunds the tech's full cost and removes the buffer element; if the slot
// was the in-production one (slot 0 while ResearchState.Busy), it clears
// Busy/Remaining so ResearchSystem promotes the new slot 0 next tick.
// Because GetResearchActions hides queued techs, cancelling a tech makes it
// reappear as a buildable action automatically.

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.UI;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Carries a slot index for cancelling a research queue entry on a
    /// building. Kept as a marker for symmetry with the
    /// XxxCommand / XxxCommandHelper pattern; the helper is called directly.
    /// </summary>
    public struct CancelResearchCommand : IComponentData
    {
        public int SlotIndex;
    }

    public static class CancelResearchCommandHelper
    {
        /// <summary>
        /// Cancel the research queue entry at <paramref name="slotIndex"/>:
        ///  - refund the tech's base cost to the building's faction,
        ///  - if slot 0 is in progress (ResearchState.Busy), zero
        ///    Busy / Remaining / Total so the next tick starts fresh,
        ///  - remove the buffer element.
        /// Returns true if a slot was actually cancelled.
        /// </summary>
        public static bool Execute(EntityManager em, Entity building, int slotIndex)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
            if (!em.HasBuffer<ResearchQueueItem>(building)) return false;

            var queue = em.GetBuffer<ResearchQueueItem>(building);
            if (slotIndex < 0 || slotIndex >= queue.Length) return false;

            string techId = queue[slotIndex].TechId.ToString();

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(building))
                faction = em.GetComponentData<FactionTag>(building).Value;

            var cost = EntityActionExtractor.GetTechCost(techId);
            if (!cost.IsZero)
                FactionEconomy.Add(em, faction, cost);

            // In-production slot 0: clear the active timer BEFORE removing the
            // entry so ResearchSystem (which only starts when Busy == 0) picks
            // up whatever is now at index 0.
            if (slotIndex == 0 && em.HasComponent<ResearchState>(building))
            {
                var rs = em.GetComponentData<ResearchState>(building);
                if (rs.Busy != 0)
                {
                    rs.Busy = 0;
                    rs.Remaining = 0f;
                    em.SetComponentData(building, rs);
                }
            }

            queue.RemoveAt(slotIndex);
            return true;
        }
    }
}
