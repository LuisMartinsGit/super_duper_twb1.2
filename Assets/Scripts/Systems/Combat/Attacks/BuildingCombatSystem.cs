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

        // Border emplacements only fire on worker-class units inside this
        // range (guard-the-well rule, Curse & Shardroot canon §2.1) — the
        // crystal fields ring the wells at 24–32 m, comfortably outside.
        private const float BorderWorkerGraceRange = 9f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingRangedAttack>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

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
                .WithNone<UnderConstruction, BuildingUpgrading, NodeDormant>()
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

                // Fiendstone Keep tech ladder (Age 0): AdditionalTowers adds
                // two auto-fire targets; the emplacement techs add extra
                // per-volley shots (fired below, after the normal volley).
                bool isKeep = em.HasComponent<FiendstoneKeepTag>(entity);
                var research = isKeep ? TheWaningBorder.Economy.FactionResearchState.Instance : null;
                if (isKeep && research != null && research.HasResearched(myFaction, "AdditionalTowers"))
                    maxTargets += 2;

                // Find closest enemies within range
                var targets = new NativeList<TargetCandidate>(maxTargets, Allocator.Temp);

                for (int i = 0; i < tgtEntities.Length; i++)
                {
                    // Towers hold fire on allies. docs/Design/Teams.md
                    if (!Alliances.AreHostile(myFaction, tgtFactions[i].Value)) continue;
                    if (tgtHealth[i].Value <= 0) continue;

                    float dist = math.distance(myPos, tgtTransforms[i].Position);
                    if (dist > range) continue;

                    // Curse & Shardroot canon §2.1: BORDER emplacements
                    // (well turrets, Turret sub-nodes) GUARD the well — they
                    // don't hunt harvesters. Worker-class units (miners /
                    // builders) are only fired on when they press right up
                    // to the structure; military targets are engaged
                    // normally. This is what makes sneak-mining the crystal
                    // fields survivable.
                    if (myFaction == Faction.Border && dist > BorderWorkerGraceRange
                        && (em.HasComponent<MinerTag>(tgtEntities[i])
                            || em.HasComponent<CanBuild>(tgtEntities[i])))
                        continue;

                    // A RITUALIST CHANNELLING ON THIS STRUCTURE IS NOT ITS
                    // TARGET. The well turret is 25 damage per 1.2 s — 20.8
                    // DPS — over an 18 m reach, and every verb is channelled
                    // from 6 m, so the caster is never outside it. That kills
                    // a 90 HP Scholar in 4.3 s and a 280 HP Iconoclast in
                    // 13.4 s against channels of 35-45 s. It is not a hard
                    // fight, it is an impossible one: the well simply shoots
                    // whoever comes to claim it. The 2026-08-07 diagnostics
                    // caught it exactly — "channel BROKEN at 13.3 s:
                    // Corruptor dead (hp 0)", one attack cycle off the
                    // predicted 13.44 s.
                    //
                    // Scoped to THIS node deliberately, not blanket immunity:
                    // the well still fires on the escort and on the army
                    // assaulting it, other buildings still fire on the
                    // ritualist, and killing the caster remains the
                    // counterplay — it just has to be done by a player rather
                    // than by the objective defending itself for free.
                    if (em.HasComponent<RitualState>(tgtEntities[i])
                        && em.GetComponentData<RitualState>(tgtEntities[i]).TargetNode == entity)
                        continue;

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
                    // Veilstone buildings fire lasers instead of arrows
                    bool isBorder = em.HasComponent<BorderTag>(entity);

                    // Get building's damage type (default Ranged for arrow buildings, Magic for veilstone)
                    DamageType dmgType = isBorder ? DamageType.Magic : DamageType.Ranged;
                    if (em.HasComponent<DamageTypeData>(entity))
                        dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                    // Veilstone buff/debuff modifiers (same pattern as MeleeCombatSystem)
                    float attackerBorderMod = 1.0f;
                    if (em.HasComponent<BorderBuff>(entity))
                    {
                        var buff = em.GetComponentData<BorderBuff>(entity);
                        attackerBorderMod *= 1f + buff.AttBonus;
                    }

                    // Spawn height: use entity's Radius + 0.5f (taller buildings shoot higher)
                    float spawnYOffset = em.HasComponent<Radius>(entity)
                        ? em.GetComponentData<Radius>(entity).Value + 0.5f
                        : 1.5f;

                    for (int t = 0; t < targets.Length; t++)
                    {
                        // Apply veilstone debuff on target
                        float borderMod = attackerBorderMod;
                        if (em.HasComponent<BorderDebuff>(targets[t].Entity))
                        {
                            var debuff = em.GetComponentData<BorderDebuff>(targets[t].Entity);
                            borderMod *= 1f + debuff.AttPenalty;
                        }

                        int modifiedDamage = math.max(1, (int)(attack.ValueRO.Damage * borderMod));
                        CreateProjectile(ref ecb, myPos, targets[t].Position,
                            targets[t].Distance, entity, myFaction,
                            modifiedDamage, time, targets[t].Entity, isBorder, dmgType, spawnYOffset);
                    }

                    // Keep emplacements: extra per-volley shots at the nearest
                    // target. Ballista = single-target siege bolt; Trebuchet =
                    // arcing siege shell with splash.
                    if (isKeep && research != null)
                    {
                        var nearest = targets[0];
                        if (research.HasResearched(myFaction, "BallistaEmplacement"))
                        {
                            CreateProjectile(ref ecb, myPos, nearest.Position,
                                nearest.Distance, entity, myFaction,
                                18, time, nearest.Entity, isLaser: false,
                                DamageType.Siege, spawnYOffset);
                        }
                        if (research.HasResearched(myFaction, "TrebuchetEmplacement"))
                        {
                            CreateTrebuchetShot(ref ecb, myPos, nearest.Position,
                                entity, myFaction, 36, time, nearest.Entity, spawnYOffset);
                        }
                    }

                    attack.ValueRW.Timer = attack.ValueRO.Cooldown;
                }

                targets.Dispose();
            }

            tgtEntities.Dispose();
            tgtTransforms.Dispose();
            tgtFactions.Dispose();
            tgtHealth.Dispose();
        }

        private static void CreateProjectile(ref EntityCommandBuffer ecb,
            float3 start, float3 targetPos, float distance,
            Entity shooter, Faction faction, int damage, float time, Entity target,
            bool isLaser = false, DamageType dmgType = DamageType.Ranged, float spawnYOffset = 1.5f)
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
                Position = start + new float3(0, spawnYOffset, 0),
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

            // Veilstone buildings fire lasers — tag for visual system
            if (isLaser)
            {
                ecb.AddComponent<LaserProjectileTag>(projectile);
            }
        }

        /// <summary>
        /// Trebuchet Emplacement shot: a slow, high parabolic siege shell with
        /// splash damage on impact (mirrors the Godsplinter bombard shape —
        /// HighArcProjectile + AOEProjectile, no LaserProjectileTag so
        /// ProjectileSystem runs the Bezier arc path).
        /// </summary>
        private static void CreateTrebuchetShot(ref EntityCommandBuffer ecb,
            float3 start, float3 targetPos, Entity shooter, Faction faction,
            int damage, float time, Entity target, float spawnYOffset)
        {
            float3 spawnPos = start + new float3(0, spawnYOffset, 0);
            var direction = math.normalizesafe(targetPos - spawnPos, new float3(0, 0, 1));

            var shell = ecb.CreateEntity();
            ecb.AddComponent(shell, new LocalTransform
            {
                Position = spawnPos,
                Rotation = quaternion.LookRotation(direction, new float3(0, 1, 0)),
                Scale = 1f
            });
            ecb.AddComponent(shell, new ArrowProjectile
            {
                Velocity = direction,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = true
            });
            ecb.AddComponent(shell, new Projectile
            {
                Start = spawnPos,
                End = targetPos,
                StartTime = time,
                FlightTime = 1.2f,
                Damage = damage,
                Target = target,
                Faction = faction,
                DmgType = DamageType.Siege
            });
            ecb.AddComponent(shell, new AOEProjectile { Radius = 4f });
            ecb.AddComponent(shell, new HighArcProjectile { ArcFraction = 0.25f });
        }

        private struct TargetCandidate
        {
            public Entity Entity;
            public float3 Position;
            public float Distance;
        }
    }
}
