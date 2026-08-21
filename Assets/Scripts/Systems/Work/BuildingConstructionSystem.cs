// File: Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs
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
    /// Handles building construction by builder units.
    /// 
    /// Construction workflow:
    /// 1. Player places building ghost (UnderConstruction component, low HP)
    /// 2. Builder receives BuildOrder component pointing to construction site
    /// 3. Builder moves to site and contributes build progress
    /// 4. When Progress >= Total, building completes:
    ///    - UnderConstruction removed
    ///    - Health set to max
    ///    - DeferredDefense applied as Defense component
    /// 
    /// Multiple builders can work on the same building simultaneously.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BuildingConstructionSystem : ISystem
    {
        private const float BuildRange = 4.0f;
        private const float BuildRatePerBuilder = 1.0f; // Progress per second per builder

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

            // task-063 phase 1: _templeQuery removed — was only used by the old
            // GrantShrineRPBonus path that required an existing Temple to award the
            // shrine bonus. The new design grants the +1 RP unconditionally on
            // Shrine completion (latched once per faction).
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot all builders with orders
            var builderQuery = SystemAPI.QueryBuilder()
                .WithAll<CanBuild, LocalTransform, BuildOrder>()
                .Build();

            var builders = new NativeList<Entity>(Allocator.Temp);
            var builderPositions = new NativeList<float3>(Allocator.Temp);
            var builderOrders = new NativeList<BuildOrder>(Allocator.Temp);

            foreach (var (transform, order, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<BuildOrder>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                builders.Add(entity);
                builderPositions.Add(transform.ValueRO.Position);
                builderOrders.Add(order.ValueRO);
            }

            // Process each builder
            for (int i = 0; i < builders.Length; i++)
            {
                Entity builder = builders[i];
                float3 bPos = builderPositions[i];
                Entity site = builderOrders[i].Site;

                // Validate construction site exists
                if (!em.Exists(site))
                {
                    // Site destroyed - clear order
                    em.RemoveComponent<BuildOrder>(builder);
                    continue;
                }

                // Check if site is still under construction
                if (!em.HasComponent<UnderConstruction>(site))
                {
                    // Already finished - clear order
                    em.RemoveComponent<BuildOrder>(builder);
                    continue;
                }

                // Get site position
                float3 sitePos = em.GetComponentData<LocalTransform>(site).Position;
                // Measure to the building's edge, not its centre, so builders can
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

                    if (em.HasComponent<DesiredDestination>(builder))
                    {
                        em.SetComponentData(builder, new DesiredDestination
                        {
                            Position = approach,
                            Has = 1
                        });
                    }
                    else
                    {
                        em.AddComponentData(builder, new DesiredDestination
                        {
                            Position = approach,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In range - plant, face the site, and contribute to construction
                    TargetGeometry.StopAndFace(em, builder, sitePos, dt);

                    // Add build progress
                    var uc = em.GetComponentData<UnderConstruction>(site);
                    float buildRate = BuildRatePerBuilder;

                    // Self-constructing sites (choice buildings, wall extensions)
                    // already tick at 1.0/s via AutoConstructionSystem; builders
                    // only ACCELERATE them, each adding +25 % of the base rate
                    // (design: 4 workers halve the 90 s choice-building timer).
                    if (em.HasComponent<AutoConstructTag>(site))
                        buildRate = BuildRatePerBuilder * 0.25f;

                    // Mason's Charter (Renewal, all buildings) and Deep
                    // Foundations (Fortitude, defensive structures only) both
                    // speed construction, and stack on a wall or tower.
                    if (em.HasComponent<FactionTag>(site))
                    {
                        var siteFaction = em.GetComponentData<FactionTag>(site).Value;
                        buildRate *= TheWaningBorder.Economy.SectResearchEffects
                            .ConstructionSpeedMultiplier(siteFaction,
                                TheWaningBorder.Data.BuildCosts.IdFromEntity(em, site));
                    }

                    uc.Progress += buildRate * dt;

                    if (uc.Progress >= uc.Total)
                    {
                        // Construction complete!
                        CompleteConstruction(em, site);
                        em.RemoveComponent<BuildOrder>(builder);

                        // Auto-build nearby unfinished structures within LOS
                        Entity nextSite = FindNearbyUnfinishedBuilding(em, builder, bPos);
                        if (nextSite != Entity.Null)
                        {
                            if (!em.HasComponent<BuildOrder>(builder))
                                em.AddComponentData(builder, new BuildOrder { Site = nextSite });
                                else
                                    em.SetComponentData(builder, new BuildOrder { Site = nextSite });
                        }
                        else
                        {
                            // No nearby sites — update guard point so builder stays here
                            if (em.HasComponent<GuardPoint>(builder))
                            {
                                em.SetComponentData(builder, new GuardPoint
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

            builders.Dispose();
            builderPositions.Dispose();
            builderOrders.Dispose();

            AdoptAbandonedSites(ref state, em);
        }

        /// <summary>
        /// Give idle builders a nearby unfinished structure to resume.
        ///
        /// CLAUDE.md documents "builders auto-chain to nearby unfinished
        /// structures within LOS", but the chain only ever ran in the
        /// completion branch above — at the instant a builder FINISHED
        /// something. A builder the player walked away mid-job (a plain move
        /// order strips BuildOrder, see CommandCleanup.ClearWorkOrders) was
        /// therefore never offered the site again, and the foundation sat
        /// half-built forever with no in-game way to resume it. That is the
        /// reported bug: resources spent, nothing to show, no recourse.
        ///
        /// Deliberately does NOT touch builders under an explicit
        /// UserMoveOrder — if the player is walking a builder somewhere, it
        /// should walk there, not get captured by the first foundation it
        /// passes. Idleness is judged by the absence of work orders, never by
        /// DesiredDestination (movement consumes that flag, so reading it as
        /// "idle" is wrong).
        /// </summary>
        private void AdoptAbandonedSites(ref SystemState state, EntityManager em)
        {
            // Throttled: this is an O(builders x sites) proximity scan and the
            // answer cannot change meaningfully between frames.
            if (_adoptTimer.DueStep(SystemAPI.Time.DeltaTime, AdoptScanInterval) <= 0f) return;

            // Collect first: issuing a build adds components, and structural
            // changes invalidate an in-flight query iteration.
            var idle = new NativeList<Entity>(Allocator.Temp);
            var idlePos = new NativeList<float3>(Allocator.Temp);

            // BuildCommand is in the exclusion list because a builder WALKING
            // to its site has BuildCommand but not yet BuildOrder — without it
            // this pass would treat a builder mid-journey as idle and hand it a
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
                // its job lives in MinerState / GatherCommand — so this pass
                // read every working miner as free labour and adopted it onto
                // the nearest foundation. MiningSystem then sees the BuildOrder,
                // drops the miner to Idle, and the gathering job is silently
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
        /// already running (MinerState past Idle — walking to a deposit counts,
        /// or a worker would be poached during the walk out).
        /// </summary>
        private static bool IsGathering(EntityManager em, Entity worker)
        {
            if (em.HasComponent<TheWaningBorder.Core.Commands.Types.GatherCommand>(worker))
                return true;
            if (em.HasComponent<TheWaningBorder.Core.Commands.Types.GatherVeilCommand>(worker))
                return true;
            if (em.HasComponent<MinerState>(worker)
                && em.GetComponentData<MinerState>(worker).State != MinerWorkState.Idle)
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

            // Post-game chart milestone: choice building (Shrine/Vault/Keep)
            // completed. Only one completion path can fire per site — the
            // UnderConstruction removal above gates the other path out.
            if (em.HasComponent<ChoiceBuildingTag>(building) && em.HasComponent<FactionTag>(building))
                TheWaningBorder.UI.HUD.GameStatsTracker.RecordEvent(
                    em.GetComponentData<FactionTag>(building).Value,
                    TheWaningBorder.UI.HUD.GameEventKind.SpecialBuilding);
            // Also remove Buildable if present (leftover from CreateUnderConstruction)
            if (em.HasComponent<Buildable>(building))
                em.RemoveComponent<Buildable>(building);
            // A builder can finish a self-constructing site (choice building /
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

            // Safety net: ensure GathererHuts have SuppliesIncome after completion
            if (em.HasComponent<GathererHutTag>(building) && !em.HasComponent<SuppliesIncome>(building))
            {
                em.AddComponentData(building, new SuppliesIncome { PerTick = 10f, Interval = 10f });
            }

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

            // Shrine RP bonus: grant +1 RP (latched, one-time) when the Shrine of
            // Ahridan completes. The new design (task-063) routes this through
            // FactionReligionPointsHelper rather than the legacy
            // ReligionPoints { Value } singleton path.
            //
            // BuildingFactory tags Shrine entities with ShrineTag (not the
            // earlier-design ChapelSmallTag marker), so gate the bonus on
            // ShrineTag — the previous check matched nothing on real Shrines
            // and never awarded the +1 RP grant.
            if (em.HasComponent<ShrineTag>(building) && em.HasComponent<FactionTag>(building))
            {
                var faction = em.GetComponentData<FactionTag>(building).Value;
                FactionReligionPointsHelper.TryAwardShrineBonus(em, faction);
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
            // grants RP only on age-up + Shrine completion + chapel completion.
            // Temple of Ridan finishing construction is no longer an RP source.
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
        /// Find the nearest friendly unfinished building within the builder's line of sight.
        /// </summary>
        private Entity FindNearbyUnfinishedBuilding(EntityManager em, Entity builder, float3 builderPos)
        {
            float los = em.HasComponent<LineOfSight>(builder)
                ? em.GetComponentData<LineOfSight>(builder).Radius
                : 12f;

            Faction builderFaction = em.HasComponent<FactionTag>(builder)
                ? em.GetComponentData<FactionTag>(builder).Value
                : Faction.Blue;

            using var buildings = _unfinishedBuildingQuery.ToEntityArray(Allocator.Temp);
            using var factions = _unfinishedBuildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = _unfinishedBuildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < buildings.Length; i++)
            {
                if (factions[i].Value != builderFaction) continue;

                float dist = DistXZ(builderPos, transforms[i].Position);
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
    /// Moves builders to construction sites and manages the build workflow.
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
                // buildable from where the navmesh lets a builder stand. Falls back
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
                        if (em.HasComponent<UnderConstruction>(targetBuilding))
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
                        Entity nearest = FindNearestUnderConstruction(em, targetPos, BuildRange * 2f);
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
        private Entity FindNearestUnderConstruction(EntityManager em, float3 position, float searchRadius)
        {
            using var entities = _underConstructionQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _underConstructionQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float bestDist = searchRadius;

            for (int i = 0; i < entities.Length; i++)
            {
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