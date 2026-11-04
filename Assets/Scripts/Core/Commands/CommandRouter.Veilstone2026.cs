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
        /// <summary>Set the post to ONE route (Hold = none).</summary>
        public static void IssueSetOutpostMode(EntityManager em, Entity outpost, TradeRecipe recipe,
            CommandSource source = CommandSource.LocalPlayer)
            => IssueOutpostRoutes(em, outpost, (int)recipe, source);

        /// <summary>One click on a route (2026-11-04): a free slot takes one more
        /// of it; on a full post that runs it, every copy of it stops; a full post
        /// without it refuses. Hold clears them all.</summary>
        public static void IssueCycleOutpostRoute(EntityManager em, Entity outpost, TradeRecipe recipe,
            CommandSource source = CommandSource.LocalPlayer)
            => IssueOutpostRoutes(em, outpost, OutpostCycleFlag | (int)recipe, source);

        /// <summary>Set the post's whole route counts (TradeRoutes); locked routes
        /// drop out, the rest are trimmed to the level's capacity.</summary>
        public static void IssueSetOutpostRoutes(EntityManager em, Entity outpost, uint counts,
            CommandSource source = CommandSource.LocalPlayer)
            => IssueOutpostRoutes(em, outpost, OutpostCountsFlag | (int)(counts & TradeRoutes.AllCountsMask), source);

        public const int OutpostCycleFlag = 0x100000, OutpostCountsFlag = 0x200000;

        private static void IssueOutpostRoutes(EntityManager em, Entity outpost, int payload,
            CommandSource source)
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
                    SetOutpostModeDirect(em, outpost, payload);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.SetOutpostMode,
                    EntityNetworkId = id,
                    TargetEntityId = payload,   // recipe, or a route cycle / counts (flags above)
                });
            }
            else
            {
                SetOutpostModeDirect(em, outpost, payload);
            }
        }

        public static void SetOutpostModeDirect(EntityManager em, Entity outpost, TradeRecipe recipe)
            => SetOutpostModeDirect(em, outpost, (int)recipe);

        /// <summary>The one place a post's routes change (every peer runs it from
        /// the same payload): a single recipe, a cycle (OutpostCycleFlag) or whole
        /// counts (OutpostCountsFlag). Locked routes never set; whole counts are
        /// trimmed to the level's capacity.</summary>
        public static void SetOutpostModeDirect(EntityManager em, Entity outpost, int payload)
        {
            if (outpost == Entity.Null || !em.Exists(outpost)) return;
            if (!em.HasComponent<TradingOutpostTag>(outpost)) return;
            bool hasFaction = em.HasComponent<FactionTag>(outpost);
            var faction = hasFaction ? em.GetComponentData<FactionTag>(outpost).Value : Faction.Border;
            uint current = em.HasComponent<TradingOutpostMode>(outpost)
                ? TradeRoutes.CountsOf(em.GetComponentData<TradingOutpostMode>(outpost)) : 0u;
            int cap = TheWaningBorder.Systems.Economy.TradingOutpostSystem.RouteCapacity(em, outpost);

            uint want;
            if ((payload & OutpostCountsFlag) != 0) want = (uint)payload & TradeRoutes.AllCountsMask;
            else
            {
                int code = payload & 0xFF;
                if (code >= TradeRoutes.Count) return;
                var recipe = (TradeRecipe)code;
                if ((payload & OutpostCycleFlag) != 0)
                {
                    int have = TradeRoutes.CountIn(current, recipe);
                    if (recipe == TradeRecipe.Hold) want = 0u;
                    else if (TradeRoutes.Total(current) < cap) want = TradeRoutes.With(current, recipe, have + 1);
                    else if (have > 0) want = TradeRoutes.With(current, recipe, 0);   // full: this click stops it
                    else return;                                                      // full without it: level the post
                }
                else want = TradeRoutes.With(0u, recipe, 1);
            }

            uint clean = 0u; int left = cap;
            for (int i = 0; i < TradeRoutes.All.Length && left > 0; i++)
            {
                var r = TradeRoutes.All[i];
                int n = TradeRoutes.CountIn(want, r);
                if (n <= 0) continue;
                if (hasFaction && !TheWaningBorder.Systems.Economy.TradingOutpostSystem.IsUnlocked(faction, r)) continue;
                if (n > left) n = left;
                clean = TradeRoutes.With(clean, r, n);
                left -= n;
            }
            em.AddComponentData(outpost, TradeRoutes.ModeOf(clean));
        }
    }
}
