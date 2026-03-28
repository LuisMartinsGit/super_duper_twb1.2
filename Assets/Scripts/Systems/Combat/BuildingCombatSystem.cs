// File: Assets/Scripts/Systems/Combat/BuildingCombatSystem.cs
// Handles ranged attacks for buildings (Hall, Fiendstone Keep, etc.)
// Buildings auto-target and fire at enemies within range.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct BuildingCombatSystem : ISystem
    {
        private const float ArrowSpeed = 25f;
        private const float LaserSpeed = 55f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingRangedAttack>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Snapshot all potential targets (anything with Health + FactionTag + Transform)
            var targetQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                .Build();

            var tgtEntities = targetQuery.ToEntityArray(Allocator.Temp);
            var tgtTransforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var tgtFactions = targetQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var tgtHealth = targetQuery.ToComponentDataArray<Health>(Allocator.Temp);

            // Process all buildings with ranged attack
            foreach (var (transform, attack, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<BuildingRangedAttack>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                // Tick cooldown
                if (attack.ValueRO.Timer > 0f)
                {
                    attack.ValueRW.Timer -= dt;
                    continue;
                }

                var myPos = transform.ValueRO.Position;
                var myFaction = faction.ValueRO.Value;
                float range = attack.ValueRO.Range;
                int maxTargets = math.max(1, attack.ValueRO.MaxTargets);

                // Find closest enemies within range
                var targets = new NativeList<TargetCandidate>(maxTargets, Allocator.Temp);

                for (int i = 0; i < tgtEntities.Length; i++)
                {
                    if (tgtFactions[i].Value == myFaction) continue;
                    if (tgtHealth[i].Value <= 0) continue;

                    float dist = math.distance(myPos, tgtTransforms[i].Position);
                    if (dist > range) continue;

                    // Insert sorted by distance (keep only maxTargets closest)
                    var candidate = new TargetCandidate
                    {
                        Entity = tgtEntities[i],
                        Position = tgtTransforms[i].Position,
                        Distance = dist
                    };

                    if (targets.Length < maxTargets)
                    {
                        targets.Add(candidate);
                    }
                    else if (dist < targets[targets.Length - 1].Distance)
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

                // Fire at each target
                if (targets.Length > 0)
                {
                    // Crystal buildings fire lasers instead of arrows
                    bool isCrystal = em.HasComponent<CrystalTag>(entity);

                    // Get building's damage type (default Ranged for arrow buildings, Magic for crystal)
                    DamageType dmgType = isCrystal ? DamageType.Magic : DamageType.Ranged;
                    if (em.HasComponent<DamageTypeData>(entity))
                        dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                    // Crystal buff/debuff modifiers (same pattern as MeleeCombatSystem)
                    float attackerCrystalMod = 1.0f;
                    if (em.HasComponent<CrystalBuff>(entity))
                    {
                        var buff = em.GetComponentData<CrystalBuff>(entity);
                        attackerCrystalMod *= 1f + buff.AttBonus;
                    }

                    for (int t = 0; t < targets.Length; t++)
                    {
                        // Apply crystal debuff on target
                        float crystalMod = attackerCrystalMod;
                        if (em.HasComponent<CrystalDebuff>(targets[t].Entity))
                        {
                            var debuff = em.GetComponentData<CrystalDebuff>(targets[t].Entity);
                            crystalMod *= 1f + debuff.AttPenalty;
                        }

                        int modifiedDamage = math.max(1, (int)(attack.ValueRO.Damage * crystalMod));
                        CreateProjectile(ref ecb, myPos, targets[t].Position,
                            targets[t].Distance, entity, myFaction,
                            modifiedDamage, time, targets[t].Entity, isCrystal, dmgType);
                    }
                    attack.ValueRW.Timer = attack.ValueRO.Cooldown;
                }

                targets.Dispose();
            }

            tgtEntities.Dispose();
            tgtTransforms.Dispose();
            tgtFactions.Dispose();
            tgtHealth.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }

        private static void CreateProjectile(ref EntityCommandBuffer ecb,
            float3 start, float3 targetPos, float distance,
            Entity shooter, Faction faction, int damage, float time, Entity target,
            bool isLaser = false, DamageType dmgType = DamageType.Ranged)
        {
            float speed = isLaser ? LaserSpeed : ArrowSpeed;
            var direction = math.normalize(targetPos - start);

            // Add upward arc for arrows only — lasers fly straight
            if (!isLaser)
            {
                float minPitch = math.radians(10f);
                float currentPitch = math.asin(direction.y);
                if (currentPitch < minPitch)
                {
                    float3 horizontalDir = math.normalize(new float3(direction.x, 0, direction.z));
                    direction = horizontalDir * math.cos(minPitch) + new float3(0, math.sin(minPitch), 0);
                    direction = math.normalize(direction);
                }
            }

            var velocity = direction * speed;
            var flightTime = distance / speed;

            var projectile = ecb.CreateEntity();

            ecb.AddComponent(projectile, new LocalTransform
            {
                Position = start + new float3(0, 3f, 0), // Spawn above building
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(projectile, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = false
            });

            ecb.AddComponent(projectile, new Projectile
            {
                Start = start,
                End = targetPos,
                StartTime = time,
                FlightTime = flightTime,
                Damage = damage,
                Target = target,
                Faction = faction,
                DmgType = dmgType
            });

            // Crystal buildings fire lasers — tag for visual system
            if (isLaser)
            {
                ecb.AddComponent<LaserProjectileTag>(projectile);
            }
        }

        private struct TargetCandidate
        {
            public Entity Entity;
            public float3 Position;
            public float Distance;
        }
    }
}
