// File: Assets/GameData/TechTree/Buildings/Runai/TradeHub/TradingPostSystem.cs
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
    ///
    /// All entity creation is deferred outside SystemAPI.Query loops to avoid structural change errors.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TradingPostSystem : ISystem
    {
        private const float NodeDiscoveryInterval = 2f;
        private const float TraderSpawnInterval = 30f;
        private const float PatrolSpawnInterval = 10f;
        private const int MaxTradersPerFaction = 30;
        private const int PatrolsPerTrader = 5;
        private const int DefaultPatrolCap = 5; // For Hall/Bazaar (non-hub nodes)

        private float _discoveryTimer;
        private uint _randomSeed;

        private struct TraderSpawnRequest
        {
            public float3 SpawnPos;
            public Faction Faction;
            public Entity Dest;
            public float3 DestPos;
        }

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
            // PHASE 2: Trader Spawning (TradeHubs only) — collect then spawn
            // =============================================================
            SpawnTraders(ref state, em, dt);

            // =============================================================
            // PHASE 3 + 4: Patrol spawning + follow disabled (spec refinement #3).
            // Caravans replaced the separate patrol entity type — they fight
            // back natively (Damage + Target components on the Caravan).
            // The old SpawnPatrolsFromHubs / SpawnPatrolsFromNodes /
            // UpdatePatrolFollowers helpers stay in this file as dead code
            // for now; a future cleanup pass can delete them along with the
            // TradePatrol entity factory.
            // =============================================================
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
        /// Collects requests during iteration, spawns after loop completes.
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

            // Collect spawn requests (no structural changes during iteration)
            var traderRequests = new NativeList<TraderSpawnRequest>(8, Allocator.Temp);
            // Also collect which hub entities need TradersSpawned incremented
            var hubIncrements = new NativeList<Entity>(8, Allocator.Temp);

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
                    s.TraderTimer = TraderSpawnInterval;
                    continue;
                }

                float3 spawnPos = transform.ValueRO.Position + new float3(2f, 0f, 0f);

                // Find a random destination
                if (!TryPickRandomNode(em, faction.ValueRO.Value, entity, spawnPos, out Entity dest, out float3 destPos))
                {
                    s.TraderTimer = 5f;
                    continue;
                }

                traderRequests.Add(new TraderSpawnRequest
                {
                    SpawnPos = spawnPos,
                    Faction = faction.ValueRO.Value,
                    Dest = dest,
                    DestPos = destPos
                });

                s.TradersSpawned++;
                s.TraderTimer = TraderSpawnInterval;
                factionTraderCount[fKey] = currentCount + 1;
            }

            // Now spawn traders outside the iteration
            for (int i = 0; i < traderRequests.Length; i++)
            {
                var req = traderRequests[i];
                Entity trader = Caravan.Create(em, req.SpawnPos, req.Faction);

                em.AddComponentData(trader, new RunaiTraderState
                {
                    CurrentDest = req.Dest,
                    AccumulatedSupplies = 0f,
                    AccumulatedVeilstone = 0f,
                    PreviousPosition = req.SpawnPos
                });

                em.SetComponentData(trader, new DesiredDestination
                {
                    Position = req.DestPos,
                    Has = 1
                });

                // PR3 — pre-warm removed; NavMeshPathRequestSystem handles it lazily.
            }

            traderRequests.Dispose();
            hubIncrements.Dispose();
            factionTraderCount.Dispose();
        }

        // Patrol-spawning helpers (SpawnPatrolsFromHubs / SpawnPatrolsFromNodes /
        // UpdatePatrolFollowers / SpawnPatrolUnit) were deleted in slice 28
        // alongside their callsites in OnUpdate. Spec refinement #3 collapsed
        // caravans + patrols into a single combat-capable trader entity, so
        // these helpers had no live callers. The PatrolFollowUpdate +
        // PatrolSpawnRequest deferred-request structs went with them.
        /// <summary>
        /// Pick a random TradeNodeTag entity of the same faction, excluding a specific entity.
        /// Returns a position offset 3 units from the building center toward the approaching unit.
        /// </summary>
        private bool TryPickRandomNode(EntityManager em, Faction faction, Entity exclude,
            float3 fromPos, out Entity node, out float3 position)
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

            _randomSeed = _randomSeed * 1103515245 + 12345;
            int pick = (int)(_randomSeed % (uint)candidates.Length);
            int idx = candidates[pick];

            node = entities[idx];
            float3 buildingPos = transforms[idx].Position;

            // Offset 3 units from building center in the direction the unit is
            // approaching from. Earlier missing braces meant the +X fallback ran
            // unconditionally, so traders/patrols always approached every
            // building from the +X side regardless of their actual route —
            // visible as clustering on one face of trade hubs. (task-056 / MB-5)
            float3 dir = fromPos - buildingPos;
            dir.y = 0f;
            float len = math.length(dir);
            if (len > 0.01f)
                position = buildingPos + (dir / len) * 3f;
            else
                position = buildingPos + new float3(3f, 0f, 0f);

            candidates.Dispose();
            return true;
        }
    }
}