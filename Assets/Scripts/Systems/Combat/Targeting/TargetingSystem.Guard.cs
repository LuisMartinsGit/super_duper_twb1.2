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
            in EnemyScan scan, in StanceSettings settings,
            ref NativeHashMap<Entity, int> attackerCount)
        {
            var em = state.EntityManager;
            uint tick = _tick;
            float clock = _clock;

            foreach (var (transform, guardPoint, faction, lineOfSight, rtgTarget, eng, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<GuardPoint>, RefRO<FactionTag>, RefRO<LineOfSight>,
                       RefRO<Target>, RefRW<UnitEngagement>>()
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
                    if (TransientState.Active<GuardSuppressed>(em, entity))
                        TransientState.Clear<GuardSuppressed>(em, ecb, entity);
                    continue;
                }
                if (guardPoint.ValueRO.Has == 0) continue;
                // Acquired earlier this tick (the Target write is still in the
                // ECB): it is about to fight, not to walk home.
                if (eng.ValueRO.AutoTarget != Entity.Null) continue;

                // Stuck recovery has already resolved this unit against THIS
                // guard point — crowded arrival, or an order it cancelled after
                // its detours ran out. Re-issuing the destination it just gave
                // up on is what produced the endless circling at a target
                // location: the recovery cleared DesiredDestination and stripped
                // UserMoveOrder / AttackMoveTag, which is precisely the state
                // this pass matches on. Any genuinely new order moves the guard
                // point and re-arms the leash. See GuardSuppressed.
                if (TransientState.Active<GuardSuppressed>(em, entity)
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
                // They stay exactly where they are. So does an emplaced
                // engine, which is bolted to its platform.
                if (em.HasComponent<HoldPositionTag>(entity) || IsFixedMount(em, entity))
                    continue;

                // Attack-move units: resume advancing toward destination after combat
                // instead of returning to guard point (guard point IS the destination)
                if (TransientState.Active<AttackMoveTag>(em, entity))
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
                    // Re-engage on the way home — by STANCE, as an AUTO target
                    // under the leash (docs/Design/Stances.md §3). This branch
                    // used to scan the whole line of sight, whatever the
                    // unit's stance, and install an AttackCommand — which made
                    // the engagement an ORDER, so nothing ever leashed it and
                    // one scout could tow the unit across the map.
                    //
                    // Only for a unit that is actually walking (a stationary
                    // idle unit was just scanned by the acquire pass), only on
                    // its stagger tick, never inside a leash-break cooldown,
                    // and only once it is back inside its own leash — so a
                    // unit coming home from a broken leash does not turn round
                    // at the first sight of the target it just gave up on.
                    // A unit that never moves for a fight (Defensive, support:
                    // leash 0) still returns fire on the way home — the
                    // target has to be in reach anyway, so the leash test
                    // below only applies to a unit that may chase.
                    var stance = EffectiveStance(em, entity);
                    bool scanDue = ((tick + eng.ValueRO.ScanPhase) & (AcquireStagger - 1)) == 0;
                    if (hasLiveDest && scanDue && clock >= eng.ValueRO.NextAcquireAt
                        && !em.HasComponent<SectVeiled>(entity)   // veiled: may move, nothing else
                        && em.HasComponent<Damage>(entity)
                        && em.GetComponentData<Damage>(entity).Value > 0)
                    {
                        var cls = em.GetComponentData<UnitTag>(entity).Class;
                        bool hitRecently = clock - eng.ValueRO.HitAt <= settings.RetaliationWindow;
                        var rules = RulesFor(em, entity, cls, stance, false, los, hitRecently, in settings);
                        if (cls != UnitClass.Economy && cls != UnitClass.Miner
                            && rules.Acquire != 0
                            && (rules.Leash <= 0f || distToGuard <= rules.Leash))
                        {
                            Entity nearestEnemy = FindAutoTarget(em, entity, myPos, myFaction,
                                in rules, in scan, ref attackerCount);
                            if (nearestEnemy != Entity.Null && em.Exists(nearestEnemy))
                            {
                                ecb.SetComponent(entity, new Target { Value = nearestEnemy });
                                ref var e = ref eng.ValueRW;
                                e.AutoTarget = nearestEnemy;
                                e.Anchor = gpPos;
                                e.Leash = rules.Leash;
                                continue; // Don't return to guard point
                            }
                        }
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