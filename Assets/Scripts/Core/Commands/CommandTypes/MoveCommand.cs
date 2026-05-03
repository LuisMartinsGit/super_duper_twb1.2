// MoveCommand.cs
// Move command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/MoveCommand.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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

            // Battalion members are positioned by BattalionSyncSystem — never give them movement state
            if (em.HasComponent<BattalionMemberData>(unit)) return;

            // Clear conflicting commands
            ClearConflictingCommands(em, unit);

            // Battalion leader: also clear member combat state. ClearConflictingCommands
            // only nulls the leader's Target/AttackCommand, but member Targets persist —
            // the battalion would resume firing at the old target as soon as it stops.
            if (em.HasComponent<BattalionLeader>(unit) && em.HasBuffer<BattalionMember>(unit))
            {
                var membersBuf = em.GetBuffer<BattalionMember>(unit);
                int memberCount = membersBuf.Length;
                var memberEntities = new NativeArray<Entity>(memberCount, Allocator.Temp);
                for (int i = 0; i < memberCount; i++)
                    memberEntities[i] = membersBuf[i].Value;

                for (int i = 0; i < memberCount; i++)
                {
                    var m = memberEntities[i];
                    if (m == Entity.Null || !em.Exists(m)) continue;
                    if (em.HasComponent<Target>(m))
                        em.SetComponentData(m, new Target { Value = Entity.Null });
                    if (em.HasComponent<AttackCommand>(m))
                        em.RemoveComponent<AttackCommand>(m);
                }
                memberEntities.Dispose();

                if (em.HasComponent<BattalionAttackTarget>(unit))
                    em.RemoveComponent<BattalionAttackTarget>(unit);
            }

            // Add MoveCommand for MovementSystem to process
            if (!em.HasComponent<MoveCommand>(unit))
                em.AddComponent<MoveCommand>(unit);
            em.SetComponentData(unit, new MoveCommand { Destination = destination });

            // Also set DesiredDestination directly for immediate response
            if (!em.HasComponent<DesiredDestination>(unit))
                em.AddComponent<DesiredDestination>(unit);
            em.SetComponentData(unit, new DesiredDestination { Position = destination, Has = 1 });

            // Reset stuck/smoothing state so a previously-stuck unit immediately
            // accepts the new order instead of cancelling it on the next frame
            // (MovementSystem stuck recovery cancels DesiredDestination at counter > 30).
            if (em.HasComponent<StuckState>(unit))
                em.SetComponentData(unit, new StuckState { Counter = 0, LastAttempt = 0 });
            if (em.HasComponent<SmoothedDirection>(unit))
                em.SetComponentData(unit, new SmoothedDirection { Value = float3.zero });
            if (em.HasComponent<MovementCache>(unit))
                em.SetComponentData(unit, new MovementCache
                {
                    LastDestination = new float3(float.MaxValue),
                    LastSnappedDest = -1,
                    LastHeightCell = new int2(int.MinValue),
                    CachedHeight = 0f
                });

            // Pre-warm pathfinding for this destination
            if (GameSettings.UseFlowFields)
            {
                FlowFieldManager.Instance?.RequestFlowField(destination);
            }
            else
            {
                var pos = em.GetComponentData<LocalTransform>(unit).Position;
                AStarPathStore.Instance?.RequestPath(unit, pos, destination);
            }

            // Add UserMoveOrder to prevent auto-targeting from overriding
            if (!em.HasComponent<UserMoveOrder>(unit))
                em.AddComponent<UserMoveOrder>(unit);

            // Update guard point to new destination
            if (em.HasComponent<GuardPoint>(unit))
                em.SetComponentData(unit, new GuardPoint { Position = destination, Has = 1 });
                else
                    em.AddComponentData(unit, new GuardPoint { Position = destination, Has = 1 });

            // Battalion leader: store destination facing so formation rotates to match preview on arrival
            if (em.HasComponent<BattalionLeader>(unit))
            {
                float3 currentPos = em.HasComponent<LocalTransform>(unit)
                    ? em.GetComponentData<LocalTransform>(unit).Position
                    : destination;
                float3 dir = destination - currentPos;
                dir.y = 0;
                if (math.lengthsq(dir) < 0.01f)
                    dir = new float3(0, 0, 1);
                dir = math.normalize(dir);
                var bl = em.GetComponentData<BattalionLeader>(unit);
                bl.DestinationRot = quaternion.LookRotationSafe(dir, new float3(0, 1, 0));
                bl.HasDestinationRot = 1;
                bl.NeedsReassignment = 1;
                em.SetComponentData(unit, bl);
            }
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
            // Clear Litharch healing state (healer uses LitharchState internally)
            if (em.HasComponent<LitharchState>(unit))
            {
                var ls = em.GetComponentData<LitharchState>(unit);
                if (ls.IsHealing != 0)
                {
                    ls.HealTarget = Entity.Null;
                    ls.IsHealing = 0;
                    em.SetComponentData(unit, ls);
                }
            }
        }
    }
}