// TargetingSystem.Commands.cs
// Combat-component init + player/AI AttackCommand processing + stale-command cleanup.
// Partial of TargetingSystem.cs -- split 2026-08-12 for readability.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct TargetingSystem : ISystem
    {
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void InitializeCombatComponents(ref SystemState state, ref EntityCommandBuffer ecb)
        {
            // Initialize GuardPoint for units that don't have one
            // Skip border units — long-range hunters driven by BorderAISystem wave dispatch;
            // the 20m guard leash below would yank them home mid-march.
            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithNone<GuardPoint>()
                .WithNone<BorderUnitTag>()
                .WithEntityAccess())
            {
                ecb.AddComponent(entity, new GuardPoint
                {
                    Position = transform.ValueRO.Position,
                    Has = 1
                });
            }

            // Initialize AttackCooldown for units that don't have one
            foreach (var (tag, entity) in SystemAPI.Query<RefRO<UnitTag>>()
                .WithNone<AttackCooldown>()
                .WithEntityAccess())
            {
                ecb.AddComponent(entity, new AttackCooldown
                {
                    Cooldown = 1.5f,
                    Timer = 0f
                });
            }
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void ProcessAttackCommands(ref SystemState state, ref EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;

            foreach (var (attackCmd, transform, entity) in SystemAPI
                .Query<RefRO<AttackCommand>, RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                // Check if unit is actively moving by player command
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    var dd = em.GetComponentData<DesiredDestination>(entity);
                    if (dd.Has != 0)
                    {
                        bool isReturningToGuard = false;
                        if (em.HasComponent<GuardPoint>(entity))
                        {
                            var gp = em.GetComponentData<GuardPoint>(entity);
                            if (gp.Has != 0)
                            {
                                var distToGuard = DistXZ(dd.Position, gp.Position);
                                isReturningToGuard = distToGuard < 1f;
                            }
                        }

                        if (!isReturningToGuard)
                        {
                            // Only strip AttackCommand if BOTH Target component and
                            // the AttackCommand's own target are null — prevents race
                            // where Target hasn't been set from AttackCommand yet.
                            var currentTarget = em.GetComponentData<Target>(entity);
                            if (currentTarget.Value == Entity.Null
                                && attackCmd.ValueRO.Target == Entity.Null)
                            {
                                ecb.RemoveComponent<AttackCommand>(entity);
                                continue;
                            }
                        }
                    }
                }

                var target = attackCmd.ValueRO.Target;

                // Validate target exists
                if (target == Entity.Null || !em.Exists(target))
                {
                    ecb.RemoveComponent<AttackCommand>(entity);
                    continue;
                }

                // Validate target is alive
                if (em.HasComponent<Health>(target))
                {
                    var targetHealth = em.GetComponentData<Health>(target);
                    if (targetHealth.Value <= 0)
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                        continue;
                    }
                }

                // Set guard point if not already set
                if (em.HasComponent<GuardPoint>(entity))
                {
                    var gp = em.GetComponentData<GuardPoint>(entity);
                    if (gp.Has == 0)
                    {
                        gp.Position = transform.ValueRO.Position;
                        gp.Has = 1;
                        ecb.SetComponent(entity, gp);
                    }
                }
                else
                {
                    ecb.AddComponent(entity, new GuardPoint
                    {
                        Position = transform.ValueRO.Position,
                        Has = 1
                    });
                }

                // Set target component (Target always present on combat units)
                ecb.SetComponent(entity, new Target { Value = target });

                // Clear destination when attacking — but NOT for Veilstingers
                // (they fire while moving — handled inside VeilstingerCombatSystem,
                // which keeps their movement intact and only writes a destination
                // on retreat/chase branches).
                if (em.HasComponent<DesiredDestination>(entity)
                    && !em.HasComponent<VeilstingerState>(entity))
                {
                    ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                }
            }
        }
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void CleanupStaleCommands(ref SystemState state, ref EntityCommandBuffer ecb)
        {
            var em = state.EntityManager;

            foreach (var (dd, staleTarget, entity) in SystemAPI
                .Query<RefRO<DesiredDestination>, RefRO<Target>>()
                .WithAll<AttackCommand>()
                .WithEntityAccess())
            {
                // Only clean up if unit has no active target
                if (staleTarget.ValueRO.Value != Entity.Null) continue;

                if (dd.ValueRO.Has == 0 && em.HasComponent<AttackCommand>(entity))
                {
                    ecb.RemoveComponent<AttackCommand>(entity);
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void CleanupLastAttacker(ref SystemState state, ref EntityCommandBuffer ecb)
        {
            // Remove LastAttackerEntity ONLY when the attacker no longer exists,
            // not unconditionally. Earlier this stripped the component from every
            // entity each frame so combat systems had to re-add it on every hit
            // (4 archetype mutations per attacker per attack — measurable on a
            // 200-unit fight). Now the component sticks around as long as the
            // attacker entity is alive; combat systems still overwrite the value
            // when a new hit lands. (task-062 Q-12)
            var em = state.EntityManager;
            foreach (var (lastAttacker, entity) in SystemAPI.Query<RefRO<LastAttackerEntity>>()
                .WithEntityAccess())
            {
                if (!em.Exists(lastAttacker.ValueRO.Value))
                    ecb.RemoveComponent<LastAttackerEntity>(entity);
            }
        }
    }
}