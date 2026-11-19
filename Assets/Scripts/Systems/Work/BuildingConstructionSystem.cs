using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles building construction by worker units.
    /// 
    /// Construction workflow:
    /// 1. Player places building ghost (UnderConstruction component, low HP)
    /// 2. Worker receives BuildOrder component pointing to construction site
    /// 3. Worker moves to site and contributes build progress
    /// 4. When Progress >= Total, building completes:
    ///    - UnderConstruction removed
    ///    - Health set to max
    ///    - DeferredDefense applied as Defense component
    /// 
    /// Multiple workers can work on the same building simultaneously.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BuildingConstructionSystem : ISystem
    {
        // Reach and stand rules: BuildingConstructionSystem.asset, through
        // BuilderStand (2026-11-18). The old flat 4 m BuildRange let a worker
        // build from 4 m away - and from INSIDE the footprint, which measures
        // as distance 0.

        /// <summary>
        /// Each of <paramref name="crew"/> workers' share of its own rate on
        /// one site, so the crew's total is (n + 2) / 3 of one worker's — n
        /// workers take 3T / (n + 2) (Age of Empires II). One worker: 1. Used
        /// by construction and repair alike.
        /// </summary>
        public static float CrewShare(int crew)
            => crew <= 1 ? 1f : (crew + 2f) / (3f * crew);

        // Cached EntityQueries — initialized in OnCreate()
        private EntityQuery _unfinishedBuildingQuery;
        private EntityQuery _activeOrders;
        private EntityQuery _footprints;

        public void OnCreate(ref SystemState state)
        {
            _footprints = BuilderStand.FootprintQuery(ref state);
            // BuildOrder is enableable and pre-added DISABLED on every Worker
            // (2026-10-15). RequireForUpdate ignores enable bits (it tests
            // IsEmptyIgnoreFilter), so it now passes whenever a Worker exists;
            // the IsEmpty test at the top of OnUpdate restores the old gate —
            // the system (adoption scan and its timer included) runs only
            // while some worker holds an ACTIVE build order.
            state.RequireForUpdate<BuildOrder>();
            _activeOrders = state.GetEntityQuery(ComponentType.ReadOnly<BuildOrder>());

            // Use state.GetEntityQuery so the SystemState owns the query lifetime
            // and disposes on system teardown. Earlier this called
            // EntityManager.CreateEntityQuery which leaks the query handle on
            // world reload. (task-062 Q-42)
            _unfinishedBuildingQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<UnderConstruction>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            // task-063 phase 1: _templeQuery removed — it was only used by an
            // old RP-bonus path that no longer exists.
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_activeOrders.IsEmpty) return;   // see OnCreate
            var cfg = BuildingConstructionSystemConfig.I;
            if (cfg == null) return;   // Require has logged the data bug
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            var workers = new NativeList<Entity>(Allocator.Temp);
            var workerPositions = new NativeList<float3>(Allocator.Temp);
            var workerOrders = new NativeList<BuildOrder>(Allocator.Temp);

            foreach (var (transform, order, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<BuildOrder>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                workers.Add(entity);
                workerPositions.Add(transform.ValueRO.Position);
                workerOrders.Add(order.ValueRO);
            }

            // THE CREW RULE (2026-11-17, developer: "Do points 1-4"): builders
            // on one site add up with diminishing returns, as in Age of
            // Empires II — n builders finish in 3T / (n + 2), so 1 takes T, 4
            // take T/2, 8 take 0.3 T. Counted first: every builder in reach of
            // its site this tick (the same reach test the loop below uses).
            var crew = new NativeHashMap<Entity, int>(16, Allocator.Temp);
            // Every spot already held round each site, so a new builder takes
            // a free one (BuilderStand).
            var claimed = new NativeHashSet<int4>(16, Allocator.Temp);
            for (int i = 0; i < workers.Length; i++)
            {
                Entity s0 = workerOrders[i].Site;
                if (!em.Exists(s0) || !em.HasComponent<UnderConstruction>(s0)) continue;
                BuilderStand.Claim(claimed, s0, workerOrders[i].Stand);
                if (!BuilderStand.InReach(TargetGeometry.Extent(em, s0), workerPositions[i],
                        BuilderStand.RadiusOf(em, workers[i]), cfg)) continue;
                crew.TryGetValue(s0, out int c0);
                crew[s0] = c0 + 1;
            }
            var footprints = new BuilderStand.Footprints(_footprints);
            // Builders of a Farm finished this tick, and the Farm (decision 83).
            var stepInWorkers = new NativeList<Entity>(Allocator.Temp);
            var stepInFarms = new NativeList<Entity>(Allocator.Temp);

            // Process each worker
            for (int i = 0; i < workers.Length; i++)
            {
                Entity worker = workers[i];
                float3 bPos = workerPositions[i];
                Entity site = workerOrders[i].Site;

                // Validate construction site exists
                if (!em.Exists(site))
                {
                    // Site destroyed - clear order
                    TransientState.Clear<BuildOrder>(em, worker);
                    continue;
                }

                // Check if site is still under construction
                if (!em.HasComponent<UnderConstruction>(site))
                {
                    // Already finished - clear order
                    TransientState.Clear<BuildOrder>(em, worker);
                    // A crewmate finished the Farm this worker was building:
                    // it goes in too (decision 83), after the loop.
                    if (em.HasComponent<FarmTag>(site)) { stepInWorkers.Add(worker); stepInFarms.Add(site); }
                    continue;
                }

                // Get site position
                float3 sitePos = em.GetComponentData<LocalTransform>(site).Position;
                // Measured to the building's edge, not its centre (the 9 m wall
                // hub), and the worker must stand OUTSIDE it: BuilderStand walks
                // a worker that is inside, overlapping, or too far to its own
                // latched spot on a ring just beyond the edge, and says Work
                // only within reach of the edge (2026-11-18).
                var extent = TargetGeometry.Extent(em, site);
                var order = workerOrders[i];
                var stand = BuilderStand.Tick(em, worker, bPos, site, extent, ref order.Stand,
                    claimed, ref footprints, cfg, dt);
                if (!BuilderStand.SameStand(order.Stand, workerOrders[i].Stand))
                    em.SetComponentData(worker, order);

                if (stand == BuilderStand.Step.GiveUp)
                {
                    // Every spot round the site was tried and none could be
                    // reached: stop here rather than orbit the site for ever.
                    BuilderStand.StandDown<BuildOrder>(em, worker, bPos);
                    continue;
                }
                if (stand == BuilderStand.Step.Walk) continue;
                else
                {
                    // In range - plant, face the site, and contribute to construction
                    TargetGeometry.StopAndFace(em, worker, sitePos, dt);

                    // Add build progress
                    var uc = em.GetComponentData<UnderConstruction>(site);
                    // The builder's own rate, from its unit SO (Worker.asset
                    // buildSpeed, copied onto CanBuild by the factory).
                    float workerRate = em.HasComponent<CanBuild>(worker)
                        ? em.GetComponentData<CanBuild>(worker).BuildRate : 0f;
                    crew.TryGetValue(site, out int crewSize);
                    float buildRate = workerRate * CrewShare(crewSize);

                    // Self-constructing sites (wall extensions; the landmarks no
                    // longer self-build, 2026-11-17)
                    // already tick at 1.0/s via AutoConstructionSystem; workers
                    // only ACCELERATE them, each adding +25 % of the base rate
                    // (design: 4 workers halve the 90 s choice-building timer).
                    if (em.HasComponent<AutoConstructTag>(site))
                        buildRate = workerRate * CrewShare(crewSize) * 0.25f;

                    // Deep Foundations (Fortitude) speeds construction of
                    // defensive structures. It is the only sect research that
                    // touches build rate.
                    if (em.HasComponent<FactionTag>(site))
                    {
                        var siteFaction = em.GetComponentData<FactionTag>(site).Value;
                        buildRate *= TheWaningBorder.Economy.SectResearchEffects
                            .ConstructionSpeedMultiplier(siteFaction,
                                TheWaningBorder.Data.BuildCosts.IdFromEntity(em, site));

                        // The Hall's tools line. It used to speed up gathering,
                        // which no longer exists; building is what a worker
                        // still does. See WorkerToolsEffects.
                        buildRate *= TheWaningBorder.Entities.WorkerToolsEffects
                            .BuildSpeedMultiplier(siteFaction);
                    }

                    uc.Progress += buildRate * dt;

                    if (uc.Progress >= uc.Total)
                    {
                        // Construction complete!
                        CompleteConstruction(em, site);
                        TransientState.Clear<BuildOrder>(em, worker);

                        // THE FARM TAKES ITS BUILDER (decision 83): unless it
                        // has something queued, the worker goes inside the
                        // Farm it finished (after the loop: structural)
                        // instead of chaining to the next site.
                        if (em.HasComponent<FarmTag>(site)
                            && !TheWaningBorder.Entities.Farm.HasQueuedAction(em, worker))
                        {
                            stepInWorkers.Add(worker);
                            stepInFarms.Add(site);
                            continue;
                        }

                        // Auto-build nearby unfinished structures within LOS
                        Entity nextSite = FindNearbyUnfinishedBuilding(em, worker, bPos);
                        if (nextSite != Entity.Null)
                        {
                            if (!TransientState.Active<BuildOrder>(em, worker))
                                TransientState.Set(em, worker, new BuildOrder { Site = nextSite });
                                else
                                    em.SetComponentData(worker, new BuildOrder { Site = nextSite });
                        }
                        else
                        {
                            // No nearby sites — update guard point so worker stays here
                            if (TransientState.Active<GuardPoint>(em, worker))
                            {
                                em.SetComponentData(worker, new GuardPoint
                                {
                                    Position = bPos,
                                    Has = 1
                                });
                            }
                        }
                    }
                    else
                    {
                        // Apply HP as a DELTA from the previous construction tick so
                        // any combat damage taken between ticks survives. Earlier
                        // this overwrote hp.Value with `hp.Max * ratio`, erasing
                        // damage every tick. (task-062 Q-23)
                        if (em.HasComponent<Health>(site))
                        {
                            var hp = em.GetComponentData<Health>(site);
                            float ratio = math.clamp(uc.Progress / uc.Total, 0f, 1f);
                            int newProgressHp = math.max(1, (int)math.round(hp.Max * ratio));
                            int delta = newProgressHp - uc.LastProgressHp;
                            if (delta != 0)
                            {
                                hp.Value = math.clamp(hp.Value + delta, 1, hp.Max);
                                em.SetComponentData(site, hp);
                            }
                            uc.LastProgressHp = newProgressHp;
                        }

                        em.SetComponentData(site, uc);
                    }
                }
            }

            if (stepInWorkers.Length > 0)
                TheWaningBorder.Entities.Farm.StepInBuilders(em, stepInWorkers, stepInFarms);
            stepInWorkers.Dispose();
            stepInFarms.Dispose();

            workers.Dispose();
            workerPositions.Dispose();
            workerOrders.Dispose();
            crew.Dispose();
            claimed.Dispose();
            footprints.Dispose();

            AdoptAbandonedSites(ref state, em);
        }

        /// <summary>
        /// Give idle workers a nearby unfinished structure to resume.
        ///
        /// CLAUDE.md documents "workers auto-chain to nearby unfinished
        /// structures within LOS", but the chain only ever ran in the
        /// completion branch above — at the instant a worker FINISHED
        /// something. A worker the player walked away mid-job (a plain move
        /// order strips BuildOrder, see CommandCleanup.ClearWorkOrders) was
        /// therefore never offered the site again, and the foundation sat
        /// half-built forever with no in-game way to resume it. That is the
        /// reported bug: resources spent, nothing to show, no recourse.
        ///
        /// Deliberately does NOT touch workers under an explicit
        /// UserMoveOrder — if the player is walking a worker somewhere, it
        /// should walk there, not get captured by the first foundation it
        /// passes. Idleness is judged by the absence of work orders, never by
        /// DesiredDestination (movement consumes that flag, so reading it as
        /// "idle" is wrong).
        /// </summary>
        private void AdoptAbandonedSites(ref SystemState state, EntityManager em)
        {
            // Throttled: this is an O(workers x sites) proximity scan and the
            // answer cannot change meaningfully between frames.
            if (_adoptTimer.DueStep(SystemAPI.Time.DeltaTime, AdoptScanInterval) <= 0f) return;

            // Collect first: issuing a build adds components, and structural
            // changes invalidate an in-flight query iteration.
            var idle = new NativeList<Entity>(Allocator.Temp);
            var idlePos = new NativeList<float3>(Allocator.Temp);

            // BuildCommand is in the exclusion list because a worker WALKING
            // to its site has BuildCommand but not yet BuildOrder — without it
            // this pass would treat a worker mid-journey as idle and hand it a
            // different site every second.
            foreach (var (transform, entity) in SystemAPI
                .Query<RefRO<LocalTransform>>()
                .WithAll<CanBuild>()
                .WithNone<BuildOrder, RepairOrder, UserMoveOrder>()
                .WithEntityAccess())
            {
                // WithNone takes at most three types here, so the remaining
                // exclusions are checked inline.
                if (TransientState.Active<TheWaningBorder.Core.Commands.Types.BuildCommand>(em, entity))
                    continue;

                // A VILLAGER MID-JOB IS NOT IDLE. A worker that is mining
                // carries none of the build-order components excluded above —
                // its job lives in WorkerState / GatherCommand — so this pass
                // read every working worker as free labour and adopted it onto
                // the nearest foundation. MiningSystem then sees the BuildOrder,
                // drops the worker to Idle, and the gathering job is silently
                // lost: the player watches their economy wander off to a
                // building site they never sent anyone to.
                //
                // Command follow-through: a worker finishes what it was told to
                // do. Only genuinely unoccupied workers get adopted.
                if (IsGathering(em, entity)) continue;

                idle.Add(entity);
                idlePos.Add(transform.ValueRO.Position);
            }

            for (int i = 0; i < idle.Length; i++)
            {
                // The player's own queued plan comes first — those are sites
                // they explicitly asked for, at any distance. Only once the
                // queue is empty do we fall back to adopting whatever
                // abandoned foundation happens to be in sight.
                if (TheWaningBorder.Core.Commands.Types.BuildCommandHelper
                        .TryStartNextQueued(em, idle[i]))
                    continue;

                if (_unfinishedBuildingQuery.IsEmptyIgnoreFilter) continue;

                Entity site = FindNearbyUnfinishedBuilding(em, idle[i], idlePos[i]);
                if (site == Entity.Null) continue;

                TransientState.Set(em, idle[i], new BuildOrder { Site = site });
            }

            idle.Dispose();
            idlePos.Dispose();
        }

        /// <summary>
        /// Is this worker busy gathering? Covers both the order that was issued
        /// (GatherCommand / GatherVeilCommand, still pending) and the job it is
        /// already running (WorkerState past Idle — walking to a deposit counts,
        /// or a worker would be poached during the walk out).
        /// </summary>
        private static bool IsGathering(EntityManager em, Entity worker)
        {
            // Gathering was removed with the territory economy
            // (docs/Design/Regions.md §4): the Worker only builds, so there is
            // no gather order to be busy with. The WorkerState check below is
            // kept rather than deleted -- it is now always false, and leaving
            // it means this reads correctly if a work state is ever added back.
            if (em.HasComponent<WorkerState>(worker)
                && em.GetComponentData<WorkerState>(worker).State != WorkerActivity.Idle)
                return true;
            return false;
        }

        private SimCadence.Periodic _adoptTimer;
        private const float AdoptScanInterval = 1.0f;

        /// <summary>
        /// Finalizes building construction:
        /// - Removes UnderConstruction component
        /// - Sets health to maximum
        /// - Applies deferred defense stats
        /// </summary>
        private void CompleteConstruction(EntityManager em, Entity building)
        {
            // Read the progress-HP watermark BEFORE dropping the component —
            // the health step below needs it to tell build progress apart from
            // combat damage.
            int lastProgressHp = em.HasComponent<UnderConstruction>(building)
                ? em.GetComponentData<UnderConstruction>(building).LastProgressHp
                : 0;

            // Remove the construction markers — UnderConstruction, plus the
            // AutoConstructTag (a worker can finish a self-constructing site
            // — choice building / wall extension — before
            // AutoConstructionSystem does) and the Buildable leftover from
            // CreateUnderConstruction — in ONE structural change
            // (2026-10-15). They used to go one at a time, the two optional
            // ones after the landmark hook below, which reads neither.
            var drop = new FixedList128Bytes<ComponentType>();
            drop.Add(ComponentType.ReadWrite<UnderConstruction>());
            if (em.HasComponent<AutoConstructTag>(building)) drop.Add(ComponentType.ReadWrite<AutoConstructTag>());
            if (em.HasComponent<Buildable>(building)) drop.Add(ComponentType.ReadWrite<Buildable>());
            em.RemoveComponent(building, new ComponentTypeSet(drop));

            // Post-game chart milestone: landmark (Vault/Keep)
            // completed. Only one completion path can fire per site — the
            // UnderConstruction removal above gates the other path out.
            if (em.HasComponent<ChoiceBuildingTag>(building) && em.HasComponent<FactionTag>(building))
                TheWaningBorder.Core.Diagnostics.GameStatsTracker.RecordEvent(
                    em.GetComponentData<FactionTag>(building).Value,
                    TheWaningBorder.Core.Diagnostics.GameEventKind.SpecialBuilding);
            // A finished landmark IS the age-up (Age_0.md § Age-up by landmark).
            LandmarkAgeUp.OnConstructionComplete(em, building);
            // (Buildable / AutoConstructTag went with UnderConstruction above.)

            // Finish the HP ramp WITHOUT healing combat damage.
            //
            // The per-tick loop applies build progress as a DELTA precisely so
            // damage taken mid-build survives (task-062 Q-23) — and then this
            // used to slam hp.Value to Max and undo all of it, so a site that
            // was nearly razed during construction popped out pristine. Add
            // only the progress still owed (Max - LastProgressHp): a building
            // that took 50% damage completes at 50%.
            if (em.HasComponent<Health>(building))
            {
                var hp = em.GetComponentData<Health>(building);
                int remainingProgress = hp.Max - lastProgressHp;
                hp.Value = math.clamp(hp.Value + math.max(0, remainingProgress), 1, hp.Max);
                em.SetComponentData(building, hp);
            }

            // Restore full scale (safety net for any construction scale changes)
            if (em.HasComponent<LocalTransform>(building))
            {
                var lt = em.GetComponentData<LocalTransform>(building);
                lt.Scale = 1f;
                em.SetComponentData(building, lt);
            }

            // NO SuppliesIncome for a Gatherer's Hut: it pays ONLY its
            // territory slot (TerritoryIncomeSystem). The safety net that
            // re-added a flat income here paid it twice (2026-10-03, item 9).

            // Safety net: GathererHuts carry the Guild level ladder marker so the
            // culture auto-level + manual upgrade path can bump them (L1-L3).
            if (em.HasComponent<GathererHutTag>(building) && !em.HasComponent<BuildingUpgradeable>(building))
            {
                em.AddComponent<BuildingUpgradeable>(building);
            }

            // Apply deferred defense if present
            if (em.HasComponent<DeferredDefense>(building))
            {
                var def = em.GetComponentData<DeferredDefense>(building);

                if (!em.HasComponent<Defense>(building))
                {
                    em.AddComponentData(building, new Defense
                    {
                        Melee = def.Melee,
                        Ranged = def.Ranged,
                        Siege = def.Siege,
                        Magic = def.Magic
                    });
                }
                else
                {
                    em.SetComponentData(building, new Defense
                    {
                        Melee = def.Melee,
                        Ranged = def.Ranged,
                        Siege = def.Siege,
                        Magic = def.Magic
                    });
                }

                em.RemoveComponent<DeferredDefense>(building);
            }


            // (Feraldis House raider waves retired 2026-10-12 — the Feraldis
            // House is the Slave Pens; docs/Design/Age_1_Feraldis.md §2.)

            // task-063 phase 1: GrantTempleConstructionRP removed. The new design
            // grants RP per docs/Design/Religion.md; Temple of Ridan finishing
            // construction is not an RP source.
        }

        /// <summary>
        /// Find the nearest friendly unfinished building within the worker's line of sight.
        /// </summary>
        private Entity FindNearbyUnfinishedBuilding(EntityManager em, Entity worker, float3 workerPos)
        {
            float los = em.HasComponent<LineOfSight>(worker)
                ? em.GetComponentData<LineOfSight>(worker).Radius
                : 12f;

            Faction workerFaction = em.HasComponent<FactionTag>(worker)
                ? em.GetComponentData<FactionTag>(worker).Value
                : Faction.Blue;

            using var buildings = _unfinishedBuildingQuery.ToEntityArray(Allocator.Temp);
            using var factions = _unfinishedBuildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = _unfinishedBuildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < buildings.Length; i++)
            {
                if (factions[i].Value != workerFaction) continue;

                float dist = DistXZ(workerPos, transforms[i].Position);
                if (dist < nearestDist && dist <= los)
                {
                    nearest = buildings[i];
                    nearestDist = dist;
                }
            }

            return nearest;
        }

    }

    /// <summary>
    /// Processes BuildCommand components issued through CommandGateway.
    /// Moves workers to construction sites and manages the build workflow.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BuildingConstructionSystem))]
    public partial struct BuildCommandSystem : ISystem
    {
        // The hand-over reach is BuildingConstructionSystem.asset handoverRange.

        // Cached EntityQuery — initialized in OnCreate()
        private EntityQuery _underConstructionQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            // state.GetEntityQuery → SystemState owns lifetime, auto-disposes
            // on system teardown. (task-062 Q-42)
            _underConstructionQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<UnderConstruction>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            var cfg = BuildingConstructionSystemConfig.I;
            if (cfg == null) return;   // Require has logged the data bug
            float handover = cfg.handoverRange;

            foreach (var (transform, buildCmd, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<BuildCommand>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                var myPos = transform.ValueRO.Position;
                var targetPos = buildCmd.ValueRO.Position;
                var targetBuilding = buildCmd.ValueRO.TargetBuilding;
                // Reach to the building edge so large footprints (9 m wall hub) are
                // buildable from where the navmesh lets a worker stand. A bare
                // ground position (no target entity yet — the site hasn't been
                // placed) measures to the footprint that building WILL have.
                bool hasTarget = targetBuilding != Entity.Null && em.Exists(targetBuilding);
                var extent = hasTarget
                    ? TargetGeometry.Extent(em, targetBuilding)
                    : BuilderStand.ExtentAt(buildCmd.ValueRO.BuildingId, targetPos);
                float dist = extent.SurfaceDistXZ(myPos);

                // Move to build site if not in range. Walk to the near side of
                // the footprint, just outside its edge — never to its centre: a
                // plan does not block, so a worker sent to the centre walked
                // right onto the foundation and was standing inside the site
                // when it broke ground (2026-11-18).
                if (dist > handover)
                {
                    float3 dest = extent.ApproachPoint(myPos, BuilderStand.StandOff(em, entity, cfg));
                    dest.y = targetPos.y;
                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = dest,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = dest,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In hand-over range - face the site. Not planted: the
                    // build order this becomes walks the worker on to its own
                    // spot beside the footprint (BuilderStand), and a plan
                    // breaks ground on this tick or a later one.
                    TargetGeometry.Face(em, entity, targetPos, dt);

                    // Convert BuildCommand to BuildOrder if target building exists
                    if (targetBuilding != Entity.Null && em.Exists(targetBuilding))
                    {
                        // A PLAN is PlannedBuildingSystem's: it breaks ground
                        // and re-points this order at the real site. Never read
                        // it as "already complete" (docs/Design/Planned_Buildings.md).
                        if (em.HasComponent<PlannedBuilding>(targetBuilding)) { }
                        else if (em.HasComponent<UnderConstruction>(targetBuilding))
                        {
                            // Add BuildOrder and remove BuildCommand
                            if (!TransientState.Active<BuildOrder>(em, entity))
                            {
                                TransientState.Set(ecb, entity, new BuildOrder { Site = targetBuilding });
                            }
                            else
                            {
                                ecb.SetComponent(entity, new BuildOrder { Site = targetBuilding });
                            }
                            
                            TransientState.Clear<BuildCommand>(em, ecb, entity);
                        }
                        else
                        {
                            // Building already complete - clear command
                            TransientState.Clear<BuildCommand>(em, ecb, entity);
                        }
                    }
                    else
                    {
                        // Target building is null or destroyed — find nearest UnderConstruction
                        // building at the build position. This handles the multiplayer case where
                        // the building is created via lockstep AFTER the build command was issued.
                        Entity nearest = FindNearestUnderConstruction(em, targetPos, handover * 2f,
                            em.HasComponent<FactionTag>(entity)
                                ? em.GetComponentData<FactionTag>(entity).Value : Faction.Border);
                        if (nearest != Entity.Null)
                        {
                            if (!TransientState.Active<BuildOrder>(em, entity))
                                TransientState.Set(ecb, entity, new BuildOrder { Site = nearest });
                                else
                                    ecb.SetComponent(entity, new BuildOrder { Site = nearest });
                            TransientState.Clear<BuildCommand>(em, ecb, entity);
                        }
                        // else: building not placed yet — keep waiting (don't clear command)
                    }
                }
            }
        }


        /// <summary>
        /// Find the nearest building with UnderConstruction within searchRadius of position.
        /// Used when a BuildCommand has no target entity (multiplayer: building created via lockstep).
        /// </summary>
        private Entity FindNearestUnderConstruction(EntityManager em, float3 position, float searchRadius,
            Faction faction)
        {
            using var entities = _underConstructionQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _underConstructionQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float bestDist = searchRadius;

            for (int i = 0; i < entities.Length; i++)
            {
                // Only the worker's OWN faction's sites — a worker sent to an
                // empty spot used to adopt an enemy foundation within 8 m.
                if (em.HasComponent<FactionTag>(entities[i])
                    && em.GetComponentData<FactionTag>(entities[i]).Value != faction) continue;
                float dist = DistXZ(position, transforms[i].Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = entities[i];
                }
            }

            return nearest;
        }
    }

    /// <summary>
    /// WHERE A BUILDER STANDS (2026-11-18, developer: "builders should move out
    /// of the construction footprint before starting the construction. Also,
    /// they should only be able to build when very close to the square
    /// footprint of the building ... Some build and repair paths leave the
    /// workers moving in circles without working."). docs/Design/Age_0.md
    /// § Worker. Shared by construction and repair, numbers on
    /// BuildingConstructionSystem.asset.
    ///
    /// * A worker works only while its centre is OUTSIDE the footprint and
    ///   within reach of its edge: no nearer than Radius - bodyOverlapTolerance,
    ///   no further than Radius + reachBeyondRadius. Inside reads as distance 0
    ///   on TargetExtent.SurfaceDistXZ, which is why workers used to build from
    ///   inside the site.
    /// * Otherwise it walks to ITS OWN spot: one slot on a rounded ring
    ///   Radius + standGap outside the edge, slotSpacing apart — the nearest
    ///   slot no other builder on the site holds, on walkable ground and not
    ///   inside another building. The spot is latched on the order, so the walk
    ///   target never moves; the old target was recomputed from the worker's
    ///   position every tick, and a goal that slides with the walker reads as a
    ///   fresh order to StuckRedirectSystem every half second, so an orbit was
    ///   never declared stuck.
    /// * A worker within stallRadius of its spot that gets no closer for
    ///   stallSeconds gives that slot up for the next; with every slot tried
    ///   the job is dropped (Step.GiveUp) rather than orbited for ever.
    ///
    /// Deterministic: slots are enumerated in index order, ties go to the lower
    /// index, claims are set membership only, and every input is sim state.
    /// </summary>
    public static class BuilderStand
    {
        public enum Step : byte { Work, Walk, GiveUp }

        /// <summary>Slots per site — the width of <see cref="WorkStand.Tried"/>.</summary>
        public const int MaxSlots = 64;
        /// <summary>Fewest slots on any ring (one a side).</summary>
        private const int MinSlots = 4;
        /// <summary>Geometric epsilon: "outside" means strictly past the edge.</summary>
        private const float MinOutside = 0.05f;

        /// <summary>Every standing building with a footprint, for the "not
        /// inside another building" spot test.</summary>
        public static EntityQuery FootprintQuery(ref SystemState state)
            => state.GetEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<BuildingSize>(),
                ComponentType.ReadOnly<LocalTransform>());

        public static float RadiusOf(EntityManager em, Entity worker)
            => em.HasComponent<Radius>(worker) ? em.GetComponentData<Radius>(worker).Value : 0f;

        static float Inner(float radius, BuildingConstructionSystemConfig cfg)
            => math.max(MinOutside, radius - cfg.bodyOverlapTolerance);

        static float Outer(float radius, BuildingConstructionSystemConfig cfg)
            => math.max(Inner(radius, cfg), radius + cfg.reachBeyondRadius);

        /// <summary>Centre-to-edge distance of a worker's spot: its body plus
        /// standGap, kept inside the reach band.</summary>
        public static float StandOff(float radius, BuildingConstructionSystemConfig cfg)
            => math.clamp(radius + cfg.standGap, Inner(radius, cfg), Outer(radius, cfg));

        public static float StandOff(EntityManager em, Entity worker, BuildingConstructionSystemConfig cfg)
            => StandOff(RadiusOf(em, worker), cfg);

        /// <summary>May this worker build / repair from where it stands?</summary>
        public static bool InReach(in TargetExtent ext, float3 pos, float radius,
            BuildingConstructionSystemConfig cfg)
        {
            float d = ext.SurfaceDistXZ(pos);
            return d >= Inner(radius, cfg) && d <= Outer(radius, cfg);
        }

        /// <summary>The footprint a building of this id will have at
        /// <paramref name="centre"/> — for a Build order whose site does not
        /// exist yet.</summary>
        public static TargetExtent ExtentAt(FixedString64Bytes buildingId, float3 centre)
        {
            var e = new TargetExtent { Center = centre };
            if (buildingId.Length == 0) return e;
            int2 size = BuildingSizeConfig.GetSize(buildingId.ToString());
            if (size.x <= 0 || size.y <= 0) return e;
            e.IsBox = true;
            e.HalfW = size.x * 0.5f;
            e.HalfH = size.y * 0.5f;
            return e;
        }

        static int4 Key(Entity site, int slot) => new int4(site.Index, site.Version, slot, 0);

        /// <summary>Record a held spot, so no other builder picks it.</summary>
        public static void Claim(NativeHashSet<int4> claimed, Entity site, in WorkStand stand)
        {
            if (stand.HasSpot != 0) claimed.Add(Key(site, stand.SlotIndex));
        }

        public static bool SameStand(in WorkStand a, in WorkStand b)
            => a.HasSpot == b.HasSpot && a.SlotIndex == b.SlotIndex && a.Tried == b.Tried
               && a.Stall == b.Stall && a.BestDist == b.BestDist && a.Spot.Equals(b.Spot);

        /// <summary>
        /// One tick of a builder's approach. Work: in reach, do the job (the
        /// caller plants and faces). Walk: the destination is set to the spot.
        /// GiveUp: no slot round the site can be reached.
        /// </summary>
        public static Step Tick(EntityManager em, Entity worker, float3 pos, Entity site,
            in TargetExtent ext, ref WorkStand stand, NativeHashSet<int4> claimed,
            ref Footprints footprints, BuildingConstructionSystemConfig cfg, float dt)
        {
            float radius = RadiusOf(em, worker);
            if (InReach(ext, pos, radius, cfg))
            {
                stand.Stall = 0f;
                return Step.Work;
            }

            if (stand.HasSpot == 0
                && !Pick(ext, site, pos, radius, ref stand, claimed, ref footprints, cfg))
                return Step.GiveUp;

            float d = MathUtil.DistXZ(pos, stand.Spot);
            if (d < stand.BestDist - cfg.stallMinProgress)
            {
                stand.BestDist = d;
                stand.Stall = 0f;
            }
            else if (d <= cfg.stallRadius)
            {
                stand.Stall += dt;
                if (stand.Stall >= cfg.stallSeconds)
                {
                    // This slot cannot be reached (or something stands on it):
                    // the next one, never the same one again.
                    if (stand.SlotIndex < MaxSlots) stand.Tried |= 1ul << stand.SlotIndex;
                    stand.HasSpot = 0;
                    if (!Pick(ext, site, pos, radius, ref stand, claimed, ref footprints, cfg))
                        return Step.GiveUp;
                }
            }

            SetDestination(em, worker, stand.Spot);
            return Step.Walk;
        }

        /// <summary>Drop the job: clear the order, stop, and make where the
        /// worker stands its guard point (as finishing a job does).</summary>
        public static void StandDown<T>(EntityManager em, Entity worker, float3 pos)
            where T : unmanaged, IComponentData, IEnableableComponent
        {
            TransientState.Clear<T>(em, worker);
            if (em.HasComponent<DesiredDestination>(worker))
                em.SetComponentData(worker, new DesiredDestination { Has = 0 });
            if (TransientState.Active<GuardPoint>(em, worker))
                em.SetComponentData(worker, new GuardPoint { Position = pos, Has = 1 });
        }

        static void SetDestination(EntityManager em, Entity worker, float3 spot)
        {
            if (em.HasComponent<DesiredDestination>(worker))
            {
                var dd = em.GetComponentData<DesiredDestination>(worker);
                if (dd.Has != 0 && dd.Position.Equals(spot)) return;
                em.SetComponentData(worker, new DesiredDestination { Position = spot, Has = 1 });
            }
            else
            {
                em.AddComponentData(worker, new DesiredDestination { Position = spot, Has = 1 });
            }
        }

        /// <summary>The nearest usable slot: untried, unheld (held ones only
        /// when every free one is unusable — a crew bigger than the ring),
        /// walkable, and not inside another building.</summary>
        static bool Pick(in TargetExtent ext, Entity site, float3 pos, float radius,
            ref WorkStand stand, NativeHashSet<int4> claimed, ref Footprints footprints,
            BuildingConstructionSystemConfig cfg)
        {
            float so = StandOff(radius, cfg);
            float per = Perimeter(ext, so);
            int n = SlotCount(per, cfg.slotSpacing);

            for (int pass = 0; pass < 2; pass++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                float3 bestP = default;
                for (int k = 0; k < n; k++)
                {
                    if ((stand.Tried & (1ul << k)) != 0) continue;
                    if (pass == 0 && claimed.Contains(Key(site, k))) continue;
                    float3 p = SlotPoint(ext, so, per, k, n);
                    float dx = p.x - pos.x, dz = p.z - pos.z;
                    float dsq = dx * dx + dz * dz;
                    if (dsq >= bestD) continue;
                    if (!Usable(p, site, ref footprints)) continue;
                    best = k; bestD = dsq; bestP = p;
                }
                if (best < 0) continue;

                stand.Spot = bestP;
                stand.HasSpot = 1;
                stand.SlotIndex = (byte)best;
                stand.BestDist = float.MaxValue;
                stand.Stall = 0f;
                claimed.Add(Key(site, best));
                return true;
            }
            return false;
        }

        static bool Usable(float3 p, Entity site, ref Footprints footprints)
        {
            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWalkableAt(p.x, p.z, out _, out _))
                return false;
            return !footprints.Covers(p, site);
        }

        static int SlotCount(float perimeter, float spacing)
            => math.clamp((int)math.floor(perimeter / math.max(0.1f, spacing)), MinSlots, MaxSlots);

        /// <summary>Length of the ring <paramref name="so"/> outside the
        /// footprint: a rounded rectangle for a box, a circle otherwise.</summary>
        static float Perimeter(in TargetExtent ext, float so)
            => ext.IsBox
                ? 4f * (ext.HalfW + ext.HalfH) + 2f * math.PI * so
                : 2f * math.PI * (ext.Radius + so);

        /// <summary>Slot <paramref name="k"/> of <paramref name="n"/>, evenly
        /// along the ring, every point exactly <paramref name="so"/> from the
        /// footprint's edge.</summary>
        static float3 SlotPoint(in TargetExtent ext, float so, float per, int k, int n)
        {
            float3 c = ext.Center;
            float s = (k + 0.5f) * per / n;
            if (!ext.IsBox)
            {
                float a = s / math.max(1e-4f, ext.Radius + so);
                float r = ext.Radius + so;
                return new float3(c.x + math.cos(a) * r, c.y, c.z + math.sin(a) * r);
            }

            float hw = ext.HalfW, hh = ext.HalfH;
            float arc = 0.5f * math.PI * so;
            // South edge (west to east), SE arc, east edge (south to north), NE
            // arc, north edge (east to west), NW arc, west edge (north to
            // south), SW arc.
            float len = 2f * hw;
            if (s < len) return new float3(c.x - hw + s, c.y, c.z - hh - so);
            s -= len;
            if (s < arc) return Corner(c, hw, -hh, -0.5f * math.PI + s / so, so);
            s -= arc;
            len = 2f * hh;
            if (s < len) return new float3(c.x + hw + so, c.y, c.z - hh + s);
            s -= len;
            if (s < arc) return Corner(c, hw, hh, s / so, so);
            s -= arc;
            len = 2f * hw;
            if (s < len) return new float3(c.x + hw - s, c.y, c.z + hh + so);
            s -= len;
            if (s < arc) return Corner(c, -hw, hh, 0.5f * math.PI + s / so, so);
            s -= arc;
            len = 2f * hh;
            if (s < len) return new float3(c.x - hw - so, c.y, c.z + hh - s);
            s -= len;
            return Corner(c, -hw, -hh, math.PI + math.min(s, arc) / math.max(1e-4f, so), so);
        }

        static float3 Corner(float3 c, float ox, float oz, float angle, float so)
            => new float3(c.x + ox + math.cos(angle) * so, c.y, c.z + oz + math.sin(angle) * so);

        /// <summary>The footprints of standing buildings, read once a tick and
        /// only when some worker needs a new spot.</summary>
        public struct Footprints : System.IDisposable
        {
            EntityQuery _query;
            bool _loaded;
            NativeArray<Entity> _ents;
            NativeArray<LocalTransform> _xfs;
            NativeArray<BuildingSize> _sizes;

            public Footprints(EntityQuery query)
            {
                _query = query;
                _loaded = false;
                _ents = default;
                _xfs = default;
                _sizes = default;
            }

            /// <summary>Is <paramref name="p"/> inside any building's
            /// footprint other than <paramref name="except"/>?</summary>
            public bool Covers(float3 p, Entity except)
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _ents = _query.ToEntityArray(Allocator.Temp);
                    _xfs = _query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                    _sizes = _query.ToComponentDataArray<BuildingSize>(Allocator.Temp);
                }
                for (int i = 0; i < _ents.Length; i++)
                {
                    if (_ents[i] == except) continue;
                    float3 c = _xfs[i].Position;
                    if (math.abs(p.x - c.x) < _sizes[i].Width * 0.5f
                        && math.abs(p.z - c.z) < _sizes[i].Height * 0.5f) return true;
                }
                return false;
            }

            public void Dispose()
            {
                if (_ents.IsCreated) _ents.Dispose();
                if (_xfs.IsCreated) _xfs.Dispose();
                if (_sizes.IsCreated) _sizes.Dispose();
            }
        }
    }
}