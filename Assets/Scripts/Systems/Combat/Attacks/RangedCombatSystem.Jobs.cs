// RangedCombatSystem.Jobs.cs
// The per-unit ranged loop as Burst jobs (2026-10-15 perf pass). Partial of
// RangedCombatSystem.cs.
//
// SHAPE (the melee shape, MeleeCombatSystem.Jobs.cs):
//   1. RangedPlanJob (parallel): the whole per-unit decision — validity,
//      the Wall Rule, retreat / aim / chase, the aim and cooldown timers, the
//      facing turn, the siege aim gate — written as the unit's FINAL archer
//      state, Target, rotation and destination write, plus "fires now".
//   2. RangedApplyJob (Burst, main thread, .Run) writes those in query order
//      and stops at each unit that fires; the shot itself (damage riders,
//      projectile spawn — managed helpers) runs here exactly as before, then
//      the apply resumes at the next unit.
//
// Nothing a ranged unit's turn writes is read by another unit's turn — no
// Health changes in this loop (damage lands at projectile impact), and every
// write is to the unit's own components or through the ECB — so planning
// every unit up front decides exactly what the old in-order loop decided.
// The stop-and-resume keeps the ECB command stream in the old order.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct RangedCombatSystem : ISystem
    {
        internal const byte RangedSkip = 0;   // dead or disarmed: untouched
        internal const byte RangedApply = 1;  // write the plan
        internal const byte RangedFire = 2;   // write the plan, then shoot

        internal const byte DestNone = 0;
        internal const byte DestStop = 1;     // StopAndFace's Has = 0
        internal const byte DestGoTo = 2;     // retreat / chase point

        internal struct RangedPlan
        {
            public byte Kind;
            public byte ClearTarget;
            public byte DropCmd;
            public byte DestKind;
            public byte HasDest;
            public byte Turn;
            public byte IsSiege;
            public quaternion NewRot;
            public float3 DestPos;
            public ArcherState Archer;
            // Shot data (Kind == RangedFire).
            public Entity Target;
            public float3 MyPos;
            public float3 TargetPos;
            public float3 AimPos;
            public float AimDist;
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        internal struct RangedPlanJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Units;
            [ReadOnly] public CombatLookups Lk;
            [ReadOnly] public ComponentLookup<Target> Targets;
            [ReadOnly] public ComponentLookup<ArcherState> Archers;
            [ReadOnly] public ComponentLookup<Damage> Damages;
            [ReadOnly] public ComponentLookup<TrebuchetState> Trebuchets;
            [ReadOnly] public EntityStorageInfoLookup Storage;
            public float Dt;
            [WriteOnly] public NativeArray<RangedPlan> Plans;

            public void Execute(int idx)
            {
                var entity = Units[idx];
                var p = new RangedPlan { Kind = RangedSkip };
                Evaluate(entity, ref p);
                Plans[idx] = p;
            }

            private void Drop(Entity entity, ref RangedPlan p)
            {
                p.ClearTarget = 1;
                p.DropCmd = (byte)(CombatLookups.Active(in Lk.AttackCommand, entity) ? 1 : 0);
            }

            private void Evaluate(Entity entity, ref RangedPlan p)
            {
                if (Lk.Health.TryGetComponent(entity, out var myHp) && myHp.Value <= 0) return;
                if (Damages[entity].Value <= 0) return;

                p.Kind = RangedApply;
                var archer = Archers[entity];

                if (archer.CooldownTimer > 0 && !Lk.CodexFrozen.HasComponent(entity))
                    archer.CooldownTimer -= Dt;

                var tgt = Targets[entity].Value;
                p.Target = tgt;

                if (tgt == Entity.Null || !Storage.Exists(tgt)
                    || !Lk.Health.TryGetComponent(tgt, out var targetHealth)
                    || targetHealth.Value <= 0
                    || (Lk.Wall.HasComponent(tgt) && !Lk.DealsSiege(entity)))
                {
                    Drop(entity, ref p);
                    p.Archer = archer;
                    return;
                }

                if (CombatLookups.Active(in Lk.Invulnerable, tgt)) { p.Archer = archer; return; }

                bool isMoving = Lk.Destination.TryGetComponent(entity, out var dest) && dest.Has != 0;
                p.HasDest = (byte)(Lk.Destination.HasComponent(entity) ? 1 : 0);
                if (isMoving)
                {
                    archer.AimTimer = 0;
                    archer.IsFiring = 0;
                }

                var myXf = Lk.Transform[entity];
                var myPos = myXf.Position;
                var targetPos = Lk.Transform[tgt].Position;
                var dist = DistXZ(myPos, targetPos);

                float minRange = math.max(0f, archer.MinRange);
                float maxRange = archer.MaxRange > 0 ? archer.MaxRange : DefaultMaxRange;
                float heightMult = HeightAdvantage.Multiplier(myPos.y, targetPos.y);
                maxRange *= heightMult;

                var extent = Lk.Extent(tgt);
                float edgeDist = extent.SurfaceDistXZ(myPos);

                if (edgeDist < minRange)
                {
                    // RETREAT (a fixed mount drops the target instead).
                    if (TargetingSystem.IsFixedMount(in Lk, entity))
                    {
                        archer.AimTimer = 0;
                        archer.IsRetreating = 0;
                        Drop(entity, ref p);
                        p.Archer = archer;
                        return;
                    }

                    archer.IsRetreating = 1;
                    archer.AimTimer = 0;
                    var retreatDir = math.normalize(myPos - targetPos);
                    p.DestKind = DestGoTo;
                    p.DestPos = myPos + retreatDir * (minRange - edgeDist + 3f);
                    p.Archer = archer;
                    return;
                }

                if (edgeDist <= maxRange)
                {
                    // AIM AND FIRE.
                    archer.IsRetreating = 0;

                    bool firesOnMove = Lk.FiresOnMove.HasComponent(entity);
                    quaternion rot = myXf.Rotation;
                    if (!firesOnMove)
                    {
                        // TargetGeometry.StopAndFace.
                        if (p.HasDest != 0) p.DestKind = DestStop;
                        var turned = TurnTowardXZ(myXf.Rotation, myXf.Position, targetPos, Dt, DefaultTurnSpeed);
                        if (!turned.Equals(myXf.Rotation))
                        {
                            p.Turn = 1;
                            p.NewRot = turned;
                            rot = turned;
                        }
                    }

                    if (isMoving && !firesOnMove) { p.Archer = archer; return; }
                    if (CombatLookups.Active(in Lk.TempDisarm, entity)) { p.Archer = archer; return; }

                    if (Trebuchets.TryGetComponent(entity, out var treb) && treb.Deployed == 0)
                    {
                        archer.AimTimer = 0;
                        archer.IsFiring = 0;
                        p.Archer = archer;
                        return;
                    }

                    archer.AimTimer += Dt;
                    float effectiveAimRequired = archer.AimTimeRequired;

                    bool isSiege = Lk.Siege.HasComponent(entity);
                    if (isSiege)
                    {
                        float3 toTarget = targetPos - myPos;
                        toTarget.y = 0;
                        float3 forward = math.mul(rot, new float3(0, 0, 1));
                        forward.y = 0;
                        if (math.lengthsq(toTarget) > 0.01f && math.lengthsq(forward) > 0.01f)
                        {
                            float dot = math.dot(math.normalizesafe(forward), math.normalizesafe(toTarget));
                            if (dot < 0.9f)
                            {
                                archer.AimTimer = 0;
                                p.Archer = archer;
                                return;
                            }
                        }
                    }

                    if (archer.AimTimer >= effectiveAimRequired && archer.CooldownTimer <= 0)
                    {
                        archer.IsFiring = 1;
                        p.Kind = RangedFire;
                        p.IsSiege = (byte)(isSiege ? 1 : 0);
                        p.MyPos = myPos;
                        p.TargetPos = targetPos;
                        p.AimPos = extent.IsBox ? extent.ApproachPoint(myPos, 0f) : targetPos;
                        p.AimDist = isSiege ? math.distance(myPos, p.AimPos) : dist;
                    }
                    p.Archer = archer;
                    return;
                }

                // CHASE.
                if (!TargetingSystem.MayChase(in Lk, entity, tgt))
                {
                    archer.AimTimer = 0;
                    Drop(entity, ref p);
                    p.Archer = archer;
                    return;
                }

                archer.IsRetreating = 0;
                archer.AimTimer = 0;
                float3 toT = targetPos - myPos;
                float3 dirToTarget = math.normalizesafe(toT);
                float stopDist = (dist - edgeDist) + maxRange - 2f;
                p.DestKind = DestGoTo;
                p.DestPos = targetPos - dirToTarget * stopDist;
                p.Archer = archer;
            }
        }

        /// <summary>Writes the plans from <see cref="Start"/> on in query
        /// order; stops at a unit that fires (index in <see cref="Stop"/>,
        /// Units.Length when done).</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        internal struct RangedApplyJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Units;
            [ReadOnly] public NativeArray<RangedPlan> Plans;
            public ComponentLookup<Target> Targets;
            public ComponentLookup<ArcherState> Archers;
            public ComponentLookup<LocalTransform> Transforms;
            public EntityCommandBuffer Ecb;
            public int Start;
            public NativeReference<int> Stop;

            public void Execute()
            {
                int n = Units.Length;
                for (int i = Start; i < n; i++)
                {
                    var p = Plans[i];
                    if (p.Kind == RangedSkip) continue;
                    var entity = Units[i];

                    Archers[entity] = p.Archer;
                    if (p.ClearTarget != 0)
                    {
                        Targets[entity] = new Target { Value = Entity.Null };
                        if (p.DropCmd != 0) Ecb.SetComponentEnabled<AttackCommand>(entity, false);
                    }

                    if (p.DestKind == DestStop)
                    {
                        Ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }
                    else if (p.DestKind == DestGoTo)
                    {
                        if (p.HasDest == 0)
                            Ecb.AddComponent(entity, new DesiredDestination { Position = p.DestPos, Has = 1 });
                        else
                            Ecb.SetComponent(entity, new DesiredDestination { Position = p.DestPos, Has = 1 });
                    }
                    if (p.Turn != 0)
                    {
                        var xf = Transforms[entity];
                        xf.Rotation = p.NewRot;
                        Transforms[entity] = xf;
                    }

                    if (p.Kind == RangedFire)
                    {
                        Stop.Value = i;
                        return;
                    }
                }
                Stop.Value = n;
            }
        }
    }
}
