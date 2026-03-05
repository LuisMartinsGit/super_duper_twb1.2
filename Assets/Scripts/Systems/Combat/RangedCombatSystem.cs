// File: Assets/Scripts/Systems/Combat/RangedCombatSystem.cs
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles ranged combat processing for archer units.
    /// 
    /// Features:
    /// - Minimum range enforcement with retreat behavior
    /// - Dynamic aim time based on distance
    /// - Height-based damage modifiers for arrows
    /// - Arrow projectile creation
    /// - Attack cooldown management
    /// 
    /// Archers will:
    /// - Retreat if enemies get too close (below MinRange)
    /// - Stop and aim when in optimal range
    /// - Chase enemies that are too far away
    /// 
    /// Runs after TargetingSystem to process acquired targets.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct RangedCombatSystem : ISystem
    {
        // Default range values (can be overridden by ArcherState)
        private const float DefaultMinRange = 10f;
        private const float DefaultMaxRange = 25f;
        private const float ArrowSpeed = 30f;

        // Height damage modifier settings
        private const float HeightDamageScale = 0.04f;
        private const float MaxHeightBonus = 0.20f;
        private const float MaxHeightPenalty = -0.20f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var dt = SystemAPI.Time.DeltaTime;
            var time = SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            foreach (var (transform, target, archerState, damage, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<ArcherState>, RefRO<Damage>, RefRO<FactionTag>>()
                .WithAll<ArcherTag>()
                .WithEntityAccess())
            {
                ref var tgt = ref target.ValueRW;
                ref var archer = ref archerState.ValueRW;

                // Update cooldown timer
                if (archer.CooldownTimer > 0)
                {
                    archer.CooldownTimer -= dt;
                }

                // Validate target exists
                if (tgt.Value == Entity.Null || !em.Exists(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    archer.CurrentTarget = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                // Validate target is alive
                var targetHealth = em.GetComponentData<Health>(tgt.Value);
                if (targetHealth.Value <= 0)
                {
                    tgt.Value = Entity.Null;
                    archer.CurrentTarget = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                archer.CurrentTarget = tgt.Value;
                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);

                // Use archer's configured ranges or defaults
                float minRange = archer.MinRange > 0 ? archer.MinRange : DefaultMinRange;
                float maxRange = archer.MaxRange > 0 ? archer.MaxRange : DefaultMaxRange;

                // =============================================================================
                // BEHAVIOR: Too close - RETREAT
                // =============================================================================
                if (dist < minRange)
                {
                    archer.IsRetreating = 1;
                    archer.AimTimer = 0;

                    // Calculate retreat direction (away from target)
                    var retreatDir = math.normalize(myPos - targetPos);
                    var retreatTarget = myPos + retreatDir * (minRange - dist + 3f);

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = retreatTarget,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = retreatTarget,
                            Has = 1
                        });
                    }
                }
                // =============================================================================
                // BEHAVIOR: In optimal range - AIM AND FIRE
                // =============================================================================
                else if (dist <= maxRange)
                {
                    archer.IsRetreating = 0;

                    // Stop moving when in range
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    // Calculate dynamic aim time based on distance
                    // Closer = faster aim, farther = slower aim
                    var minAimTime = 0.3f;
                    var maxAimTime = 1.2f;
                    var aimRange = maxAimTime - minAimTime;
                    var distRatio = (dist - minRange) / (maxRange - minRange);
                    archer.AimTimeRequired = minAimTime + (aimRange * distRatio);

                    // Accumulate aim time
                    archer.AimTimer += dt;

                    // Fire when aim is ready and cooldown is complete
                    if (archer.AimTimer >= archer.AimTimeRequired && archer.CooldownTimer <= 0)
                    {
                        archer.IsFiring = 1;

                        // Calculate height-based damage modifier for arrow
                        float heightModifier = CalculateHeightDamageModifier(myPos.y, targetPos.y);
                        int finalDamage = CalculateFinalDamage(damage.ValueRO.Value, heightModifier);

                        // Create arrow projectile
                        CreateArrow(ref ecb, myPos, targetPos, dist, entity,
                            faction.ValueRO.Value, finalDamage, (float)time, tgt.Value);

                        // Reset state
                        archer.CooldownTimer = 1.5f;
                        archer.AimTimer = 0;
                        archer.IsFiring = 0;
                    }
                }
                // =============================================================================
                // BEHAVIOR: Too far - CHASE
                // =============================================================================
                else
                {
                    archer.IsRetreating = 0;
                    archer.AimTimer = 0;

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Calculate height-based damage modifier.
        /// Returns multiplier: 0.8 to 1.2 (±20% cap)
        /// </summary>
        [BurstCompile]
        private static float CalculateHeightDamageModifier(float attackerHeight, float targetHeight)
        {
            float heightDiff = attackerHeight - targetHeight;
            float modifier = heightDiff * HeightDamageScale;
            modifier = math.clamp(modifier, MaxHeightPenalty, MaxHeightBonus);
            return 1.0f + modifier;
        }

        /// <summary>
        /// Apply damage with minimum guarantee and height modifier.
        /// </summary>
        [BurstCompile]
        private static int CalculateFinalDamage(int baseDamage, float heightModifier)
        {
            float modifiedDamage = baseDamage * heightModifier;
            int finalDamage = (int)math.round(modifiedDamage);
            return math.max(1, finalDamage);
        }

        /// <summary>
        /// Create an arrow projectile entity.
        /// </summary>
        [BurstCompile]
        private void CreateArrow(ref EntityCommandBuffer ecb, float3 start, float3 targetPos,
            float distance, Entity shooter, Faction faction, int damage, float time, Entity targetEntity)
        {
            // Calculate initial velocity towards target
            var direction = math.normalize(targetPos - start);

            // Add slight upward arc for visual appeal
            float minPitch = math.radians(5f);
            float currentPitch = math.asin(direction.y);
            if (currentPitch < minPitch)
            {
                float3 horizontalDir = math.normalize(new float3(direction.x, 0, direction.z));
                direction = horizontalDir * math.cos(minPitch) + new float3(0, math.sin(minPitch), 0);
                direction = math.normalize(direction);
            }

            var velocity = direction * ArrowSpeed;
            var estimatedFlightTime = distance / ArrowSpeed;

            // Create arrow entity
            var arrow = ecb.CreateEntity();

            ecb.AddComponent(arrow, new LocalTransform
            {
                Position = start + new float3(0, 1.5f, 0), // Spawn at archer height
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(arrow, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,  // No gravity for homing arrows
                Shooter = shooter,
                IsParabolic = false
            });

            ecb.AddComponent(arrow, new Projectile
            {
                Start = start,
                End = targetPos,
                StartTime = time,
                FlightTime = estimatedFlightTime,
                Damage = damage,
                Target = targetEntity,  // Store target entity for homing
                Faction = faction
            });
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}