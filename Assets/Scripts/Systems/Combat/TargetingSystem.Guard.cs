// TargetingSystem.Guard.cs
// Return-to-guard behaviour: leashing idle units back to their guard point.
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
                // Skip units that have an active target
                if (rtgTarget.ValueRO.Value != Entity.Null)
                {
                    // Engaging is a fresh intent that will carry the unit away
                    // from its post under its own steam. Drop any stale
                    // crowded-arrival suppression so the leash is live again
                    // once the fight ends — otherwise one crowded arrival would
                    // exempt the unit from return-to-guard for the rest of the
                    // match and it would simply stand wherever combat left it.
                    if (em.HasComponent<GuardSuppressed>(entity))
                        ecb.RemoveComponent<GuardSuppressed>(entity);
                    continue;
                }
                if (guardPoint.ValueRO.Has == 0) continue;

                // Stuck recovery has already resolved this unit against THIS
                // guard point — crowded arrival, or an order it cancelled after
                // its detours ran out. Re-issuing the destination it just gave
                // up on is what produced the endless circling at a target
                // location: the recovery cleared DesiredDestination and stripped
                // UserMoveOrder / AttackMoveTag, which is precisely the state
                // this pass matches on. Any genuinely new order moves the guard
                // point and re-arms the leash. See GuardSuppressed.
                if (em.HasComponent<GuardSuppressed>(entity)
                    && DistXZ(em.GetComponentData<GuardSuppressed>(entity).Point,
                              guardPoint.ValueRO.Position) < GuardSuppressed.Epsilon)
                    continue;

                // Scouts are vision-only roamers steered by the ScoutDirector
                // (AI) or the player — they must NEVER snap back to a guard
                // point on their own. Without this exemption (mirror of the
                // AutoAcquireTargets scout gate above), return-to-guard
                // overrode every far scouting assignment with a recall to the
                // spawn Hall on the next frame.
                if (em.GetComponentData<UnitTag>(entity).Class == UnitClass.Scout)
                    continue;

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

                // Return-to-guard RESUMES a movement intent; it must never
                // overwrite one that is still live. The attack-move and patrol
                // branches below used to re-stamp DesiredDestination every
                // single frame while the unit was en route, which silently
                // disabled every recovery in the nav stack — StuckRedirect's
                // detour leg survived exactly one frame, and both its cancel
                // and the integrator's own stuck-cancel were undone on the next
                // tick. An attack-moving unit that could not reach its point
                // therefore had no exit at all, which is why the AI's armies
                // (attack-move is their only movement mode) were the worst
                // offenders for the endless-circling report.
                bool hasLiveDest = em.HasComponent<DesiredDestination>(entity)
                    && em.GetComponentData<DesiredDestination>(entity).Has != 0;

                // Hold position units: do NOT return to guard point or chase
                // They stay exactly where they are
                if (em.HasComponent<HoldPositionTag>(entity))
                    continue;

                // Attack-move units: resume advancing toward destination after combat
                // instead of returning to guard point (guard point IS the destination)
                if (em.HasComponent<AttackMoveTag>(entity))
                {
                    if (!hasLiveDest && distToGuard > GuardReturnThreshold)
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
                    if (!hasLiveDest && distToGuard > GuardReturnThreshold)
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

                    // Buildings-only siege (Battering Ram): this engage branch
                    // is auto-acquisition too — same building-only filter as
                    // AutoAcquireTargets above.
                    bool buildingsOnly = em.HasComponent<BuildingsOnlyAttacker>(entity);

                    // The Wall Rule: same non-siege wall filter as above.
                    bool nonSiegeAttacker = !em.HasComponent<DamageTypeData>(entity)
                        || em.GetComponentData<DamageTypeData>(entity).Value != DamageType.Siege;

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
                                // Allies are never re-acquired on return-to-guard.
                                // docs/Design/Teams.md
                                if (!Alliances.AreHostile(myFaction, allEnemyFactions[i].Value)) continue;
                                if (allEnemyHealth[i].Value <= 0) continue;

                                // Buildings-only siege: units are invisible to
                                // the ram's target scan.
                                if (buildingsOnly && !em.HasComponent<BuildingTag>(allEnemies[i])) continue;

                                // The Wall Rule: wall pieces are invisible to
                                // non-siege target scans.
                                if (nonSiegeAttacker && em.HasComponent<WallTag>(allEnemies[i])) continue;

                                var enemyPos = allEnemyTransforms[i].Position;
                                var dist = DistXZ(myPos, enemyPos);

                                // Skip stealthed enemies unless within proximity
                                // reveal range (3u) or exposed by a Lorekeeper
                                // (Antiquity detection stamp).
                                if (em.HasComponent<StealthTag>(allEnemies[i]) && dist > 3f
                                    && !em.HasComponent<StealthRevealed>(allEnemies[i]))
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

                    // No enemies found: Return to guard point — but only if the
                    // unit is genuinely idle. Any live destination is left
                    // alone (see hasLiveDest above): a unit already executing a
                    // movement intent, including a stuck recovery's detour leg,
                    // is not idle. This subsumes the old "already heading to
                    // the guard point" test, which only caught the destination
                    // being within 1 m of the post and happily clobbered
                    // everything else.
                    if (!hasLiveDest)
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
    }
}