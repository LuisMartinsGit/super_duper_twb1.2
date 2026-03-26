// File: Assets/Scripts/Systems/Movement/CommandQueueSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Drains the per-entity command queue sequentially.
    /// When the current command completes (no DesiredDestination, no Target),
    /// pops the next QueuedCommand and issues it via existing command helpers.
    /// Removes CommandQueueActive when the buffer is empty.
    /// Runs before MovementSystem so queued commands set DesiredDestination before movement.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MovementSystem))]
    public partial struct CommandQueueSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (dd, target, entity) in SystemAPI
                .Query<RefRO<DesiredDestination>, RefRO<Target>>()
                .WithAll<CommandQueueActive>()
                .WithEntityAccess())
            {
                // Current command still in progress
                if (dd.ValueRO.Has != 0) continue;
                if (target.ValueRO.Value != Entity.Null) continue;

                if (!em.HasBuffer<QueuedCommand>(entity))
                {
                    em.RemoveComponent<CommandQueueActive>(entity);
                    continue;
                }

                var buffer = em.GetBuffer<QueuedCommand>(entity);
                if (buffer.Length == 0)
                {
                    em.RemoveComponent<CommandQueueActive>(entity);
                    continue;
                }

                // Pop the next command
                var cmd = buffer[0];
                buffer.RemoveAt(0);

                switch (cmd.Type)
                {
                    case QueuedCommandType.Move:
                        MoveCommandHelper.Execute(em, entity, cmd.TargetPosition);
                        break;
                    case QueuedCommandType.AttackMove:
                        AttackMoveCommandHelper.Execute(em, entity, cmd.TargetPosition);
                        break;
                    case QueuedCommandType.Patrol:
                        PatrolCommandHelper.Execute(em, entity, cmd.TargetPosition);
                        break;
                }
            }
        }
    }
}
