// File: Assets/Scripts/Systems/Combat/RangedCombatSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles ranged combat processing for archer units.
    ///
    /// Features:
    /// - Minimum range enforcement with retreat behavior
    /// - Dynamic aim time based on distance
    /// - Height-based damage modifiers for arrows
    /// - Damage-type propagation to projectiles (via DmgType on Projectile)
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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

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
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

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
                    // Battalion members do NOT retreat independently
                    if (em.HasComponent<BattalionMemberData>(entity))
                    {
                        archer.AimTimer = 0;
                        continue;
                    }

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

                    // Stop moving when in range (skip battalion members — no DesiredDestination)
                    if (!em.HasComponent<BattalionMemberData>(entity) && em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    // Use the unit's configured AimTimeRequired as-is
                    // (set per-unit in entity factories: Archer=0.5, Ballista=1.0, etc.)

                    // Accumulate aim time
                    archer.AimTimer += dt;

                    // Effective aim duration reduced by sect ranged accuracy bonus
                    float effectiveAimRequired = archer.AimTimeRequired;
                    if (FactionSectState.Instance != null)
                    {
                        var aimMults = FactionSectState.Instance.GetMultipliers(faction.ValueRO.Value);
                        if (aimMults.RangedAccuracy > 0f)
                            effectiveAimRequired *= (1f - math.min(aimMults.RangedAccuracy, 0.9f));
                    }

                    // Siege units must face their target before firing
                    bool isSiege = em.HasComponent<SiegeTag>(entity);
                    if (isSiege)
                    {
                        float3 toTarget = targetPos - myPos;
                        toTarget.y = 0;
                        float3 forward = math.mul(transform.ValueRO.Rotation, new float3(0, 0, 1));
                        forward.y = 0;
                        if (math.lengthsq(toTarget) > 0.01f && math.lengthsq(forward) > 0.01f)
                        {
                            float dot = math.dot(math.normalizesafe(forward), math.normalizesafe(toTarget));
                            if (dot < 0.9f) // ~25° tolerance
                            {
                                // Not facing target — rotate toward it
                                archer.AimTimer = 0;
                                float3 dir = math.normalizesafe(toTarget);
                                quaternion targetRot = quaternion.LookRotationSafe(dir, new float3(0, 1, 0));
                                var xf = em.GetComponentData<LocalTransform>(entity);
                                // Smooth rotation toward target (lerp 3x per second)
                                xf.Rotation = math.slerp(xf.Rotation, targetRot, math.min(1f, dt * 3f));
                                em.SetComponentData(entity, xf);
                                continue;
                            }
                        }
                    }

                    // Fire when aim is ready and cooldown is complete
                    if (archer.AimTimer >= effectiveAimRequired && archer.CooldownTimer <= 0)
                    {
                        archer.IsFiring = 1;

                        // Calculate height-based damage modifier for arrow
                        float heightModifier = CalculateHeightDamageModifier(myPos.y, targetPos.y);
                        int finalDamage = CalculateFinalDamage(damage.ValueRO.Value, heightModifier);

                        // Crystal buff on attacker (bonus damage)
                        if (em.HasComponent<CrystalBuff>(entity))
                        {
                            var buff = em.GetComponentData<CrystalBuff>(entity);
                            finalDamage = (int)math.round(finalDamage * (1f + buff.AttBonus));
                        }
                        // Note: CrystalDebuff on target is applied at projectile impact, not here
                        finalDamage = math.max(1, finalDamage);

                        // Apply sect ranged damage and damage-vs-crystal multipliers
                        if (FactionSectState.Instance != null)
                        {
                            var rMults = FactionSectState.Instance.GetMultipliers(faction.ValueRO.Value);
                            finalDamage = (int)(finalDamage * rMults.RangedDamage);
                            if (em.HasComponent<CrystalTag>(tgt.Value))
                                finalDamage = (int)(finalDamage * rMults.DamageVsCrystal);
                            finalDamage = math.max(1, finalDamage);
                        }

                        // Fortified armor bonus on target (flat defense increase)
                        if (em.HasComponent<Fortified>(tgt.Value))
                        {
                            var fort = em.GetComponentData<Fortified>(tgt.Value);
                            int fortReduction = (int)fort.ArmorBonus;
                            finalDamage = math.max(1, finalDamage - fortReduction);
                        }

                        // Condemned mark: target takes bonus damage
                        if (em.HasComponent<Condemned>(tgt.Value))
                        {
                            var condemned = em.GetComponentData<Condemned>(tgt.Value);
                            finalDamage = (int)(finalDamage * condemned.DamageMultiplier);
                        }

                        // IgniteBuff: attacker's next attacks deal bonus fire damage
                        if (em.HasComponent<IgniteBuff>(entity))
                        {
                            var ignite = em.GetComponentData<IgniteBuff>(entity);
                            if (ignite.AttacksRemaining > 0)
                            {
                                finalDamage += (int)ignite.BonusDamage;
                                ignite.AttacksRemaining--;
                                if (ignite.AttacksRemaining <= 0)
                                    ecb.RemoveComponent<IgniteBuff>(entity);
                                else
                                    em.SetComponentData(entity, ignite);
                            }
                        }

                        // VoidStrikeBuff: attacker's next attack deals bonus damage
                        if (em.HasComponent<VoidStrikeBuff>(entity))
                        {
                            var voidStrike = em.GetComponentData<VoidStrikeBuff>(entity);
                            float bonus = em.HasComponent<CrystalTag>(tgt.Value) ? voidStrike.BonusVsCrystal : voidStrike.BonusDamage;
                            finalDamage += (int)bonus;
                            ecb.RemoveComponent<VoidStrikeBuff>(entity);
                        }

                        // DamageReflect: target reflects damage back to attacker
                        if (em.HasComponent<SpellBuff>(tgt.Value))
                        {
                            var tgtBuff = em.GetComponentData<SpellBuff>(tgt.Value);
                            if (tgtBuff.DamageReflect > 0f)
                            {
                                int reflected = math.max(1, (int)(finalDamage * tgtBuff.DamageReflect));
                                var attackerHealth = em.GetComponentData<Health>(entity);
                                attackerHealth.Value -= reflected;
                                em.SetComponentData(entity, attackerHealth);
                            }
                        }

                        finalDamage = math.max(1, finalDamage);

                        // Get shooter's damage type (default Ranged for archers)
                        DamageType dmgType = DamageType.Ranged;
                        if (em.HasComponent<DamageTypeData>(entity))
                            dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                        // Spawn height: use entity's Radius + 0.5f (taller units shoot higher)
                        float spawnYOffset = em.HasComponent<Radius>(entity)
                            ? em.GetComponentData<Radius>(entity).Value + 0.5f
                            : 1.5f;

                        // Create arrow projectile
                        bool isAOE = em.HasComponent<AOEShooterData>(entity);
                        float aoeRadius = isAOE ? em.GetComponentData<AOEShooterData>(entity).Radius : 0f;
                        CreateArrow(ref ecb, myPos, targetPos, dist, entity,
                            faction.ValueRO.Value, finalDamage, (float)time, tgt.Value, dmgType,
                            isAOE, aoeRadius, spawnYOffset);

                        // Reset state — use unit's configured cooldown
                        float cooldownValue = 1.5f;
                        if (em.HasComponent<AttackCooldown>(entity))
                            cooldownValue = em.GetComponentData<AttackCooldown>(entity).Cooldown;

                        // Apply sect attack speed multiplier to cooldown
                        if (FactionSectState.Instance != null)
                        {
                            var asMults = FactionSectState.Instance.GetMultipliers(faction.ValueRO.Value);
                            if (asMults.AttackSpeed > 1f)
                                cooldownValue /= asMults.AttackSpeed;
                        }

                        archer.CooldownTimer = cooldownValue;
                        archer.AimTimer = 0;
                        archer.IsFiring = 0;
                    }
                }
                // =============================================================================
                // BEHAVIOR: Too far - CHASE (unless holding position or defensive stance)
                // =============================================================================
                else
                {
                    // Hold position units do NOT chase - clear target instead
                    if (em.HasComponent<HoldPositionTag>(entity))
                    {
                        tgt.Value = Entity.Null;
                        archer.AimTimer = 0;
                        if (em.HasComponent<AttackCommand>(entity))
                            ecb.RemoveComponent<AttackCommand>(entity);
                        continue;
                    }

                    archer.IsRetreating = 0;
                    archer.AimTimer = 0;

                    // Battalion members: BattalionSyncSystem handles movement, skip DesiredDestination
                    if (em.HasComponent<BattalionMemberData>(entity))
                        continue;

                    // Move to a position just inside max range, not all the way to target
                    float3 toTarget = targetPos - myPos;
                    float3 dirToTarget = math.normalizesafe(toTarget);
                    float stopDist = maxRange - 2f; // Stop 2 units inside max range
                    float3 chasePos = targetPos - dirToTarget * stopDist;

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = chasePos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = chasePos,
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
        private static int CalculateFinalDamage(int baseDamage, float heightModifier)
        {
            float modifiedDamage = baseDamage * heightModifier;
            int finalDamage = (int)math.round(modifiedDamage);
            return math.max(1, finalDamage);
        }

        /// <summary>
        /// Create an arrow projectile entity.
        /// </summary>
        private void CreateArrow(ref EntityCommandBuffer ecb, float3 start, float3 targetPos,
            float distance, Entity shooter, Faction faction, int damage, float time, Entity targetEntity,
            DamageType dmgType = DamageType.Ranged, bool isAOE = false, float aoeRadius = 0f,
            float spawnYOffset = 1.5f)
        {
            // Calculate initial velocity towards target
            var direction = math.normalize(targetPos - start);

            // Apply 25° uncertainty to projectile direction (spread fire)
            const float UncertaintyDeg = 25f;
            float halfRad = math.radians(UncertaintyDeg * 0.5f);
            // Deterministic pseudo-random based on shooter + time
            uint seed = (uint)(shooter.Index * 17 + (int)(time * 1000f));
            seed = seed * 1103515245 + 12345;
            float yawOffset = ((seed % 1000) / 1000f - 0.5f) * 2f * halfRad;
            seed = seed * 1103515245 + 12345;
            float pitchOffset = ((seed % 1000) / 1000f - 0.5f) * 2f * halfRad * 0.3f; // less vertical spread
            quaternion yawRot = quaternion.AxisAngle(new float3(0, 1, 0), yawOffset);
            quaternion pitchRot = quaternion.AxisAngle(math.normalizesafe(math.cross(direction, new float3(0, 1, 0))), pitchOffset);
            direction = math.mul(yawRot, math.mul(pitchRot, direction));
            direction = math.normalizesafe(direction);

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
                Position = start + new float3(0, spawnYOffset, 0),
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(arrow, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,
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
                Target = targetEntity,
                Faction = faction,
                DmgType = dmgType
            });

            if (isAOE)
                ecb.AddComponent(arrow, new AOEProjectile { Radius = aoeRadius });

            // Siege projectiles (Ballista bolts) pierce through multiple targets
            if (dmgType == DamageType.Siege)
                ecb.AddComponent(arrow, new PiercingProjectile { RemainingPierces = 5 });
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}