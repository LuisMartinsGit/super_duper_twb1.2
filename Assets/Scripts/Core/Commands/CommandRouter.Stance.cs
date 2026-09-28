// CommandRouter.Stance.cs
// Partial class extension: unit stances (docs/Design/Stances.md).
//
// A stance is replicated per unit: one SetStance lockstep command each,
// carrying the UnitStanceMode byte in TargetEntityId. Hold keeps the legacy
// IssueHoldPosition entry point (the H key), which now routes here.

using Unity.Entities;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// Put a unit in a stance. Aggressive / Defensive leave the unit's
        /// current order alone; Hold also stops it where it stands.
        /// </summary>
        public static void IssueStance(EntityManager em, Entity unit, UnitStanceMode stance,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (unit == Entity.Null || !em.Exists(unit)) return;
            if (IsBlockedByNotControllable(em, unit, source)) return;
            if (em.HasComponent<BuildingTag>(unit)) return;

            if (ShouldQueueForLockstep(source))
            {
                QueueStanceForLockstep(em, unit, stance);
                return;
            }

            StanceCommandHelper.Execute(em, unit, stance);
        }

        private static void QueueStanceForLockstep(EntityManager em, Entity unit, UnitStanceMode stance)
        {
            int networkId = GetNetworkId(em, unit);
            if (networkId <= 0)
            {
                if (!MayExecuteLocally(em, unit, "SetStance")) return;
                StanceCommandHelper.Execute(em, unit, stance);
                return;
            }

            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.SetStance,
                EntityNetworkId = networkId,
                TargetEntityId = (int)(byte)stance,
            });
        }
    }
}
