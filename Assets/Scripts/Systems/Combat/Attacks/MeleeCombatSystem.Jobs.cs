// MeleeCombatSystem.Jobs.cs
// The per-unit melee loop as Burst jobs (2026-10-15 perf pass). Partial of
// MeleeCombatSystem.cs.
//
// SHAPE
//   1. MeleePlanJob (parallel, read-only): every per-unit test the old loop
//      made that no other unit's turn can change — target validity, the
//      flying / buildings-only / Wall / ally rules, disarm and invulnerable,
//      the surface distance and height gate, the facing turn, MayChase and
//      the chase-anchor candidates. One plan slot per unit.
//   2. Managed: the chase destination's standability (NavGridQuery is
//      managed). A pure read of the nav grid, which nothing in this loop
//      writes.
//   3. MeleeApplyJob (Burst, main thread, .Run) walks the units IN QUERY
//      ORDER and applies each plan — the cooldown tick, target drops,
//      stop-and-face, chase writes — and STOPS at the first unit that swings.
//      The swing (the damage pipeline: armour, sect riders, Feraldis riders,
//      reflect, cleave — all managed) runs on the main thread exactly as
//      before, then the apply job resumes at the next unit.
//
// RESULT-IDENTICAL ORDER. The only state a swing changes that an earlier
// plan read is Health (damage, reflect, lifesteal, cleave, whirl): the apply
// pass reads the attacker's and the target's Health LIVE, at the same point
// in the sequence the old loop did, so a unit killed — or a target killed —
// by an earlier swing this tick is skipped / dropped exactly as before.
// Everything else a plan reads is written only through the ECB or only by
// its own unit's turn (rotation, cooldown, Target).

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct MeleeCombatSystem : ISystem
    {
        internal const byte MeleeSkip = 0;      // disarmed (Damage <= 0)
        internal const byte MeleeDropPre = 1;   // dropped BEFORE the target-alive test
        internal const byte MeleeIdle = 2;      // Full Gallop / invulnerable target: wait
        internal const byte MeleeInRange = 3;   // stop, face, swing when ready
        internal const byte MeleeChase = 4;     // walk to the chase anchor
        internal const byte MeleeDropPost = 5;  // dropped AFTER the target-alive test

        internal struct MeleePlan
        {
            public byte Kind;
            /// <summary>Cooldown recovers this tick (Timer &gt; 0, not CodexFrozen).</summary>
            public byte CdDec;
            /// <summary>AttackCommand live — the drop disables it.</summary>
            public byte DropCmd;
            /// <summary>InRange: plant (not FiresOnMove).</summary>
            public byte Face;
            /// <summary>The unit carries DesiredDestination.</summary>
            public byte HasDest;
            /// <summary>InRange: the facing turn changes the rotation.</summary>
            public byte Turn;
            /// <summary>Chase: the latched anchor may be reused if still standable.</summary>
            public byte AnchorCandidate;
            /// <summary>Chase: a fresh anchor was derived (set at resolve).</summary>
            public byte NewAnchor;
            public Entity Target;
            public quaternion NewRot;
            public float3 TargetPos;
            /// <summary>Chase: the latched anchor point (candidate).</summary>
            public float3 AnchorPoint;
            /// <summary>Chase: the fresh approach point, before the standability walk.</summary>
            public float3 FreshPoint;
            /// <summary>Chase: the final destination (filled at resolve).</summary>
            public float3 Point;
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        internal struct MeleePlanJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Units;
            [ReadOnly] public CombatLookups Lk;
            [ReadOnly] public ComponentLookup<Target> Targets;
            [ReadOnly] public ComponentLookup<AttackCooldown> Cooldowns;
            [ReadOnly] public ComponentLookup<Damage> Damages;
            [ReadOnly] public EntityStorageInfoLookup Storage;
            public float Dt;
            [WriteOnly] public NativeArray<MeleePlan> Plans;

            public void Execute(int idx)
            {
                var entity = Units[idx];
                var p = new MeleePlan { Kind = MeleeSkip };
                Evaluate(entity, ref p);
                Plans[idx] = p;
            }

            private void Evaluate(Entity entity, ref MeleePlan p)
            {
                if (Damages[entity].Value <= 0) return;   // Skip

                var cd = Cooldowns[entity];
                p.CdDec = (byte)(cd.Timer > 0 && !Lk.CodexFrozen.HasComponent(entity) ? 1 : 0);
                p.DropCmd = (byte)(CombatLookups.Active(in Lk.AttackCommand, entity) ? 1 : 0);

                var tgt = Targets[entity].Value;
                p.Target = tgt;
                p.Kind = MeleeDropPre;
                if (tgt == Entity.Null || !Storage.Exists(tgt)) return;
                if (!Lk.Health.HasComponent(tgt)) return;
                if (Lk.Flying.HasComponent(tgt) && !TargetingSystem.CanHitFlying(in Lk, entity)) return;

                // ── after the target-alive test (re-read live at apply) ──
                p.Kind = MeleeDropPost;
                if (Lk.BuildingsOnly.HasComponent(entity) && !Lk.Building.HasComponent(tgt)) return;
                if (Lk.Wall.HasComponent(tgt) && !Lk.DealsSiege(entity)) return;
                if (!CanDamageBurst(in Lk, entity, tgt)) return;

                p.Kind = MeleeIdle;
                if (CombatLookups.Active(in Lk.TempDisarm, entity)) return;
                if (CombatLookups.Active(in Lk.Invulnerable, tgt)) return;

                var myXf = Lk.Transform[entity];
                var myPos = myXf.Position;
                var targetPos = Lk.Transform[tgt].Position;
                p.TargetPos = targetPos;

                var extent = Lk.Extent(tgt);
                float surfaceDist = extent.SurfaceDistXZ(myPos);

                p.HasDest = (byte)(Lk.Destination.HasComponent(entity) ? 1 : 0);

                if (surfaceDist <= MeleeRange)
                {
                    if (TargetingSystem.MeleeHeightBlocks(myPos.y, targetPos.y,
                            Lk.Building.HasComponent(tgt), TargetingSystem.IsOnRampart(in Lk, entity)))
                    {
                        p.Kind = MeleeDropPost;
                        return;
                    }
                    p.Kind = MeleeInRange;
                    if (!Lk.FiresOnMove.HasComponent(entity))
                    {
                        p.Face = 1;
                        var turned = TheWaningBorder.Core.MathUtil.TurnTowardXZ(myXf.Rotation, myXf.Position,
                            targetPos, Dt, TheWaningBorder.Core.MathUtil.DefaultTurnSpeed);
                        if (!turned.Equals(myXf.Rotation))
                        {
                            p.Turn = 1;
                            p.NewRot = turned;
                        }
                    }
                    return;
                }

                if (!TargetingSystem.MayChase(in Lk, entity, tgt))
                {
                    p.Kind = MeleeDropPost;
                    return;
                }

                p.Kind = MeleeChase;
                if (CombatLookups.Active(in Lk.ChaseAnchor, entity))
                {
                    var a = Lk.ChaseAnchor[entity];
                    bool sameTarget = a.Target == tgt;
                    float dx = a.TargetPos.x - targetPos.x;
                    float dz = a.TargetPos.z - targetPos.z;
                    bool targetHeld = dx * dx + dz * dz
                        <= ChaseAnchor.RecomputeDistance * ChaseAnchor.RecomputeDistance;
                    if (sameTarget && targetHeld)
                    {
                        p.AnchorCandidate = 1;
                        p.AnchorPoint = a.Point;
                    }
                }
                float3 point = extent.ApproachPoint(myPos, MeleeRange * 0.5f);
                point.y = targetPos.y;
                p.FreshPoint = point;
            }
        }

        /// <summary><see cref="CombatDamageHelper.CanDamage(in CombatLookups, Entity, Entity)"/>
        /// with the Burst-safe hostility test (same rules, SharedStatic team table).</summary>
        internal static bool CanDamageBurst(in CombatLookups lk, Entity attacker, Entity target)
        {
            if (CombatDamageHelper.Frenzied(in lk, attacker) || CombatDamageHelper.Frenzied(in lk, target))
                return attacker != target;
            if (!lk.Faction.TryGetComponent(attacker, out var af)
                || !lk.Faction.TryGetComponent(target, out var tf))
                return true;
            return Alliances.AreHostileBurst(af.Value, tf.Value);
        }

        /// <summary>
        /// Applies the plans from <see cref="Start"/> on, in query order, and
        /// stops at the first unit whose swing is due (writes its index to
        /// <see cref="Stop"/>; Units.Length when the pass is finished).
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        internal struct MeleeApplyJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Units;
            [ReadOnly] public NativeArray<MeleePlan> Plans;
            [ReadOnly] public ComponentLookup<Health> Health;
            public ComponentLookup<Target> Targets;
            public ComponentLookup<AttackCooldown> Cooldowns;
            public ComponentLookup<LocalTransform> Transforms;
            public EntityCommandBuffer Ecb;
            public float Dt;
            public int Start;
            public NativeReference<int> Stop;

            public void Execute()
            {
                int n = Units.Length;
                for (int i = Start; i < n; i++)
                {
                    var p = Plans[i];
                    if (p.Kind == MeleeSkip) continue;
                    var entity = Units[i];

                    // A corpse does not swing — live: an earlier swing this
                    // tick may have killed it (or a lifesteal healed it).
                    if (Health.TryGetComponent(entity, out var myHp) && myHp.Value <= 0) continue;

                    var cd = Cooldowns[entity];
                    if (p.CdDec != 0)
                    {
                        cd.Timer -= Dt;
                        Cooldowns[entity] = cd;
                    }

                    if (p.Kind == MeleeDropPre) { Drop(entity, in p); continue; }

                    // Target alive — live, for the same reason.
                    if (Health[p.Target].Value <= 0) { Drop(entity, in p); continue; }

                    switch (p.Kind)
                    {
                        case MeleeDropPost:
                            Drop(entity, in p);
                            break;

                        case MeleeIdle:
                            break;

                        case MeleeInRange:
                            if (p.Face != 0)
                            {
                                // TargetGeometry.StopAndFace: ECB stop, rotation now.
                                if (p.HasDest != 0)
                                    Ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                                if (p.Turn != 0)
                                {
                                    var xf = Transforms[entity];
                                    xf.Rotation = p.NewRot;
                                    Transforms[entity] = xf;
                                }
                            }
                            if (cd.Timer <= 0)
                            {
                                Stop.Value = i;
                                return;
                            }
                            break;

                        case MeleeChase:
                            if (p.NewAnchor != 0)
                                TransientState.Set(Ecb, entity, new ChaseAnchor
                                {
                                    Target = p.Target,
                                    TargetPos = p.TargetPos,
                                    Point = p.Point,
                                });
                            if (p.HasDest == 0)
                                Ecb.AddComponent(entity, new DesiredDestination { Position = p.Point, Has = 1 });
                            else
                                Ecb.SetComponent(entity, new DesiredDestination { Position = p.Point, Has = 1 });
                            break;
                    }
                }
                Stop.Value = n;
            }

            private void Drop(Entity entity, in MeleePlan p)
            {
                Targets[entity] = new Target { Value = Entity.Null };
                if (p.DropCmd != 0)
                    Ecb.SetComponentEnabled<AttackCommand>(entity, false);
            }
        }

        /// <summary>
        /// <see cref="ResolveChaseAnchor"/>'s standability half, for every
        /// chasing plan: reuse the latched anchor while it stands on legal
        /// ground, else walk the fresh approach point outward until it does.
        /// Managed (NavGridQuery / PassabilityGrid); a pure read of the grid.
        /// </summary>
        private static void ResolveChasePoints(NativeArray<MeleePlan> plans)
        {
            for (int i = 0; i < plans.Length; i++)
            {
                var p = plans[i];
                if (p.Kind != MeleeChase) continue;

                if (p.AnchorCandidate != 0
                    && TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(p.AnchorPoint))
                {
                    p.Point = p.AnchorPoint;
                    p.NewAnchor = 0;
                    plans[i] = p;
                    continue;
                }

                float3 point = p.FreshPoint;
                if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(point))
                {
                    float3 outward = point - p.TargetPos;
                    outward.y = 0f;
                    float len = math.length(outward);
                    if (len > 1e-4f)
                    {
                        outward /= len;
                        for (float extra = 0.75f; extra <= 3f; extra += 0.75f)
                        {
                            float3 probe = point + outward * extra;
                            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(probe))
                                continue;
                            point = probe;
                            break;
                        }
                    }
                }
                p.Point = point;
                p.NewAnchor = 1;
                plans[i] = p;
            }
        }
    }
}
