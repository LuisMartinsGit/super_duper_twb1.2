// File: Assets/Scripts/Systems/Economy/TradeRouteSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Manages Runai trade routes between TradeHubs and Outposts.
    ///
    /// Responsibilities:
    /// 1. Route Discovery: Periodically finds the nearest same-faction Outpost
    ///    for each TradeHub and establishes a TradeRoute.
    /// 2. Caravan Spawning: Spawns caravans (with escorts) on a 22-second timer,
    ///    up to 3 active caravans per route.
    ///
    /// Design philosophy: Longer trade routes = more income per trip.
    /// Formula: baseIncome (25) * (routeLength / 30.0), with +25% tariff bonus
    /// for routes longer than 60 units.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TradeRouteSystem : ISystem
    {
        // ==================== Constants ====================
        private const float SpawnInterval = 22f;
        private const int MaxCaravansPerRoute = 3;
        private const float BaseIncome = 25f;
        private const float RouteLengthDivisor = 30f;
        private const float TariffThreshold = 60f;
        private const float TariffBonus = 0.25f;
        private const float RouteDiscoveryInterval = 2f;

        private float _routeDiscoveryTimer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================
            // PHASE 1: Route Discovery (every 2 seconds)
            // =============================================================
            _routeDiscoveryTimer -= dt;
            if (_routeDiscoveryTimer <= 0f)
            {
                _routeDiscoveryTimer = RouteDiscoveryInterval;
                DiscoverRoutes(ref state, em, ecb);
            }

            // =============================================================
            // PHASE 2: Caravan Spawning
            // =============================================================
            SpawnCaravans(ref state, em, ecb, dt);
        }

        /// <summary>
        /// For each completed TradeHub, find the nearest same-faction Outpost
        /// and create/update a TradeRoute component.
        /// </summary>
        private void DiscoverRoutes(ref SystemState state, EntityManager em, EntityCommandBuffer ecb)
        {
            // Collect all completed Outpost positions and factions for lookup
            var outpostQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<OutpostTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );

            using var outpostEntities = outpostQuery.ToEntityArray(Allocator.Temp);
            using var outpostFactions = outpostQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var outpostTransforms = outpostQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Process each TradeHub
            foreach (var (hubTransform, hubFaction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradeHubTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                float3 hubPos = hubTransform.ValueRO.Position;
                Faction hubFac = hubFaction.ValueRO.Value;

                // Find nearest same-faction Outpost
                Entity nearestOutpost = Entity.Null;
                float nearestDistSq = float.MaxValue;

                for (int i = 0; i < outpostEntities.Length; i++)
                {
                    if (outpostFactions[i].Value != hubFac) continue;

                    float3 outpostPos = outpostTransforms[i].Position;
                    float distSq = math.distancesq(hubPos, outpostPos);

                    if (distSq < nearestDistSq)
                    {
                        nearestDistSq = distSq;
                        nearestOutpost = outpostEntities[i];
                    }
                }

                // Update or add TradeRoute component
                if (nearestOutpost != Entity.Null)
                {
                    float routeLength = math.sqrt(nearestDistSq);

                    if (em.HasComponent<TradeRoute>(entity))
                    {
                        // Update route target and length only.
                        // Do NOT touch ActiveCaravans or SpawnTimer -- SpawnCaravans manages those.
                        var existing = em.GetComponentData<TradeRoute>(entity);
                        existing.OutpostEntity = nearestOutpost;
                        existing.RouteLength = routeLength;
                        existing.RouteValid = 1;
                        em.SetComponentData(entity, existing);
                    }
                    else
                    {
                        // First-time route setup (deferred via ECB since adding new component)
                        ecb.AddComponent(entity, new TradeRoute
                        {
                            OutpostEntity = nearestOutpost,
                            RouteLength = routeLength,
                            ActiveCaravans = 0,
                            SpawnTimer = SpawnInterval,
                            RouteValid = 1
                        });
                    }
                }
                else
                {
                    // No outpost found
                    if (em.HasComponent<TradeRoute>(entity))
                    {
                        var existing = em.GetComponentData<TradeRoute>(entity);
                        existing.RouteValid = 0;
                        em.SetComponentData(entity, existing);
                    }
                    else
                    {
                        ecb.AddComponent(entity, new TradeRoute
                        {
                            OutpostEntity = Entity.Null,
                            RouteLength = 0f,
                            ActiveCaravans = 0,
                            SpawnTimer = SpawnInterval,
                            RouteValid = 0
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Tick spawn timers on TradeHubs with valid routes.
        /// Spawn caravan + escort when timer expires and under max count.
        /// </summary>
        private void SpawnCaravans(ref SystemState state, EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (route, hubTransform, hubFaction, hubEntity) in SystemAPI
                .Query<RefRW<TradeRoute>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradeHubTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var r = ref route.ValueRW;

                // Skip invalid routes
                if (r.RouteValid == 0) continue;

                // Tick spawn timer
                r.SpawnTimer -= dt;

                if (r.SpawnTimer <= 0f && r.ActiveCaravans < MaxCaravansPerRoute)
                {
                    // Validate outpost still exists
                    if (!em.Exists(r.OutpostEntity) || !em.HasComponent<OutpostTag>(r.OutpostEntity))
                    {
                        r.RouteValid = 0;
                        r.SpawnTimer = SpawnInterval;
                        continue;
                    }

                    Faction faction = hubFaction.ValueRO.Value;
                    float3 hubPos = hubTransform.ValueRO.Position;
                    float3 outpostPos = em.GetComponentData<LocalTransform>(r.OutpostEntity).Position;

                    // Calculate max cargo for this route
                    float maxCargo = BaseIncome * (r.RouteLength / RouteLengthDivisor);

                    // Apply tariff bonus for long routes
                    if (r.RouteLength > TariffThreshold)
                    {
                        maxCargo *= (1f + TariffBonus);
                    }

                    // Spawn caravan at hub position (offset slightly)
                    float3 spawnPos = hubPos + new float3(2f, 0f, 2f);
                    Entity caravan = Caravan.Create(em, spawnPos, faction);

                    // Spawn escort at same position
                    Entity escort = CaravanEscortUnit.Create(em, spawnPos + new float3(1f, 0f, 0f), faction);

                    // Configure caravan state
                    em.SetComponentData(caravan, new CaravanState
                    {
                        Origin = hubEntity,
                        Destination = r.OutpostEntity,
                        CurrentCargo = maxCargo, // Start loaded with cargo
                        MaxCargo = maxCargo,
                        IsReturning = 0,
                        EscortEntity = escort
                    });

                    // Set caravan destination to outpost
                    em.SetComponentData(caravan, new DesiredDestination
                    {
                        Position = outpostPos,
                        Has = 1
                    });

                    // Configure escort
                    em.SetComponentData(escort, new CaravanEscort
                    {
                        CaravanEntity = caravan
                    });

                    // Set escort destination to outpost (will be updated by CaravanMovementSystem)
                    em.SetComponentData(escort, new DesiredDestination
                    {
                        Position = outpostPos,
                        Has = 1
                    });

                    // Update route state
                    r.ActiveCaravans++;
                    r.SpawnTimer = SpawnInterval;
                }
                else if (r.SpawnTimer <= 0f)
                {
                    // At max caravans, just reset timer
                    r.SpawnTimer = SpawnInterval;
                }
            }
        }
    }
}
