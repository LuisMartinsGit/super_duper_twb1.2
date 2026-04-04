// File: Assets/Scripts/Systems/Combat/ProjectileSystem.cs
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Projectile system handling both arched arrows and straight-line lasers.
    ///
    /// Arrows: Quadratic Bezier curve trajectories with guaranteed hits.
    /// Lasers: Straight-line flight that collides with terrain (blocked by hills/cliffs).
    ///
    /// Runs after RangedCombatSystem to process spawned projectiles.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    public partial struct ProjectileSystem : ISystem
    {
        // Flight parameters
        private const float FlightDuration = 0.8f;     // How long arrows take to reach target
        private const float ArrowSpeed = 30f;            // Projectile speed (matches RangedCombatSystem)
        private const float BoltSpeed = 55f;             // Siege bolt speed (matches RangedCombatSystem)
        private const float ArcHeight = 3f;            // Height of arc above midpoint
        private const float HitRadius = 0.8f;          // Distance to register a hit

        // Maximum pitch angle in radians (60 degrees)
        private const float MaxPitchAngle = 1.047f;

        // Terrain collision margin above ground for lasers
        private const float TerrainCollisionMargin = 0.5f;

        // Cached query for AOE splash targets
        private EntityQuery _aoeTargetQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _aoeTargetQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>()
            );
        }

        // Not Burst-compiled: laser path uses managed TerrainUtility.GetHeight
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var time = SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            foreach (var (transform, arrow, projectile, entity)
                     in SystemAPI.Query<RefRW<LocalTransform>, RefRW<ArrowProjectile>, RefRW<Projectile>>()
                     .WithEntityAccess())
            {
                ref var trans = ref transform.ValueRW;
                ref var arr = ref arrow.ValueRW;
                ref readonly var proj = ref projectile.ValueRO;

                var arrowPos = trans.Position;
                var shouldDestroy = false;

                // Calculate elapsed time since spawn
                var elapsed = (float)(time - proj.StartTime);

                // Determine if this is a laser projectile (straight-line + terrain collision)
                bool isLaser = em.HasComponent<LaserProjectileTag>(entity);

                if (isLaser)
                {
                    // =================================================================
                    // LASER PATH: straight-line flight, collides with terrain
                    // =================================================================
                    float t = elapsed / proj.FlightTime;

                    if (t > 1.5f)
                    {
                        shouldDestroy = true;
                    }
                    else
                    {
                        // Advance position along constant velocity (straight line)
                        float dt = SystemAPI.Time.DeltaTime;
                        float3 newPos = arrowPos + arr.Velocity * dt;

                        // --- Terrain collision: laser is blocked by terrain ---
                        if (TerrainUtility.IsReady())
                        {
                            float terrainHeight = TerrainUtility.GetHeight(newPos.x, newPos.z);
                            if (newPos.y < terrainHeight + TerrainCollisionMargin)
                            {
                                shouldDestroy = true;
                            }
                        }

                        if (!shouldDestroy)
                        {
                            // Check hit on target
                            float3 targetPos = proj.End;
                            Entity targetEntity = proj.Target;
                            bool targetIsAlive = false;

                            if (targetEntity != Entity.Null && em.Exists(targetEntity))
                            {
                                if (em.HasComponent<Health>(targetEntity))
                                {
                                    var targetHealth = em.GetComponentData<Health>(targetEntity);
                                    if (targetHealth.Value > 0)
                                    {
                                        targetIsAlive = true;
                                        if (em.HasComponent<LocalTransform>(targetEntity))
                                            targetPos = em.GetComponentData<LocalTransform>(targetEntity).Position;
                                    }
                                }
                            }

                            float distToTarget = math.length(targetPos - newPos);

                            if (t >= 0.95f || distToTarget < HitRadius)
                            {
                                ApplyDamage(em, ecb, proj, targetEntity, targetIsAlive, arr.Shooter);
                                shouldDestroy = true;
                            }
                            else
                            {
                                trans.Position = newPos;
                                // Rotation stays fixed (set at creation, pointing along velocity)
                            }
                        }
                    }
                }
                else
                {
                    // =================================================================
                    // ARROW PATH: arched Bezier trajectory with guaranteed hits
                    // =================================================================
                    // Use per-projectile FlightTime for constant speed; fallback to FlightDuration
                    float flightTime = proj.FlightTime > 0.01f ? proj.FlightTime : FlightDuration;
                    float t = elapsed / flightTime;

                    if (t > 1.5f)
                    {
                        shouldDestroy = true;
                    }
                    else
                    {
                        float3 targetPos = proj.End;
                        Entity targetEntity = proj.Target;
                        bool targetIsAlive = false;

                        if (targetEntity != Entity.Null && em.Exists(targetEntity))
                        {
                            if (em.HasComponent<Health>(targetEntity))
                            {
                                var targetHealth = em.GetComponentData<Health>(targetEntity);
                                if (targetHealth.Value > 0)
                                {
                                    targetIsAlive = true;
                                    if (em.HasComponent<LocalTransform>(targetEntity))
                                        targetPos = em.GetComponentData<LocalTransform>(targetEntity).Position;
                                }
                            }
                        }

                        float distToTarget = math.length(targetPos - arrowPos);

                        bool isPiercing = em.HasComponent<PiercingProjectile>(entity);

                        // Piercing bolts: scan for ALL enemies near the bolt's path each frame
                        if (isPiercing && !shouldDestroy)
                        {
                            var pierceScan = _aoeTargetQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                            var pierceTransforms = _aoeTargetQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
                            var pierceFactions = _aoeTargetQuery.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
                            var pierceHealth = _aoeTargetQuery.ToComponentDataArray<Health>(Unity.Collections.Allocator.Temp);

                            var pierce = em.GetComponentData<PiercingProjectile>(entity);
                            for (int pi = 0; pi < pierceScan.Length; pi++)
                            {
                                if (pierceFactions[pi].Value == proj.Faction) continue;
                                if (pierceHealth[pi].Value <= 0) continue; // skip dead
                                // Use XZ distance — bolts fly with upward pitch and no gravity,
                                // so 3D distance would cause them to fly over targets
                                float3 diff = pierceTransforms[pi].Position - arrowPos;
                                float d = math.length(new float2(diff.x, diff.z));
                                if (d < 2.5f)
                                {
                                    ApplyDamage(em, ecb, proj, pierceScan[pi], true, arr.Shooter);
                                    pierce.RemainingPierces--;
                                    if (pierce.RemainingPierces <= 0) { shouldDestroy = true; break; }
                                }
                            }
                            if (!shouldDestroy) em.SetComponentData(entity, pierce);

                            pierceScan.Dispose();
                            pierceTransforms.Dispose();
                            pierceFactions.Dispose();
                            pierceHealth.Dispose();

                            // Move bolt straight through (ignore Bezier homing)
                            if (!shouldDestroy)
                            {
                                float boltDt = SystemAPI.Time.DeltaTime;
                                float3 newPos = arrowPos + math.normalizesafe(arr.Velocity) * BoltSpeed * boltDt;
                                newPos.y = math.max(newPos.y, TerrainUtility.GetHeight(newPos.x, newPos.z) + 0.5f);
                                trans.Position = newPos;
                                // Destroy if past max flight time
                                if (t > 1.5f) shouldDestroy = true;
                            }
                        }
                        else if (!shouldDestroy && (t >= 0.95f || distToTarget < HitRadius))
                        {
                            ApplyDamage(em, ecb, proj, targetEntity, targetIsAlive, arr.Shooter);
                            shouldDestroy = true;
                        }
                        else if (!shouldDestroy)
                        {
                            // Use terrain-sampled heights for slope-accurate start/end
                            float3 startPos = proj.Start; // Already offset +1.5f at creation
                            float terrainAtStart = TerrainUtility.GetHeight(startPos.x, startPos.z);
                            startPos.y = terrainAtStart + 1.5f; // Chest height above terrain

                            float terrainAtEnd = TerrainUtility.GetHeight(targetPos.x, targetPos.z);
                            targetPos.y = terrainAtEnd + 1.0f; // Target center above terrain

                            float3 midpoint = (startPos + targetPos) * 0.5f;
                            float horizontalDist = math.length(new float2(targetPos.x - startPos.x, targetPos.z - startPos.z));
                            float dynamicArcHeight = ArcHeight * math.min(1f, horizontalDist / 15f);
                            float3 controlPoint = midpoint + new float3(0, dynamicArcHeight, 0);

                            float oneMinusT = 1f - t;
                            float3 newPosition =
                                oneMinusT * oneMinusT * startPos +
                                2f * oneMinusT * t * controlPoint +
                                t * t * targetPos;

                            float3 velocity =
                                2f * oneMinusT * (controlPoint - startPos) +
                                2f * t * (targetPos - controlPoint);

                            velocity = math.normalize(velocity) * (horizontalDist / flightTime);

                            trans.Position = newPosition;

                            if (math.lengthsq(velocity) > 0.001f)
                            {
                                CalculateArrowRotation(in velocity, out var rot);
                                trans.Rotation = rot;
                            }

                            arr.Velocity = velocity;
                        }
                    }
                }

                if (shouldDestroy)
                {
                    // AOE splash damage on impact
                    if (em.HasComponent<AOEProjectile>(entity))
                    {
                        ApplyAOEDamage(em, ecb, proj, em.GetComponentData<AOEProjectile>(entity),
                            trans.Position, arrow.ValueRO.Shooter);
                    }

                    ecb.DestroyEntity(entity);
                }
            }
        }

        /// <summary>
        /// Apply projectile damage to a target entity.
        /// Uses CombatModifiers for damage-type vs armor-type modifiers and defense reduction.
        /// Shared between arrow and laser impact paths.
        /// </summary>
        private static void ApplyDamage(EntityManager em, EntityCommandBuffer ecb, in Projectile proj,
            Entity targetEntity, bool targetIsAlive, Entity shooter = default)
        {
            if (!targetIsAlive || targetEntity == Entity.Null || !em.Exists(targetEntity)) return;
            if (!em.HasComponent<Health>(targetEntity)) return;

            int baseDamage = proj.Damage;
            DamageType dmgType = proj.DmgType;

            // Get target's armor type (default InfantryLight if missing)
            ArmorType armorType = ArmorType.InfantryLight;
            if (em.HasComponent<ArmorTypeData>(targetEntity))
                armorType = em.GetComponentData<ArmorTypeData>(targetEntity).Value;

            // Get target's defense for this damage type
            int defenseValue = 0;
            if (em.HasComponent<Defense>(targetEntity))
                defenseValue = CombatModifiers.GetDefenseValue(em.GetComponentData<Defense>(targetEntity), dmgType);

            // Crystal debuff on defender (takes more damage from projectiles)
            float crystalMod = 1.0f;
            if (em.HasComponent<CrystalDebuff>(targetEntity))
            {
                var debuff = em.GetComponentData<CrystalDebuff>(targetEntity);
                crystalMod = 1f + debuff.AttPenalty;
            }

            // Height modifier is already baked into proj.Damage for arrows,
            // so pass 1.0 here to avoid double-applying.
            int impactDamage = CombatModifiers.CalculateFinalDamage(
                baseDamage, dmgType, armorType, defenseValue, 1.0f, crystalMod);

            var targetHealth = em.GetComponentData<Health>(targetEntity);
            targetHealth.Value -= impactDamage;
            if (targetHealth.Value <= 0) targetHealth.Value = 0;
            em.SetComponentData(targetEntity, targetHealth);

            // Track last damager faction for kill credit (used by PillageSystem, CaravanDeathSystem)
            // Use ECB for AddComponent to avoid structural changes during iteration
            if (em.HasComponent<LastDamagedByFaction>(targetEntity))
                em.SetComponentData(targetEntity, new LastDamagedByFaction { Value = proj.Faction });
            else
                ecb.AddComponent(targetEntity, new LastDamagedByFaction { Value = proj.Faction });

            // Track attacker entity for defensive stance return-fire
            if (shooter != Entity.Null && em.Exists(shooter))
            {
                if (em.HasComponent<LastAttackerEntity>(targetEntity))
                    em.SetComponentData(targetEntity, new LastAttackerEntity { Value = shooter });
                else
                    ecb.AddComponent(targetEntity, new LastAttackerEntity { Value = shooter });
            }
        }

        /// <summary>
        /// Apply AOE splash damage to all enemies within radius of impact point.
        /// Skips the primary target (already damaged by ApplyDamage).
        /// </summary>
        private void ApplyAOEDamage(EntityManager em, EntityCommandBuffer ecb, in Projectile proj,
            in AOEProjectile aoe, float3 impactPos, Entity shooter)
        {
            using var entities = _aoeTargetQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _aoeTargetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var factions = _aoeTargetQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            float radiusSq = aoe.Radius * aoe.Radius;

            for (int i = 0; i < entities.Length; i++)
            {
                // Skip same faction
                if (factions[i].Value == proj.Faction) continue;
                // Skip primary target (already hit)
                if (entities[i] == proj.Target) continue;

                float distSq = math.distancesq(impactPos, transforms[i].Position);
                if (distSq > radiusSq) continue;

                // Check health > 0
                var hp = em.GetComponentData<Health>(entities[i]);
                if (hp.Value <= 0) continue;

                // Get target's armor and defense
                ArmorType armorType = ArmorType.InfantryLight;
                if (em.HasComponent<ArmorTypeData>(entities[i]))
                    armorType = em.GetComponentData<ArmorTypeData>(entities[i]).Value;

                int defenseValue = 0;
                if (em.HasComponent<Defense>(entities[i]))
                    defenseValue = CombatModifiers.GetDefenseValue(em.GetComponentData<Defense>(entities[i]), proj.DmgType);

                float crystalMod = 1.0f;
                if (em.HasComponent<CrystalDebuff>(entities[i]))
                    crystalMod = 1f + em.GetComponentData<CrystalDebuff>(entities[i]).AttPenalty;

                int splashDmg = CombatModifiers.CalculateFinalDamage(
                    proj.Damage, proj.DmgType, armorType, defenseValue, 1.0f, crystalMod);

                hp.Value = math.max(0, hp.Value - splashDmg);
                em.SetComponentData(entities[i], hp);

                // Track last damager for kill credit
                if (em.HasComponent<LastDamagedByFaction>(entities[i]))
                    em.SetComponentData(entities[i], new LastDamagedByFaction { Value = proj.Faction });
                else
                    ecb.AddComponent(entities[i], new LastDamagedByFaction { Value = proj.Faction });

                if (shooter != Entity.Null && em.Exists(shooter))
                {
                    if (em.HasComponent<LastAttackerEntity>(entities[i]))
                        em.SetComponentData(entities[i], new LastAttackerEntity { Value = shooter });
                    else
                        ecb.AddComponent(entities[i], new LastAttackerEntity { Value = shooter });
                }
            }
        }

        /// <summary>
        /// Calculate arrow rotation pointing along velocity vector.
        /// Allows full range of pitch angles for realistic arcing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateArrowRotation(in float3 velocity, out quaternion result)
        {
            float3 horizontalDir = math.normalize(new float3(velocity.x, 0, velocity.z));
            float horizontalSpeed = math.length(new float2(velocity.x, velocity.z));

            if (horizontalSpeed < 0.001f)
            {
                result = quaternion.LookRotation(new float3(0, 0, 1), math.up());
                return;
            }

            float pitchAngle = math.atan2(velocity.y, horizontalSpeed);
            pitchAngle = math.clamp(pitchAngle, -MaxPitchAngle, MaxPitchAngle);
            float yaw = math.atan2(horizontalDir.x, horizontalDir.z);

            quaternion yawRotation = quaternion.RotateY(yaw);
            quaternion pitchRotation = quaternion.RotateX(-pitchAngle);
            result = math.mul(yawRotation, pitchRotation);
        }
    }
}