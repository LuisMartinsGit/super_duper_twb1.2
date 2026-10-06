// CommandRouter.Veilstone2026.cs
// The order the 2026-10-01 veilstone pass added
// (docs/Design/Veilstone_Economy.md §3.1):
//
//   SetOutpostMode — switch an Alanthor Trading Outpost between its three
//                    trades: Buy Veilstone (default), Forge Veilsteel and
//                    Sell Veilsteel — the last two once researched.
//
// It replicates: the mode decides what the faction spends and earns every
// cycle, so a peer that missed it diverges in the bank on the next cycle. The
// issuing side never mutates the world itself.

using Unity.Entities;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>Set the trade a Trading Outpost runs.</summary>
        public static void IssueSetOutpostMode(EntityManager em, Entity outpost, TradeRecipe recipe,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (outpost == Entity.Null || !em.Exists(outpost)) return;
            if (!em.HasComponent<TradingOutpostTag>(outpost)) return;
            if (IsBlockedByNotControllable(em, outpost, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                int id = GetNetworkId(em, outpost);
                if (id <= 0)
                {
                    if (!MayExecuteLocally(em, outpost, "SetOutpostMode")) return;
                    SetOutpostModeDirect(em, outpost, recipe);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SetOutpostMode,
                    EntityNetworkId = id,
                    TargetEntityId = (int)recipe,
                });
            }
            else
            {
                SetOutpostModeDirect(em, outpost, recipe);
            }
        }

        /// <summary>Executor — runs on every peer. Refuses a trade the owner
        /// has not researched, so a stale or forged order cannot unlock one.</summary>
        public static void SetOutpostModeDirect(EntityManager em, Entity outpost, TradeRecipe recipe)
        {
            if (outpost == Entity.Null || !em.Exists(outpost)) return;
            if (!em.HasComponent<TradingOutpostTag>(outpost)) return;
            if (recipe > TradeRecipe.Hold) return;
            if (em.HasComponent<FactionTag>(outpost)
                && !TheWaningBorder.Systems.Economy.TradingOutpostSystem.IsUnlocked(
                       em.GetComponentData<FactionTag>(outpost).Value, recipe)) return;
            em.AddComponentData(outpost, new TradingOutpostMode { Recipe = recipe });
        }
    }
}
