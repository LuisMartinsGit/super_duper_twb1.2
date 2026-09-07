// Cancels one entry of a building's production queue — a unit, a technology
// or a level-up — and refunds it.
//
// Cost is taken at QUEUE time, so a queue you cannot cancel is a queue that
// can eat a misclick. Training always had this (CancelTrainCommandHelper);
// research had no cancel at all while it lived in its own floating strip.
// With the three kinds in one buffer there is one helper, and one lockstep
// opcode — the old CancelTrain opcode still lands here.
//
// The refund is computed by the SAME code that charged: the train executor's
// cost formula with the recorded Call to Arms multiplier for a unit,
// CommandRouter.ResearchCost for a tech, the item's recorded Level for an
// upgrade — so the two cannot drift apart. Slot 0 is cancellable: the timer
// is cleared so ProductionQueueSystem starts the new head cleanly on its next
// tick.

using Unity.Entities;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    public static class CancelProductionCommandHelper
    {
        /// <summary>
        /// Cancel the production queue entry at <paramref name="slotIndex"/>:
        /// refund it, drop it, and — when it was the running head — clear the
        /// timer and the level-up gate. Returns true if a slot was cancelled.
        /// </summary>
        public static bool Execute(EntityManager em, Entity building, int slotIndex)
        {
            if (!em.Exists(building)) return false;
            if (!em.HasBuffer<ProductionQueueItem>(building)) return false;

            var queue = em.GetBuffer<ProductionQueueItem>(building);
            if (slotIndex < 0 || slotIndex >= queue.Length) return false;

            var item = queue[slotIndex];
            bool wasRunningHead = slotIndex == 0
                && em.HasComponent<ProductionState>(building)
                && em.GetComponentData<ProductionState>(building).Busy != 0;

            // ── Refund ─────────────────────────────────────────────────────
            // Both the spend and this refund run inside per-peer executors
            // (lockstep replays the cancel on every peer), so the banks — and
            // the desync checksum built from them — stay aligned
            // (docs/Multiplayer_LAN_Readiness.md).
            if (em.HasComponent<FactionTag>(building))
            {
                var faction = em.GetComponentData<FactionTag>(building).Value;
                switch (item.Kind)
                {
                    case ProductionKind.Train:
                    {
                        string unitId = item.Id.ToString();
                        // The EXACT formula TrainCommandDirect charged: base
                        // catalog cost through War's military discount, then
                        // the Call to Arms multiplier as RECORDED at queue
                        // time — not as it stands now. The boon is a 15-30 s
                        // window, so recomputing it here would refund full
                        // price for a half-price unit the moment the window
                        // lapsed: a resource printer, one cancel at a time.
                        var cost = WarSectCostHelper.MilitaryDiscount(
                            em, faction, unitId, TheWaningBorder.Data.UnitCosts.Get(unitId));
                        cost = WarSectCostHelper.ApplyPaidMultiplier(cost, item.PaidCostMultiplier);
                        if (!cost.IsZero) FactionEconomy.Add(em, faction, cost);
                        break;
                    }
                    case ProductionKind.Research:
                    {
                        var cost = CommandRouter.ResearchCost(faction, item.Id.ToString());
                        if (!cost.IsZero) FactionEconomy.Add(em, faction, cost);
                        break;
                    }
                    default:
                        UpgradeBuildingCommandHelper.RefundQueued(em, building, item.Level);
                        break;
                }
            }

            queue.RemoveAt(slotIndex);

            if (wasRunningHead)
            {
                if (em.HasComponent<ProductionState>(building))
                {
                    var ps = em.GetComponentData<ProductionState>(building);
                    ps.Busy = 0;
                    ps.Remaining = 0f;
                    ps.Total = 0f;
                    em.SetComponentData(building, ps);
                }
                // The gate goes with the item that raised it, so the building
                // can shoot again immediately.
                if (item.Kind == ProductionKind.BuildingUpgrade
                    && em.HasComponent<BuildingUpgrading>(building))
                    em.RemoveComponent<BuildingUpgrading>(building);
            }

            return true;
        }
    }
}
