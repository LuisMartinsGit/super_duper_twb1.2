// File: Assets/Scripts/Systems/Economy/TradingPostSystem.cs
// Renamed internally to RunaiTradeHubSystem — manages the Runai trade network.
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Movement;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Manages the Runai trade network between TradeHubs, Halls, and Bazaars.
    ///
    /// Responsibilities:
    /// 1. Node Discovery: Tags completed trade buildings with TradeNodeTag.
    /// 2. Trader Spawning: Each TradeHub spawns 1 trader every 30s (faction max 30).
    /// 3. Patrol Spawning: All trade nodes spawn patrol soldiers (1 every 20s, cap 5 per trader).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TradingPostSystem : ISystem
    {
        private const float NodeDiscoveryInterval = 2f;
        private const float TraderSpawnInterval = 30f;
        private const float PatrolSpawnInterval = 20f;
        private const int MaxTradersPerFaction = 30;
        private const int PatrolsPerTrader = 5;
        private const int DefaultPatrolCap = 5; // For Hall/Bazaar (non-hub nodes)

        private float _discoveryTimer;
        private uint _randomSeed;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _randomSeed = 42;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // =============================================================
            // PHASE 1: Node Discovery (every 2 seconds)
            // =============================================================
            _discoveryTimer -= dt;
            if (_discoveryTimer <= 0f)
            {
                _discoveryTimer = NodeDiscoveryInterval;
                DiscoverTradeNodes(em);
            }

            // =============================================================
            // PHASE 2: Trader Spawning (TradeHubs only)
            // =============================================================
            SpawnTraders(ref state, em, dt);

            // =============================================================
            // PHASE 3: Patrol Spawning (all trade nodes)
            // =============================================================
            SpawnPatrolsFromHubs(ref state, em, dt);
            SpawnPatrolsFromNodes(ref state, em, dt);
        }

        /// <summary>
        /// Tag completed TradeHubs, Bazaars, and Halls of Runai factions with TradeNodeTag.
        /// Also add spawner components where missing.
        /// </summary>
        private void DiscoverTradeNodes(EntityManager em)
        {
            // --- TradeHubs ---
            DiscoverBuildingType<TradeHubTag>(em, addHubSpawner: true);

            // --- Bazaars ---
            DiscoverBuildingType<BazaarTag>(em, addHubSpawner: false);

            // --- Halls (only Runai factions) ---
            DiscoverHalls(em);
        }

        private void DiscoverBuildingType<T>(EntityManager em, bool addHubSpawner) where T : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<UnderConstruction>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                // Only Runai factions participate in trade network
                if (FactionColors.GetFactionCulture(factions[i].Value) != Cultures.Runai) continue;

                Entity e = entities[i];

                if (!em.HasComponent<TradeNodeTag>(e))
                    em.AddComponent<TradeNodeTag>(e);

                if (addHubSpawner && !em.HasComponent<TradeHubSpawner>(e))
                {
                    em.AddComponentData(e, new TradeHubSpawner
                    {
                        TraderTimer = TraderSpawnInterval,
                        PatrolTimer = PatrolSpawnInterval,
                        TradersSpawned = 0,
                        PatrolsSpawned = 0
                    });
                }
                else if (!addHubSpawner && !em.HasComponent<TradeNodePatrolSpawner>(e))
                {
                    em.AddComponentData(e, new TradeNodePatrolSpawner
                    {
                        PatrolTimer = PatrolSpawnInterval,
                        PatrolsSpawned = 0,
                        PatrolCap = DefaultPatrolCap
                    });
                }
            }
        }

        private void DiscoverHalls(EntityManager em)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<UnderConstruction>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (FactionColors.GetFactionCulture(factions[i].Value) != Cultures.Runai) continue;

                Entity e = entities[i];

                if (!em.HasComponent<TradeNodeTag>(e))
                    em.AddComponent<TradeNodeTag>(e);

                if (!em.HasComponent<TradeNodePatrolSpawner>(e))
                {
                    em.AddComponentData(e, new TradeNodePatrolSpawner
                    {
                        PatrolTimer = PatrolSpawnInterval,
                        PatrolsSpawned = 0,
                        PatrolCap = DefaultPatrolCap
                    });
                }
            }
        }

        /// <summary>
        /// Spawn traders from TradeHubs. Each hub spawns 1 trader every 30s, faction max 30.
        /// </summary>
        private void SpawnTraders(ref SystemState state, EntityManager em, float dt)
        {
            // Count active traders per faction
            var factionTraderCount = new NativeHashMap<int, int>(8, Allocator.Temp);
            foreach (var (traderFaction, _) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<RunaiTraderState>>()
                .WithAll<CaravanTag>())
            {
                int fKey = (int)traderFaction.ValueRO.Value;
                factionTraderCount.TryGetValue(fKey, out int count);
                factionTraderCount[fKey] = count + 1;
            }

            foreach (var (spawner, transform, faction, entity) in SystemAPI
                .Query<RefRW<TradeHubSpawner>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradeHubTag, TradeNodeTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var s = ref spawner.ValueRW;
                s.TraderTimer -= dt;

                if (s.TraderTimer > 0f) continue;

                // Check faction cap
                int fKey = (int)faction.ValueRO.Value;
                factionTraderCount.TryGetValue(fKey, out int currentCount);

                if (currentCount >= MaxTradersPerFaction)
                {
                    s.TraderTimer = TraderSpawnInterval; // Reset and try later
                    continue;
                }

                // Find a random destination
                if (!TryPickRandomNode(em, faction.ValueRO.Value, entity, out Entity dest, out float3 destPos))
                {
                    s.TraderTimer = 5f; // Retry sooner if no destinations
                    continue;
                }

                // Spawn trader
                float3 spawnPos = transform.ValueRO.Position + new float3(2f, 0f, 0f);
                Entity trader = Caravan.Create(em, spawnPos, faction.ValueRO.Value);

                em.AddComponentData(trader, new RunaiTraderState
                {
                    CurrentDest = dest,
                    AccumulatedSupplies = 0f,
                    AccumulatedCrystal = 0f,
                    PreviousPosition = spawnPos
                });

                em.SetComponentData(trader, new DesiredDestination
                {
                    Position = destPos,
                    Has = 1
                });

                s.TradersSpawned++;
                s.TraderTimer = TraderSpawnInterval;

                factionTraderCount[fKey] = currentCount + 1;

                FlowFieldManager.Instance?.RequestFlowField(destPos);
            }

            factionTraderCount.Dispose();
        }

        /// <summary>
        /// Spawn patrols from TradeHub nodes.
        /// </summary>
        private void SpawnPatrolsFromHubs(ref SystemState state, EntityManager em, float dt)
        {
            foreach (var (spawner, transform, faction, entity) in SystemAPI
                .Query<RefRW<TradeHubSpawner>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradeHubTag, TradeNodeTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var s = ref spawner.ValueRW;
                s.PatrolTimer -= dt;

                if (s.PatrolTimer > 0f) continue;

                int patrolCap = s.TradersSpawned * PatrolsPerTrader;
                if (patrolCap <= 0) patrolCap = DefaultPatrolCap; // At least some patrols
                if (s.PatrolsSpawned >= patrolCap)
                {
                    s.PatrolTimer = PatrolSpawnInterval;
                    continue;
                }

                SpawnPatrolUnit(em, entity, transform.ValueRO.Position, faction.ValueRO.Value);
                s.PatrolsSpawned++;
                s.PatrolTimer = PatrolSpawnInterval;
            }
        }

        /// <summary>
        /// Spawn patrols from Hall/Bazaar trade nodes.
        /// </summary>
        private void SpawnPatrolsFromNodes(ref SystemState state, EntityManager em, float dt)
        {
            foreach (var (spawner, transform, faction, entity) in SystemAPI
                .Query<RefRW<TradeNodePatrolSpawner>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradeNodeTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var s = ref spawner.ValueRW;
                s.PatrolTimer -= dt;

                if (s.PatrolTimer > 0f) continue;

                if (s.PatrolsSpawned >= s.PatrolCap)
                {
                    s.PatrolTimer = PatrolSpawnInterval;
                    continue;
                }

                SpawnPatrolUnit(em, entity, transform.ValueRO.Position, faction.ValueRO.Value);
                s.PatrolsSpawned++;
                s.PatrolTimer = PatrolSpawnInterval;
            }
        }

        /// <summary>
        /// Spawn a patrol unit at the given building, with waypoints to 2 random trade nodes.
        /// </summary>
        private void SpawnPatrolUnit(EntityManager em, Entity sourceBuilding, float3 sourcePos, Faction faction)
        {
            // Pick 2 random destination nodes for patrol waypoints
            if (!TryPickRandomNode(em, faction, sourceBuilding, out Entity destA, out float3 posA))
                return;

            if (!TryPickRandomNode(em, faction, destA, out Entity destB, out float3 posB))
                posB = sourcePos; // Fallback: patrol between source and destA

            Entity patrol = TradePatrol.Create(em, sourcePos, faction, sourceBuilding, destA, sourcePos, posA);

            // Update waypoints to include destB for variety
            if (em.HasBuffer<PatrolWaypoint>(patrol))
            {
                var waypoints = em.GetBuffer<PatrolWaypoint>(patrol);
                waypoints.Clear();
                waypoints.Add(new PatrolWaypoint { Position = sourcePos, WaitSeconds = 0f });
                waypoints.Add(new PatrolWaypoint { Position = posA, WaitSeconds = 0f });
                waypoints.Add(new PatrolWaypoint { Position = posB, WaitSeconds = 0f });
            }
        }

        /// <summary>
        /// Pick a random TradeNodeTag entity of the same faction, excluding a specific entity.
        /// </summary>
        private bool TryPickRandomNode(EntityManager em, Faction faction, Entity exclude,
            out Entity node, out float3 position)
        {
            node = Entity.Null;
            position = float3.zero;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TradeNodeTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Build candidates list
            var candidates = new NativeList<int>(entities.Length, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (entities[i] == exclude) continue;
                candidates.Add(i);
            }

            if (candidates.Length == 0)
            {
                candidates.Dispose();
                return false;
            }

            // Pick random
            _randomSeed = _randomSeed * 1103515245 + 12345; // LCG
            int pick = (int)(_randomSeed % (uint)candidates.Length);
            int idx = candidates[pick];

            node = entities[idx];
            position = transforms[idx].Position;

            candidates.Dispose();
            return true;
        }
    }
}
