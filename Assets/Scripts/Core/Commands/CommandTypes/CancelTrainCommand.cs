// CancelTrainCommand.cs
// Cancel-train command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/CancelTrainCommand.cs
//
// Cancels a training queue slot on a building. Unlike the legacy IMGUI
// EntityActionPanel.CancelQueueItem (which refused to cancel slot 0 while
// busy), this helper handles the in-production slot too — it clears
// TrainingState.Busy/Remaining/Total so TrainingSystem.OnUpdate promotes
// the new slot 0 cleanly on the next tick.

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.UI;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Carries a slot index for cancelling a training queue entry on a
    /// building. Not actually added to entities — the helper consumes the
    /// call directly. Kept as a marker for symmetry with the
    /// XxxCommand / XxxCommandHelper pattern used elsewhere in this folder.
    /// </summary>
    public struct CancelTrainCommand : IComponentData
    {
        public int SlotIndex;
    }

    /// <summary>
    /// Helper class for executing cancel-train commands.
    /// </summary>
    public static class CancelTrainCommandHelper
    {
        /// <summary>
        /// Cancel the training queue entry at <paramref name="slotIndex"/>:
        ///  - refund the unit's full cost to the building's faction
        ///  - if slotIndex == 0 and TrainingState.Busy == 1, zero
        ///    Busy / Remaining / Total so the next tick promotes the new
        ///    slot 0 (TrainingSystem.OnUpdate's "idle building with
        ///    non-empty queue → start" branch picks it up)
        ///  - remove the buffer element
        /// Returns true if a slot was actually cancelled.
        /// </summary>
        public static bool Execute(EntityManager em, Entity building, int slotIndex)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
            if (!em.HasBuffer<TrainQueueItem>(building)) return false;

            var queue = em.GetBuffer<TrainQueueItem>(building);
            if (slotIndex < 0 || slotIndex >= queue.Length) return false;

            string unitId = queue[slotIndex].UnitId.ToString();

            // Refund — read base cost from TechTreeDB via the existing
            // helper so we don't duplicate the lookup logic. Matches the
            // legacy CancelQueueItem behaviour (refund base, accept the
            // Feraldis 1.75× tax as cost-of-doing-business).
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(building))
                faction = em.GetComponentData<FactionTag>(building).Value;

            var cost = EntityActionExtractor.GetUnitCost(unitId);
            if (!cost.IsZero)
                FactionEconomy.Add(em, faction, cost);

            // Slot 0 in-production: clear the active production timer
            // BEFORE removing the buffer entry, so the next tick starts
            // fresh on whatever's now at index 0. TrainingSystem starts
            // training only when Busy == 0.
            if (slotIndex == 0 && em.HasComponent<TrainingState>(building))
            {
                var ts = em.GetComponentData<TrainingState>(building);
                if (ts.Busy != 0)
                {
                    ts.Busy = 0;
                    ts.Remaining = 0f;
                    ts.Total = 0f;
                    em.SetComponentData(building, ts);
                }
            }

            queue.RemoveAt(slotIndex);
            return true;
        }
    }
}
