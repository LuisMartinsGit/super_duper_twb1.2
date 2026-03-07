// MoveCommand.cs
// Move command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/MoveCommand.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Systems.Movement;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a move command for a unit.
    /// When attached to an entity, MovementSystem will process it.
    /// </summary>
    public struct MoveCommand : IComponentData
    {
        /// <summary>The world position to move to</summary>
        public float3 Destination;
    }

    /// <summary>
    /// Helper class for executing move commands
    /// </summary>
    public static class MoveCommandHelper
    {
        /// <summary>
        /// Execute a move command on a unit.
        /// Clears conflicting commands and sets up movement state.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, float3 destination)
        {
            if (!em.Exists(unit)) return;

            // Clear conflicting commands
            ClearConflictingCommands(em, unit);

            // Add MoveCommand for MovementSystem to process
            if (!em.HasComponent<MoveCommand>(unit))
                em.AddComponent<MoveCommand>(unit);
            em.SetComponentData(unit, new MoveCommand { Destination = destination });

            // Also set DesiredDestination directly for immediate response
            if (!em.HasComponent<DesiredDestination>(unit))
                em.AddComponent<DesiredDestination>(unit);
            em.SetComponentData(unit, new DesiredDestination { Position = destination, Has = 1 });

            // Pre-warm flow field cache for this destination
            FlowFieldManager.Instance?.RequestFlowField(destination);

            // Add UserMoveOrder to prevent auto-targeting from overriding
            if (!em.HasComponent<UserMoveOrder>(unit))
                em.AddComponent<UserMoveOrder>(unit);

            // Update guard point to new destination
            if (em.HasComponent<GuardPoint>(unit))
                em.SetComponentData(unit, new GuardPoint { Position = destination, Has = 1 });
            else
                em.AddComponentData(unit, new GuardPoint { Position = destination, Has = 1 });
        }

        /// <summary>
        /// Check if a move command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit)) return false;
            
            // Buildings can't move
            if (em.HasComponent<BuildingTag>(unit)) return false;
            
            return true;
        }

        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            // Clear attack - moving cancels attack
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
        }
    }
}