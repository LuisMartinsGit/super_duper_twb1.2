// LitharchHealingSystem.cs
// Processes healing for Litharch support units
// Location: Assets/Scripts/Systems/Combat/LitharchHealingSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles Litharch healing behavior:
    /// 1. Processes explicit HealCommand (player right-clicks friendly unit)
    /// 2. Auto-searches for injured friendlies when idle
    /// 3. Moves to heal target and applies healing over time
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct LitharchHealingSystem : ISystem
    {
        private const float SearchInterval = 1.0f;
        private const float HealTickInterval = 1.0f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LitharchState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Phase 1: Process explicit HealCommands
            foreach (var (healCmd, lithState, transform, entity) in SystemAPI
                         .Query<RefRO<HealCommand>, RefRW<LitharchState>, RefRO<LocalTransform>>()
                         .WithAll<LitharchTag>()
                         .WithEntityAccess())
            {
                var target = healCmd.ValueRO.Target;
                if (target != Entity.Null && em.Exists(target) && em.HasComponent<Health>(target)
                    && !em.HasComponent<UnhealableTag>(target))
                {
                    lithState.ValueRW.HealTarget = target;
                    lithState.ValueRW.IsHealing = 1;
                }
                ecb.RemoveComponent<HealCommand>(entity);
            }

            // Phase 2: Auto-search for injured friendlies when idle
            foreach (var (lithState, canHeal, factionTag, transform, entity) in SystemAPI
                         .Query<RefRW<LitharchState>, RefRO<CanHeal>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                         .WithAll<LitharchTag>()
                         .WithNone<HealCommand>()
                         .WithEntityAccess())
            {
                // UserMoveOrder = player/AI issued a manual command — cancel healing, obey command
                if (em.HasComponent<UserMoveOrder>(entity))
                {
                    if (lithState.ValueRO.IsHealing != 0)
                    {
                        lithState.ValueRW.HealTarget = Entity.Null;
                        lithState.ValueRW.IsHealing = 0;
                    }
                    continue;
                }

                // Skip if already has a valid heal target
                if (lithState.ValueRO.HealTarget != Entity.Null &&
                    em.Exists(lithState.ValueRO.HealTarget))
                {
                    var tgtHealth = em.GetComponentData<Health>(lithState.ValueRO.HealTarget);
                    if (tgtHealth.Value < tgtHealth.Max && tgtHealth.Value > 0)
                        goto ProcessHealing;
                    // Target is full HP or dead, clear it
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                }

                // Search for injured nearby friendlies periodically
                lithState.ValueRW.SearchTimer += dt;
                if (lithState.ValueRO.SearchTimer < SearchInterval)
                    continue;
                lithState.ValueRW.SearchTimer = 0f;

                {
                    var myPos = transform.ValueRO.Position;
                    var myFaction = factionTag.ValueRO.Value;
                    float healRange = canHeal.ValueRO.HealRange;
                    float searchRange = healRange * 3f; // search a bit wider
                    float bestDist = float.MaxValue;
                    Entity bestTarget = Entity.Null;

                    foreach (var (tHealth, tFaction, tTransform, tEntity) in SystemAPI
                                 .Query<RefRO<Health>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                                 .WithAll<UnitTag>()
                                 .WithEntityAccess())
                    {
                        if (tEntity == entity) continue;
                        if (tFaction.ValueRO.Value != myFaction) continue;
                        if (em.HasComponent<UnhealableTag>(tEntity)) continue;
                        if (tHealth.ValueRO.Value >= tHealth.ValueRO.Max) continue;
                        if (tHealth.ValueRO.Value <= 0) continue;

                        float dist = DistXZ(myPos, tTransform.ValueRO.Position);
                        if (dist < searchRange && dist < bestDist)
                        {
                            bestDist = dist;
                            bestTarget = tEntity;
                        }
                    }

                    if (bestTarget != Entity.Null)
                    {
                        lithState.ValueRW.HealTarget = bestTarget;
                        lithState.ValueRW.IsHealing = 1;
                    }
                }

                ProcessHealing:

                // Phase 3: Move to target and heal
                if (lithState.ValueRO.HealTarget == Entity.Null) continue;
                if (!em.Exists(lithState.ValueRO.HealTarget))
                {
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                    continue;
                }

                {
                    var healTarget = lithState.ValueRO.HealTarget;
                    var targetPos = em.GetComponentData<LocalTransform>(healTarget).Position;
                    var myPos2 = transform.ValueRO.Position;
                    float dist = DistXZ(myPos2, targetPos);
                    float range = canHeal.ValueRO.HealRange;

                    if (dist > range)
                    {
                        // Move toward heal target
                        if (em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.SetComponent(entity, new DesiredDestination
                            {
                                Position = targetPos,
                                Has = 1
                            });
                        }
                        else
                        {
                            ecb.AddComponent(entity, new DesiredDestination
                            {
                                Position = targetPos,
                                Has = 1
                            });
                        }

                        // Update guard point so ProcessReturnToGuard doesn't snap healer back
                        if (em.HasComponent<GuardPoint>(entity))
                            ecb.SetComponent(entity, new GuardPoint { Position = targetPos, Has = 1 });
                    }
                    else
                    {
                        // In range - stop moving and heal
                        if (em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                        }

                        lithState.ValueRW.HealTimer += dt;
                        if (lithState.ValueRO.HealTimer >= HealTickInterval)
                        {
                            lithState.ValueRW.HealTimer = 0f;

                            var targetHealth = em.GetComponentData<Health>(healTarget);
                            float healRate = canHeal.ValueRO.HealRate;
                            int healAmount = (int)(healRate * HealTickInterval);
                            if (healAmount < 1) healAmount = 1;

                            targetHealth.Value = math.min(targetHealth.Value + healAmount, targetHealth.Max);
                            // Fix #228: write health immediately via EntityManager
                            // to match how combat systems apply damage. ECB playback
                            // happens AFTER Melee/RangedCombatSystem in the same
                            // frame, so an ECB heal write would overwrite damage
                            // dealt this frame — effectively nullifying it.
                            em.SetComponentData(healTarget, targetHealth);

                            // If fully healed, clear target
                            if (targetHealth.Value >= targetHealth.Max)
                            {
                                lithState.ValueRW.HealTarget = Entity.Null;
                                lithState.ValueRW.IsHealing = 0;
                            }
                        }
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}
