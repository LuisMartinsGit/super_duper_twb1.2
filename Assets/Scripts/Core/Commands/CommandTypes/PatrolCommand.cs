// PatrolCommand.cs
// Patrol command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/PatrolCommand.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a patrol command for a unit.
    /// Units move back and forth between their start position and the destination,
    /// auto-acquiring enemies along the way (like attack-move).
    /// Processed by MovementSystem (for initial destination) and PatrolSystem (for waypoint cycling).
    /// </summary>
    public struct PatrolCommand : IComponentData
    {
        /// <summary>The world position to patrol toward (waypoint 1)</summary>
        public float3 Destination;
    }

    /// <summary>
    /// Helper class for executing patrol commands
    /// </summary>
    public static class PatrolCommandHelper
    {
        /// <summary>
        /// Execute a patrol command on a unit.
        /// Sets up two waypoints (current position and destination) and begins patrol loop.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, float3 destination)
        {
            if (!em.Exists(unit)) return;

            // Clear conflicting commands
            ClearConflictingCommands(em, unit);

            // Get unit's current position as the start waypoint
            float3 startPos = float3.zero;
            if (em.HasComponent<LocalTransform>(unit))
                startPos = em.GetComponentData<LocalTransform>(unit).Position;

            // Set up PatrolWaypoint buffer with two waypoints
            if (!em.HasBuffer<PatrolWaypoint>(unit))
                em.AddBuffer<PatrolWaypoint>(unit);

            var buffer = em.GetBuffer<PatrolWaypoint>(unit);
            buffer.Clear();
            buffer.Add(new PatrolWaypoint { Position = startPos, WaitSeconds = 0f });
            buffer.Add(new PatrolWaypoint { Position = destination, WaitSeconds = 0f });

            // Set up PatrolAgent state - start heading toward waypoint 1 (destination)
            if (!em.HasComponent<PatrolAgent>(unit))
                em.AddComponent<PatrolAgent>(unit);
            em.SetComponentData(unit, new PatrolAgent
            {
                Index = 1,
                Loop = 1,
                WaitTimer = 0f
            });

            // Add PatrolTag marker
            if (!em.HasComponent<PatrolTag>(unit))
                em.AddComponent<PatrolTag>(unit);

            // Set DesiredDestination to the patrol destination (waypoint 1)
            if (!em.HasComponent<DesiredDestination>(unit))
                em.AddComponent<DesiredDestination>(unit);
            em.SetComponentData(unit, new DesiredDestination { Position = destination, Has = 1 });

            // Set GuardPoint to the start position (unit returns here after max chase)
            if (em.HasComponent<GuardPoint>(unit))
                em.SetComponentData(unit, new GuardPoint { Position = startPos, Has = 1 });
            else
                em.AddComponentData(unit, new GuardPoint { Position = startPos, Has = 1 });

            // Do NOT add UserMoveOrder - patrolling units should auto-acquire targets
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);

            // Remove AttackMoveTag if present (patrol replaces attack-move)
            if (em.HasComponent<AttackMoveTag>(unit))
                em.RemoveComponent<AttackMoveTag>(unit);
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

            // Clear attack-move
            if (em.HasComponent<AttackMoveCommand>(unit))
                em.RemoveComponent<AttackMoveCommand>(unit);
            if (em.HasComponent<AttackMoveTag>(unit))
                em.RemoveComponent<AttackMoveTag>(unit);

            // Clear move
            if (em.HasComponent<MoveCommand>(unit))
                em.RemoveComponent<MoveCommand>(unit);
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);
        }
    }
}
