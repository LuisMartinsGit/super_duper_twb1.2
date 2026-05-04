// File: Assets/Scripts/Systems/Movement/CommandQueueSystem.cs
using Unity.Collections;
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
    /// Skips entities tagged CommandQueueFrozen (Shift held — see RTSInputManager).
    /// Runs before MovementSystem so queued commands set DesiredDestination before movement.
    ///
    /// Earlier this code mutated archetypes inside the SystemAPI.Query foreach
    /// — `em.RemoveComponent&lt;CommandQueueActive&gt;` on empty buffers, and the
    /// command helpers (MoveCommandHelper.Execute etc.) which add/remove a
    /// dozen components apiece. That invalidated the iterator, so the second
    /// shift-queue command on a unit silently dropped (the foreach aborted
    /// after the first structural change). Phase 1 now snapshots per-entity
    /// decisions into a NativeList; Phase 2 mutates after the foreach has
    /// finished. (task-062 Q-5)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MovementSystem))]
    public partial struct CommandQueueSystem : ISystem
    {
        private enum DispatchKind : byte
        {
            None = 0,           // No-op (queue was empty + no buffer — should never collect)
            ClearActive = 1,    // Buffer empty / missing → remove CommandQueueActive
            Move = 2,
            AttackMove = 3,
            Patrol = 4,
        }

        private struct PendingDispatch
        {
            public Entity Entity;
            public DispatchKind Kind;
            public float3 TargetPosition;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Phase 1: snapshot decisions. Buffer reads + buffer.RemoveAt are
            // safe inside the foreach (they don't change archetype); helpers
            // and RemoveComponent are deferred to Phase 2.
            var pending = new NativeList<PendingDispatch>(16, Allocator.Temp);

            foreach (var (dd, target, entity) in SystemAPI
                .Query<RefRO<DesiredDestination>, RefRO<Target>>()
                .WithAll<CommandQueueActive>()
                .WithNone<CommandQueueFrozen>()
                .WithEntityAccess())
            {
                // Current command still in progress
                if (dd.ValueRO.Has != 0) continue;
                if (target.ValueRO.Value != Entity.Null) continue;

                if (!em.HasBuffer<QueuedCommand>(entity))
                {
                    pending.Add(new PendingDispatch { Entity = entity, Kind = DispatchKind.ClearActive });
                    continue;
                }

                var buffer = em.GetBuffer<QueuedCommand>(entity);
                if (buffer.Length == 0)
                {
                    pending.Add(new PendingDispatch { Entity = entity, Kind = DispatchKind.ClearActive });
                    continue;
                }

                // Pop the head — buffer mutation, not a structural change.
                var cmd = buffer[0];
                buffer.RemoveAt(0);

                var kind = cmd.Type switch
                {
                    QueuedCommandType.Move       => DispatchKind.Move,
                    QueuedCommandType.AttackMove => DispatchKind.AttackMove,
                    QueuedCommandType.Patrol     => DispatchKind.Patrol,
                    _ => DispatchKind.None,
                };
                if (kind == DispatchKind.None) continue;

                pending.Add(new PendingDispatch
                {
                    Entity = entity,
                    Kind = kind,
                    TargetPosition = cmd.TargetPosition,
                });
            }

            // Phase 2: apply structural changes after iteration is done.
            for (int i = 0; i < pending.Length; i++)
            {
                var p = pending[i];
                if (!em.Exists(p.Entity)) continue;

                switch (p.Kind)
                {
                    case DispatchKind.ClearActive:
                        if (em.HasComponent<CommandQueueActive>(p.Entity))
                            em.RemoveComponent<CommandQueueActive>(p.Entity);
                        break;
                    case DispatchKind.Move:
                        MoveCommandHelper.Execute(em, p.Entity, p.TargetPosition);
                        break;
                    case DispatchKind.AttackMove:
                        AttackMoveCommandHelper.Execute(em, p.Entity, p.TargetPosition);
                        break;
                    case DispatchKind.Patrol:
                        PatrolCommandHelper.Execute(em, p.Entity, p.TargetPosition);
                        break;
                }
            }

            pending.Dispose();
        }
    }
}
