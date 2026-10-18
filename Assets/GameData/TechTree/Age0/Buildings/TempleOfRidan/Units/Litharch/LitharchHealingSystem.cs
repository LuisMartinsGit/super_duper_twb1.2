// LitharchHealingSystem.cs
// Processes healing for Litharch support units

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles Litharch healing behavior:
    /// 1. Processes explicit HealCommand (player right-clicks friendly unit)
    /// 2. Auto-searches for injured friendlies when idle — preferring patients
    ///    that are NOT in melee contact
    /// 3. Walks to a STAND-OFF point inside heal range (never onto the
    ///    patient) and heals over time
    /// 4. Steps away from an armed enemy that gets too close
    ///
    /// A support unit stays out of melee (docs/Design/Stances.md § Support
    /// units). A player / AI move order always wins: while UserMoveOrder is
    /// live nothing here touches the Litharch. The Hold stance keeps it
    /// planted: it heals only what is already in range and never steps away,
    /// unless the heal was ORDERED (orders win on every stance).
    /// Tunables: LitharchHealingSystem.asset; heal range / rate: Litharch.asset.
    ///
    /// PERF (2026-10-15): the three scans that made this system expensive —
    /// the patient search (every unit, with a threat test per far patient),
    /// the step-away threat test, and the stand-point safety test — run in a
    /// parallel Burst job (<see cref="LithPlanJob"/>) before the per-Litharch
    /// loop, which then reads their answers. The loop itself is unchanged and
    /// still runs in query order. The only state the loop changes that a scan
    /// read is a patient's Health (a heal), and a heal can only take a patient
    /// OUT of the candidate set (to full): so a planned pick that is still
    /// injured when its Litharch's turn comes is exactly the pick the old
    /// in-loop search would have made, and one that is not falls back to that
    /// search, live. The threat and stand answers read nothing the loop writes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct LitharchHealingSystem : ISystem
    {
        private const float SearchInterval = 1.0f;
        private const float HealTickInterval = 1.0f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LitharchState>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Managed config read (this system is not Burst-compiled). A
            // missing asset is logged loudly by Require; no code-side default.
            var cfg = ComponentConfig.Require<LitharchHealingSystemConfig>();
            if (cfg == null) return;

            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            // Fix #225: Singleton ECB.
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Phase 1: Process explicit HealCommands
            foreach (var (healCmd, lithState, transform, entity) in SystemAPI
                         .Query<RefRO<HealCommand>, RefRW<LitharchState>, RefRO<LocalTransform>>()
                         .WithAll<LitharchTag>()
                         .WithEntityAccess())
            {
                var target = healCmd.ValueRO.Target;
                if (target != Entity.Null && em.Exists(target) && em.HasComponent<Health>(target)
                    && !em.HasComponent<UnhealableTag>(target))
                {
                    lithState.ValueRW.HealTarget = target;
                    lithState.ValueRW.IsHealing = 1;
                    lithState.ValueRW.Ordered = 1;
                }
                ecb.RemoveComponent<HealCommand>(entity);
            }

            // ── Phase 2 set-up ────────────────────────────────────────────
            // The Phase 2 Litharchs, in the order the loop walks them.
            var lithQuery = SystemAPI.QueryBuilder()
                .WithAllRW<LitharchState>()
                .WithAll<CanHeal, FactionTag, LocalTransform, LitharchTag>()
                .WithNone<HealCommand>()
                .Build();
            using var liths = lithQuery.ToEntityArray(Allocator.TempJob);
            int nL = liths.Length;
            if (nL == 0) return;

            // Armed, living units — what every threat / stand-point check
            // reads. Built once, in Burst. (The old loop built it on first
            // need; its content cannot differ: a heal never raises a unit
            // from 0 HP, and nothing else here touches Health or Damage.)
            var armedQuery = SystemAPI.QueryBuilder()
                .WithAll<Health, FactionTag, LocalTransform, Damage, UnitTag>()
                .WithNone<DeathAnimationState>()
                .Build();
            using var armedEntities = armedQuery.ToEntityArray(Allocator.TempJob);
            using var armed = new NativeList<ArmedUnit>(armedEntities.Length, Allocator.TempJob);
            // The armed set's cell index (perf pass 2026-10-16): every threat
            // and stand-point test used to walk the WHOLE armed list — per
            // Litharch per tick, and per far patient inside every search.
            using var armedStart = new NativeList<int>(64, Allocator.TempJob);
            using var armedItems = new NativeList<int>(armedEntities.Length, Allocator.TempJob);
            using var armedGrid = new NativeReference<ArmedGridParams>(Allocator.TempJob);
            var armedJob = new BuildArmedJob
            {
                Entities = armedEntities,
                Health = SystemAPI.GetComponentLookup<Health>(true),
                Faction = SystemAPI.GetComponentLookup<FactionTag>(true),
                Transform = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Damage = SystemAPI.GetComponentLookup<Damage>(true),
                Net = SystemAPI.GetComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(true),
                Armed = armed,
                Start = armedStart,
                Items = armedItems,
                Grid = armedGrid,
            };
            armedJob.Run();
            var armedIndex = new ArmedIndex
            {
                Units = armed.AsArray(),
                Start = armedStart.AsArray(),
                Items = armedItems.AsArray(),
                P = armedGrid.Value,
            };

            // Every possible patient, in the order the old search walked them.
            var candQuery = SystemAPI.QueryBuilder()
                .WithAll<Health, FactionTag, LocalTransform, UnitTag>()
                .Build();
            using var candEntities = candQuery.ToEntityArray(Allocator.TempJob);
            using var candXf = candQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using var candFaction = candQuery.ToComponentDataArray<FactionTag>(Allocator.TempJob);

            var ctx = new SearchCtx
            {
                Cands = candEntities,
                CandXf = candXf,
                CandFaction = candFaction,
                Armed = armedIndex,
                Health = SystemAPI.GetComponentLookup<Health>(true),
                Unhealable = SystemAPI.GetComponentLookup<UnhealableTag>(true),
                Net = SystemAPI.GetComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(true),
                Targets = SystemAPI.GetComponentLookup<Target>(true),
                LastAttacker = SystemAPI.GetComponentLookup<LastAttackerEntity>(true),
                Transform = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Storage = SystemAPI.GetEntityStorageInfoLookup(),
                ThreatRadius = cfg.threatRadius,
                StandOffMargin = cfg.standOffMargin,
                StandSafetyMargin = cfg.standSafetyMargin,
                MeleeContactRadius = cfg.meleeContactRadius,
            };

            // Per-Litharch inputs (managed, cheap: one stance read each).
            var lithIn = new NativeArray<LithInput>(nL, Allocator.TempJob);
            for (int i = 0; i < nL; i++)
            {
                var e = liths[i];
                var ls = em.GetComponentData<LitharchState>(e);
                var ch = em.GetComponentData<CanHeal>(e);
                bool hold = StanceCommandHelper.Effective(em, e) == UnitStanceMode.Hold;
                bool curValid = false;
                if (ls.HealTarget != Entity.Null && em.Exists(ls.HealTarget)
                    && em.HasComponent<Health>(ls.HealTarget))
                {
                    var h = em.GetComponentData<Health>(ls.HealTarget);
                    curValid = h.Value < h.Max && h.Value > 0;
                }
                // Plan only what the loop below will read (perf pass
                // 2026-10-16). The threat check runs on its own cadence: the
                // loop subtracts dt and checks at <= 0, or re-checks at once
                // when a step-away ends — exactly these two cases. The search
                // runs only when the current patient is not (still) valid; a
                // patient that turns invalid later this tick (healed to full
                // by an earlier Litharch) takes the loop's live fallback
                // search, as a stale plan already did.
                bool threatDue = !hold
                    && (ls.SteppingAway != 0 || ls.ThreatTimer - dt <= 0f);
                lithIn[i] = new LithInput
                {
                    Self = e,
                    Pos = em.GetComponentData<LocalTransform>(e).Position,
                    Faction = em.GetComponentData<FactionTag>(e).Value,
                    HealRange = ch.HealRange,
                    SearchRange = hold ? ch.HealRange : cfg.autoSearchRange,
                    Hold = (byte)(hold ? 1 : 0),
                    NeedThreat = (byte)(threatDue ? 1 : 0),
                    NeedSearch = (byte)(!curValid && ls.SearchTimer + dt >= SearchInterval ? 1 : 0),
                    CurTarget = curValid ? ls.HealTarget : Entity.Null,
                };
            }

            using var plans = new NativeArray<LithPlan>(nL, Allocator.TempJob);
            var planJob = new LithPlanJob { In = lithIn, Ctx = ctx, Plans = plans };
            var planHandle = planJob.Schedule(nL, 1, state.Dependency);
            planHandle.Complete();

            // Phase 2: Auto-search for injured friendlies when idle
            for (int li = 0; li < nL; li++)
            {
                var entity = liths[li];
                var plan = plans[li];
                var lithState = SystemAPI.GetComponentRW<LitharchState>(entity);
                var canHeal = em.GetComponentData<CanHeal>(entity);
                var myPos = lithIn[li].Pos;
                var myFaction = lithIn[li].Faction;

                // UserMoveOrder = player/AI issued a manual command — cancel healing, obey command
                if (TransientState.Active<UserMoveOrder>(em, entity))
                {
                    if (lithState.ValueRO.IsHealing != 0)
                    {
                        lithState.ValueRW.HealTarget = Entity.Null;
                        lithState.ValueRW.IsHealing = 0;
                        lithState.ValueRW.Ordered = 0;
                    }
                    lithState.ValueRW.SteppingAway = 0;
                    continue;
                }
                // An ordered attack (Warrior Priests Litharch) is also an order
                // healing must not fight over the destination with.
                if (TransientState.Active<AttackCommand>(em, entity)) continue;
                // Channelling a Cleanse (Territory_Claims.md §6.9.7): it stands
                // and heals through the channel, not by walking to patients —
                // a step toward one would carry it off its circle.
                if (TheWaningBorder.Abilities.LitharchCleanse.IsChanneling(em, entity))
                {
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                    lithState.ValueRW.SteppingAway = 0;
                    continue;
                }

                bool hold = lithIn[li].Hold != 0;

                // ── Step away from an armed enemy that got too close. ──
                // Checked on its own cadence; a running step-away blocks the
                // heal movement below until the step is FINISHED — it reached
                // its destination, or ran out its time bound. Ending it on the
                // threat-check cadence (0.5 s) cut a 6 m step short at ~3 m
                // and the heal branch walked the healer straight back in: the
                // yo-yo.
                lithState.ValueRW.ThreatTimer -= dt;
                if (lithState.ValueRO.SteppingAway != 0)
                {
                    lithState.ValueRW.StepTimer -= dt;
                    bool arrived = DistXZ(myPos, lithState.ValueRO.StepTarget) <= cfg.stepArriveTolerance;
                    if (!arrived && lithState.ValueRO.StepTimer > 0f) continue;
                    lithState.ValueRW.SteppingAway = 0;
                    // Re-check at once: an enemy that followed gets another step.
                    lithState.ValueRW.ThreatTimer = 0f;
                }
                if (!hold && lithState.ValueRO.ThreatTimer <= 0f)
                {
                    lithState.ValueRW.ThreatTimer = cfg.threatCheckInterval;
                    // Planned (NearestArmedEnemy on the armed set, which this
                    // loop never changes). Planned exactly when this check
                    // runs (LithInput.NeedThreat) — the fallback below is a
                    // guard, never taken while the two agree.
                    bool threatFound = plan.ThreatFound != 0;
                    float3 threatPos = plan.ThreatPos;
                    if (lithIn[li].NeedThreat == 0)
                        threatFound = NearestArmedEnemy(in ctx.Armed, myPos, myFaction,
                            cfg.threatRadius, out threatPos);
                    if (threatFound)
                    {
                        float3 away = myPos - threatPos;
                        away.y = 0f;
                        float len = math.length(away);
                        away = len > 1e-3f ? away / len : new float3(0f, 0f, -1f);
                        float3 to = myPos + away * cfg.stepAwayDistance;
                        SetDestination(em, ref ecb, entity, to);
                        // The spot it stepped to is its post, so return-to-guard
                        // does not walk it straight back into the fight.
                        if (TransientState.Active<GuardPoint>(em, entity))
                            ecb.SetComponent(entity, new GuardPoint { Position = to, Has = 1 });
                        // Time bound derived from the unit's own speed (a
                        // floor of 1 m/s so a slowed healer still ends it).
                        float speed = em.HasComponent<MoveSpeed>(entity)
                            ? em.GetComponentData<MoveSpeed>(entity).Value : 0f;
                        lithState.ValueRW.StepTarget = to;
                        lithState.ValueRW.StepTimer =
                            cfg.stepAwayDistance / math.max(speed, 1f) + cfg.threatCheckInterval;
                        lithState.ValueRW.SteppingAway = 1;
                        continue;
                    }
                }

                // Skip if already has a valid heal target
                if (lithState.ValueRO.HealTarget != Entity.Null &&
                    em.Exists(lithState.ValueRO.HealTarget))
                {
                    var tgtHealth = em.GetComponentData<Health>(lithState.ValueRO.HealTarget);
                    if (tgtHealth.Value < tgtHealth.Max && tgtHealth.Value > 0)
                        goto ProcessHealing;
                    // Target is full HP or dead, clear it
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                    lithState.ValueRW.Ordered = 0;
                }

                // Search for injured nearby friendlies periodically
                lithState.ValueRW.SearchTimer += dt;
                if (lithState.ValueRO.SearchTimer < SearchInterval)
                    continue;
                lithState.ValueRW.SearchTimer = 0f;

                {
                    // The planned pick stands while it is still injured (a heal
                    // earlier this tick can only REMOVE candidates — see the
                    // class note); otherwise search again, live.
                    Entity bestTarget;
                    if (plan.SearchDone != 0
                        && (plan.SearchBest == Entity.Null || StillInjured(em, plan.SearchBest)))
                    {
                        bestTarget = plan.SearchBest;
                    }
                    else
                    {
                        ctx.Health.Update(ref state);
                        bestTarget = SearchCore(entity, myPos, myFaction, lithIn[li].SearchRange,
                            canHeal.HealRange, in ctx);
                    }

                    if (bestTarget != Entity.Null)
                    {
                        lithState.ValueRW.HealTarget = bestTarget;
                        lithState.ValueRW.IsHealing = 1;
                        lithState.ValueRW.Ordered = 0;
                    }
                }

                ProcessHealing:

                // Phase 3: Move to target and heal
                if (lithState.ValueRO.HealTarget == Entity.Null) continue;
                if (!em.Exists(lithState.ValueRO.HealTarget))
                {
                    lithState.ValueRW.HealTarget = Entity.Null;
                    lithState.ValueRW.IsHealing = 0;
                    lithState.ValueRW.Ordered = 0;
                    continue;
                }

                {
                    var healTarget = lithState.ValueRO.HealTarget;
                    var targetPos = em.GetComponentData<LocalTransform>(healTarget).Position;
                    float dist = DistXZ(myPos, targetPos);
                    float range = canHeal.HealRange;

                    if (dist > range)
                    {
                        // Hold: never moves on its own — an AUTO patient that
                        // walked out of reach is let go. An ordered heal is
                        // followed on every stance.
                        if (hold && lithState.ValueRO.Ordered == 0)
                        {
                            lithState.ValueRW.HealTarget = Entity.Null;
                            lithState.ValueRW.IsHealing = 0;
                            continue;
                        }

                        // Walk to a STAND-OFF point, not to the patient,
                        // (healRange − margin) out from the patient — on the
                        // side AWAY from the nearest enemy when one is near it,
                        // else on the patient→Litharch line (the RitualApproach
                        // pattern). Pathing to the patient's exact spot walked
                        // the healer into the melee it was healing.
                        //
                        // Never walk in to an unsafe stand point. Hold here
                        // (and make here the post, so return-to-guard does not
                        // walk it in either); an AUTO patient is let go so the
                        // next search picks a safer one, an ORDERED one is
                        // waited on until the point clears.
                        float3 stand;
                        bool unsafeStand;
                        if (plan.StandDone != 0 && plan.StandTarget == healTarget)
                        {
                            stand = plan.Stand;
                            unsafeStand = plan.StandUnsafe != 0;
                        }
                        else
                        {
                            stand = StandPoint(in ctx.Armed, targetPos, myPos, myFaction,
                                math.max(1f, range - cfg.standOffMargin), cfg.threatRadius);
                            unsafeStand = ThreatNear(in ctx.Armed, stand, myFaction,
                                cfg.threatRadius + cfg.standSafetyMargin);
                        }

                        if (unsafeStand)
                        {
                            TheWaningBorder.Core.TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);
                            if (TransientState.Active<GuardPoint>(em, entity))
                                ecb.SetComponent(entity, new GuardPoint { Position = myPos, Has = 1 });
                            if (lithState.ValueRO.Ordered == 0)
                            {
                                lithState.ValueRW.HealTarget = Entity.Null;
                                lithState.ValueRW.IsHealing = 0;
                            }
                            continue;
                        }

                        bool rewrite = true;
                        if (em.HasComponent<DesiredDestination>(entity))
                        {
                            var dd = em.GetComponentData<DesiredDestination>(entity);
                            // Same point as already set — do not churn the
                            // path request every frame.
                            rewrite = dd.Has == 0 || DistXZ(dd.Position, stand) > 1f;
                        }
                        if (rewrite) SetDestination(em, ref ecb, entity, stand);

                        // Guard from the stand point (NOT the patient), so
                        // return-to-guard neither snaps the healer home nor
                        // parks it on top of the fight when the heal ends.
                        if (TransientState.Active<GuardPoint>(em, entity))
                            ecb.SetComponent(entity, new GuardPoint { Position = stand, Has = 1 });
                    }
                    else
                    {
                        // In range - plant, face the patient, and heal
                        TheWaningBorder.Core.TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);

                        lithState.ValueRW.HealTimer += dt;
                        if (lithState.ValueRO.HealTimer >= HealTickInterval)
                        {
                            lithState.ValueRW.HealTimer = 0f;

                            var targetHealth = em.GetComponentData<Health>(healTarget);
                            float healRate = canHeal.HealRate;

                            int healAmount = (int)(healRate * HealTickInterval);
                            if (healAmount < 1) healAmount = 1;

                            targetHealth.Value = math.min(targetHealth.Value + healAmount, targetHealth.Max);
                            // Fix #228: write health immediately via EntityManager
                            // to match how combat systems apply damage. ECB playback
                            // happens AFTER Melee/RangedCombatSystem in the same
                            // frame, so an ECB heal write would overwrite damage
                            // dealt this frame — effectively nullifying it.
                            em.SetComponentData(healTarget, targetHealth);

                            // If fully healed, clear target
                            if (targetHealth.Value >= targetHealth.Max)
                            {
                                lithState.ValueRW.HealTarget = Entity.Null;
                                lithState.ValueRW.IsHealing = 0;
                                lithState.ValueRW.Ordered = 0;
                            }
                        }
                    }
                }
            }
            lithIn.Dispose();
            // ECB plays back automatically at EndSimulation.
        }

        /// <summary>The search's own "is this still a patient" test, live.</summary>
        private static bool StillInjured(EntityManager em, Entity e)
        {
            if (!em.Exists(e) || !em.HasComponent<Health>(e)) return false;
            var h = em.GetComponentData<Health>(e);
            return h.Value < h.Max && h.Value > 0;
        }

        /// <summary>An armed, living unit, as the threat checks see it.</summary>
        private struct ArmedUnit
        {
            public float3 Pos;
            public Faction Faction;
            public uint Key;
        }

        /// <summary>One Litharch's plan inputs, gathered on the main thread.</summary>
        private struct LithInput
        {
            public Entity Self;
            public float3 Pos;
            public Faction Faction;
            public float HealRange;
            public float SearchRange;
            /// <summary>The Hold stance (heals only what is in reach, never steps away).</summary>
            public byte Hold;
            public byte NeedThreat;
            public byte NeedSearch;
            /// <summary>The current heal target when it is still a patient.</summary>
            public Entity CurTarget;
        }

        /// <summary>One Litharch's planned answers.</summary>
        private struct LithPlan
        {
            public byte ThreatFound;
            public float3 ThreatPos;
            public byte SearchDone;
            public Entity SearchBest;
            public byte StandDone;
            public byte StandUnsafe;
            public Entity StandTarget;
            public float3 Stand;
        }

        /// <summary>Everything the patient search and the threat tests read.</summary>
        private struct SearchCtx
        {
            [ReadOnly] public NativeArray<Entity> Cands;
            [ReadOnly] public NativeArray<LocalTransform> CandXf;
            [ReadOnly] public NativeArray<FactionTag> CandFaction;
            public ArmedIndex Armed;
            [ReadOnly] public ComponentLookup<Health> Health;
            [ReadOnly] public ComponentLookup<UnhealableTag> Unhealable;
            [ReadOnly] public ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> Net;
            [ReadOnly] public ComponentLookup<Target> Targets;
            [ReadOnly] public ComponentLookup<LastAttackerEntity> LastAttacker;
            [ReadOnly] public ComponentLookup<LocalTransform> Transform;
            [ReadOnly] public EntityStorageInfoLookup Storage;
            public float ThreatRadius;
            public float StandOffMargin;
            public float StandSafetyMargin;
            public float MeleeContactRadius;
        }

        /// <summary>The armed set, in query order, with its peer-stable keys.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct BuildArmedJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public ComponentLookup<Health> Health;
            [ReadOnly] public ComponentLookup<FactionTag> Faction;
            [ReadOnly] public ComponentLookup<LocalTransform> Transform;
            [ReadOnly] public ComponentLookup<Damage> Damage;
            [ReadOnly] public ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> Net;
            public NativeList<ArmedUnit> Armed;
            public NativeList<int> Start;
            public NativeList<int> Items;
            public NativeReference<ArmedGridParams> Grid;

            public void Execute()
            {
                float minX = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxZ = float.MinValue;
                for (int i = 0; i < Entities.Length; i++)
                {
                    var e = Entities[i];
                    if (Damage[e].Value <= 0 || Health[e].Value <= 0) continue;
                    var pos = Transform[e].Position;
                    Armed.Add(new ArmedUnit
                    {
                        Pos = pos,
                        Faction = Faction[e].Value,
                        Key = StableKey(in Net, e, pos),
                    });
                    minX = math.min(minX, pos.x); maxX = math.max(maxX, pos.x);
                    minZ = math.min(minZ, pos.z); maxZ = math.max(maxZ, pos.z);
                }

                int n = Armed.Length;
                if (n == 0) { minX = minZ = maxX = maxZ = 0f; }
                // Grow the cell when the spread is huge (a stray unit far off
                // the map), so the table stays bounded.
                float cell = math.max(ArmedCellSize, math.max(maxX - minX, maxZ - minZ) / ArmedMaxCellsPerAxis);
                int cellsX = math.max(1, (int)math.floor((maxX - minX) / cell) + 1);
                int cellsZ = math.max(1, (int)math.floor((maxZ - minZ) / cell) + 1);
                int buckets = cellsX * cellsZ;

                // Counting sort, stable: each cell lists its units in armed
                // (query) order.
                Start.ResizeUninitialized(buckets + 1);
                for (int b = 0; b <= buckets; b++) Start[b] = 0;
                var cellOf = new NativeArray<int>(math.max(1, n), Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < n; i++)
                {
                    var p = Armed[i].Pos;
                    int cx = math.clamp((int)math.floor((p.x - minX) / cell), 0, cellsX - 1);
                    int cz = math.clamp((int)math.floor((p.z - minZ) / cell), 0, cellsZ - 1);
                    int b = cz * cellsX + cx;
                    cellOf[i] = b;
                    Start[b + 1] = Start[b + 1] + 1;
                }
                for (int b = 0; b < buckets; b++) Start[b + 1] = Start[b + 1] + Start[b];
                var fill = new NativeArray<int>(buckets, Allocator.Temp);
                Items.ResizeUninitialized(math.max(1, n));
                for (int i = 0; i < n; i++)
                {
                    int b = cellOf[i];
                    Items[Start[b] + fill[b]] = i;
                    fill[b] = fill[b] + 1;
                }
                fill.Dispose();
                cellOf.Dispose();

                Grid.Value = new ArmedGridParams
                {
                    MinX = minX,
                    MinZ = minZ,
                    Cell = cell,
                    CellsX = cellsX,
                    CellsZ = cellsZ,
                };
            }
        }

        /// <summary>Every Litharch's threat check, patient search and
        /// stand-point test, in parallel; each writes only its own plan.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private struct LithPlanJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<LithInput> In;
            [ReadOnly] public SearchCtx Ctx;
            [WriteOnly] public NativeArray<LithPlan> Plans;

            public void Execute(int i)
            {
                var input = In[i];
                var p = new LithPlan();

                if (input.NeedThreat != 0)
                {
                    p.ThreatFound = (byte)(NearestArmedEnemy(in Ctx.Armed, input.Pos, input.Faction,
                        Ctx.ThreatRadius, out p.ThreatPos) ? 1 : 0);
                }

                // The patient the walk branch will most likely use: the
                // current one while it is still injured, else the search pick.
                Entity standFor = input.CurTarget;
                if (input.NeedSearch != 0)
                {
                    p.SearchDone = 1;
                    p.SearchBest = SearchCore(input.Self, input.Pos, input.Faction, input.SearchRange,
                        input.HealRange, in Ctx);
                    if (standFor == Entity.Null) standFor = p.SearchBest;
                }

                if (standFor != Entity.Null && Ctx.Storage.Exists(standFor)
                    && Ctx.Transform.TryGetComponent(standFor, out var txf))
                {
                    var targetPos = txf.Position;
                    if (DistXZ(input.Pos, targetPos) > input.HealRange)
                    {
                        p.StandDone = 1;
                        p.StandTarget = standFor;
                        p.Stand = StandPoint(in Ctx.Armed, targetPos, input.Pos, input.Faction,
                            math.max(1f, input.HealRange - Ctx.StandOffMargin), Ctx.ThreatRadius);
                        p.StandUnsafe = (byte)(ThreatNear(in Ctx.Armed, p.Stand, input.Faction,
                            Ctx.ThreatRadius + Ctx.StandSafetyMargin) ? 1 : 0);
                    }
                }

                Plans[i] = p;
            }
        }

        /// <summary>
        /// The patient search — the old in-loop search, unchanged in its tests
        /// and its order (every unit in query order, peer-stable tie-breaks):
        /// the nearest allied, injured, healable unit inside the search range,
        /// preferring one NOT in melee contact, and skipping a far one whose
        /// stand point is not clear of armed enemies. Reads Health through the
        /// lookup, so it is the live value whenever it is called.
        /// </summary>
        private static Entity SearchCore(Entity self, float3 myPos, Faction myFaction,
            float searchRange, float healRange, in SearchCtx c)
        {
            float standDist = math.max(1f, healRange - c.StandOffMargin);
            float bestFree = float.MaxValue, bestContact = float.MaxValue;
            uint bestFreeKey = uint.MaxValue, bestContactKey = uint.MaxValue;
            Entity freeTarget = Entity.Null, contactTarget = Entity.Null;

            for (int i = 0; i < c.Cands.Length; i++)
            {
                var tEntity = c.Cands[i];
                if (tEntity == self) continue;
                // Litharchs heal allies too. docs/Design/Teams.md
                if (!Alliances.AreAlliedBurst(myFaction, c.CandFaction[i].Value)) continue;
                if (c.Unhealable.HasComponent(tEntity)) continue;
                var tHealth = c.Health[tEntity];
                if (tHealth.Value >= tHealth.Max) continue;
                if (tHealth.Value <= 0) continue;

                var tPos = c.CandXf[i].Position;
                float dist = DistXZ(myPos, tPos);
                if (dist >= searchRange) continue;

                // A patient it would have to WALK to is only a candidate if
                // the stand point it would walk to is clear of armed enemies;
                // otherwise it waits for another patient rather than walking
                // into the fight.
                if (dist > healRange)
                {
                    float3 stand = StandPoint(in c.Armed, tPos, myPos, myFaction, standDist, c.ThreatRadius);
                    if (ThreatNear(in c.Armed, stand, myFaction, c.ThreatRadius + c.StandSafetyMargin))
                        continue;
                }

                // Exact-distance ties broken on a peer-stable key, not chunk
                // order (lockstep).
                uint key = StableKey(in c.Net, tEntity, tPos);

                // Prefer a patient who is NOT trading blows: healing one in
                // melee contact drags the healer into the fight.
                if (InMeleeContact(in c, tEntity, tPos, c.MeleeContactRadius))
                {
                    if (Better(dist, key, bestContact, bestContactKey))
                    { bestContact = dist; bestContactKey = key; contactTarget = tEntity; }
                }
                else if (Better(dist, key, bestFree, bestFreeKey))
                { bestFree = dist; bestFreeKey = key; freeTarget = tEntity; }
            }

            return freeTarget != Entity.Null ? freeTarget : contactTarget;
        }

        /// <summary>A peer-stable identity for tie-breaks: the lockstep
        /// NetworkId, else the position bits. Never chunk order, which differs
        /// between peers.</summary>
        private static uint StableKey(in ComponentLookup<TheWaningBorder.Core.Multiplayer.NetworkedEntity> net,
            Entity e, float3 pos)
        {
            if (net.TryGetComponent(e, out var n))
                return (uint)n.NetworkId;
            return math.hash(pos) | 0x80000000u;
        }

        private static bool Better(float d, uint key, float bestD, uint bestKey)
            => d < bestD || (d == bestD && key < bestKey);

        /// <summary>Where to stand to heal the patient: <paramref name="standDist"/>
        /// out from it, on the side AWAY from the nearest armed enemy within
        /// standDist + threatRadius of the patient (so the healer is not placed
        /// in, or pathed through, the melee); with no such enemy, on the
        /// patient→healer line. Degenerate directions fall back to a fixed
        /// bearing, so the result is the same on every peer.</summary>
        private static float3 StandPoint(in ArmedIndex armed, float3 patient, float3 healer,
            Faction myFaction, float standDist, float threatRadius)
        {
            float3 d;
            if (NearestArmedEnemy(in armed, patient, myFaction, standDist + threatRadius, out float3 enemy))
                d = patient - enemy;
            else
                d = healer - patient;
            d.y = 0f;
            float len = math.length(d);
            d = len > 1e-3f ? d / len : new float3(0f, 0f, -1f);
            return patient + d * standDist;
        }

        /// <summary>Is any armed hostile within <paramref name="radius"/>?
        /// Existential, so the cell walk order cannot change the answer.</summary>
        private static bool ThreatNear(in ArmedIndex armed, float3 pos, Faction myFaction, float radius)
        {
            armed.Cells(pos, radius, out int cx0, out int cx1, out int cz0, out int cz1);
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int b = cz * armed.P.CellsX + cx;
                int end = armed.Start[b + 1];
                for (int k = armed.Start[b]; k < end; k++)
                {
                    var a = armed.Units[armed.Items[k]];
                    if (!Alliances.AreHostileBurst(myFaction, a.Faction)) continue;
                    if (DistXZ(pos, a.Pos) <= radius) return true;
                }
            }
            return false;
        }

        /// <summary>Is this patient trading blows — its own target, or the unit
        /// that last hit it, standing within <paramref name="radius"/>?</summary>
        private static bool InMeleeContact(in SearchCtx c, Entity patient, float3 pos, float radius)
        {
            if (c.Targets.TryGetComponent(patient, out var tc))
            {
                var t = tc.Value;
                if (t != Entity.Null && c.Storage.Exists(t) && c.Transform.TryGetComponent(t, out var txf)
                    && DistXZ(pos, txf.Position) <= radius)
                    return true;
            }
            if (c.LastAttacker.HasComponent(patient) && c.LastAttacker.IsComponentEnabled(patient))
            {
                var a = c.LastAttacker[patient].Value;
                if (a != Entity.Null && c.Storage.Exists(a) && c.Transform.TryGetComponent(a, out var axf)
                    && DistXZ(pos, axf.Position) <= radius)
                    return true;
            }
            return false;
        }

        /// <summary>The nearest hostile, living, ARMED unit within
        /// <paramref name="radius"/> of <paramref name="pos"/>. The cells are
        /// walked in any order, so the old list walk's tie rule is kept
        /// explicitly: smallest (distance, key), and among exact equals the
        /// EARLIEST in armed order — the one the old strict walk kept.</summary>
        private static bool NearestArmedEnemy(in ArmedIndex armed, float3 pos,
            Faction myFaction, float radius, out float3 threatPos)
        {
            threatPos = default;
            float best = float.MaxValue;
            uint bestKey = uint.MaxValue;
            int bestIdx = int.MaxValue;
            bool found = false;
            armed.Cells(pos, radius, out int cx0, out int cx1, out int cz0, out int cz1);
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int b = cz * armed.P.CellsX + cx;
                int end = armed.Start[b + 1];
                for (int k = armed.Start[b]; k < end; k++)
                {
                    int idx = armed.Items[k];
                    var a = armed.Units[idx];
                    if (!Alliances.AreHostileBurst(myFaction, a.Faction)) continue;
                    float d = DistXZ(pos, a.Pos);
                    if (d > radius) continue;
                    // Peer-stable tie-break (the old '<=' kept the LAST in chunk
                    // order, which can differ between lockstep peers).
                    if (Better(d, a.Key, best, bestKey)
                        || (d == best && a.Key == bestKey && idx < bestIdx))
                    {
                        best = d; bestKey = a.Key; bestIdx = idx; threatPos = a.Pos; found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>Cell edge of the armed index, and the cap on cells per
        /// axis. Loop resolution, not tunables.</summary>
        private const float ArmedCellSize = 8f;
        private const float ArmedMaxCellsPerAxis = 256f;

        private struct ArmedGridParams
        {
            public float MinX, MinZ, Cell;
            public int CellsX, CellsZ;
        }

        /// <summary>The armed set plus its uniform cell index (BuildArmedJob).
        /// Cell c's units are Units[Items[Start[c] .. Start[c+1])], in armed
        /// order.</summary>
        private struct ArmedIndex
        {
            [ReadOnly] public NativeArray<ArmedUnit> Units;
            [ReadOnly] public NativeArray<int> Start;
            [ReadOnly] public NativeArray<int> Items;
            public ArmedGridParams P;

            /// <summary>The cells a radius query must visit: the query box
            /// grown by a metre, so float rounding at a cell edge can never
            /// drop a unit the exact distance test would accept.</summary>
            public void Cells(float3 pos, float radius, out int cx0, out int cx1, out int cz0, out int cz1)
            {
                float r = radius + 1f;
                cx0 = math.clamp((int)math.floor((pos.x - r - P.MinX) / P.Cell), 0, P.CellsX - 1);
                cx1 = math.clamp((int)math.floor((pos.x + r - P.MinX) / P.Cell), 0, P.CellsX - 1);
                cz0 = math.clamp((int)math.floor((pos.z - r - P.MinZ) / P.Cell), 0, P.CellsZ - 1);
                cz1 = math.clamp((int)math.floor((pos.z + r - P.MinZ) / P.Cell), 0, P.CellsZ - 1);
                if (Units.Length == 0) { cx0 = 0; cx1 = -1; cz0 = 0; cz1 = -1; }
            }
        }

        private static void SetDestination(EntityManager em, ref EntityCommandBuffer ecb, Entity e, float3 pos)
        {
            if (em.HasComponent<DesiredDestination>(e))
                ecb.SetComponent(e, new DesiredDestination { Position = pos, Has = 1 });
            else
                ecb.AddComponent(e, new DesiredDestination { Position = pos, Has = 1 });
        }
    }
}
