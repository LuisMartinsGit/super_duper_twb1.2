// File: Assets/Scripts/Systems/Crystal/VeilstingerCombatSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Dual-target ranged combat system for Veilstinger crystal units.
    ///
    /// Veilstingers fire lasers at up to two targets simultaneously:
    /// - Primary target is provided by TargetingSystem (Target component)
    /// - Secondary target is the nearest other enemy within range
    ///
    /// Behavior:
    /// - Too close (below MinRange): retreat away from target
    /// - In range (MinRange..MaxRange): aim, then fire dual lasers
    /// - Too far (above MaxRange): chase target (unless HoldPositionTag)
    ///
    /// Uses Projectile + ArrowProjectile entities for laser visuals,
    /// processed by the existing ProjectileSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct VeilstingerCombatSystem : ISystem
    {
        private const float LaserSpeed = 40f;
        private const float FireCooldown = 1.5f;

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

            // Snapshot all potential targets for secondary target search
            var targetQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                .Build();

            var tgtEntities = targetQuery.ToEntityArray(Allocator.Temp);
            var tgtTransforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var tgtFactions = targetQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var tgtHealth = targetQuery.ToComponentDataArray<Health>(Allocator.Temp);

            foreach (var (transform, target, veilState, damage, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<VeilstingerState>, RefRO<Damage>, RefRO<FactionTag>>()
                .WithAll<CrystalUnitTag>()
                .WithEntityAccess())
            {
                ref var tgt = ref target.ValueRW;
                ref var vs = ref veilState.ValueRW;

                // Update cooldown timer
                if (vs.CooldownTimer > 0)
                {
                    vs.CooldownTimer -= dt;
                }

                // Validate primary target exists and is alive
                if (tgt.Value == Entity.Null || !em.Exists(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    vs.Target1 = Entity.Null;
                    vs.Target2 = Entity.Null;
                    vs.AimTimer = 0;
                    continue;
                }

                var targetHealth = em.GetComponentData<Health>(tgt.Value);
                if (targetHealth.Value <= 0)
                {
                    tgt.Value = Entity.Null;
                    vs.Target1 = Entity.Null;
                    vs.Target2 = Entity.Null;
                    vs.AimTimer = 0;
                    continue;
                }

                vs.Target1 = tgt.Value;
                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);

                float minRange = vs.MinRange;
                float maxRange = vs.MaxRange;

                // =============================================================================
                // BEHAVIOR: Too close - RETREAT
                // =============================================================================
                if (dist < minRange)
                {
                    vs.AimTimer = 0;
                    vs.IsFiring = 0;

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
                // BEHAVIOR: In range - AIM AND FIRE dual lasers
                // =============================================================================
                else if (dist <= maxRange)
                {
                    // Stop moving when in range
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    // Accumulate aim time
                    vs.AimTimer += dt;

                    // Fire when aim is ready and cooldown is complete
                    if (vs.AimTimer >= vs.AimTimeRequired && vs.CooldownTimer <= 0)
                    {
                        vs.IsFiring = 1;
                        int dmg = damage.ValueRO.Value;
                        Faction myFaction = faction.ValueRO.Value;

                        // Crystal buff on attacker (bonus damage)
                        if (em.HasComponent<CrystalBuff>(entity))
                        {
                            var buff = em.GetComponentData<CrystalBuff>(entity);
                            dmg = (int)math.round(dmg * (1f + buff.AttBonus));
                            dmg = math.max(1, dmg);
                        }

                        // Fire primary laser at Target1
                        CreateLaser(ref ecb, myPos, targetPos, dist, entity, myFaction, dmg, time, tgt.Value);

                        // Find secondary target: nearest enemy within range that isn't primary
                        Entity secondTarget = Entity.Null;
                        float3 secondPos = float3.zero;
                        float bestDist = float.MaxValue;

                        for (int i = 0; i < tgtEntities.Length; i++)
                        {
                            if (tgtFactions[i].Value == myFaction) continue;
                            if (tgtHealth[i].Value <= 0) continue;
                            if (tgtEntities[i] == tgt.Value) continue; // Skip primary target

                            float d = DistXZ(myPos, tgtTransforms[i].Position);
                            if (d > maxRange) continue;

                            if (d < bestDist)
                            {
                                bestDist = d;
                                secondTarget = tgtEntities[i];
                                secondPos = tgtTransforms[i].Position;
                            }
                        }

                        // Fire second laser if secondary target found
                        if (secondTarget != Entity.Null)
                        {
                            vs.Target2 = secondTarget;
                            CreateLaser(ref ecb, myPos, secondPos, bestDist, entity, myFaction, dmg, time, secondTarget);
                        }
                        else
                        {
                            vs.Target2 = Entity.Null;
                        }

                        // Reset cooldown and aim
                        vs.CooldownTimer = FireCooldown;
                        vs.AimTimer = 0;
                        vs.IsFiring = 0;
                    }
                }
                // =============================================================================
                // BEHAVIOR: Too far - CHASE (unless holding position)
                // =============================================================================
                else
                {
                    // Hold position units do NOT chase
                    if (em.HasComponent<HoldPositionTag>(entity))
                    {
                        tgt.Value = Entity.Null;
                        vs.Target1 = Entity.Null;
                        vs.Target2 = Entity.Null;
                        vs.AimTimer = 0;
                        continue;
                    }

                    vs.AimTimer = 0;
                    vs.IsFiring = 0;

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
                Position = start + new float3(0, 1.2f, 0), // Spawn at unit height
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
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}
