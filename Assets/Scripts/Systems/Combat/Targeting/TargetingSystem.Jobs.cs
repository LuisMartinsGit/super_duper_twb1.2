// TargetingSystem.Jobs.cs
// The per-scanner acquisition, as Burst jobs (2026-10-15 perf pass).
// Partial of TargetingSystem.cs.
//
// SHAPE: a parallel PLAN job per pass (acquire, return-to-guard) does every
// read-only step for one unit — the gates, the stance rules, the spatial-hash
// candidate scan — and writes ONE result slot per unit. The main thread
// (still Burst: OnUpdate is Burst-compiled) then walks the plans in the
// original query order and applies them: the ECB writes, the engagement
// writes, and the one thing that genuinely depends on the units before it —
// the melee SPREAD pick, which reads the running per-target attacker count.
//
// RESULT-IDENTICAL to the old single-threaded loop:
//   - every read a plan makes is of state nothing in either pass writes
//     directly (Target / DesiredDestination / AttackCommand go through the
//     ECB; only UnitEngagement is written live, and the one field of it a
//     later unit could see changed — AutoTarget — is re-read live at apply);
//   - the spread pick keeps, per melee scanner, its nearest candidates in
//     (distance, visit order) — exactly the order the old strict-'<' walk
//     ranked them in — and resolves "nearest under-cap" against the LIVE
//     counts at apply time. When the kept list cannot prove the answer (it
//     overflowed and every kept entry is saturated) the old full scan runs
//     for that unit, on the main thread, with the live counts;
//   - the scan arithmetic is the same code on the same values, compiled with
//     the same deterministic float mode, so every distance is the same float.

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
    public partial struct TargetingSystem : ISystem
    {
        private const byte PlanNone = 0;
        private const byte PlanSetDest = 1;
        private const byte PlanScan = 2;
        private const byte PlanClearSuppressed = 3;

        /// <summary>One candidate kept for the melee spread pick.</summary>
        private struct ScanCand
        {
            public Entity E;
            public float Dist;
        }

        /// <summary>A unit's scan, minus the attacker-count dependent part.</summary>
        private struct ScanOut
        {
            /// <summary>The nearest pick AFTER the bounded value tie-break.</summary>
            public Entity AnyBest;
            public float AnyBestDist;
            public Entity PrefBest;
            public byte IsMelee;
            /// <summary>1 = more melee candidates passed than <see cref="Near"/> holds.</summary>
            public byte Overflow;
            /// <summary>Melee only: the nearest passing candidates, ascending by
            /// (distance, visit order).</summary>
            public FixedList512Bytes<ScanCand> Near;
        }

        /// <summary>What the apply pass does for one unit.</summary>
        private struct UnitPlan
        {
            public byte Kind;
            /// <summary>The unit carried DesiredDestination at plan time
            /// (Add vs Set in SetDestination).</summary>
            public byte HasDest;
            /// <summary>Attack-move / patrol scanner (acquire pass).</summary>
            public byte Scanner;
            public Faction MyFaction;
            /// <summary>SetDest: the destination. Scan: the engagement anchor.</summary>
            public float3 Pos;
            public float3 MyPos;
            public AcquireRules Rules;
            public ScanOut Scan;
        }

        /// <summary>Every per-unit read the plan jobs make.</summary>
        private struct UnitLookups
        {
            [ReadOnly] public ComponentLookup<LocalTransform> Transform;
            [ReadOnly] public ComponentLookup<FactionTag> Faction;
            [ReadOnly] public ComponentLookup<LineOfSight> Los;
            [ReadOnly] public ComponentLookup<Target> Target;
            [ReadOnly] public ComponentLookup<UnitTag> Unit;
            [ReadOnly] public ComponentLookup<Damage> Damage;
            [ReadOnly] public ComponentLookup<UnitEngagement> Engagement;
            [ReadOnly] public ComponentLookup<AttackMoveTag> AttackMove;
            [ReadOnly] public ComponentLookup<PatrolTag> Patrol;
            [ReadOnly] public ComponentLookup<UserMoveOrder> UserMove;
            [ReadOnly] public ComponentLookup<FormationMemberState> Formation;
            [ReadOnly] public ComponentLookup<GuardPoint> Guard;
            [ReadOnly] public ComponentLookup<QueuedMoveStep> Queued;
            [ReadOnly] public ComponentLookup<DesiredDestination> Dest;
            [ReadOnly] public ComponentLookup<HoldPositionTag> Hold;
            [ReadOnly] public ComponentLookup<UnitStance> Stance;
            [ReadOnly] public ComponentLookup<BuildingsOnlyAttacker> BuildingsOnly;
            [ReadOnly] public ComponentLookup<DamageTypeData> DmgType;
            [ReadOnly] public ComponentLookup<ArcherState> ArcherState;
            [ReadOnly] public ComponentLookup<TargetPreference> Pref;
            [ReadOnly] public ComponentLookup<EmplacedEngineTag> Emplaced;
            [ReadOnly] public ComponentLookup<StationaryAutoFire> StationaryFire;
            [ReadOnly] public ComponentLookup<NavLayerIndex> NavLayer;
            [ReadOnly] public ComponentLookup<BuildingTag> Building;
            [ReadOnly] public ComponentLookup<ArcherTag> Archer;
            [ReadOnly] public ComponentLookup<SiegeTag> Siege;
            [ReadOnly] public ComponentLookup<BallistaShooterTag> Ballista;
            [ReadOnly] public ComponentLookup<LastAttackerEntity> LastAttacker;
            [ReadOnly] public ComponentLookup<GuardSuppressed> Suppressed;
            [ReadOnly] public ComponentLookup<LitharchState> Litharch;
            [ReadOnly] public ComponentLookup<SectVeiled> Veiled;
            [ReadOnly] public EntityStorageInfoLookup Storage;

            public static bool On<T>(in ComponentLookup<T> lk, Entity e)
                where T : unmanaged, IComponentData, IEnableableComponent
                => lk.HasComponent(e) && lk.IsComponentEnabled(e);

            public bool IsFixedMount(Entity e)
                => Emplaced.HasComponent(e) || StationaryFire.HasComponent(e);

            public bool CanHitFlying(Entity e)
            {
                if (Building.HasComponent(e)) return true;
                if (!Archer.HasComponent(e)) return false;
                return !Siege.HasComponent(e) || Ballista.HasComponent(e);
            }

            public bool IsOnRampart(Entity e)
                => NavLayer.TryGetComponent(e, out var nav) && nav.Layer == NavLayerIndex.LayerRampart;

            /// <summary><see cref="EffectiveStance(EntityManager, Entity)"/>.</summary>
            public UnitStanceMode EffectiveStance(Entity e)
            {
                if (On(in Hold, e)) return UnitStanceMode.Hold;
                if (On(in Stance, e)) return Stance[e].Value;
                return UnitStanceMode.Aggressive;
            }

            /// <summary><see cref="AttackReach(EntityManager, Entity)"/>.</summary>
            public float AttackReach(Entity e)
            {
                if (ArcherState.TryGetComponent(e, out var a))
                    return a.MaxRange > 0f ? a.MaxRange : RangedFallbackReach;
                return MeleeCombatSystem.MeleeRange;
            }

            /// <summary><see cref="RulesFor(EntityManager, Entity, UnitClass, UnitStanceMode, bool, float, bool, in StanceSettings)"/>,
            /// the same table in the same precedence, through the lookups.</summary>
            public AcquireRules RulesFor(Entity e, UnitClass cls, UnitStanceMode stance, bool scanner,
                float los, bool hitRecently, in StanceSettings s)
            {
                var r = new AcquireRules
                {
                    Acquire = 1,
                    IsMelee = (byte)(cls == UnitClass.Melee ? 1 : 0),
                    BuildingsOnly = (byte)(BuildingsOnly.HasComponent(e) ? 1 : 0),
                    NonSiege = (byte)((!DmgType.TryGetComponent(e, out var dt)
                        || dt.Value != DamageType.Siege) ? 1 : 0),
                    HitRecently = (byte)(hitRecently ? 1 : 0),
                };

                float reach = math.min(los, AttackReach(e));

                if (Pref.TryGetComponent(e, out var pref))
                {
                    r.Pref = pref;
                    r.HasPref = (byte)(r.Pref.IsEmpty ? 0 : 1);
                    r.PreferReach = reach;
                }

                if (IsFixedMount(e))
                {
                    r.ScanRadius = reach;
                    return r;
                }
                if (stance == UnitStanceMode.Hold && !scanner)
                {
                    r.Acquire = 0;
                    return r;
                }
                if (cls == UnitClass.Support)
                {
                    r.ScanRadius = reach;
                    r.ThreatsOnly = 1;
                    return r;
                }
                if (scanner || stance == UnitStanceMode.Aggressive)
                {
                    r.ScanRadius = los;
                    r.Leash = s.AggressiveLeash;
                    return r;
                }
                r.ScanRadius = reach;
                r.ThreatsOnly = 1;
                return r;
            }
        }

        private UnitLookups GetUnitLookups(ref SystemState state) => new UnitLookups
        {
            Transform = SystemAPI.GetComponentLookup<LocalTransform>(true),
            Faction = SystemAPI.GetComponentLookup<FactionTag>(true),
            Los = SystemAPI.GetComponentLookup<LineOfSight>(true),
            Target = SystemAPI.GetComponentLookup<Target>(true),
            Unit = SystemAPI.GetComponentLookup<UnitTag>(true),
            Damage = SystemAPI.GetComponentLookup<Damage>(true),
            Engagement = SystemAPI.GetComponentLookup<UnitEngagement>(true),
            AttackMove = SystemAPI.GetComponentLookup<AttackMoveTag>(true),
            Patrol = SystemAPI.GetComponentLookup<PatrolTag>(true),
            UserMove = SystemAPI.GetComponentLookup<UserMoveOrder>(true),
            Formation = SystemAPI.GetComponentLookup<FormationMemberState>(true),
            Guard = SystemAPI.GetComponentLookup<GuardPoint>(true),
            Queued = SystemAPI.GetComponentLookup<QueuedMoveStep>(true),
            Dest = SystemAPI.GetComponentLookup<DesiredDestination>(true),
            Hold = SystemAPI.GetComponentLookup<HoldPositionTag>(true),
            Stance = SystemAPI.GetComponentLookup<UnitStance>(true),
            BuildingsOnly = SystemAPI.GetComponentLookup<BuildingsOnlyAttacker>(true),
            DmgType = SystemAPI.GetComponentLookup<DamageTypeData>(true),
            ArcherState = SystemAPI.GetComponentLookup<ArcherState>(true),
            Pref = SystemAPI.GetComponentLookup<TargetPreference>(true),
            Emplaced = SystemAPI.GetComponentLookup<EmplacedEngineTag>(true),
            StationaryFire = SystemAPI.GetComponentLookup<StationaryAutoFire>(true),
            NavLayer = SystemAPI.GetComponentLookup<NavLayerIndex>(true),
            Building = SystemAPI.GetComponentLookup<BuildingTag>(true),
            Archer = SystemAPI.GetComponentLookup<ArcherTag>(true),
            Siege = SystemAPI.GetComponentLookup<SiegeTag>(true),
            Ballista = SystemAPI.GetComponentLookup<BallistaShooterTag>(true),
            LastAttacker = SystemAPI.GetComponentLookup<LastAttackerEntity>(true),
            Suppressed = SystemAPI.GetComponentLookup<GuardSuppressed>(true),
            Litharch = SystemAPI.GetComponentLookup<LitharchState>(true),
            Veiled = SystemAPI.GetComponentLookup<SectVeiled>(true),
            Storage = SystemAPI.GetEntityStorageInfoLookup(),
        };

        // ── The scan ───────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="FindAutoTarget"/>'s candidate walk, read-only: the same
        /// filters in the same order on the same arrays. Everything but the
        /// attacker-count dependent spread pick is resolved here; the melee
        /// candidates are kept in (distance, visit order) for it.
        /// </summary>
        private static void ScanCore(Entity self, float3 myPos, Faction myFaction,
            in AcquireRules rules, in UnitLookups lk, in EnemyScan scan, ref ScanOut o)
        {
            o.AnyBest = Entity.Null;
            o.AnyBestDist = float.MaxValue;
            o.PrefBest = Entity.Null;
            o.IsMelee = rules.IsMelee;
            o.Overflow = 0;
            o.Near.Clear();

            int myF = (int)myFaction;
            if (myF < 0 || myF >= FactionSlots) return;

            bool isMelee = rules.IsMelee != 0;
            bool selfOnRampart = isMelee && lk.IsOnRampart(self);
            bool hitsAir = lk.CanHitFlying(self);

            Entity anyBest = Entity.Null;
            float anyBestDist = float.MaxValue;
            byte anyBestPrio = 0;
            Entity prioBest = Entity.Null;
            float prioBestDist = float.MaxValue;
            byte prioBestPrio = 0;
            Entity prefBest = Entity.Null;
            float prefBestDist = float.MaxValue;
            int prefBestDensity = -1;

            Entity lastAttacker = UnitLookups.On(in lk.LastAttacker, self)
                ? lk.LastAttacker[self].Value : Entity.Null;

            float rejectBase = rules.ScanRadius * 1.001f + 0.01f;

            int radius = (int)math.ceil(rules.ScanRadius / TargetingCellSize);
            var myCell = new int2(
                (int)math.floor(myPos.x / TargetingCellSize),
                (int)math.floor(myPos.z / TargetingCellSize));

            for (int f = 0; f < FactionSlots; f++)
            {
                if (scan.FactionPresent[f] == 0) continue;
                if (scan.Hostile[myF * FactionSlots + f] == 0) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var key = new int3(myCell.x + dx, myCell.y + dy, f);
                        if (!scan.Map.TryGetFirstValue(key, out int i, out var it)) continue;
                        do
                        {
                            if (scan.Health[i].Value <= 0) continue;
                            byte flags = scan.Flags[i];

                            if (rules.BuildingsOnly != 0 && (flags & FlagBuilding) == 0) continue;
                            if (rules.NonSiege != 0 && (flags & FlagWall) != 0) continue;

                            var enemyPos = scan.Transforms[i].Position;
                            float px = enemyPos.x - myPos.x, pz = enemyPos.z - myPos.z;
                            float rejectR = rejectBase + scan.Pad[i];
                            if (px * px + pz * pz > rejectR * rejectR) continue;

                            var cand = scan.Entities[i];
                            var dist = scan.SurfaceDist(i, myPos, enemyPos);
                            if (dist > rules.ScanRadius) continue;

                            if ((flags & FlagFlying) != 0 && !hitsAir) continue;

                            if (myFaction == Faction.Border)
                            {
                                byte veil = RunaiVeil.TierOf(f);
                                if (veil >= RunaiVeil.VeiledPilgrims
                                    && (flags & (FlagWorker | FlagCivilian)) != 0) continue;
                                if (veil >= RunaiVeil.HushedVeil
                                    && dist > rules.ScanRadius * RunaiVeil.HushedScanMultiplier) continue;
                            }

                            if (myFaction == Faction.Border && dist > 9f && (flags & FlagWorker) != 0)
                                continue;

                            if (isMelee && MeleeHeightBlocks(myPos.y, enemyPos.y,
                                    (flags & FlagBuilding) != 0, selfOnRampart))
                                continue;

                            if (dist > 3f && scan.IsHidden(cand))
                                continue;

                            if (rules.ThreatsOnly != 0
                                && !IsThreat(in scan.Targets, cand, self, lastAttacker, rules.HitRecently != 0))
                                continue;

                            if (rules.HasPref != 0 && dist <= rules.PreferReach
                                && (flags & FlagBuilding) == 0
                                && (!rules.Pref.HasRules
                                    || rules.Pref.Matches(scan.Tags[i], (flags & FlagHero) != 0)))
                            {
                                int density = 0;
                                if (rules.Pref.Massed != 0)
                                    density = scan.Map.CountValuesForKey(new int3(
                                        (int)math.floor(enemyPos.x / TargetingCellSize),
                                        (int)math.floor(enemyPos.z / TargetingCellSize), f));
                                if (density > prefBestDensity
                                    || (density == prefBestDensity && dist < prefBestDist))
                                {
                                    prefBest = cand;
                                    prefBestDist = dist;
                                    prefBestDensity = density;
                                }
                            }

                            byte prio = scan.Priority[i];
                            if (dist < anyBestDist) { anyBest = cand; anyBestDist = dist; anyBestPrio = prio; }
                            if (prio > prioBestPrio || (prio == prioBestPrio && dist < prioBestDist))
                            {
                                prioBest = cand;
                                prioBestDist = dist;
                                prioBestPrio = prio;
                            }
                            if (isMelee) AddNear(ref o, cand, dist);
                        } while (scan.Map.TryGetNextValue(out i, ref it));
                    }
                }
            }

            if (prioBest != Entity.Null && prioBestPrio > anyBestPrio
                && prioBestDist <= math.max(anyBestDist, NearDistFloor) * ValuePickDistRatio)
            {
                anyBest = prioBest;
                anyBestDist = prioBestDist;
            }

            o.AnyBest = anyBest;
            o.AnyBestDist = anyBestDist;
            o.PrefBest = prefBest;
        }

        /// <summary>Keep the list ascending by (distance, visit order): a new
        /// entry goes AFTER every equal distance, because the old walk's strict
        /// '&lt;' kept the first-visited of equals.</summary>
        private static void AddNear(ref ScanOut o, Entity e, float d)
        {
            int n = o.Near.Length;
            if (n == o.Near.Capacity)
            {
                o.Overflow = 1;
                if (d >= o.Near[n - 1].Dist) return;
                o.Near.RemoveAt(n - 1);
                n--;
            }
            int pos = n;
            while (pos > 0 && o.Near[pos - 1].Dist > d) pos--;
            o.Near.Insert(pos, new ScanCand { E = e, Dist = d });
        }

        /// <summary>
        /// The pick <see cref="FindAutoTarget"/> makes from a finished scan,
        /// against the live attacker counts, and the count it records. Sets
        /// <paramref name="fallback"/> (and records nothing) when the kept
        /// melee list cannot prove the spread answer — the caller then runs
        /// the full scan.
        /// </summary>
        private static Entity PickFromScan(in ScanOut s, ref NativeHashMap<Entity, int> attackerCount,
            out bool fallback)
        {
            fallback = false;
            Entity best;
            if (s.PrefBest != Entity.Null)
            {
                best = s.PrefBest;
            }
            else if (s.IsMelee == 0)
            {
                best = s.AnyBest;
            }
            else
            {
                float limit = math.max(s.AnyBestDist, NearDistFloor) * SpreadDistRatio;
                Entity under = Entity.Null;
                float underDist = float.MaxValue;
                bool beyond = false;
                for (int k = 0; k < s.Near.Length; k++)
                {
                    var c = s.Near[k];
                    // Past the spread band: nothing from here on can be picked
                    // over the nearest, so the nearest stands.
                    if (!(c.Dist <= limit)) { beyond = true; break; }
                    int cur = attackerCount.TryGetValue(c.E, out int cv) ? cv : 0;
                    if (cur < MaxAttackersPerEnemy) { under = c.E; underDist = c.Dist; break; }
                }
                if (under == Entity.Null && s.Overflow != 0 && !beyond)
                {
                    fallback = true;
                    return Entity.Null;
                }
                best = PickSpreadOrNearest(under, underDist, s.AnyBest, s.AnyBestDist);
            }

            if (best != Entity.Null)
            {
                int prev = attackerCount.TryGetValue(best, out int pv) ? pv : 0;
                attackerCount[best] = prev + 1;
            }
            return best;
        }

        // ── Plan jobs ──────────────────────────────────────────────────────

        /// <summary>The acquire pass's per-unit gates and scan
        /// (<see cref="AutoAcquireTargets"/>), one plan slot per unit.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct AcquirePlanJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Units;
            [ReadOnly] public UnitLookups Lk;
            [ReadOnly] public EnemyScan Scan;
            public StanceSettings Settings;
            public uint Tick;
            public float Clock;
            [WriteOnly] public NativeArray<UnitPlan> Plans;
            /// <summary>Plan kind per slot, apart from the (large) plan, so the
            /// apply loop copies a plan only when there is one.</summary>
            [WriteOnly] public NativeArray<byte> Kinds;

            public void Execute(int idx)
            {
                var entity = Units[idx];
                var plan = new UnitPlan { Kind = PlanNone };
                Evaluate(entity, ref plan);
                Kinds[idx] = plan.Kind;
                if (plan.Kind != PlanNone) Plans[idx] = plan;
            }

            private void Evaluate(Entity entity, ref UnitPlan plan)
            {
                if (Lk.Target[entity].Value != Entity.Null) return;
                var eng = Lk.Engagement[entity];
                if (eng.AutoTarget != Entity.Null) return;

                var cls = Lk.Unit[entity].Class;
                if (cls == UnitClass.Scout || cls == UnitClass.Economy || cls == UnitClass.Worker)
                    return;
                if (Lk.Damage[entity].Value <= 0) return;
                if (((Tick + eng.ScanPhase) & (AcquireStagger - 1)) != 0) return;
                if (Clock < eng.NextAcquireAt) return;

                bool hasAttackMove = UnitLookups.On(in Lk.AttackMove, entity);
                bool hasPatrol = Lk.Patrol.HasComponent(entity);
                bool hasUserMoveOrder = UnitLookups.On(in Lk.UserMove, entity);
                bool isActiveScanner = hasAttackMove || hasPatrol;

                var myPos = Lk.Transform[entity].Position;

                if (hasAttackMove
                    && UnitLookups.On(in Lk.Formation, entity)
                    && UnitLookups.On(in Lk.Guard, entity))
                {
                    var fgp = Lk.Guard[entity];
                    if (fgp.Has != 0 && DistXZ(myPos, fgp.Position) > FormationHoldRadius)
                    {
                        bool underFire = Clock - eng.HitAt <= Settings.UnderFireWindow;
                        if (!underFire) return;
                    }
                }

                if (!isActiveScanner && hasUserMoveOrder) return;
                if (!isActiveScanner && UnitLookups.On(in Lk.Queued, entity)) return;

                bool hasDest = Lk.Dest.TryGetComponent(entity, out var dd);
                if (!isActiveScanner && hasDest && dd.Has != 0) return;

                var myFaction = Lk.Faction[entity].Value;
                var stance = Lk.EffectiveStance(entity);

                bool hitRecently = Clock - eng.HitAt <= Settings.RetaliationWindow;
                var rules = Lk.RulesFor(entity, cls, stance, isActiveScanner,
                    Lk.Los[entity].Radius, hitRecently, in Settings);
                if (rules.Acquire == 0) return;

                float3 anchor = myPos;
                if (!isActiveScanner && stance != UnitStanceMode.Hold
                    && !Lk.IsFixedMount(entity)
                    && UnitLookups.On(in Lk.Guard, entity))
                {
                    var guardPoint = Lk.Guard[entity];
                    if (guardPoint.Has != 0)
                    {
                        anchor = guardPoint.Position;
                        var distFromGuard = DistXZ(myPos, guardPoint.Position);
                        if (distFromGuard > MaxGuardDistance)
                        {
                            plan.Kind = PlanSetDest;
                            plan.HasDest = (byte)(hasDest ? 1 : 0);
                            plan.Pos = guardPoint.Position;
                            return;
                        }
                        if (rules.Leash > 0f && distFromGuard > rules.Leash) return;
                    }
                }

                plan.Kind = PlanScan;
                plan.Scanner = (byte)(isActiveScanner ? 1 : 0);
                plan.MyFaction = myFaction;
                plan.MyPos = myPos;
                plan.Pos = isActiveScanner ? myPos : anchor;
                plan.Rules = rules;
                ScanCore(entity, myPos, myFaction, in rules, in Lk, in Scan, ref plan.Scan);
            }
        }

        /// <summary>The return-to-guard pass's per-unit gates and scan
        /// (<see cref="ProcessReturnToGuard"/>), one plan slot per unit. The
        /// "acquired earlier this tick" gate is re-checked live at apply.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct GuardPlanJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Units;
            [ReadOnly] public UnitLookups Lk;
            [ReadOnly] public EnemyScan Scan;
            public StanceSettings Settings;
            public uint Tick;
            public float Clock;
            [WriteOnly] public NativeArray<UnitPlan> Plans;
            /// <summary>Plan kind per slot, apart from the (large) plan, so the
            /// apply loop copies a plan only when there is one.</summary>
            [WriteOnly] public NativeArray<byte> Kinds;

            public void Execute(int idx)
            {
                var entity = Units[idx];
                var plan = new UnitPlan { Kind = PlanNone };
                Evaluate(entity, ref plan);
                Kinds[idx] = plan.Kind;
                if (plan.Kind != PlanNone) Plans[idx] = plan;
            }

            private void Evaluate(Entity entity, ref UnitPlan plan)
            {
                if (Lk.Target[entity].Value != Entity.Null)
                {
                    if (UnitLookups.On(in Lk.Suppressed, entity))
                        plan.Kind = PlanClearSuppressed;
                    return;
                }
                var guardPoint = Lk.Guard[entity];
                if (guardPoint.Has == 0) return;
                var eng = Lk.Engagement[entity];
                if (eng.AutoTarget != Entity.Null) return;

                if (UnitLookups.On(in Lk.Suppressed, entity)
                    && DistXZ(Lk.Suppressed[entity].Point, guardPoint.Position) < GuardSuppressed.Epsilon)
                    return;

                var cls = Lk.Unit[entity].Class;
                if (cls == UnitClass.Scout) return;

                if (Lk.Litharch.TryGetComponent(entity, out var ls))
                {
                    if (ls.IsHealing != 0 && ls.HealTarget != Entity.Null && Lk.Storage.Exists(ls.HealTarget))
                        return;
                }

                var myPos = Lk.Transform[entity].Position;
                var gpPos = guardPoint.Position;
                var distToGuard = DistXZ(myPos, gpPos);

                bool hasDest = Lk.Dest.TryGetComponent(entity, out var dd);
                bool hasLiveDest = hasDest && dd.Has != 0;
                plan.HasDest = (byte)(hasDest ? 1 : 0);

                if (UnitLookups.On(in Lk.Hold, entity) || Lk.IsFixedMount(entity))
                    return;

                if (UnitLookups.On(in Lk.AttackMove, entity) || Lk.Patrol.HasComponent(entity))
                {
                    if (!hasLiveDest && distToGuard > GuardReturnThreshold)
                    {
                        plan.Kind = PlanSetDest;
                        plan.Pos = gpPos;
                    }
                    return;
                }

                if (!(distToGuard > GuardReturnThreshold)) return;

                var stance = Lk.EffectiveStance(entity);
                bool scanDue = ((Tick + eng.ScanPhase) & (AcquireStagger - 1)) == 0;
                if (hasLiveDest && scanDue && Clock >= eng.NextAcquireAt
                    && !UnitLookups.On(in Lk.Veiled, entity)
                    && Lk.Damage.TryGetComponent(entity, out var dmg)
                    && dmg.Value > 0)
                {
                    bool hitRecently = Clock - eng.HitAt <= Settings.RetaliationWindow;
                    var rules = Lk.RulesFor(entity, cls, stance, false, Lk.Los[entity].Radius,
                        hitRecently, in Settings);
                    if (cls != UnitClass.Economy && cls != UnitClass.Worker
                        && rules.Acquire != 0
                        && (rules.Leash <= 0f || distToGuard <= rules.Leash))
                    {
                        var myFaction = Lk.Faction[entity].Value;
                        plan.Kind = PlanScan;
                        plan.MyFaction = myFaction;
                        plan.MyPos = myPos;
                        plan.Pos = gpPos;
                        plan.Rules = rules;
                        ScanCore(entity, myPos, myFaction, in rules, in Lk, in Scan, ref plan.Scan);
                        // A scan that finds nothing falls through to the
                        // return below only when the unit has no live
                        // destination — and it scanned BECAUSE it has one.
                        return;
                    }
                }

                if (!hasLiveDest)
                {
                    plan.Kind = PlanSetDest;
                    plan.Pos = gpPos;
                }
            }
        }

        // ── Apply ─────────────────────────────────────────────────────────

        /// <summary>Resolve one Scan plan: the spread pick against the live
        /// counts, or the full scan when the kept list cannot decide.</summary>
        private static Entity ResolveScan(EntityManager em, Entity entity, in UnitPlan plan,
            in EnemyScan scan, ref NativeHashMap<Entity, int> attackerCount)
        {
            Entity best = PickFromScan(in plan.Scan, ref attackerCount, out bool fallback);
            if (fallback)
                best = FindAutoTarget(em, entity, plan.MyPos, plan.MyFaction, in plan.Rules,
                    in scan, ref attackerCount);
            return best;
        }

        private static void SetDestination(ref EntityCommandBuffer ecb, Entity entity, float3 pos, bool hasDest)
        {
            if (!hasDest)
                ecb.AddComponent(entity, new DesiredDestination { Position = pos, Has = 1 });
            else
                ecb.SetComponent(entity, new DesiredDestination { Position = pos, Has = 1 });
        }
    }
}
