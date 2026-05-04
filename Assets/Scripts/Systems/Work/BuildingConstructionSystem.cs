// File: Assets/Scripts/Systems/Work/BuildingConstructionSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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
                float dist = DistXZ(bPos, sitePos);

                if (dist > BuildRange)
                {
                    // Move toward site
                    if (em.HasComponent<DesiredDestination>(builder))
                    {
                        em.SetComponentData(builder, new DesiredDestination
                        {
                            Position = sitePos,
                            Has = 1
                        });
                    }
                    else
                    {
                        em.AddComponentData(builder, new DesiredDestination
                        {
                            Position = sitePos,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In range - stop moving and contribute to construction
                    if (em.HasComponent<DesiredDestination>(builder))
                    {
                        em.SetComponentData(builder, new DesiredDestination { Has = 0 });
                    }

                    // Add build progress
                    var uc = em.GetComponentData<UnderConstruction>(site);
                    float buildRate = BuildRatePerBuilder;

                    // task-063 phase 1: sect BuildSpeed multiplier removed with the
                    // FactionSectState bridge. Phase 2 reintroduces build-speed levers.

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
        }

        /// <summary>
        /// Finalizes building construction:
        /// - Removes UnderConstruction component
        /// - Sets health to maximum
        /// - Applies deferred defense stats
        /// </summary>
        private void CompleteConstruction(EntityManager em, Entity building)
        {
            // Remove construction marker
            em.RemoveComponent<UnderConstruction>(building);
            // Also remove Buildable if present (leftover from CreateUnderConstruction)
            if (em.HasComponent<Buildable>(building))
                em.RemoveComponent<Buildable>(building);

            // Set health to max (sect BuildingHP multiplier removed in task-063
            // phase 1; Phase 2's Fortitude/Oath-Stone levers reintroduce this).
            if (em.HasComponent<Health>(building))
            {
                var hp = em.GetComponentData<Health>(building);
                hp.Value = hp.Max;
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
                em.AddComponentData(building, new SuppliesIncome { PerTick = 15f, Interval = 10f });
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
            // ChapelSmallTag is the existing ECS marker on the Shrine. Phase 2
            // will introduce 12 distinct chapel buildings for the actual sect
            // adoption mechanism — those are NOT this code path.
            if (em.HasComponent<ChapelSmallTag>(building) && em.HasComponent<FactionTag>(building))
            {
                var faction = em.GetComponentData<FactionTag>(building).Value;
                FactionReligionPointsHelper.TryAwardShrineBonus(em, faction);
            }

            // task-063 phase 1: GrantTempleConstructionRP removed. The new design
            // grants RP only on age-up + Shrine completion + chapel completion.
            // Temple of Ridan finishing construction is no longer an RP source.
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

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
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

            foreach (var (transform, buildCmd, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<BuildCommand>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                var myPos = transform.ValueRO.Position;
                var targetPos = buildCmd.ValueRO.Position;
                var targetBuilding = buildCmd.ValueRO.TargetBuilding;
                var dist = DistXZ(myPos, targetPos);

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
                    // In range - stop moving
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

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

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
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