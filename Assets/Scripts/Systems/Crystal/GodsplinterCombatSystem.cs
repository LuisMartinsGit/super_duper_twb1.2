// File: Assets/Scripts/Systems/Crystal/GodsplinterCombatSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Hybrid siege/ranged combat system for Godsplinter crystal units.
    ///
    /// Two combat modes:
    /// 1. Siege mode (close range): Direct damage to target, 2x vs buildings
    /// 2. Laser mode (medium range): Multi-target laser barrage (up to 4 targets)
    ///
    /// Behavior priority:
    /// - If in siege range and siege cooldown ready: siege attack
    /// - Else if in laser range and laser cooldown ready: laser barrage
    /// - Else if beyond laser range: chase (unless HoldPositionTag)
    ///
    /// Uses Projectile + ArrowProjectile entities for laser visuals,
    /// processed by the existing ProjectileSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct GodsplinterCombatSystem : ISystem
    {
        private const float LaserSpeed = 55f;
        private const float SiegeCooldownDuration = 3.0f;
        private const float LaserCooldownDuration = 2.0f;
        private const int BuildingDamageMultiplier = 2;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            float dt = SystemAPI.Time.DeltaTime;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            // Snapshot all potential targets for multi-target laser search
            var targetQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                .Build();

            var tgtEntities = targetQuery.ToEntityArray(Allocator.Temp);
            var tgtTransforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var tgtFactions = targetQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var tgtHealth = targetQuery.ToComponentDataArray<Health>(Allocator.Temp);

            foreach (var (transform, target, godState, damage, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<GodsplinterState>, RefRO<Damage>, RefRO<FactionTag>>()
                .WithAll<CrystalUnitTag>()
                .WithEntityAccess())
            {
                ref var tgt = ref target.ValueRW;
                ref var gs = ref godState.ValueRW;

                // Update cooldown timers
                if (gs.SiegeCooldownTimer > 0)
                    gs.SiegeCooldownTimer -= dt;
                if (gs.LaserCooldownTimer > 0)
                    gs.LaserCooldownTimer -= dt;

                // Validate target exists and is alive
                if (tgt.Value == Entity.Null || !em.Exists(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    gs.IsSieging = 0;
                    continue;
                }

                var targetHealth = em.GetComponentData<Health>(tgt.Value);
                if (targetHealth.Value <= 0)
                {
                    tgt.Value = Entity.Null;
                    gs.IsSieging = 0;
                    continue;
                }

                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);
                int baseDmg = damage.ValueRO.Value;
                Faction myFaction = faction.ValueRO.Value;

                // =============================================================================
                // BEHAVIOR 1: Siege mode - close range direct damage
                // =============================================================================
                if (dist <= gs.SiegeRange && gs.SiegeCooldownTimer <= 0)
                {
                    gs.IsSieging = 1;

                    // Stop moving
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    // Calculate siege damage (2x vs buildings)
                    int siegeDmg = baseDmg;
                    if (em.HasComponent<BuildingTag>(tgt.Value))
                    {
                        siegeDmg *= BuildingDamageMultiplier;
                    }

                    // Crystal buff on attacker (bonus damage)
                    if (em.HasComponent<CrystalBuff>(entity))
                    {
                        var buff = em.GetComponentData<CrystalBuff>(entity);
                        siegeDmg = (int)math.round(siegeDmg * (1f + buff.AttBonus));
                    }
                    // Crystal debuff on defender (takes more damage)
                    if (em.HasComponent<CrystalDebuff>(tgt.Value))
                    {
                        var debuff = em.GetComponentData<CrystalDebuff>(tgt.Value);
                        siegeDmg = (int)math.round(siegeDmg * (1f + debuff.AttPenalty));
                    }
                    siegeDmg = math.max(1, siegeDmg);

                    // Apply direct damage to target
                    var health = em.GetComponentData<Health>(tgt.Value);
                    health.Value -= siegeDmg;
                    if (health.Value < 0) health.Value = 0;
                    ecb.SetComponent(tgt.Value, health);

                    // Reset siege cooldown
                    gs.SiegeCooldownTimer = SiegeCooldownDuration;
                }
                // =============================================================================
                // BEHAVIOR 2: Laser barrage - medium range multi-target
                // =============================================================================
                else if (dist <= gs.LaserRange && gs.LaserCooldownTimer <= 0)
                {
                    gs.IsSieging = 0;

                    // Stop moving
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    int maxTargets = math.max(1, gs.LaserMaxTargets);

                    // Crystal buff on attacker (bonus damage for laser barrage)
                    int laserDmg = baseDmg;
                    if (em.HasComponent<CrystalBuff>(entity))
                    {
                        var buff = em.GetComponentData<CrystalBuff>(entity);
                        laserDmg = (int)math.round(laserDmg * (1f + buff.AttBonus));
                        laserDmg = math.max(1, laserDmg);
                    }
                    // Note: CrystalDebuff on targets applied at projectile impact

                    // Find up to LaserMaxTargets nearest enemies within laser range
                    var targets = new NativeList<LaserTarget>(maxTargets, Allocator.Temp);

                    for (int i = 0; i < tgtEntities.Length; i++)
                    {
                        if (tgtFactions[i].Value == myFaction) continue;
                        if (tgtHealth[i].Value <= 0) continue;

                        float d = DistXZ(myPos, tgtTransforms[i].Position);
                        if (d > gs.LaserRange) continue;

                        var candidate = new LaserTarget
                        {
                            Entity = tgtEntities[i],
                            Position = tgtTransforms[i].Position,
                            Distance = d
                        };

                        if (targets.Length < maxTargets)
                        {
                            targets.Add(candidate);
                        }
                        else if (d < targets[targets.Length - 1].Distance)
                        {
                            targets[targets.Length - 1] = candidate;
                        }

                        // Bubble sort last element into position
                        for (int j = targets.Length - 1; j > 0; j--)
                        {
                            if (targets[j].Distance < targets[j - 1].Distance)
                            {
                                var tmp = targets[j];
                                targets[j] = targets[j - 1];
                                targets[j - 1] = tmp;
                            }
                        }
                    }

                    // Fire laser at each target
                    for (int t = 0; t < targets.Length; t++)
                    {
                        CreateLaser(ref ecb, myPos, targets[t].Position,
                            targets[t].Distance, entity, myFaction, laserDmg, time, targets[t].Entity);
                    }

                    if (targets.Length > 0)
                    {
                        gs.LaserCooldownTimer = LaserCooldownDuration;
                    }

                    targets.Dispose();
                }
                // =============================================================================
                // BEHAVIOR 3: Too far - CHASE (unless holding position)
                // =============================================================================
                else if (dist > gs.LaserRange)
                {
                    gs.IsSieging = 0;

                    // Hold position units do NOT chase
                    if (em.HasComponent<HoldPositionTag>(entity))
                    {
                        tgt.Value = Entity.Null;
                        continue;
                    }

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

            tgtEntities.Dispose();
            tgtTransforms.Dispose();
            tgtFactions.Dispose();
            tgtHealth.Dispose();
        }

        /// <summary>
        /// Create a laser projectile entity (reuses Projectile + ArrowProjectile pattern).
        /// </summary>
        private static void CreateLaser(ref EntityCommandBuffer ecb, float3 start, float3 targetPos,
            float distance, Entity shooter, Faction faction, int damage, float time, Entity targetEntity)
        {
            var direction = math.normalize(targetPos - start);
            var velocity = direction * LaserSpeed;
            var flightTime = distance / LaserSpeed;

            var laser = ecb.CreateEntity();

            ecb.AddComponent(laser, new LocalTransform
            {
                Position = start + new float3(0, 2.5f, 0), // Spawn above Godsplinter (tall unit)
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(laser, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = false
            });

            ecb.AddComponent(laser, new Projectile
            {
                Start = start,
                End = targetPos,
                StartTime = time,
                FlightTime = flightTime,
                Damage = damage,
                Target = targetEntity,
                Faction = faction
            });

            // Mark as laser for visual system (renders glowing beam instead of arrow)
            ecb.AddComponent<LaserProjectileTag>(laser);
        }

        private struct LaserTarget
        {
            public Entity Entity;
            public float3 Position;
            public float Distance;
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}
