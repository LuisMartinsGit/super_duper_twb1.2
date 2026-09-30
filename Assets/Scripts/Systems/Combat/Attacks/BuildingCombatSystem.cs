// Handles ranged attacks for buildings (Hall, Fiendstone Keep, etc.)
// Buildings auto-target and fire at enemies within range.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct BuildingCombatSystem : ISystem
    {
        // 25 -> 32.5 (2026-09-03): arrow flight sped up 30 percent.
        private const float ArrowSpeed = 32.5f;
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
            float time = (float)SimCadence.MatchTimeOr(SystemAPI.Time.ElapsedTime);
            var em = state.EntityManager;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Snapshot all potential targets (anything with Health + FactionTag
            // + Transform) — MINUS the three things nothing may auto-acquire.
            //
            // THE SAME EXCLUSIONS TargetingSystem APPLIES (2026-09-08). This
            // query had none at all, so every Hall, tower and Keep in the game
            // was shooting things units are forbidden to shoot:
            //
            //   * NodeNoAutoAcquire — a verb WELL. Reported from a live match:
            //     Halls were shooting wells down with arrows, which deletes the
            //     Purify / Pacify / Corrupt objective and the Shardroot with it.
            //     A well is cracked open by a Feraldis Corruptor's ritual and
            //     by nothing else (CorruptionRitualSystem removes the tag);
            //     until then it is not a target for anyone.
            //   * NodeUntargetable — a rubble / rebuilding / cleansed node.
            //     An immune husk, so firing at it was pure wasted volleys.
            //   * SectVeiled — Stoneveil (Fortitude) makes a unit untargetable.
            //     Towers ignored the veil the player had just paid for.
            var targetQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                .WithNone<NodeUntargetable, NodeNoAutoAcquire>()
                .WithNone<SectVeiled>()
                .Build();

            // PASS 1 — tick cooldowns, collect the towers ready to fire, in
            // query order (2026-09-25). The four-array snapshot of every
            // target in the world used to be taken every tick even when every
            // tower was mid-cooldown, which late game is nearly every tick.
            var ready = new NativeList<Entity>(16, Allocator.Temp);
            foreach (var (attack, entity) in SystemAPI
                .Query<RefRW<BuildingRangedAttack>>()
                .WithAll<BuildingTag, LocalTransform, FactionTag>()
                // BuildingCollapseState is the buildings' DeathAnimationState:
                // a tower that has already fallen kept firing for the whole
                // collapse.
                .WithNone<UnderConstruction, BuildingUpgrading, NodeDormant>()
                .WithNone<BuildingCollapseState>()
                .WithEntityAccess())
            {
                // Tick cooldown
                if (attack.ValueRO.Timer > 0f)
                {
                    attack.ValueRW.Timer -= dt;
                    continue;
                }
                ready.Add(entity);
            }

            if (ready.Length == 0)
            {
                ready.Dispose();
                return;
            }

            // A ready tower that finds nothing waits this long (sim time, so
            // identical on every peer) instead of re-scanning every tick.
            var cfg = BuildingCombatSystemConfig.I;
            float noTargetRetry = cfg != null ? cfg.noTargetRetryDelay : 0f;

            var tgtEntities = targetQuery.ToEntityArray(Allocator.Temp);
            var tgtTransforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var tgtFactions = targetQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var tgtHealth = targetQuery.ToComponentDataArray<Health>(Allocator.Temp);
            // Footprints, resolved once per update rather than once per
            // tower x target: range is measured to the target's SURFACE
            // (TargetGeometry), so a 7x7 Hall is in range when its wall is,
            // not only when its pivot is. `tgtPad` is the footprint's
            // farthest reach from the pivot, for the cheap reject.
            var tgtExtents = new NativeArray<TargetExtent>(tgtEntities.Length, Allocator.Temp);
            var tgtPad = new NativeArray<float>(tgtEntities.Length, Allocator.Temp);
            for (int i = 0; i < tgtEntities.Length; i++)
            {
                var ext = TargetGeometry.Extent(em, tgtEntities[i]);
                tgtExtents[i] = ext;
                tgtPad[i] = ext.IsBox
                    ? math.sqrt(ext.HalfW * ext.HalfW + ext.HalfH * ext.HalfH)
                    : ext.Radius;
            }

            // PASS 2 — the ready towers, same order as the old single loop.
            for (int ri = 0; ri < ready.Length; ri++)
            {
                var entity = ready[ri];
                var attackData = em.GetComponentData<BuildingRangedAttack>(entity);
                var myPos = em.GetComponentData<LocalTransform>(entity).Position;
                var myFaction = em.GetComponentData<FactionTag>(entity).Value;
                float range = attackData.Range;
                // Cheap squared reject before the exact distance: the bound is
                // padded, so every candidate the exact test would accept
                // still reaches it and the accepted set is unchanged.
                float rejectSq = (range * 1.001f + 0.01f) * (range * 1.001f + 0.01f);
                int maxTargets = math.max(1, attackData.MaxTargets);

                // Fiendstone Keep tech ladder (Age 0): AdditionalTowers adds
                // two auto-fire targets; the emplacement techs add extra
                // per-volley shots (fired below, after the normal volley).
                bool isKeep = em.HasComponent<FiendstoneKeepTag>(entity);
                var research = isKeep ? TheWaningBorder.Economy.FactionResearchState.Instance : null;
                if (isKeep && research != null && research.HasResearched(myFaction, "AdditionalTowers"))
                    maxTargets += 2;

                // The Wall Rule (docs/Design/Combat_Pacing.md): a building
                // whose own fire is not siege never takes a wall piece as a
                // target - ProjectileSystem would zero the hit anyway, so a
                // wall in the list only wastes a volley slot. The Keep's
                // Ballista / Trebuchet emplacement shots ARE siege, so a Keep
                // that has them remembers the nearest wall as a fallback for
                // those shots alone.
                DamageType dmgType = MainDamageType(em, entity);
                bool mainIsSiege = dmgType == DamageType.Siege;
                bool hasSiegeShot = em.HasComponent<BuildingSiegeShot>(entity);
                bool keepSiege = hasSiegeShot || (isKeep && research != null
                    && (research.HasResearched(myFaction, "BallistaEmplacement")
                        || research.HasResearched(myFaction, "TrebuchetEmplacement")));
                bool haveWall = false;
                var nearestWall = default(TargetCandidate);

                // Directed fire (docs/Design/Combat_Pacing.md § Directed
                // building fire): a legal forced target in range takes the
                // FIRST slot; out of range the order is simply kept. An
                // illegal one (dead, allied, veiled, a wall for non-siege
                // fire) ends the order.
                Entity forced = Entity.Null;
                var forcedCandidate = default(TargetCandidate);
                if (em.HasComponent<BuildingForcedTarget>(entity))
                {
                    var ft = em.GetComponentData<BuildingForcedTarget>(entity).Target;
                    if (!IsLegalForcedTarget(em, entity, ft))
                    {
                        // Direct, not through the ECB: the loop walks a copied
                        // list, so the structural change is safe here, and a
                        // deferred removal could land on a building the same
                        // playback destroys.
                        em.RemoveComponent<BuildingForcedTarget>(entity);
                    }
                    else
                    {
                        // Gate on the SURFACE distance; the pivot stays the
                        // aim point and its distance the projectile's flight.
                        var fpos = em.GetComponentData<LocalTransform>(ft).Position;
                        float fdist = math.distance(myPos, fpos);
                        if (TargetGeometry.SurfaceDistXZ(em, myPos, ft) <= range)
                        {
                            forced = ft;
                            forcedCandidate = new TargetCandidate { Entity = ft, Position = fpos, Distance = fdist };
                        }
                    }
                }
                int autoSlots = forced != Entity.Null ? maxTargets - 1 : maxTargets;

                // Find closest enemies within range
                var targets = new NativeList<TargetCandidate>(maxTargets, Allocator.Temp);

                for (int i = 0; i < tgtEntities.Length; i++)
                {
                    // Every slot belongs to the forced target: nothing to
                    // auto-pick (a one-target tower under a direct order).
                    if (autoSlots <= 0 && !keepSiege) break;
                    if (tgtEntities[i] == forced) continue;
                    // Towers hold fire on allies. docs/Design/Teams.md
                    if (!Alliances.AreHostile(myFaction, tgtFactions[i].Value)) continue;
                    if (tgtHealth[i].Value <= 0) continue;
                    // Cheap reject on the pivot, padded by the footprint so
                    // a building whose wall is in range still reaches the
                    // exact test.
                    float3 tp = tgtTransforms[i].Position;
                    float rx = tp.x - myPos.x, rz = tp.z - myPos.z;
                    float padded = math.sqrt(rejectSq) + tgtPad[i];
                    if (rx * rx + rz * rz > padded * padded) continue;

                    // Range gate on the SURFACE distance; `dist` (pivot) is
                    // still what the aim, the ordering and the flight use.
                    if (tgtExtents[i].SurfaceDistXZ(myPos) > range) continue;
                    float dist = math.distance(myPos, tp);

                    if (!mainIsSiege && em.HasComponent<WallTag>(tgtEntities[i]))
                    {
                        if (keepSiege && (!haveWall || dist < nearestWall.Distance))
                        {
                            nearestWall = new TargetCandidate
                            {
                                Entity = tgtEntities[i],
                                Position = tgtTransforms[i].Position,
                                Distance = dist
                            };
                            haveWall = true;
                        }
                        continue;
                    }
                    if (autoSlots <= 0) continue;

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

                    if (targets.Length < autoSlots)
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

                // The forced target leads the list.
                if (forced != Entity.Null)
                {
                    targets.Add(default);
                    for (int j = targets.Length - 1; j > 0; j--) targets[j] = targets[j - 1];
                    targets[0] = forcedCandidate;
                }

                // Fire at each target
                if (targets.Length > 0 || haveWall)
                {
                    // Veilstone buildings fire lasers instead of arrows
                    bool isBorder = em.HasComponent<BorderTag>(entity);

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

                        int modifiedDamage = math.max(1, (int)(attackData.Damage * borderMod));
                        CreateProjectile(ref ecb, myPos, targets[t].Position,
                            targets[t].Distance, entity, myFaction,
                            modifiedDamage, time, targets[t].Entity, isBorder, dmgType, spawnYOffset);
                    }

                    // Keep emplacements: extra per-volley shots at the nearest
                    // target. Ballista = single-target siege bolt; Trebuchet =
                    // arcing siege shell with splash. With nothing else in
                    // range they take the nearest wall piece: siege is the
                    // one thing allowed to (the Wall Rule).
                    // A levelled building's ballista bolt (BuildingSiegeShot —
                    // the Watch Tower's L3): one siege bolt per volley at the
                    // nearest target, on top of its arrows.
                    if (hasSiegeShot)
                    {
                        var nearest = targets.Length > 0 ? targets[0] : nearestWall;
                        CreateProjectile(ref ecb, myPos, nearest.Position,
                            nearest.Distance, entity, myFaction,
                            em.GetComponentData<BuildingSiegeShot>(entity).Damage, time,
                            nearest.Entity, isLaser: false, DamageType.Siege, spawnYOffset,
                            ballistaBolt: true);
                    }

                    if (isKeep && research != null)
                    {
                        var nearest = targets.Length > 0 ? targets[0] : nearestWall;
                        if (research.HasResearched(myFaction, "BallistaEmplacement"))
                        {
                            CreateProjectile(ref ecb, myPos, nearest.Position,
                                nearest.Distance, entity, myFaction,
                                18, time, nearest.Entity, isLaser: false,
                                DamageType.Siege, spawnYOffset, ballistaBolt: true);
                        }
                        if (research.HasResearched(myFaction, "TrebuchetEmplacement"))
                        {
                            CreateTrebuchetShot(ref ecb, myPos, nearest.Position,
                                entity, myFaction, 36, time, nearest.Entity, spawnYOffset);
                        }
                    }

                    attackData.Timer = attackData.Cooldown;
                    em.SetComponentData(entity, attackData);
                }
                else if (noTargetRetry > 0f)
                {
                    attackData.Timer = noTargetRetry;
                    em.SetComponentData(entity, attackData);
                }

                targets.Dispose();
            }

            tgtEntities.Dispose();
            tgtTransforms.Dispose();
            tgtFactions.Dispose();
            tgtHealth.Dispose();
            tgtExtents.Dispose();
            tgtPad.Dispose();
            ready.Dispose();
        }

        /// <summary>
        /// A building's own fire type: Magic for the curse's emplacements,
        /// Ranged for arrow buildings, unless the building carries an explicit
        /// DamageTypeData.
        /// </summary>
        public static DamageType MainDamageType(EntityManager em, Entity building)
        {
            if (em.HasComponent<DamageTypeData>(building))
                return em.GetComponentData<DamageTypeData>(building).Value;
            return em.HasComponent<BorderTag>(building) ? DamageType.Magic : DamageType.Ranged;
        }

        /// <summary>
        /// May <paramref name="building"/>'s fire be directed at
        /// <paramref name="target"/>? The same exclusions the auto-target
        /// snapshot applies, plus hostility and the Wall Rule. One test for
        /// the command's issuer, its executor on every peer, and the combat
        /// system that keeps or drops the order each volley.
        /// docs/Design/Combat_Pacing.md § Directed building fire
        /// </summary>
        public static bool IsLegalForcedTarget(EntityManager em, Entity building, Entity target)
        {
            if (building == Entity.Null || target == Entity.Null) return false;
            if (!em.Exists(building) || !em.Exists(target)) return false;
            if (!em.HasComponent<BuildingRangedAttack>(building) || !em.HasComponent<FactionTag>(building)) return false;
            if (!em.HasComponent<Health>(target) || !em.HasComponent<FactionTag>(target)
                || !em.HasComponent<LocalTransform>(target)) return false;
            if (em.GetComponentData<Health>(target).Value <= 0) return false;
            if (!Alliances.AreHostile(em.GetComponentData<FactionTag>(building).Value,
                                      em.GetComponentData<FactionTag>(target).Value)) return false;
            if (em.HasComponent<SectVeiled>(target) || em.HasComponent<NodeUntargetable>(target)
                || em.HasComponent<NodeNoAutoAcquire>(target)) return false;
            if (CombatDamageHelper.WallRuleBlocks(em, target, MainDamageType(em, building))) return false;
            return true;
        }

        private static void CreateProjectile(ref EntityCommandBuffer ecb,
            float3 start, float3 targetPos, float distance,
            Entity shooter, Faction faction, int damage, float time, Entity target,
            bool isLaser = false, DamageType dmgType = DamageType.Ranged, float spawnYOffset = 1.5f,
            bool ballistaBolt = false)
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
            if (ballistaBolt)
                ecb.AddComponent<BallistaBoltTag>(projectile);
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
            // Rendered as the Synty siege rock, like every trebuchet stone.
            ecb.AddComponent<TrebuchetStoneTag>(shell);
        }

        private struct TargetCandidate
        {
            public Entity Entity;
            public float3 Position;
            public float Distance;
        }
    }
}
