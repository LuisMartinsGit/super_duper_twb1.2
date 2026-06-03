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

            // Clear conflicting commands
            ClearConflictingCommands(em, unit);

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

        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            // Clear attack
            if (em.HasComponent<AttackCommand>(unit))
                em.RemoveComponent<AttackCommand>(unit);

            // Clear target
            if (em.HasComponent<Target>(unit))
                em.SetComponentData(unit, new Target { Value = Entity.Null });

            // Clear gather
            if (em.HasComponent<GatherCommand>(unit))
                em.RemoveComponent<GatherCommand>(unit);

            // Clear build
            if (em.HasComponent<BuildCommand>(unit))
                em.RemoveComponent<BuildCommand>(unit);
            if (em.HasComponent<BuildOrder>(unit))
                em.RemoveComponent<BuildOrder>(unit);

            // Clear heal
            if (em.HasComponent<HealCommand>(unit))
                em.RemoveComponent<HealCommand>(unit);
            // Clear Litharch healing state
            if (em.HasComponent<LitharchState>(unit))
            {
                var ls = em.GetComponentData<LitharchState>(unit);
                if (ls.IsHealing != 0) { ls.HealTarget = Entity.Null; ls.IsHealing = 0; em.SetComponentData(unit, ls); }
            }
        }
    }
}
