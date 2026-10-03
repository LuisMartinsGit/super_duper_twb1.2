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
        private const float BuildRange = 4.0f;
        private const float BuildRatePerWorker = 1.0f; // Progress per second per worker

        // Cached EntityQueries — initialized in OnCreate()
        private EntityQuery _unfinishedBuildingQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildOrder>();

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
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot all workers with orders
            var workerQuery = SystemAPI.QueryBuilder()
                .WithAll<CanBuild, LocalTransform, BuildOrder>()
                .Build();

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
                    em.RemoveComponent<BuildOrder>(worker);
                    continue;
                }

                // Check if site is still under construction
                if (!em.HasComponent<UnderConstruction>(site))
                {
                    // Already finished - clear order
                    em.RemoveComponent<BuildOrder>(worker);
                    continue;
                }

                // Get site position
                float3 sitePos = em.GetComponentData<LocalTransform>(site).Position;
                // Measure to the building's edge, not its centre, so workers can
                // construct large footprints (e.g. the 9 m wall hub, which blocks the
                // navmesh well beyond BuildRange of the centre). Sized buildings use
                // their exact rect rather than the inscribed legacy Radius, which
                // under-measures at corners.
                var extent = TargetGeometry.Extent(em, site);
                float dist = extent.SurfaceDistXZ(bPos);

                if (dist > BuildRange)
                {
                    // Walk to a point on the footprint's edge, a half-step back so
                    // the destination is walkable ground rather than a cell inside
                    // the site itself.
                    float3 approach = extent.ApproachPoint(bPos, BuildRange * 0.5f);
                    approach.y = sitePos.y;

                    if (em.HasComponent<DesiredDestination>(worker))
                    {
                        em.SetComponentData(worker, new DesiredDestination
                        {
                            Position = approach,
                            Has = 1
                        });
                    }
                    else
                    {
                        em.AddComponentData(worker, new DesiredDestination
                        {
                            Position = approach,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In range - plant, face the site, and contribute to construction
                    TargetGeometry.StopAndFace(em, worker, sitePos, dt);

                    // Add build progress
                    var uc = em.GetComponentData<UnderConstruction>(site);
                    float buildRate = BuildRatePerWorker;

                    // Self-constructing sites (choice buildings, wall extensions)
                    // already tick at 1.0/s via AutoConstructionSystem; workers
                    // only ACCELERATE them, each adding +25 % of the base rate
                    // (design: 4 workers halve the 90 s choice-building timer).
                    if (em.HasComponent<AutoConstructTag>(site))
                        buildRate = BuildRatePerWorker * 0.25f;

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
                        em.RemoveComponent<BuildOrder>(worker);

                        // Auto-build nearby unfinished structures within LOS
                        Entity nextSite = FindNearbyUnfinishedBuilding(em, worker, bPos);
                        if (nextSite != Entity.Null)
                        {
                            if (!em.HasComponent<BuildOrder>(worker))
                                em.AddComponentData(worker, new BuildOrder { Site = nextSite });
                                else
                                    em.SetComponentData(worker, new BuildOrder { Site = nextSite });
                        }
                        else
                        {
                            // No nearby sites — update guard point so worker stays here
                            if (em.HasComponent<GuardPoint>(worker))
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

            workers.Dispose();
            workerPositions.Dispose();
            workerOrders.Dispose();

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
                if (em.HasComponent<TheWaningBorder.Core.Commands.Types.BuildCommand>(entity))
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

                em.AddComponentData(idle[i], new BuildOrder { Site = site });
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

            // Remove construction marker
            em.RemoveComponent<UnderConstruction>(building);

            // Post-game chart milestone: landmark (Vault/Keep)
            // completed. Only one completion path can fire per site — the
            // UnderConstruction removal above gates the other path out.
            if (em.HasComponent<ChoiceBuildingTag>(building) && em.HasComponent<FactionTag>(building))
                TheWaningBorder.Core.Diagnostics.GameStatsTracker.RecordEvent(
                    em.GetComponentData<FactionTag>(building).Value,
                    TheWaningBorder.Core.Diagnostics.GameEventKind.SpecialBuilding);
            // A finished landmark IS the age-up (Age_0.md § Age-up by landmark).
            LandmarkAgeUp.OnConstructionComplete(em, building);
            // Also remove Buildable if present (leftover from CreateUnderConstruction)
            if (em.HasComponent<Buildable>(building))
                em.RemoveComponent<Buildable>(building);
            // A worker can finish a self-constructing site (choice building /
            // wall extension) before AutoConstructionSystem does — drop the
            // auto tag so it doesn't linger on the completed building.
            if (em.HasComponent<AutoConstructTag>(building))
                em.RemoveComponent<AutoConstructTag>(building);

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


            // task-066 Phase 3 / design §5.3: Feraldis Houses spawn raiders on
            // construction completion (L1 = 1 raider). Upgrade ticks (L2/L3) are
            // owned by FeraldisRaiderSpawnSystem, which watches BuildingLevel changes.
            if (em.HasComponent<HutTag>(building) && em.HasComponent<FactionTag>(building))
            {
                var faction = em.GetComponentData<FactionTag>(building).Value;
                if (FactionColors.GetFactionCulture(faction) == Cultures.Feraldis)
                {
                    SpawnFeraldisRaidersAtHouse(em, building, faction, count: 1);
                }
            }

            // task-063 phase 1: GrantTempleConstructionRP removed. The new design
            // grants RP per docs/Design/Religion.md; Temple of Ridan finishing
            // construction is not an RP source.
        }

        /// <summary>
        /// Spawn N Feraldis Raider units at a House's position. Called on House
        /// completion (Phase 3 of task-066). Raiders are uncontrollable and
        /// driven by FeraldisRaiderPatrolSystem.
        /// </summary>
        private static void SpawnFeraldisRaidersAtHouse(EntityManager em, Entity house, Faction faction, int count)
        {
            if (!em.HasComponent<LocalTransform>(house)) return;

            float3 housePos = em.GetComponentData<LocalTransform>(house).Position;
            for (int i = 0; i < count; i++)
            {
                // Spread spawn positions slightly so raiders don't stack on creation.
                float angle = (i / (float)math.max(count, 1)) * math.PI * 2f;
                float3 offset = new float3(math.cos(angle) * 1.5f, 0f, math.sin(angle) * 1.5f);
                TheWaningBorder.Entities.FeraldisRaider.CreateUncontrolled(em, housePos + offset, faction);
            }
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
        private const float BuildRange = 4f;

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

            foreach (var (transform, buildCmd, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<BuildCommand>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                var myPos = transform.ValueRO.Position;
                var targetPos = buildCmd.ValueRO.Position;
                var targetBuilding = buildCmd.ValueRO.TargetBuilding;
                // Reach to the building edge so large footprints (9 m wall hub) are
                // buildable from where the navmesh lets a worker stand. Falls back
                // to plain centre distance for a bare ground position (no target
                // entity yet — the site hasn't been placed).
                float dist = targetBuilding != Entity.Null && em.Exists(targetBuilding)
                    ? TargetGeometry.SurfaceDistXZ(em, myPos, targetBuilding)
                    : DistXZ(myPos, targetPos);

                // Move to build site if not in range
                if (dist > BuildRange)
                {
                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In range - plant and face the site
                    TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);

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
                            if (!em.HasComponent<BuildOrder>(entity))
                            {
                                ecb.AddComponent(entity, new BuildOrder { Site = targetBuilding });
                            }
                            else
                            {
                                ecb.SetComponent(entity, new BuildOrder { Site = targetBuilding });
                            }
                            
                            ecb.RemoveComponent<BuildCommand>(entity);
                        }
                        else
                        {
                            // Building already complete - clear command
                            ecb.RemoveComponent<BuildCommand>(entity);
                        }
                    }
                    else
                    {
                        // Target building is null or destroyed — find nearest UnderConstruction
                        // building at the build position. This handles the multiplayer case where
                        // the building is created via lockstep AFTER the build command was issued.
                        Entity nearest = FindNearestUnderConstruction(em, targetPos, BuildRange * 2f,
                            em.HasComponent<FactionTag>(entity)
                                ? em.GetComponentData<FactionTag>(entity).Value : Faction.Border);
                        if (nearest != Entity.Null)
                        {
                            if (!em.HasComponent<BuildOrder>(entity))
                                ecb.AddComponent(entity, new BuildOrder { Site = nearest });
                                else
                                    ecb.SetComponent(entity, new BuildOrder { Site = nearest });
                            ecb.RemoveComponent<BuildCommand>(entity);
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
}