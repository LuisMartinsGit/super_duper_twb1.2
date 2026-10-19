// CommandRouter.Shardroot.cs
// The player's enshrine order (Curse_And_Shardroot.md § 3.1d, 2026-10-20):
// the Shardroot's bearer walks to its own Temple of Ridan and enshrines the
// artifact there. One lockstep command, LockstepCommandType.EnshrineShardroot
// (bearer in EntityNetworkId, Temple in TargetEntityId); the executor is
// TheWaningBorder.Entities.ShardrootEnshrine.Execute, run on every peer.

using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Entities;
using Unity.Entities;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// Order <paramref name="bearer"/> to enshrine the Shardroot it carries
        /// in <paramref name="temple"/>. Validated here (without the price: a
        /// faction that cannot pay yet still walks there and waits, § 3.1c —
        /// the input layer refuses an unaffordable click itself) and again by
        /// the executor on every peer.
        /// </summary>
        public static void IssueEnshrineShardroot(EntityManager em, Entity bearer, Entity temple,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (bearer == Entity.Null || !em.Exists(bearer)) return;
            if (IsBlockedByNotControllable(em, bearer, source)) return;
            if (ShardrootEnshrine.Check(em, bearer, temple, checkPrice: false) != ShardrootEnshrine.Refusal.None)
                return;

            if (ShouldQueueForLockstep(source))
            {
                int bearerId = GetNetworkId(em, bearer);
                int templeId = GetNetworkId(em, temple);
                if (bearerId <= 0 || templeId <= 0)
                {
                    if (!MayExecuteLocally(em, bearer, "EnshrineShardroot")) return;
                    ShardrootEnshrine.Execute(em, bearer, temple);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.EnshrineShardroot,
                    EntityNetworkId = bearerId,
                    TargetEntityId = templeId,
                });
            }
            else
            {
                ShardrootEnshrine.Execute(em, bearer, temple);
            }
        }

        /// <summary>Executor — runs on every peer (LockstepManager).</summary>
        public static void EnshrineShardrootDirect(EntityManager em, Entity bearer, Entity temple)
            => ShardrootEnshrine.Execute(em, bearer, temple);
    }
}
