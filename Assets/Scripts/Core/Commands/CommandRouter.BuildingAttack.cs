// CommandRouter.BuildingAttack.cs
// Partial class extension: directed building fire
// (docs/Design/Combat_Pacing.md § Directed building fire).
//
// One BuildingAttack lockstep command per building, carrying the forced
// target's network id in TargetEntityId. 0 clears the order — that is what a
// Stop on a building sends. The issuer validates for its own feedback; the
// executor validates again on every peer, because it is the only answer that
// is the same everywhere.

using Unity.Entities;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// True when <paramref name="building"/> is a finished-or-not
        /// shooting building owned by <paramref name="faction"/> — the thing a
        /// directed-fire order may be given to.
        /// </summary>
        public static bool CanDirectFire(EntityManager em, Entity building, Faction faction)
            => building != Entity.Null && em.Exists(building)
               && em.HasComponent<BuildingTag>(building)
               && em.HasComponent<BuildingRangedAttack>(building)
               && em.HasComponent<FactionTag>(building)
               && em.GetComponentData<FactionTag>(building).Value == faction;

        /// <summary>
        /// Direct a building's fire at <paramref name="target"/>, or clear the
        /// order with <c>Entity.Null</c>. Refused (silently — the input layer
        /// owns the feedback) when the building does not shoot or the target
        /// is not a legal one for it, the Wall Rule included.
        /// </summary>
        public static void IssueBuildingAttack(EntityManager em, Entity building, Entity target,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (ShouldDropCommand(source)) return;
            if (building == Entity.Null || !em.Exists(building)) return;
            if (IsBlockedByNotControllable(em, building, source)) return;
            if (!em.HasComponent<BuildingRangedAttack>(building)) return;
            if (source == CommandSource.LocalPlayer
                && !CanDirectFire(em, building, GameSettings.LocalPlayerFaction)) return;
            if (target != Entity.Null && !BuildingCombatSystem.IsLegalForcedTarget(em, building, target))
                return;

            if (ShouldQueueForLockstep(source))
            {
                QueueBuildingAttackForLockstep(em, building, target);
                return;
            }

            ExecuteBuildingAttack(em, building, target);
        }

        /// <summary>
        /// The mutation every peer runs. A null or no-longer-legal target
        /// clears the order.
        /// </summary>
        public static void ExecuteBuildingAttack(EntityManager em, Entity building, Entity target)
        {
            if (building == Entity.Null || !em.Exists(building)) return;
            if (!em.HasComponent<BuildingRangedAttack>(building)) return;

            if (target == Entity.Null || !BuildingCombatSystem.IsLegalForcedTarget(em, building, target))
            {
                if (em.HasComponent<BuildingForcedTarget>(building))
                    em.RemoveComponent<BuildingForcedTarget>(building);
                return;
            }

            if (em.HasComponent<BuildingForcedTarget>(building))
                em.SetComponentData(building, new BuildingForcedTarget { Target = target });
            else
                em.AddComponentData(building, new BuildingForcedTarget { Target = target });
        }

        private static void QueueBuildingAttackForLockstep(EntityManager em, Entity building, Entity target)
        {
            int networkId = GetNetworkId(em, building);
            int targetId = target != Entity.Null ? GetNetworkId(em, target) : 0;
            if (networkId <= 0 || (target != Entity.Null && targetId <= 0))
            {
                if (!MayExecuteLocally(em, building, "BuildingAttack", target)) return;
                ExecuteBuildingAttack(em, building, target);
                return;
            }

            LockstepServiceLocator.Instance.QueueCommand(new LockstepCommand
            {
                Type = LockstepCommandType.BuildingAttack,
                EntityNetworkId = networkId,
                TargetEntityId = targetId,
            });
        }
    }
}
