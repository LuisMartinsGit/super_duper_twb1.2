// TargetingSystem.Acquire.cs
// Auto target acquisition by STANCE (spatial-hash enemy scan), the
// spread/nearest pick, and the chase leash on auto-acquired engagements.
// docs/Design/Stances.md is canonical for the rules.
// Partial of TargetingSystem.cs -- split 2026-08-12 for readability.

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
        /// <summary>What a unit may auto-acquire right now (stance + order).</summary>
        private struct AcquireRules
        {
            /// <summary>0 = this unit acquires nothing on its own (Hold).</summary>
            public byte Acquire;
            /// <summary>Cell sweep radius and the hard distance cap.</summary>
            public float ScanRadius;
            /// <summary>1 = a candidate is accepted only if it is attacking
            /// THIS unit (it hit us within the retaliation window, or its
            /// Target is us) -- the Defensive "return fire" rule.</summary>
            public byte ThreatsOnly;
            /// <summary>1 = this unit was hit within the retaliation window,
            /// so its LastAttackerEntity counts as a threat.</summary>
            public byte HitRecently;
            /// <summary>Chase leash for an engagement acquired under these
            /// rules; 0 = the unit never moves for it (the combat systems
            /// drop the target the moment it leaves reach).</summary>
            public float Leash;
            public byte IsMelee;
            public byte BuildingsOnly;
            public byte NonSiege;
            /// <summary>1 = the unit carries a TargetPreference
            /// (Combat_Pacing.md § Target preference).</summary>
            public byte HasPref;
            public TargetPreference Pref;
            /// <summary>A preferred candidate counts only inside this — the
            /// unit's own attack reach — so a preference never drags it off.</summary>
            public float PreferReach;
        }

        /// <summary>A unit's own attack reach, to the target's surface:
        /// melee MeleeRange, ranged its ArcherState.MaxRange.</summary>
        private static float AttackReach(EntityManager em, Entity e)
        {
            if (em.HasComponent<ArcherState>(e))
            {
                var a = em.GetComponentData<ArcherState>(e);
                return a.MaxRange > 0f ? a.MaxRange : RangedFallbackReach;
            }
            return MeleeCombatSystem.MeleeRange;
        }

        /// <summary>RangedCombatSystem.DefaultMaxRange, for an archer whose SO
        /// left MaxRange at 0 -- the two must agree or a unit acquires what it
        /// then refuses to shoot.</summary>
        private const float RangedFallbackReach = 25f;

        /// <summary>
        /// A fixed mount (Stances.md §1a): an emplaced engine, or anything a
        /// scenario planted with <see cref="StationaryAutoFire"/>. It fires at
        /// whatever is inside its reach whatever its stance, and never moves
        /// for a target -- ordered or not. Public: the melee and ranged
        /// systems ask the same question.
        /// </summary>
        public static bool IsFixedMount(EntityManager em, Entity e)
            => em.HasComponent<EmplacedEngineTag>(e) || em.HasComponent<StationaryAutoFire>(e);

        /// <summary>
        /// May this unit MOVE to engage <paramref name="target"/>? The one
        /// question the melee and ranged chase branches ask (Stances.md §1, §5).
        ///
        /// - A fixed mount never moves, and a channelling ritualist holds its
        ///   ground.
        /// - A target TargetingSystem picked on its own (UnitEngagement.AutoTarget)
        ///   may be chased only when its engagement carries a leash -- the
        ///   Aggressive stance, attack-move and patrol. Defensive, Hold and
        ///   support units get a 0 leash: they fight what is in reach and let
        ///   the rest go.
        /// - Anything else is an ORDER (player or AI) and is always chased, on
        ///   every stance, Hold included. Player orders win.
        /// </summary>
        public static bool MayChase(EntityManager em, Entity e, Entity target)
        {
            if (IsFixedMount(em, e)) return false;
            if (em.HasComponent<RitualState>(e)) return false;
            if (TransientState.Active<UnitEngagement>(em, e))
            {
                var eng = em.GetComponentData<UnitEngagement>(e);
                if (eng.AutoTarget != Entity.Null && eng.AutoTarget == target)
                    return eng.Leash > 0f;
            }
            return true;
        }

        /// <summary><see cref="IsFixedMount(EntityManager, Entity)"/> through
        /// the combat loops' shared lookups.</summary>
        public static bool IsFixedMount(in CombatLookups lk, Entity e)
            => lk.Emplaced.HasComponent(e) || lk.StationaryFire.HasComponent(e);

        /// <summary><see cref="MayChase(EntityManager, Entity, Entity)"/>
        /// through the combat loops' shared lookups — the same rules, in the
        /// same order.</summary>
        public static bool MayChase(in CombatLookups lk, Entity e, Entity target)
        {
            if (IsFixedMount(in lk, e)) return false;
            if (lk.Ritual.HasComponent(e)) return false;
            if (lk.Engagement.TryGetComponent(e, out var eng) && lk.Engagement.IsComponentEnabled(e))
            {
                if (eng.AutoTarget != Entity.Null && eng.AutoTarget == target)
                    return eng.Leash > 0f;
            }
            return true;
        }

        /// <summary>
        /// The stance table (docs/Design/Stances.md §1), in precedence order:
        ///
        /// 1. Fixed mount -- anything in reach, never moves.
        /// 2. Hold (and not attack-moving / patrolling) -- nothing at all.
        /// 3. Support class -- only what is attacking it and inside its reach,
        ///    never moves (even on attack-move: a healer never runs into melee).
        /// 4. Aggressive, attack-move or patrol -- anything in line of sight,
        ///    chased up to the Aggressive leash.
        /// 5. Defensive -- only what is attacking it and inside its reach,
        ///    never moves.
        /// </summary>
        private static AcquireRules RulesFor(EntityManager em, Entity e, UnitClass cls,
            UnitStanceMode stance, bool scanner, float los, bool hitRecently, in StanceSettings s)
        {
            var r = new AcquireRules
            {
                Acquire = 1,
                IsMelee = (byte)(cls == UnitClass.Melee ? 1 : 0),
                // Buildings-only siege (Battering Ram): never auto-acquire a
                // non-building target -- it exists to crack walls, not to
                // swing at soldiers (the melee fire path refuses those anyway).
                BuildingsOnly = (byte)(em.HasComponent<BuildingsOnlyAttacker>(e) ? 1 : 0),
                // The Wall Rule (docs/Design/Combat_Pacing.md): only siege
                // damages wall pieces, so non-siege never auto-acquires one.
                NonSiege = (byte)((!em.HasComponent<DamageTypeData>(e)
                    || em.GetComponentData<DamageTypeData>(e).Value != DamageType.Siege) ? 1 : 0),
                HitRecently = (byte)(hitRecently ? 1 : 0),
            };

            float reach = math.min(los, AttackReach(em, e));

            // TARGET PREFERENCE (2026-10-03): the anti-army siege engines
            // (Ballista: heroes, heavy cavalry, siege; Catapult: the densest
            // knot of enemies) pick their own kind of target from what is
            // already inside their reach. Data on the unit SO (preferTargets).
            if (em.HasComponent<TargetPreference>(e))
            {
                r.Pref = em.GetComponentData<TargetPreference>(e);
                r.HasPref = (byte)(r.Pref.IsEmpty ? 0 : 1);
                r.PreferReach = reach;
            }

            if (IsFixedMount(em, e))
            {
                r.ScanRadius = reach;
                return r;
            }
            if (stance == UnitStanceMode.Hold && !scanner)
            {
                // Hold is passive: it neither returns fire nor pursues.
                r.Acquire = 0;
                return r;
            }
            if (cls == UnitClass.Support)
            {
                r.ScanRadius = reach;
                r.ThreatsOnly = 1;
                return r;
            }
            // The 1.5x line-of-sight acquire bonus is GONE: a unit no longer
            // notices enemies it cannot see.
            if (scanner || stance == UnitStanceMode.Aggressive)
            {
                r.ScanRadius = los;
                r.Leash = s.AggressiveLeash;
                return r;
            }
            // Defensive: return fire, do not pursue.
            r.ScanRadius = reach;
            r.ThreatsOnly = 1;
            return r;
        }

        /// <summary>
        /// PHASE 2 + 3 together: auto-acquire for idle / attack-move / patrol
        /// units, then return-to-guard. Both passes' per-unit gates and
        /// candidate scans run as parallel Burst plan jobs
        /// (TargetingSystem.Jobs.cs); the plans are then applied here in the
        /// original order — every acquire unit in query order, then every
        /// guard unit in query order — which is where the shared attacker
        /// count and the ECB writes happen, exactly as the old loops did them.
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void AutoAcquireAndReturnToGuard(ref SystemState state, ref EntityCommandBuffer ecb,
            in EnemyScan scan, in StanceSettings settings,
            ref NativeHashMap<Entity, int> attackerCount)
        {
            var em = state.EntityManager;

            // The same entity sets, in the same order, as the two old foreach
            // loops (see the comments there for why each exclusion exists).
            var acquireQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, FactionTag, LineOfSight, Target, UnitTag, Damage>()
                .WithAllRW<UnitEngagement>()
                .WithNone<DeathAnimationState, TheWaningBorder.Entities.Launched, AttackCommand>()
                .WithNone<PassiveWorkerTag, BuildCommand, SectVeiled>()
                .WithNone<WorkerTag, RitualState>()
                .Build();
            var guardQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, GuardPoint, FactionTag, LineOfSight, Target>()
                .WithAllRW<UnitEngagement>()
                .WithAll<UnitTag>()
                .WithNone<AttackCommand, UserMoveOrder, HealCommand>()
                .WithNone<PassiveWorkerTag, BuildCommand, WorkerTag, RitualState>()
                .Build();

            using var acquireUnits = acquireQuery.ToEntityArray(Allocator.TempJob);
            using var guardUnits = guardQuery.ToEntityArray(Allocator.TempJob);
            using var acquirePlans = new NativeArray<UnitPlan>(acquireUnits.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            using var guardPlans = new NativeArray<UnitPlan>(guardUnits.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            using var acquireKinds = new NativeArray<byte>(acquireUnits.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            using var guardKinds = new NativeArray<byte>(guardUnits.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            var lk = GetUnitLookups(ref state);
            var jobA = new AcquirePlanJob
            {
                Units = acquireUnits,
                Lk = lk,
                Scan = scan,
                Settings = settings,
                Tick = _tick,
                Clock = _clock,
                Plans = acquirePlans,
                Kinds = acquireKinds,
            };
            var hA = jobA.Schedule(acquireUnits.Length, 8, state.Dependency);
            var jobG = new GuardPlanJob
            {
                Units = guardUnits,
                Lk = lk,
                Scan = scan,
                Settings = settings,
                Tick = _tick,
                Clock = _clock,
                Plans = guardPlans,
                Kinds = guardKinds,
            };
            var hG = jobG.Schedule(guardUnits.Length, 8, state.Dependency);
            // Completed here, but the handle must still BE the system's
            // Dependency: the safety system checks that every job which read
            // a component (LineOfSight, via the lookups) was handed to it, and
            // logged an error every tick otherwise (2026-10-16).
            var plansDone = Unity.Jobs.JobHandle.CombineDependencies(hA, hG);
            plansDone.Complete();
            state.Dependency = plansDone;

            var engRW = SystemAPI.GetComponentLookup<UnitEngagement>(false);

            // ── Apply: acquisition, in query order. ──
            for (int i = 0; i < acquireUnits.Length; i++)
            {
                if (acquireKinds[i] == PlanNone) continue;
                var plan = acquirePlans[i];
                var entity = acquireUnits[i];
                if (plan.Kind == PlanSetDest)
                {
                    SetDestination(ref ecb, entity, plan.Pos, plan.HasDest != 0);
                    continue;
                }

                Entity bestTarget = ResolveScan(em, entity, in plan, in scan, ref attackerCount);
                if (bestTarget != Entity.Null && em.Exists(bestTarget))
                {
                    ecb.SetComponent(entity, new Target { Value = bestTarget });
                    var e = engRW[entity];
                    e.AutoTarget = bestTarget;
                    e.Anchor = plan.Pos;
                    e.Leash = plan.Rules.Leash;
                    engRW[entity] = e;

                    // Attack-move and patrol units also issue an AttackCommand so combat systems chase
                    // Do NOT clear DesiredDestination - unit resumes movement after combat.
                    // Not for a unit that may not chase (support on attack-move):
                    // it returns fire where it stands and keeps marching.
                    if (plan.Scanner != 0 && plan.Rules.Leash > 0f)
                        TransientState.Set(ecb, entity, new AttackCommand { Target = bestTarget });
                }
            }

            // ── Apply: return-to-guard, in query order. ──
            for (int i = 0; i < guardUnits.Length; i++)
            {
                if (guardKinds[i] == PlanNone) continue;
                var plan = guardPlans[i];
                var entity = guardUnits[i];
                if (plan.Kind == PlanClearSuppressed)
                {
                    // TransientState.Clear: the plan saw it present.
                    ecb.SetComponentEnabled<GuardSuppressed>(entity, false);
                    continue;
                }
                // Acquired earlier this tick (the Target write is still in the
                // ECB): it is about to fight, not to walk home. Live read —
                // the acquire apply above may have just set it.
                if (engRW[entity].AutoTarget != Entity.Null) continue;

                if (plan.Kind == PlanSetDest)
                {
                    SetDestination(ref ecb, entity, plan.Pos, plan.HasDest != 0);
                    continue;
                }

                Entity nearestEnemy = ResolveScan(em, entity, in plan, in scan, ref attackerCount);
                if (nearestEnemy != Entity.Null && em.Exists(nearestEnemy))
                {
                    ecb.SetComponent(entity, new Target { Value = nearestEnemy });
                    var e = engRW[entity];
                    e.AutoTarget = nearestEnemy;
                    e.Anchor = plan.Pos;
                    e.Leash = plan.Rules.Leash;
                    engRW[entity] = e;
                }
            }
        }

        /// <summary>
        /// The candidate scan shared by acquisition and return-to-guard:
        /// hostile factions' buckets only, stance rules applied, then the
        /// value / spread / nearest pick. Records the pick in attackerCount.
        /// </summary>
        private static Entity FindAutoTarget(EntityManager em, Entity self, float3 myPos,
            Faction myFaction, in AcquireRules rules, in EnemyScan scan,
            ref NativeHashMap<Entity, int> attackerCount)
        {
            int myF = (int)myFaction;
            if (myF < 0 || myF >= FactionSlots) return Entity.Null;
            bool rogueScan = TransientState.Active<SectDisordered>(em, self);
            int row = rogueScan ? RogueSlot : myF;

            // Two-pass scan throughout: track both absolute-nearest enemy
            // ("anyBest") and nearest under-cap enemy ("underBest"). Pick
            // under-cap only when (a) the attacker is melee (ranged/siege
            // don't physically clump, so they always pick nearest), AND
            // (b) the under-cap candidate is within SpreadDistRatio of
            // anyBest (so we don't march 50m to attack a far-away target
            // when a saturated one is right in front of us). Otherwise
            // fall back to anyBest — overflow attackers will dogpile.
            bool isMelee = rules.IsMelee != 0;
            bool selfOnRampart = isMelee && IsOnRampart(em, self);
            bool hitsAir = CanHitFlying(em, self);

            Entity underBest = Entity.Null;
            float underBestDist = float.MaxValue;
            Entity anyBest = Entity.Null;
            float anyBestDist = float.MaxValue;
            byte anyBestPrio = 0;
            Entity prioBest = Entity.Null;
            float prioBestDist = float.MaxValue;
            byte prioBestPrio = 0;
            // Target preference: the best preferred candidate in reach.
            Entity prefBest = Entity.Null;
            float prefBestDist = float.MaxValue;
            int prefBestDensity = -1;

            Entity lastAttacker = TransientState.Active<LastAttackerEntity>(em, self)
                ? em.GetComponentData<LastAttackerEntity>(self).Value : Entity.Null;

            // Conservative pivot reject (2026-10-15): a candidate whose pivot
            // is farther than ScanRadius + its footprint reach (+ a float
            // margin) cannot have a surface inside ScanRadius, so it is
            // dropped before the surface distance is even computed. It only
            // drops what `dist > ScanRadius` would drop: the accepted set and
            // every distance are unchanged.
            float rejectBase = rules.ScanRadius * 1.001f + 0.01f;

            int radius = (int)math.ceil(rules.ScanRadius / TargetingCellSize);
            var myCell = new int2(
                (int)math.floor(myPos.x / TargetingCellSize),
                (int)math.floor(myPos.z / TargetingCellSize));

            // Hostile factions only: allies' buckets are never visited
            // (docs/Design/Teams.md — allies are never auto-acquired).
            for (int f = 0; f < GridSlots; f++)
            {
                if (scan.FactionPresent[f] == 0) continue;
                if (scan.Hostile[row * GridSlots + f] == 0) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var key = new int3(myCell.x + dx, myCell.y + dy, f);
                        if (!scan.Grid.Range(key, out int k0, out int k1)) continue;
                        for (int k = k0; k < k1; k++)
                        {
                            var ec = scan.Grid.Cands[k];
                            if (ec.Health <= 0) continue;
                            if (ec.E == self) continue;
                            byte flags = ec.Flags;

                            // Buildings-only siege: units are invisible to
                            // the ram's target scan.
                            if (rules.BuildingsOnly != 0 && (flags & FlagBuilding) == 0) continue;

                            // The Wall Rule: wall pieces are invisible to
                            // non-siege target scans.
                            if (rules.NonSiege != 0 && (flags & FlagWall) != 0) continue;

                            var enemyPos = ec.Pos;
                            float px = enemyPos.x - myPos.x, pz = enemyPos.z - myPos.z;
                            float rejectR = rejectBase + ec.Pad;
                            if (px * px + pz * pz > rejectR * rejectR) continue;

                            var cand = ec.E;
                            // SURFACE distance, so a big building is judged by
                            // where its walls are, not where its pivot is. A
                            // 7x7 temple's pivot sits 3.5 m inside itself; on
                            // centre distance it read as further away than a
                            // hut standing right beside it, and the "nearest
                            // enemy" pick skipped the thing the unit was
                            // literally touching. Read from the per-tick
                            // footprint arrays — the same arithmetic as
                            // TargetGeometry.SurfaceDistXZ, no EntityManager.
                            var dist = ec.SurfaceDist(myPos);
                            if (dist > rules.ScanRadius) continue;

                            if ((flags & FlagFlying) != 0 && !hitsAir) continue;

                            // The Runai curse ladder (docs/Design/Age_1_Runai.md §5.2):
                            // Veiled Pilgrims — the curse never picks a Runai
                            // caravan, trader-warrior or worker; Hushed Veil —
                            // it notices a Runai unit only up close.
                            if (myFaction == Faction.Border)
                            {
                                byte veil = RunaiVeil.TierOf(f);
                                if (veil >= RunaiVeil.VeiledPilgrims
                                    && (flags & (FlagWorker | FlagCivilian)) != 0) continue;
                                if (veil >= RunaiVeil.HushedVeil
                                    && dist > rules.ScanRadius * RunaiVeil.HushedScanMultiplier) continue;
                            }

                            // Curse & Shardroot canon §2.1: BORDER units
                            // GUARD their wells — they don't hunt
                            // harvesters. Worker-class targets (workers)
                            // are ignored unless they stray
                            // right into the horde; military targets are
                            // engaged normally. Makes sneak-mining the
                            // crystal fields survivable.
                            if (myFaction == Faction.Border && dist > 9f && (flags & FlagWorker) != 0)
                                continue;

                            // Melee can't reach a vertically separated
                            // enemy (bridge deck above / valley floor
                            // below) — don't acquire it, pick someone
                            // reachable instead. Ranged units keep the
                            // target (they shoot up/down with the
                            // high-ground rules). Buildings are measured
                            // by their body, not their pivot height — see
                            // MeleeHeightBlocks.
                            if (isMelee && MeleeHeightBlocks(myPos.y, enemyPos.y,
                                    (flags & FlagBuilding) != 0, selfOnRampart))
                                continue;

                            // Skip stealthed enemies unless within proximity
                            // reveal range (3u) or exposed by a Lorekeeper
                            // (Antiquity detection stamp). Read live, not
                            // cached: stealth comes and goes.
                            if (dist > 3f && scan.IsHidden(cand))
                                continue;

                            // DEFENSIVE / SUPPORT: return fire only -- the
                            // candidate must be attacking ME (Stances.md §1).
                            if (rules.ThreatsOnly != 0
                                && !IsThreat(in scan.Targets, cand, self, lastAttacker, rules.HitRecently != 0))
                                continue;

                            // TARGET PREFERENCE (Combat_Pacing.md § Target
                            // preference): units only, inside the unit's own
                            // reach. "Massed" ranks by how many of that
                            // faction stand in the candidate's targeting cell
                            // (the splash pick); otherwise nearest wins.
                            if (rules.HasPref != 0 && dist <= rules.PreferReach
                                && (flags & FlagBuilding) == 0
                                && (!rules.Pref.HasRules
                                    || rules.Pref.Matches(ec.Tags, (flags & FlagHero) != 0)))
                            {
                                int density = 0;
                                if (rules.Pref.Massed != 0)
                                    density = scan.Grid.Count(new int3(
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

                            byte prio = ec.Prio;
                            if (dist < anyBestDist) { anyBest = cand; anyBestDist = dist; anyBestPrio = prio; }
                            if (prio > prioBestPrio || (prio == prioBestPrio && dist < prioBestDist))
                            {
                                prioBest = cand;
                                prioBestDist = dist;
                                prioBestPrio = prio;
                            }
                            if (isMelee)
                            {
                                int curCount = attackerCount.TryGetValue(cand, out int cv) ? cv : 0;
                                if (curCount < MaxAttackersPerEnemy && dist < underBestDist)
                                {
                                    underBest = cand;
                                    underBestDist = dist;
                                }
                            }
                        }
                    }
                }
            }

            // M2 bounded value tie-break: a higher-priority candidate
            // (healer/siege/caster) replaces the nearest pick only when
            // it sits within ValuePickDistRatio of it.
            // NearDistFloor: distances are measured to the target's
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

            Entity best = PickSpreadOrNearest(underBest, underBestDist, anyBest, anyBestDist);

            // A preferred target inside reach outranks the nearest / value
            // pick outright (the engine is built for it).
            if (prefBest != Entity.Null) best = prefBest;

            // Record the assignment so the next unit in this same pass sees
            // the updated count (prevents two simultaneously-assigned
            // attackers from both picking the same enemy because both saw
            // count=0).
            if (best != Entity.Null)
            {
                int prev = attackerCount.TryGetValue(best, out int pv) ? pv : 0;
                attackerCount[best] = prev + 1;
            }
            return best;
        }

        /// <summary>
        /// Is <paramref name="cand"/> attacking me? It hit me within the
        /// retaliation window (it is my LastAttackerEntity), or its own Target
        /// is me -- the shot may still be in the air. The Defensive stance and
        /// support units engage only these (docs/Design/Stances.md §1).
        /// </summary>
        private static bool IsThreat(in ComponentLookup<Target> targets, Entity cand, Entity self,
            Entity lastAttacker, bool hitRecently)
        {
            if (hitRecently && cand == lastAttacker) return true;
            return targets.TryGetComponent(cand, out var t) && t.Value == self;
        }

        /// <summary>
        /// The chase leash (Stances.md §3). An AUTO-acquired target is dropped
        /// once the unit is farther than the engagement's leash from where the
        /// engagement started, and the unit walks back to its guard point (an
        /// attack-mover / patroller: back onto its march, whose guard point IS
        /// the destination / waypoint). Ordered targets are never touched.
        ///
        /// Writes Target DIRECTLY, not through the ECB: the melee and ranged
        /// systems run after this one in the same frame and read Target live,
        /// and an ECB clear would let them write one more chase destination
        /// over the walk home.
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void EnforceLeash(ref SystemState state, ref EntityCommandBuffer ecb,
            in StanceSettings settings)
        {
            var em = state.EntityManager;
            float clock = _clock;

            foreach (var (transform, target, eng, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<UnitEngagement>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                ref var e = ref eng.ValueRW;
                if (e.AutoTarget == Entity.Null) continue;

                var t = target.ValueRO.Value;
                // The engagement ended (target died, an order cleared it) or
                // was replaced by an order: it is no longer an auto one. (Last
                // tick's acquisition played back at the end of last frame, so
                // a mismatch here is real.)
                if (t == Entity.Null || t != e.AutoTarget)
                {
                    e.AutoTarget = Entity.Null;
                    continue;
                }

                // The curse's units are wave-driven and guard their wells on
                // their own terms (Stances.md §4).
                if (em.HasComponent<BorderUnitTag>(entity)) continue;
                if (e.Leash <= 0f) continue;   // Hold: never moves for it anyway

                if (DistXZ(transform.ValueRO.Position, e.Anchor) <= e.Leash) continue;

                // ── Break the leash. ──
                target.ValueRW = new Target { Value = Entity.Null };
                e.AutoTarget = Entity.Null;
                e.NextAcquireAt = clock + settings.LeashReacquireCooldown;
                if (TransientState.Active<AttackCommand>(em, entity))
                    TransientState.Clear<AttackCommand>(em, ecb, entity);

                // Walk home — for an attack-mover / patroller the guard point
                // is its destination / waypoint, so this resumes the march.
                if (TransientState.Active<GuardPoint>(em, entity))
                {
                    var gp = em.GetComponentData<GuardPoint>(entity);
                    if (gp.Has != 0) SetDestination(em, ref ecb, entity, gp.Position);
                }
            }
        }

        /// <summary>Health drop since last tick = hit (feeds the under-fire
        /// window and the retaliation window). Source-agnostic on purpose:
        /// melee, projectiles, spells and burns all count.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void TrackHits(ref SystemState state)
        {
            float clock = _clock;
            foreach (var (health, eng) in SystemAPI
                .Query<RefRO<Health>, RefRW<UnitEngagement>>()
                .WithAll<UnitTag>())
            {
                int hp = health.ValueRO.Value;
                ref var e = ref eng.ValueRW;
                if (hp < e.LastHealth) e.HitAt = clock;
                e.LastHealth = hp;
            }
        }

        /// <summary>The stance a unit is actually in (HoldPositionTag wins).</summary>
        private static UnitStanceMode EffectiveStance(EntityManager em, Entity e)
        {
            if (TransientState.Active<HoldPositionTag>(em, e)) return UnitStanceMode.Hold;
            if (TransientState.Active<UnitStance>(em, e)) return em.GetComponentData<UnitStance>(e).Value;
            return UnitStanceMode.Aggressive;   // the default for every unit (Stances.md §4)
        }

        private static void SetDestination(EntityManager em, ref EntityCommandBuffer ecb,
            Entity entity, float3 pos)
        {
            if (!em.HasComponent<DesiredDestination>(entity))
                ecb.AddComponent(entity, new DesiredDestination { Position = pos, Has = 1 });
            else
                ecb.SetComponent(entity, new DesiredDestination { Position = pos, Has = 1 });
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
