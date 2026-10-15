// CommandRouter.Feraldis.cs
// Every Feraldis-only order travels as ONE lockstep command,
// LockstepCommandType.FeraldisOrder (actor, target, FeraldisOp | arg << 8,
// point). The executor is TheWaningBorder.Feraldis.FeraldisOrders.Execute,
// which runs on every peer. docs/Design/Age_1_Feraldis.md (2026-10-12).

using TheWaningBorder.Core.Multiplayer;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// Issue a Feraldis order. <paramref name="actor"/> is the unit or
        /// building acting; <paramref name="target"/> may be Entity.Null;
        /// <paramref name="arg"/> must fit in 23 bits.
        /// </summary>
        public static void IssueFeraldisOrder(EntityManager em, Entity actor, Entity target,
            FeraldisOp op, int arg = 0, float3 point = default,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (actor == Entity.Null || !em.Exists(actor)) return;
            if (IsBlockedByNotControllable(em, actor, source)) return;
            if (!TheWaningBorder.Feraldis.FeraldisOrders.IsValid(em, actor, target, op, arg)) return;

            if (ShouldQueueForLockstep(source))
            {
                int actorId = GetNetworkId(em, actor);
                int targetId = target == Entity.Null ? 0 : GetNetworkId(em, target);
                if (actorId <= 0 || (target != Entity.Null && targetId <= 0))
                {
                    if (!MayExecuteLocally(em, actor, "FeraldisOrder")) return;
                    TheWaningBorder.Feraldis.FeraldisOrders.Execute(em, actor, target, op, arg, point);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.FeraldisOrder,
                    EntityNetworkId = actorId,
                    TargetEntityId = targetId,
                    SecondaryTargetId = (byte)op | (arg << 8),
                    TargetPosition = point,
                });
            }
            else
            {
                TheWaningBorder.Feraldis.FeraldisOrders.Execute(em, actor, target, op, arg, point);
            }
        }

        /// <summary>Executor — runs on every peer (LockstepManager).</summary>
        public static void FeraldisOrderDirect(EntityManager em, Entity actor, Entity target,
            byte op, int arg, float3 point)
            => TheWaningBorder.Feraldis.FeraldisOrders.Execute(em, actor, target, (FeraldisOp)op, arg, point);
    }
}
