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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildOrder>();
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
                    uc.Progress += BuildRatePerBuilder * dt;

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
                        em.SetComponentData(site, uc);

                        // Scale HP proportionally with construction progress
                        if (em.HasComponent<Health>(site))
                        {
                            var hp = em.GetComponentData<Health>(site);
                            float ratio = math.clamp(uc.Progress / uc.Total, 0f, 1f);
                            hp.Value = math.max(1, (int)math.round(hp.Max * ratio));
                            em.SetComponentData(site, hp);
                        }
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

            // Set health to max
            if (em.HasComponent<Health>(building))
            {
                var hp = em.GetComponentData<Health>(building);
                hp.Value = hp.Max;
                em.SetComponentData(building, hp);
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

            UnityEngine.Debug.Log($"Building {building.Index} construction complete!");
        }

        /// <summary>
        /// Find the nearest friendly unfinished building within the builder's line of sight.
        /// </summary>
        private static Entity FindNearbyUnfinishedBuilding(EntityManager em, Entity builder, float3 builderPos)
        {
            float los = em.HasComponent<LineOfSight>(builder)
                ? em.GetComponentData<LineOfSight>(builder).Radius
                : 12f;

            Faction builderFaction = em.HasComponent<FactionTag>(builder)
                ? em.GetComponentData<FactionTag>(builder).Value
                : Faction.Blue;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<UnderConstruction>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var buildings = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
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
                        // No valid target building - clear command
                        // (Building creation should happen elsewhere, e.g., UI system)
                        ecb.RemoveComponent<BuildCommand>(entity);
                    }
                }
            }
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}