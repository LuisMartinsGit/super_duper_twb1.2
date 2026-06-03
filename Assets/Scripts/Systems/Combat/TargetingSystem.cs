// File: Assets/Scripts/Systems/Combat/TargetingSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles target acquisition and combat command processing.
    /// 
    /// Responsibilities:
    /// - Process user AttackCommand components
    /// - Auto-acquire targets for idle units within line of sight
    /// - Initialize combat-related components (GuardPoint, AttackCooldown)
    /// - Handle return-to-guard behavior when no enemies present
    /// - Clean up stale attack commands
    /// 
    /// Respects UserMoveOrder tag to prevent interrupting player movement commands.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Navigation.UnitIntegratorSystem))]
    public partial struct TargetingSystem : ISystem
    {
        private const float MaxGuardDistance = 20f;
        private const float GuardReturnThreshold = 2f;
        private const float DefaultMeleeRange = 1.5f;

        // Fix #207: spatial-hash cell size for the enemy scan.
        // Cell=20 means a unit with LOS<=20 only visits a 3x3 neighborhood
        // (9 cells); LOS<=40 (aggressive-stance boost) visits 5x5 (25 cells).
        // Keeps per-unit inner-loop work bounded regardless of total enemy count.
        private const float TargetingCellSize = 20f;

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
            var em = state.EntityManager;

            // =============================================================================
            // PHASE 0: Initialize required components for combat
            // =============================================================================
            InitializeCombatComponents(ref state, ref ecb);

            // =============================================================================
            // PHASE 1: Handle user attack commands
            // =============================================================================
            ProcessAttackCommands(ref state, ref ecb);

            // Build enemy arrays ONCE for both auto-acquire and return-to-guard phases
            // Exclude NodeUntargetable — crystal nodes are immune to targeting unless
            // an Iconoclast is in aura range (IconoclastAuraSystem toggles the tag).
            var enemyQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, Health>()
                .WithNone<NodeUntargetable>()
                .Build();

            using var allEnemies = enemyQuery.ToEntityArray(Allocator.Temp);
            using var allEnemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var allEnemyFactions = enemyQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var allEnemyHealth = enemyQuery.ToComponentDataArray<Health>(Allocator.Temp);

            // Fix #207: build a spatial hash so per-unit enemy scans visit
            // only nearby cells instead of every enemy in the world. Shared
            // between AutoAcquireTargets and ProcessReturnToGuard.
            using var spatialMap = new NativeParallelMultiHashMap<int2, int>(
                math.max(16, allEnemies.Length * 2), Allocator.Temp);
            for (int i = 0; i < allEnemies.Length; i++)
            {
                var pos = allEnemyTransforms[i].Position;
                var cell = new int2(
                    (int)math.floor(pos.x / TargetingCellSize),
                    (int)math.floor(pos.z / TargetingCellSize));
                spatialMap.Add(cell, i);
            }

            // Per-target attacker count — spreads attackers across multiple
            // enemies so rank 2 of a melee column picks a different enemy than
            // rank 1 instead of queuing up behind it. Snapshot built from
            // existing Target components, then incremented in-place as we
            // assign new targets during this OnUpdate so the same enemy can't
            // be re-picked once it hits MaxAttackersPerEnemy.
            var attackerCount = new NativeHashMap<Entity, int>(
                math.max(16, allEnemies.Length), Allocator.Temp);
            var attackerSnapshotQuery = SystemAPI.QueryBuilder()
                .WithAll<Target, UnitTag>()
                .Build();
            using (var attackerTgts = attackerSnapshotQuery.ToComponentDataArray<Target>(Allocator.Temp))
            {
                for (int i = 0; i < attackerTgts.Length; i++)
                {
                    var t = attackerTgts[i].Value;
                    if (t == Entity.Null) continue;
                    if (attackerCount.TryGetValue(t, out int c)) attackerCount[t] = c + 1;
                    else attackerCount.Add(t, 1);
                }
            }

            // =============================================================================
            // PHASE 2: Auto-acquire targets for idle units
            // =============================================================================
            AutoAcquireTargets(ref state, ref ecb, allEnemies, allEnemyTransforms, allEnemyFactions, allEnemyHealth, spatialMap, ref attackerCount);

            // =============================================================================
            // PHASE 3: Return to guard point (handled after combat systems process)
            // =============================================================================
            ProcessReturnToGuard(ref state, ref ecb, allEnemies, allEnemyTransforms, allEnemyFactions, allEnemyHealth, spatialMap);

            attackerCount.Dispose();

            // =============================================================================
            // PHASE 4: Clean up stale AttackCommand components
            // =============================================================================
            CleanupStaleCommands(ref state, ref ecb);

            // =============================================================================
            // PHASE 5: Clear LastAttackerEntity to prevent stale references
            // =============================================================================
            CleanupLastAttacker(ref state, ref ecb);
        }

        [BurstCompile]
        private void InitializeCombatComponents(ref SystemState state, ref EntityCommandBuffer ecb)
        {
            // Initialize GuardPoint for units that don't have one
            // Skip curse units — long-range hunters driven by CrystalAISystem wave dispatch;
            // the 20m guard leash below would yank them home mid-march.
            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithNone<GuardPoint>()
                .WithNone<CrystalUnitTag>()
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

        [BurstCompile]
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

        // Cap how many MELEE attackers can target the same enemy at once.
        // Once the cap is hit, overflow melee attackers pick a different
        // nearby enemy and walk around the front-line clump to reach it.
        // Falls back to absolute-nearest if no under-cap enemy sits within
        // SpreadDistRatio × nearest, so units don't trek across the map to
        // attack a distant under-cap target when a saturated one is right
        // in front of them.
        // Does NOT apply to ranged/siege units — they fire from afar and
        // don't physically clump, so concentrating fire is fine.
        private const int MaxAttackersPerEnemy = 8;
        private const float SpreadDistRatio = 1.5f;

        [BurstCompile]
        private void AutoAcquireTargets(ref SystemState state, ref EntityCommandBuffer ecb,
            NativeArray<Entity> allEnemies, NativeArray<LocalTransform> allEnemyTransforms,
            NativeArray<FactionTag> allEnemyFactions, NativeArray<Health> allEnemyHealth,
            NativeParallelMultiHashMap<int2, int> spatialMap,
            ref NativeHashMap<Entity, int> attackerCount)
        {
            var em = state.EntityManager;

            // Single unified loop for all target-seeking units:
            // idle units, attack-move units, and patrol units.
            // Builders and miners are excluded.
            foreach (var (transform, faction, lineOfSight, target, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<LineOfSight>, RefRO<Target>>()
                .WithAll<UnitTag>()
                .WithNone<AttackCommand>()
                .WithNone<CanBuild>()           // Builders are passive workers
                .WithNone<MinerTag>()           // Miners are handled by MiningSystem
                .WithEntityAccess())
            {
                // Skip units that already have an active target
                if (target.ValueRO.Value != Entity.Null) continue;

                // Scouts are vision-only by design. Even if a future tech /
                // TechTreeDB entry ever bumps their Damage above 0, they
                // must NEVER auto-engage — they are the AI's and player's
                // sole map-vision tool and the AI patrol loop relies on
                // them staying alive. Explicit class check guarantees this
                // regardless of the damage value below.
                if (em.HasComponent<UnitTag>(entity) &&
                    em.GetComponentData<UnitTag>(entity).Class == UnitClass.Scout)
                    continue;

                // Damage gate — only units that actually deal damage
                // engage enemies. Litharchs (and any future zero-damage
                // support tier) sit idle in their formation slot until a
                // tech upgrades their damage above 0. Without this, a
                // Litharch with Damage=0 and a cooldown timer would still
                // pursue every enemy in LOS and stand in melee range
                // doing nothing — design rule per the spec sweep.
                if (!em.HasComponent<Damage>(entity)) continue;
                if (em.GetComponentData<Damage>(entity).Value <= 0) continue;

                // Cache HasComponent results to avoid repeated lookups
                bool hasAttackMove = em.HasComponent<AttackMoveTag>(entity);
                bool hasPatrol = em.HasComponent<PatrolTag>(entity);
                bool hasUserMoveOrder = em.HasComponent<UserMoveOrder>(entity);
                bool isActiveScanner = hasAttackMove || hasPatrol;

                // Idle units (no AttackMove/Patrol) with UserMoveOrder skip targeting
                if (!isActiveScanner && hasUserMoveOrder) continue;

                // Idle units skip if currently moving to a destination
                if (!isActiveScanner && em.HasComponent<DesiredDestination>(entity))
                {
                    var dd = em.GetComponentData<DesiredDestination>(entity);
                    if (dd.Has != 0)
                    {
                        continue;
                    }
                }

                var myPos = transform.ValueRO.Position;
                var myFaction = faction.ValueRO.Value;
                var los = lineOfSight.ValueRO.Radius;

                // ── LOS boost: every unit auto-acquires aggressively (+50%
                // LOS). This preserves the long-standing single-unit
                // behaviour after the battalion / stance system was removed.
                los *= 1.5f;

                // ── Idle-only guard distance constraint ──
                // An idle unit that has wandered beyond MaxGuardDistance from
                // its guard point is sent back instead of acquiring a target.
                // Attack-move / patrol scanners are exempt (they advance).
                if (!isActiveScanner)
                {
                    if (em.HasComponent<GuardPoint>(entity))
                    {
                        var guardPoint = em.GetComponentData<GuardPoint>(entity);
                        if (guardPoint.Has != 0)
                        {
                            var distFromGuard = DistXZ(myPos, guardPoint.Position);
                            if (distFromGuard > MaxGuardDistance)
                            {
                                if (!em.HasComponent<DesiredDestination>(entity))
                                {
                                    ecb.AddComponent(entity, new DesiredDestination
                                    {
                                        Position = guardPoint.Position,
                                        Has = 1
                                    });
                                }
                                else
                                {
                                    ecb.SetComponent(entity, new DesiredDestination
                                    {
                                        Position = guardPoint.Position,
                                        Has = 1
                                    });
                                }
                                continue;
                            }
                        }
                    }
                }

                // Two-pass scan throughout: track both absolute-nearest enemy
                // ("anyBest") and nearest under-cap enemy ("underBest"). Pick
                // under-cap only when (a) the attacker is melee (ranged/siege
                // don't physically clump, so they always pick nearest), AND
                // (b) the under-cap candidate is within SpreadDistRatio of
                // anyBest (so we don't march 50m to attack a far-away target
                // when a saturated one is right in front of us). Otherwise
                // fall back to anyBest — overflow attackers will dogpile.
                bool isMelee = !em.HasComponent<UnitTag>(entity)
                    || em.GetComponentData<UnitTag>(entity).Class == UnitClass.Melee;

                Entity bestTarget = Entity.Null;
                Entity underBest = Entity.Null;
                float underBestDist = float.MaxValue;
                Entity anyBest = Entity.Null;
                float anyBestDist = float.MaxValue;

                // ── Spatial-hash enemy scan (Fix #207) ──
                // Visit only cells within LOS instead of iterating all enemies.
                {
                    int radius = (int)math.ceil(los / TargetingCellSize);
                    var myCell = new int2(
                        (int)math.floor(myPos.x / TargetingCellSize),
                        (int)math.floor(myPos.z / TargetingCellSize));

                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            var cell = new int2(myCell.x + dx, myCell.y + dy);
                            if (!spatialMap.TryGetFirstValue(cell, out int i, out var it)) continue;
                            do
                            {
                                if (allEnemyFactions[i].Value == myFaction) continue;
                                if (allEnemyHealth[i].Value <= 0) continue;

                                var enemyPos = allEnemyTransforms[i].Position;
                                var dist = DistXZ(myPos, enemyPos);
                                if (dist > los) continue;

                                // Skip stealthed enemies unless within proximity reveal range (3u)
                                if (em.HasComponent<StealthTag>(allEnemies[i]) && dist > 3f)
                                    continue;

                                if (dist < anyBestDist) { anyBest = allEnemies[i]; anyBestDist = dist; }
                                if (isMelee)
                                {
                                    int curCount = attackerCount.TryGetValue(allEnemies[i], out int cv) ? cv : 0;
                                    if (curCount < MaxAttackersPerEnemy && dist < underBestDist)
                                    {
                                        underBest = allEnemies[i];
                                        underBestDist = dist;
                                    }
                                }
                            } while (spatialMap.TryGetNextValue(out i, ref it));
                        }
                    }

                    bestTarget = PickSpreadOrNearest(underBest, underBestDist, anyBest, anyBestDist);
                }

                // Record the assignment so the next iteration in this same
                // AutoAcquireTargets pass sees the updated count (prevents two
                // simultaneously-assigned attackers from both picking the same
                // enemy because both saw count=0).
                if (bestTarget != Entity.Null)
                {
                    int prev = attackerCount.TryGetValue(bestTarget, out int pv) ? pv : 0;
                    attackerCount[bestTarget] = prev + 1;
                }

                if (bestTarget != Entity.Null && em.Exists(bestTarget))
                {
                    ecb.SetComponent(entity, new Target { Value = bestTarget });

                    // Attack-move and patrol units also issue an AttackCommand so combat systems chase
                    // Do NOT clear DesiredDestination - unit resumes movement after combat
                    if (isActiveScanner)
                    {
                        if (!em.HasComponent<AttackCommand>(entity))
                            ecb.AddComponent(entity, new AttackCommand { Target = bestTarget });
                            else
                                ecb.SetComponent(entity, new AttackCommand { Target = bestTarget });
                    }
                }
            }
        }

        [BurstCompile]
        private void ProcessReturnToGuard(ref SystemState state, ref EntityCommandBuffer ecb,
            NativeArray<Entity> allEnemies, NativeArray<LocalTransform> allEnemyTransforms,
            NativeArray<FactionTag> allEnemyFactions, NativeArray<Health> allEnemyHealth,
            NativeParallelMultiHashMap<int2, int> spatialMap)
        {
            var em = state.EntityManager;

            foreach (var (transform, guardPoint, faction, lineOfSight, rtgTarget, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<GuardPoint>, RefRO<FactionTag>, RefRO<LineOfSight>, RefRO<Target>>()
                .WithAll<UnitTag>()
                .WithNone<AttackCommand>()
                .WithNone<UserMoveOrder>()
                .WithNone<HealCommand>()        // Healers actively healing should not snap back
                .WithNone<CanBuild>()           // Builders are passive workers
                .WithNone<MinerTag>()           // Miners are handled by MiningSystem
                .WithEntityAccess())
            {
                // Skip units that have an active target
                if (rtgTarget.ValueRO.Value != Entity.Null) continue;
                if (guardPoint.ValueRO.Has == 0) continue;

                // Skip healers actively healing (HealCommand is consumed immediately
                // by LitharchHealingSystem, so check LitharchState.IsHealing instead)
                if (em.HasComponent<LitharchState>(entity))
                {
                    var ls = em.GetComponentData<LitharchState>(entity);
                    if (ls.IsHealing != 0 && ls.HealTarget != Entity.Null && em.Exists(ls.HealTarget))
                        continue;
                }

                var myPos = transform.ValueRO.Position;
                var gpPos = guardPoint.ValueRO.Position;
                var myFaction = faction.ValueRO.Value;
                var los = lineOfSight.ValueRO.Radius;
                var distToGuard = DistXZ(myPos, gpPos);

                // Hold position units: do NOT return to guard point or chase
                // They stay exactly where they are
                if (em.HasComponent<HoldPositionTag>(entity))
                    continue;

                // Attack-move units: resume advancing toward destination after combat
                // instead of returning to guard point (guard point IS the destination)
                if (em.HasComponent<AttackMoveTag>(entity))
                {
                    if (distToGuard > GuardReturnThreshold)
                    {
                        // Re-set DesiredDestination to resume movement toward attack-move destination
                        if (!em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.AddComponent(entity, new DesiredDestination
                            {
                                Position = gpPos,
                                Has = 1
                            });
                        }
                        else
                        {
                            ecb.SetComponent(entity, new DesiredDestination
                            {
                                Position = gpPos,
                                Has = 1
                            });
                        }
                    }
                    continue; // Skip normal return-to-guard logic
                }

                // Patrol units: resume patrol toward current waypoint after combat
                // GuardPoint is set to the current patrol waypoint by PatrolSystem
                if (em.HasComponent<PatrolTag>(entity))
                {
                    if (distToGuard > GuardReturnThreshold)
                    {
                        // Re-set DesiredDestination to resume patrol toward current waypoint
                        if (!em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.AddComponent(entity, new DesiredDestination
                            {
                                Position = gpPos,
                                Has = 1
                            });
                        }
                        else
                        {
                            ecb.SetComponent(entity, new DesiredDestination
                            {
                                Position = gpPos,
                                Has = 1
                            });
                        }
                    }
                    continue; // Skip normal return-to-guard logic
                }

                // Only consider returning if we're far from guard point
                if (distToGuard > GuardReturnThreshold)
                {
                    // Check if there are any enemies in line of sight (Fix #207: spatial hash).
                    Entity nearestEnemy = Entity.Null;
                    float nearestDist = float.MaxValue;

                    int radius = (int)math.ceil(los / TargetingCellSize);
                    var myCell = new int2(
                        (int)math.floor(myPos.x / TargetingCellSize),
                        (int)math.floor(myPos.z / TargetingCellSize));

                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            var cell = new int2(myCell.x + dx, myCell.y + dy);
                            if (!spatialMap.TryGetFirstValue(cell, out int i, out var it)) continue;
                            do
                            {
                                if (allEnemyFactions[i].Value == myFaction) continue;
                                if (allEnemyHealth[i].Value <= 0) continue;

                                var enemyPos = allEnemyTransforms[i].Position;
                                var dist = DistXZ(myPos, enemyPos);

                                // Skip stealthed enemies unless within proximity reveal range (3u)
                                if (em.HasComponent<StealthTag>(allEnemies[i]) && dist > 3f)
                                    continue;

                                if (dist <= los && dist < nearestDist)
                                {
                                    nearestEnemy = allEnemies[i];
                                    nearestDist = dist;
                                }
                            } while (spatialMap.TryGetNextValue(out i, ref it));
                        }
                    }

                    // If we found an enemy and it still exists, engage it instead of returning
                    if (nearestEnemy != Entity.Null && em.Exists(nearestEnemy))
                    {
                        ecb.SetComponent(entity, new Target { Value = nearestEnemy });

                        if (!em.HasComponent<AttackCommand>(entity))
                        {
                            ecb.AddComponent(entity, new AttackCommand { Target = nearestEnemy });
                        }
                        else
                        {
                            ecb.SetComponent(entity, new AttackCommand { Target = nearestEnemy });
                        }

                        continue; // Don't return to guard point
                    }

                    // No enemies found: Return to guard point
                    bool isMovingToGuard = false;
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        var dest = em.GetComponentData<DesiredDestination>(entity);
                        if (dest.Has != 0)
                        {
                            var distToDest = DistXZ(dest.Position, gpPos);
                            isMovingToGuard = distToDest < 1f;
                        }
                    }

                    if (!isMovingToGuard)
                    {
                        if (!em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.AddComponent(entity, new DesiredDestination
                            {
                                Position = gpPos,
                                Has = 1
                            });
                        }
                        else
                        {
                            ecb.SetComponent(entity, new DesiredDestination
                            {
                                Position = gpPos,
                                Has = 1
                            });
                        }
                    }
                }
            }
        }

        [BurstCompile]
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

        [BurstCompile]
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

        /// <summary>
        /// Returns underBest if it's a valid candidate within SpreadDistRatio of
        /// anyBest's distance, otherwise falls back to anyBest. Keeps overflow
        /// attackers from trekking far across the map just to honour the cap —
        /// they'll dogpile a nearby capped enemy if no reasonable alternative
        /// is in range.
        /// </summary>
        private static Entity PickSpreadOrNearest(Entity underBest, float underBestDist,
            Entity anyBest, float anyBestDist)
        {
            if (underBest == Entity.Null) return anyBest;
            if (anyBest == Entity.Null) return underBest;
            // underBest is by definition >= anyBest. Only accept it if the
            // detour cost is within SpreadDistRatio of nearest.
            if (underBestDist <= anyBestDist * SpreadDistRatio) return underBest;
            return anyBest;
        }

    }
}