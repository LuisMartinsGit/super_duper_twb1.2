// CommandRouter.Delete2026.cs
// ONE BUTTON FOR "GET RID OF IT" (docs/Design/Planned_Buildings.md §4):
//
//   a PLAN                      -> cancelled, everything paid refunded
//   a construction SITE         -> cancelled, everything paid refunded
//   a finished BUILDING         -> demolished (no refund)
//   a UNIT                      -> killed
//
// Sites, buildings and units die through Health = 0, so DeathSystem runs the
// same collapse / death path an attack would, and nothing is left half-removed.
// It replicates: every peer refunds and removes the same entity on the same tick.

using Unity.Entities;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>Delete one of your own plans, sites, buildings or units.</summary>
        public static void IssueDelete(EntityManager em, Entity target,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (!CanDelete(em, target)) return;
            if (IsBlockedByNotControllable(em, target, source)) return;

            if (ShouldQueueForLockstep(source))
            {
                int id = GetNetworkId(em, target);
                if (id <= 0)
                {
                    if (!MayExecuteLocally(em, target, "DeleteEntity")) return;
                    DeleteDirect(em, target);
                    return;
                }
                LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
                {
                    Type = LockstepCommandType.DeleteEntity,
                    EntityNetworkId = id,
                });
            }
            else
            {
                DeleteDirect(em, target);
            }
        }

        /// <summary>True for anything the button may remove: a plan, a
        /// building or a unit with a faction — never the curse's, never a
        /// resource node.</summary>
        public static bool CanDelete(EntityManager em, Entity target)
        {
            if (target == Entity.Null || !em.Exists(target)) return false;
            if (!em.HasComponent<FactionTag>(target)) return false;
            if (em.GetComponentData<FactionTag>(target).Value == Faction.Border) return false;
            if (em.HasComponent<PlannedBuilding>(target)) return true;
            if (!em.HasComponent<Health>(target)) return false;
            if (em.GetComponentData<Health>(target).Value <= 0) return false;   // already dying
            return em.HasComponent<BuildingTag>(target) || em.HasComponent<UnitTag>(target);
        }

        /// <summary>Executor — runs on every peer.</summary>
        public static void DeleteDirect(EntityManager em, Entity target)
        {
            if (!CanDelete(em, target)) return;

            if (em.HasComponent<PlannedBuilding>(target))
            {
                TheWaningBorder.Entities.PlannedBuildings.Cancel(em, target, refund: true);
                return;
            }

            // A site that was never finished hands back everything paid for it.
            if (em.HasComponent<BuildingTag>(target) && em.HasComponent<UnderConstruction>(target))
            {
                var faction = em.GetComponentData<FactionTag>(target).Value;
                string id = TheWaningBorder.Data.BuildCosts.IdFromEntity(em, target);
                if (TheWaningBorder.Data.BuildCosts.TryGetPaid(em, target, id, out var paid))
                    TheWaningBorder.Economy.FactionEconomy.Add(em, faction, paid);
            }

            var hp = em.GetComponentData<Health>(target);
            hp.Value = 0;
            em.SetComponentData(target, hp);
        }
    }
}
