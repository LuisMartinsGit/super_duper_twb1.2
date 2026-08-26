// TargetingSystem.Acquire.cs
// Auto target acquisition (spatial-hash enemy scan) and the spread/nearest pick.
// Partial of TargetingSystem.cs -- split 2026-08-12 for readability.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct TargetingSystem : ISystem
    {
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void AutoAcquireTargets(ref SystemState state, ref EntityCommandBuffer ecb,
            NativeArray<Entity> allEnemies, NativeArray<LocalTransform> allEnemyTransforms,
            NativeArray<FactionTag> allEnemyFactions, NativeArray<Health> allEnemyHealth,
            NativeArray<byte> allEnemyPriority,
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
                .WithNone<PassiveWorkerTag>()   // Builders are passive workers...
                                                //   ...except Feraldis Workers, which are
                                                //   light infantry that also build. The tag
                                                //   (not CanBuild) is what marks a worker as
                                                //   non-combatant; FeraldisCultureRetrofitSystem
                                                //   strips it.
                .WithNone<BuildCommand>()       // A COMMITTED BUILDER IS BUSY. Feraldis
                                                //   Workers fight, so dropping PassiveWorkerTag
                                                //   let this pass (and return-to-guard below)
                                                //   grab them mid-build and fight the build
                                                //   order for their DesiredDestination every
                                                //   frame — the visible worker "glitching"
                                                //   reported 2026-08-06. Command follow-through:
                                                //   a worker with a build order finishes it.
                .WithNone<SectVeiled>()         // Stoneveil (Fortitude): a veiled unit may
                                                //   move, and nothing else. It cannot attack,
                                                //   gather, build or capture while veiled.
                .WithNone<MinerTag>()           // Miners are handled by MiningSystem
                .WithNone<RitualState>()        // A CHANNELLING RITUALIST IS BUSY — the same
                                                //   rule as the committed builder above, added
                                                //   for the same failure.
                                                //
                                                //   The killer is the return-to-guard branch
                                                //   below, not target acquisition. A ritualist
                                                //   clears DesiredDestination to channel, which
                                                //   makes it IDLE; its GuardPoint is still where
                                                //   it spawned, tens of metres away; so the very
                                                //   next tick walks it home at full speed. It
                                                //   `continue`s before the damage check, so even
                                                //   a 0-damage caster like the Iconoclast is
                                                //   dragged off.
                                                //
                                                //   Measured 2026-08-07: 6 m -> 14 m in ~3 s
                                                //   (2.7 m/s against a 3.2 move speed), breaking
                                                //   the 40 s channel every single time. With
                                                //   MaxGuardDistance at 10 m this hit EVERY
                                                //   ritual on every map — no well is within
                                                //   10 m of a spawn.
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

                // Economy units (workers / miners) NEVER auto-engage. A worker
                // standing on a deposit is mining, so its DesiredDestination.Has
                // is 0 — which means the "skip units with a destination" gate
                // below does NOT protect it, and it would auto-acquire a nearby
                // enemy and wander off to chase it while still assigned to the
                // deposit (walking toward the enemy base). Keep them on task,
                // same as the Scout rule above.
                if (em.HasComponent<UnitTag>(entity))
                {
                    var cls = em.GetComponentData<UnitTag>(entity).Class;
                    if (cls == UnitClass.Economy || cls == UnitClass.Miner) continue;
                }

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
                        // ── PURSUIT LEASH ──────────────────────────────────
                        // …but FIRST check whether this unit is chasing an
                        // auto-acquired target clean across the map.
                        //
                        // MaxGuardDistance below only gates ACQUISITION, and
                        // this early-out meant a unit that had already started
                        // chasing was skipped by the whole loop — so nothing
                        // ever pulled it back. One scout could therefore tow a
                        // defending army into the enemy base, where it died to
                        // the garrison plus the Hall; the survivors then towed
                        // the counter-attack home the same way (2026-08-18).
                        //
                        // Commanded aggression is exempt: attack-move and
                        // patrol are active scanners (handled above), and
                        // AttackCommand units never enter this query at all
                        // (WithNone<AttackCommand>). This leashes ONLY the
                        // auto-aggro chase, which no one ever ordered.
                        if (target.ValueRO.Value != Entity.Null
                            && em.HasComponent<GuardPoint>(entity))
                        {
                            var gp = em.GetComponentData<GuardPoint>(entity);
                            if (gp.Has != 0
                                && DistXZ(transform.ValueRO.Position, gp.Position) > MaxPursuitDistance)
                            {
                                ecb.SetComponent(entity, new Target { Value = Entity.Null });
                                ecb.SetComponent(entity, new DesiredDestination
                                {
                                    Position = gp.Position,
                                    Has = 1
                                });
                                continue;
                            }
                        }
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

                // Buildings-only siege (Battering Ram): never auto-acquire a
                // non-building target for holders — they exist to crack walls,
                // not to swing at soldiers (the melee fire path refuses those
                // anyway; see MeleeCombatSystem).
                bool buildingsOnly = em.HasComponent<BuildingsOnlyAttacker>(entity);

                // The Wall Rule (docs/Design/Combat_Pacing.md): only siege
                // damages wall pieces, so non-siege never auto-acquires one.
                bool nonSiegeAttacker = !em.HasComponent<DamageTypeData>(entity)
                    || em.GetComponentData<DamageTypeData>(entity).Value != DamageType.Siege;

                Entity bestTarget = Entity.Null;
                Entity underBest = Entity.Null;
                float underBestDist = float.MaxValue;
                Entity anyBest = Entity.Null;
                float anyBestDist = float.MaxValue;
                byte anyBestPrio = 0;
                Entity prioBest = Entity.Null;
                float prioBestDist = float.MaxValue;
                byte prioBestPrio = 0;

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
                                // Allies are never auto-acquired. This used to
                                // be a raw same-faction test, which made every
                                // teammate a valid target. docs/Design/Teams.md
                                if (!Alliances.AreHostile(myFaction, allEnemyFactions[i].Value)) continue;
                                if (allEnemyHealth[i].Value <= 0) continue;

                                // Buildings-only siege: units are invisible to
                                // the ram's target scan.
                                if (buildingsOnly && !em.HasComponent<BuildingTag>(allEnemies[i])) continue;

                                // The Wall Rule: wall pieces are invisible to
                                // non-siege target scans.
                                if (nonSiegeAttacker && em.HasComponent<WallTag>(allEnemies[i])) continue;

                                var enemyPos = allEnemyTransforms[i].Position;
                                // SURFACE distance, so a big building is judged by
                                // where its walls are, not where its pivot is. A
                                // 7x7 temple's pivot sits 3.5 m inside itself; on
                                // centre distance it read as further away than a
                                // hut standing right beside it, and the "nearest
                                // enemy" pick skipped the thing the unit was
                                // literally touching.
                                var dist = TargetGeometry.SurfaceDistXZ(em, myPos, enemyPos, allEnemies[i]);
                                if (dist > los) continue;

                                // Curse & Shardroot canon §2.1: BORDER units
                                // GUARD their wells — they don't hunt
                                // harvesters. Worker-class targets (miners /
                                // builders) are ignored unless they stray
                                // right into the horde; military targets are
                                // engaged normally. Makes sneak-mining the
                                // crystal fields survivable.
                                if (myFaction == Faction.Border && dist > 9f
                                    && (em.HasComponent<MinerTag>(allEnemies[i])
                                        || em.HasComponent<CanBuild>(allEnemies[i])))
                                    continue;

                                // Melee can't reach a vertically separated
                                // enemy (bridge deck above / valley floor
                                // below) — don't acquire it, pick someone
                                // reachable instead. Ranged units keep the
                                // target (they shoot up/down with the
                                // high-ground rules).
                                if (isMelee && math.abs(enemyPos.y - myPos.y) > MeleeMaxHeightDelta)
                                    continue;

                                // Skip stealthed enemies unless within proximity
                                // reveal range (3u) or exposed by a Lorekeeper
                                // (Antiquity detection stamp).
                                if (em.HasComponent<StealthTag>(allEnemies[i]) && dist > 3f
                                    && !em.HasComponent<StealthRevealed>(allEnemies[i]))
                                    continue;

                                byte prio = allEnemyPriority[i];
                                if (dist < anyBestDist) { anyBest = allEnemies[i]; anyBestDist = dist; anyBestPrio = prio; }
                                if (prio > prioBestPrio || (prio == prioBestPrio && dist < prioBestDist))
                                {
                                    prioBest = allEnemies[i];
                                    prioBestDist = dist;
                                    prioBestPrio = prio;
                                }
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

                    // M2 bounded value tie-break: a higher-priority candidate
                    // (healer/siege/caster) replaces the nearest pick only when
                    // it sits within ValuePickDistRatio of it.
                    // NearDistFloor: distances are now measured to the target's
                    // SURFACE, so a unit pressed against a building reads 0.0 —
                    // and a pure ratio against 0 is 0, which would let a wall
                    // permanently out-rank the soldier standing next to it.
                    // Floor the comparison basis so "within 25% of nearest" also
                    // means "or within a metre or so, absolute".
                    if (prioBest != Entity.Null && prioBestPrio > anyBestPrio
                        && prioBestDist <= math.max(anyBestDist, NearDistFloor) * ValuePickDistRatio)
                    {
                        anyBest = prioBest;
                        anyBestDist = prioBestDist;
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
            if (underBestDist <= math.max(anyBestDist, NearDistFloor) * SpreadDistRatio) return underBest;
            return anyBest;
        }
    }
}