// AttackMoveCommand.cs
// Attack-move command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/AttackMoveCommand.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing an attack-move command for a unit.
    /// Units move toward the destination while auto-acquiring enemies along the way.
    /// Processed by MovementSystem (for movement) and TargetingSystem (for auto-targeting).
    /// </summary>
    public struct AttackMoveCommand : IComponentData
    {
        /// <summary>The world position to move toward while engaging enemies</summary>
        public float3 Destination;
    }

    /// <summary>
    /// Helper class for executing attack-move commands
    /// </summary>
    public static class AttackMoveCommandHelper
    {
        /// <summary>
        /// Execute an attack-move command on a unit.
        /// Clears conflicting commands and sets up attack-move state.
        /// Unlike regular move, does NOT add UserMoveOrder so TargetingSystem can auto-acquire.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, float3 destination)
        {
            if (!em.Exists(unit)) return;

            // task-112 M3: snap onto the cost field via NavGridQuery (see
            // MoveCommandHelper for the rationale -- this replaces the
            // legacy NavMeshManager snap on the attack-move write side).
            NavGridQuery.SnapToWalkable(destination, out var snapped, out var snapOk);
            if (snapOk)
                destination = snapped;

            // Emit a real pathfinding request. Previously attack-move relied on
            // a legacy NavMeshPathRequestSystem that was deleted in the M-series
            // nav rewrite, so attack-move units never received a NavPathResult /
            // FlowDesiredDir and the integrator beelined them straight at the
            // goal -- they did not pathfind. Mirror MoveCommandHelper: reset any
            // stale path state so the follower doesn't keep walking the previous
            // goal's cached flow slabs, then enqueue the request.
            if (em.HasComponent<NavPathResult>(unit))
            {
                em.SetComponentData(unit, new NavPathResult
                {
                    Status = NavPathRequest.StatusPending,
                    Length = 0,
                    CurrentPortalIndex = -1,
                    Generation = 0,
                });
            }
            if (em.HasBuffer<NavPathPortal>(unit))
                em.GetBuffer<NavPathPortal>(unit).Clear();
            if (em.HasComponent<FlowDesiredDir>(unit))
                em.SetComponentData(unit, new FlowDesiredDir { HasValue = 0, Value = float3.zero });

            MoveCommandHelper.EmitNavPathRequest(em, unit, destination);

            // Clear conflicting commands
            ClearConflictingCommands(em, unit);

            // An individual attack-move detaches the unit from any formation
            // group (FormationMoveCommandHelper re-attaches AFTER calling
            // Execute when this order IS part of a formation attack-move).
            if (em.HasComponent<FormationMemberState>(unit))
                em.RemoveComponent<FormationMemberState>(unit);
            if (em.HasComponent<FormationSpeedOverride>(unit))
                em.RemoveComponent<FormationSpeedOverride>(unit);

            // Add AttackMoveCommand for MovementSystem to process
            if (!em.HasComponent<AttackMoveCommand>(unit))
                em.AddComponent<AttackMoveCommand>(unit);
            em.SetComponentData(unit, new AttackMoveCommand { Destination = destination });

            // Set DesiredDestination directly for immediate response
            if (!em.HasComponent<DesiredDestination>(unit))
                em.AddComponent<DesiredDestination>(unit);
            em.SetComponentData(unit, new DesiredDestination { Position = destination, Has = 1 });

            // NavMeshPathRequestSystem will pick up the new DesiredDestination
            // and compute the path lazily next frame. Legacy pre-warm gone with PR3.

            // Set GuardPoint to destination (unit resumes here after combat)
            if (em.HasComponent<GuardPoint>(unit))
                em.SetComponentData(unit, new GuardPoint { Position = destination, Has = 1 });
                else
                    em.AddComponentData(unit, new GuardPoint { Position = destination, Has = 1 });

            // Add AttackMoveTag marker
            if (!em.HasComponent<AttackMoveTag>(unit))
                em.AddComponent<AttackMoveTag>(unit);

            // Do NOT add UserMoveOrder - this is the key difference from regular move.
            // Without UserMoveOrder, TargetingSystem will auto-acquire targets while moving.
            // Remove it if present from a previous command.
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);
        }

        /// <summary>Cancel whatever this unit was doing before the new order.
        /// Steps are shared via CommandCleanup — see that file for why the four
        /// order helpers clear different sets.</summary>
        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            CommandCleanup.ClearCombat(em, unit);
            CommandCleanup.ClearWorkOrders(em, unit);
        }
    }
}
