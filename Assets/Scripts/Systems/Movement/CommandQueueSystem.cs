using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Drains the per-entity command queue in order (Shift+right-click, the
    /// AI's chained orders, planning mode).
    ///
    /// 2026-09-29 rework:
    ///   * READY, not merely stopped — a unit is busy while it walks, fights,
    ///     builds, repairs, heals or marches in a formation group;
    ///   * TARGETED steps (Attack / Build / Repair / Heal) — a step whose
    ///     target has died, finished, or (for an attack) left the sight of
    ///     the owner and its allies is removed from the queue, including the
    ///     attack step being carried out;
    ///   * FORMATION steps — Move / AttackMove / Patrol steps carrying a group
    ///     id are released only when every unit holding that step is ready,
    ///     then issued together as one formation move;
    ///   * WAYPOINTS OUTWEIGH THE STANCE — a unit walking queued plain moves
    ///     carries QueuedMoveStep until the queue is done, and does not
    ///     auto-acquire meanwhile (TargetingSystem);
    ///   * the Build / Repair / Heal helpers clear every command, the queue
    ///     included; the rest of the queue is restored after them.
    ///
    /// Structural changes are deferred past the query (Phase 2): mutating
    /// archetypes inside the foreach aborts it (task-062 Q-5). Runs on the
    /// lockstep sim on every peer; the sight test reads only replicated
    /// entities, so every peer drops the same steps.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(UnitIntegratorSystem))]
    public partial class CommandQueueSystem : SystemBase
    {
        /// <summary>How often queued targets are re-checked (seconds).</summary>
        private const float ValidateInterval = 0.5f;
        private SimCadence.Periodic _validate;

        private EntityQuery _sightQuery;

        private struct Ready
        {
            public Entity Entity;
            public QueuedCommand Head;
            public Faction Faction;
        }

        protected override void OnCreate()
        {
            _sightQuery = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            bool validate = _validate.Due(SystemAPI.Time.DeltaTime, ValidateInterval);

            var ready = new List<Ready>();
            var clear = new List<Entity>();
            var abandonAttack = new List<Entity>();
            // (faction, group) -> [holders of that head step, of whom ready]
            var groupHolders = new Dictionary<(Faction, int), int>();
            var groupReady = new Dictionary<(Faction, int), int>();

            foreach (var (dd, target, fac, entity) in SystemAPI
                .Query<RefRO<DesiredDestination>, RefRO<Target>, RefRO<FactionTag>>()
                .WithAll<CommandQueueActive>()
                .WithNone<CommandQueueFrozen>()
                .WithEntityAccess())
            {
                var faction = fac.ValueRO.Value;
                if (!em.HasBuffer<QueuedCommand>(entity))
                {
                    if (!Busy(em, entity, dd.ValueRO, target.ValueRO)) clear.Add(entity);
                    continue;
                }
                var buffer = em.GetBuffer<QueuedCommand>(entity);

                // Drop targeted steps whose target is gone (dead, finished,
                // out of sight). Buffer edits are not structural.
                if (validate)
                    for (int i = buffer.Length - 1; i >= 0; i--)
                        if (!StepStillValid(em, faction, buffer[i])) buffer.RemoveAt(i);

                // The attack step being carried out loses its target the same
                // way: stop it, so the queue moves on.
                if (validate && em.HasComponent<QueuedAttackTarget>(entity))
                {
                    var qa = em.GetComponentData<QueuedAttackTarget>(entity).Value;
                    if (!TargetAlive(em, qa) || !IsSeenBy(em, faction, PosOf(em, qa)))
                        abandonAttack.Add(entity);
                }

                bool busy = Busy(em, entity, dd.ValueRO, target.ValueRO);

                if (buffer.Length == 0)
                {
                    if (!busy) clear.Add(entity);
                    continue;
                }

                var head = buffer[0];
                if (head.Group != 0 && IsGroupable(head.Type))
                {
                    var key = (faction, head.Group);
                    groupHolders[key] = groupHolders.TryGetValue(key, out int h) ? h + 1 : 1;
                    if (!busy) groupReady[key] = groupReady.TryGetValue(key, out int r) ? r + 1 : 1;
                }
                if (!busy) ready.Add(new Ready { Entity = entity, Head = head, Faction = faction });
            }

            // ── Phase 2: structural changes, after the query ──
            foreach (var e in abandonAttack)
            {
                if (!em.Exists(e)) continue;
                TransientState.Clear<AttackCommand>(em, e);
                if (em.HasComponent<Target>(e)) em.SetComponentData(e, new Target { Value = Entity.Null });
                em.RemoveComponent<QueuedAttackTarget>(e);
            }

            foreach (var e in clear)
            {
                if (!em.Exists(e)) continue;
                if (em.HasComponent<CommandQueueActive>(e)) em.RemoveComponent<CommandQueueActive>(e);
                if (em.HasComponent<QueuedMoveStep>(e)) em.RemoveComponent<QueuedMoveStep>(e);
                if (em.HasComponent<QueuedAttackTarget>(e)) em.RemoveComponent<QueuedAttackTarget>(e);
            }

            // Formation steps: one formation order per group whose every
            // holder is ready. Units are gathered in query order, which is
            // the same on every peer.
            var groups = new Dictionary<(Faction, int), List<Ready>>();
            foreach (var r in ready)
            {
                if (r.Head.Group == 0 || !IsGroupable(r.Head.Type))
                {
                    DispatchSingle(em, r.Entity, r.Head);
                    continue;
                }
                var key = (r.Faction, r.Head.Group);
                if (!groupHolders.TryGetValue(key, out int holders)
                    || !groupReady.TryGetValue(key, out int readyCount)
                    || readyCount < holders) continue;   // someone is still on the previous leg
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Ready>();
                list.Add(r);
            }
            foreach (var kv in groups) DispatchGroup(em, kv.Value);
        }

        // ── Readiness ─────────────────────────────────────────────────────

        /// <summary>Still carrying out an order: walking, fighting, building,
        /// repairing, healing, or marching in a formation group.</summary>
        private static bool Busy(EntityManager em, Entity e, DesiredDestination dd, Target t)
        {
            if (dd.Has != 0) return true;
            if (t.Value != Entity.Null) return true;
            if (em.HasComponent<AttackCommand>(e) && em.IsComponentEnabled<AttackCommand>(e)) return true;
            if (em.HasComponent<BuildCommand>(e) || em.HasComponent<BuildOrder>(e)) return true;
            if (em.HasComponent<RepairOrder>(e)) return true;
            if (em.HasComponent<HealCommand>(e)) return true;
            if (em.HasComponent<FormationMemberState>(e)) return true;
            return false;
        }

        private static bool IsGroupable(QueuedCommandType t)
            => t == QueuedCommandType.Move || t == QueuedCommandType.AttackMove || t == QueuedCommandType.Patrol;

        // ── Target validity ───────────────────────────────────────────────

        private bool StepStillValid(EntityManager em, Faction faction, QueuedCommand step)
        {
            switch (step.Type)
            {
                case QueuedCommandType.Attack:
                    return TargetAlive(em, step.TargetEntity)
                        && IsSeenBy(em, faction, PosOf(em, step.TargetEntity));
                case QueuedCommandType.Build:
                    return TargetAlive(em, step.TargetEntity)
                        && em.HasComponent<UnderConstruction>(step.TargetEntity);
                case QueuedCommandType.Repair:
                case QueuedCommandType.Heal:
                    if (!TargetAlive(em, step.TargetEntity)) return false;
                    var hp = em.GetComponentData<Health>(step.TargetEntity);
                    return hp.Value < hp.Max;
                default:
                    return true;
            }
        }

        private static bool TargetAlive(EntityManager em, Entity t)
            => t != Entity.Null && em.Exists(t) && em.HasComponent<Health>(t)
               && em.GetComponentData<Health>(t).Value > 0;

        private static float3 PosOf(EntityManager em, Entity t)
            => t != Entity.Null && em.Exists(t) && em.HasComponent<LocalTransform>(t)
                ? em.GetComponentData<LocalTransform>(t).Position : float3.zero;

        /// <summary>
        /// In sight of <paramref name="faction"/> or an ally: inside the line
        /// of sight of one of their units or buildings. Simulation state only
        /// (never the per-peer fog renderer), so every peer agrees.
        /// </summary>
        private bool IsSeenBy(EntityManager em, Faction faction, float3 pos)
        {
            using var loss = _sightQuery.ToComponentDataArray<LineOfSight>(Allocator.Temp);
            using var facs = _sightQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = _sightQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < loss.Length; i++)
            {
                if (!Alliances.AreAllied(faction, facs[i].Value)) continue;
                float r = loss[i].Radius;
                float dx = xfs[i].Position.x - pos.x, dz = xfs[i].Position.z - pos.z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        // ── Dispatch ──────────────────────────────────────────────────────

        private static void PopHead(EntityManager em, Entity e)
        {
            var buf = em.GetBuffer<QueuedCommand>(e);
            if (buf.Length > 0) buf.RemoveAt(0);
        }

        private static void DispatchSingle(EntityManager em, Entity e, QueuedCommand step)
        {
            if (!em.Exists(e)) return;
            PopHead(em, e);
            MarkStep(em, e, step);

            switch (step.Type)
            {
                case QueuedCommandType.Move:
                    MoveCommandHelper.Execute(em, e, step.TargetPosition); break;
                case QueuedCommandType.AttackMove:
                    AttackMoveCommandHelper.Execute(em, e, step.TargetPosition); break;
                case QueuedCommandType.Patrol:
                    PatrolCommandHelper.Execute(em, e, step.TargetPosition); break;
                case QueuedCommandType.Attack:
                    AttackCommandHelper.Execute(em, e, step.TargetEntity);
                    SetOrAdd(em, e, new QueuedAttackTarget { Value = step.TargetEntity });
                    break;
                case QueuedCommandType.Build:
                    PreservingQueue(em, e, () => BuildCommandHelper.Execute(em, e, step.TargetEntity,
                        TheWaningBorder.Entities.BuildingIds.Of(step.TargetEntity, em) ?? "",
                        PosOf(em, step.TargetEntity)));
                    break;
                case QueuedCommandType.Repair:
                    PreservingQueue(em, e, () => RepairCommandHelper.Execute(em, e, step.TargetEntity)); break;
                case QueuedCommandType.Heal:
                    PreservingQueue(em, e, () => HealCommandHelper.Execute(em, e, step.TargetEntity)); break;
            }
        }

        /// <summary>One formation order for every holder of a grouped step.</summary>
        private static void DispatchGroup(EntityManager em, List<Ready> members)
        {
            var step = members[0].Head;
            var units = new List<Entity>(members.Count);
            foreach (var m in members)
            {
                if (!em.Exists(m.Entity)) continue;
                PopHead(em, m.Entity);
                MarkStep(em, m.Entity, step);
                units.Add(m.Entity);
            }
            if (units.Count == 0) return;

            if (step.Type == QueuedCommandType.Patrol || units.Count == 1)
            {
                foreach (var u in units)
                {
                    if (step.Type == QueuedCommandType.Patrol) PatrolCommandHelper.Execute(em, u, step.TargetPosition);
                    else if (step.Type == QueuedCommandType.AttackMove) AttackMoveCommandHelper.Execute(em, u, step.TargetPosition);
                    else MoveCommandHelper.Execute(em, u, step.TargetPosition);
                }
                return;
            }
            FormationMoveCommandHelper.Execute(em, units, step.TargetPosition,
                (FormationShape)step.Shape, attackMove: step.Type == QueuedCommandType.AttackMove);
        }

        /// <summary>A plain queued move holds fire (QueuedMoveStep); every
        /// other step fights by its own rules.</summary>
        private static void MarkStep(EntityManager em, Entity e, QueuedCommand step)
        {
            bool move = step.Type == QueuedCommandType.Move;
            bool has = em.HasComponent<QueuedMoveStep>(e);
            if (move && !has) em.AddComponent<QueuedMoveStep>(e);
            else if (!move && has) em.RemoveComponent<QueuedMoveStep>(e);
            if (step.Type != QueuedCommandType.Attack && em.HasComponent<QueuedAttackTarget>(e))
                em.RemoveComponent<QueuedAttackTarget>(e);
        }

        /// <summary>The Build / Repair / Heal helpers clear every command,
        /// the queue included — keep the rest of the queue across them.</summary>
        private static void PreservingQueue(EntityManager em, Entity e, System.Action issue)
        {
            var saved = em.GetBuffer<QueuedCommand>(e).ToNativeArray(Allocator.Temp);
            issue();
            if (!em.Exists(e)) { saved.Dispose(); return; }
            if (!em.HasBuffer<QueuedCommand>(e)) em.AddBuffer<QueuedCommand>(e);
            var buf = em.GetBuffer<QueuedCommand>(e);
            buf.Clear();
            for (int i = 0; i < saved.Length; i++) buf.Add(saved[i]);
            saved.Dispose();
            if (!em.HasComponent<CommandQueueActive>(e)) em.AddComponent<CommandQueueActive>(e);
        }

        private static void SetOrAdd<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }
    }
}

/// <summary>The target of the queued attack step a unit is carrying out;
/// CommandQueueSystem abandons it when the target dies or leaves sight.</summary>
public struct QueuedAttackTarget : IComponentData
{
    public Entity Value;
}
