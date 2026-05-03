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

        // Deferred request structs
        private struct PatrolFollowUpdate
        {
            public Entity Patrol;
            public float3 TargetPos;
        }

        private struct TraderSpawnRequest
        {
            public float3 SpawnPos;
            public Faction Faction;
            public Entity Dest;
            public float3 DestPos;
        }

        private struct PatrolSpawnRequest
        {
            public Entity SourceBuilding;
            public float3 SourcePos;
            public Faction Faction;
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
            // PHASE 3: Patrol Spawning (all trade nodes) — collect then spawn
            // =============================================================
            SpawnPatrolsFromHubs(ref state, em, dt);
            SpawnPatrolsFromNodes(ref state, em, dt);

            // =============================================================
            // PHASE 4: Patrol Follow — patrols track nearest same-faction caravan
            // =============================================================
            UpdatePatrolFollowers(ref state, em);
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
                    AccumulatedCrystal = 0f,
                    PreviousPosition = req.SpawnPos
                });

                em.SetComponentData(trader, new DesiredDestination
                {
                    Position = req.DestPos,
                    Has = 1
                });

                FlowFieldManager.Instance?.RequestFlowField(req.DestPos);
            }

            traderRequests.Dispose();
            hubIncrements.Dispose();
            factionTraderCount.Dispose();
        }

        /// <summary>
        /// Spawn patrols from TradeHub nodes. Collects requests, spawns after loop.
        ///
        /// Fix #233: the old code tracked cumulative PatrolsSpawned and never
        /// decremented on death, so once the cap was hit a spawner would
        /// permanently stop producing patrols even after its entire patrol
        /// pool had been wiped out. Instead, count LIVE patrols per source
        /// building each tick via TradePatrolData.PostA and compare that
        /// against the cap.
        /// </summary>
        private void SpawnPatrolsFromHubs(ref SystemState state, EntityManager em, float dt)
        {
            var patrolRequests = new NativeList<PatrolSpawnRequest>(8, Allocator.Temp);

            // Count live patrols per source building (Fix #233)
            var liveCount = new NativeParallelHashMap<Entity, int>(16, Allocator.Temp);
            foreach (var patrolData in SystemAPI.Query<RefRO<TradePatrolData>>())
            {
                var src = patrolData.ValueRO.PostA;
                if (liveCount.TryGetValue(src, out int c)) liveCount[src] = c + 1;
                else liveCount.TryAdd(src, 1);
            }

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
                if (patrolCap <= 0) patrolCap = DefaultPatrolCap;

                int current = liveCount.TryGetValue(entity, out int lc) ? lc : 0;
                s.PatrolsSpawned = current; // keep field in sync for telemetry
                if (current >= patrolCap)
                {
                    s.PatrolTimer = PatrolSpawnInterval;
                    continue;
                }

                patrolRequests.Add(new PatrolSpawnRequest
                {
                    SourceBuilding = entity,
                    SourcePos = transform.ValueRO.Position,
                    Faction = faction.ValueRO.Value
                });

                s.PatrolTimer = PatrolSpawnInterval;
            }

            // Spawn patrols outside the iteration
            for (int i = 0; i < patrolRequests.Length; i++)
            {
                var req = patrolRequests[i];
                SpawnPatrolUnit(em, req.SourceBuilding, req.SourcePos, req.Faction);
            }

            patrolRequests.Dispose();
            liveCount.Dispose();
        }

        /// <summary>
        /// Spawn patrols from Hall/Bazaar trade nodes. Collects requests, spawns after loop.
        /// Fix #233: live-patrol count (see SpawnPatrolsFromHubs).
        /// </summary>
        private void SpawnPatrolsFromNodes(ref SystemState state, EntityManager em, float dt)
        {
            var patrolRequests = new NativeList<PatrolSpawnRequest>(8, Allocator.Temp);

            var liveCount = new NativeParallelHashMap<Entity, int>(16, Allocator.Temp);
            foreach (var patrolData in SystemAPI.Query<RefRO<TradePatrolData>>())
            {
                var src = patrolData.ValueRO.PostA;
                if (liveCount.TryGetValue(src, out int c)) liveCount[src] = c + 1;
                else liveCount.TryAdd(src, 1);
            }

            foreach (var (spawner, transform, faction, entity) in SystemAPI
                .Query<RefRW<TradeNodePatrolSpawner>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradeNodeTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var s = ref spawner.ValueRW;
                s.PatrolTimer -= dt;

                if (s.PatrolTimer > 0f) continue;

                int current = liveCount.TryGetValue(entity, out int lc) ? lc : 0;
                s.PatrolsSpawned = current;
                if (current >= s.PatrolCap)
                {
                    s.PatrolTimer = PatrolSpawnInterval;
                    continue;
                }

                patrolRequests.Add(new PatrolSpawnRequest
                {
                    SourceBuilding = entity,
                    SourcePos = transform.ValueRO.Position,
                    Faction = faction.ValueRO.Value
                });

                s.PatrolTimer = PatrolSpawnInterval;
            }

            // Spawn patrols outside the iteration
            for (int i = 0; i < patrolRequests.Length; i++)
            {
                var req = patrolRequests[i];
                SpawnPatrolUnit(em, req.SourceBuilding, req.SourcePos, req.Faction);
            }

            patrolRequests.Dispose();
            liveCount.Dispose();
        }

        private const float FollowRetargetDistSq = 9f; // Re-target when > 3 units from nearest caravan

        /// <summary>
        /// Updates each CaravanFollowerTag patrol to follow the nearest same-faction caravan.
        /// Re-targets when idle OR when the nearest caravan is more than 3 units away.
        /// </summary>
        private void UpdatePatrolFollowers(ref SystemState state, EntityManager em)
        {
            // Collect all caravan positions and factions
            var caravanPositions = new NativeList<float3>(32, Allocator.Temp);
            var caravanFactions = new NativeList<int>(32, Allocator.Temp);

            foreach (var (transform, faction) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<CaravanTag, RunaiTraderState>())
            {
                caravanPositions.Add(transform.ValueRO.Position);
                caravanFactions.Add((int)faction.ValueRO.Value);
            }

            if (caravanPositions.Length == 0)
            {
                caravanPositions.Dispose();
                caravanFactions.Dispose();
                return;
            }

            // Collect patrol update requests
            var updates = new NativeList<PatrolFollowUpdate>(16, Allocator.Temp);

            foreach (var (transform, faction, dd, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<DesiredDestination>>()
                .WithAll<CaravanFollowerTag>()
                .WithEntityAccess())
            {
                float3 patrolPos = transform.ValueRO.Position;
                int fac = (int)faction.ValueRO.Value;

                // Find nearest same-faction caravan
                float bestDistSq = float.MaxValue;
                float3 bestPos = patrolPos;
                bool found = false;

                for (int i = 0; i < caravanPositions.Length; i++)
                {
                    if (caravanFactions[i] != fac) continue;
                    float dSq = math.distancesq(patrolPos, caravanPositions[i]);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestPos = caravanPositions[i];
                        found = true;
                    }
                }

                if (!found) continue;

                // Re-target if idle (arrived) or if nearest caravan is far away
                bool idle = dd.ValueRO.Has == 0;
                bool caravanFar = bestDistSq > FollowRetargetDistSq;

                if (idle || caravanFar)
                {
                    updates.Add(new PatrolFollowUpdate { Patrol = entity, TargetPos = bestPos });
                }
            }

            // Apply destination updates outside query
            for (int i = 0; i < updates.Length; i++)
            {
                var u = updates[i];
                if (!em.Exists(u.Patrol)) continue;
                em.SetComponentData(u.Patrol, new DesiredDestination { Position = u.TargetPos, Has = 1 });
            }

            updates.Dispose();
            caravanPositions.Dispose();
            caravanFactions.Dispose();
        }

        /// <summary>
        /// Spawn a patrol unit at the given building, with waypoints to 2 random trade nodes.
        /// Must be called OUTSIDE of SystemAPI.Query loops.
        /// </summary>
        private void SpawnPatrolUnit(EntityManager em, Entity sourceBuilding, float3 sourcePos, Faction faction)
        {
            // destA is only needed for TradePatrol.Create signature (PostB endpoint)
            if (!TryPickRandomNode(em, faction, sourceBuilding, sourcePos, out Entity destA, out float3 posA))
                return;

            Entity patrol = TradePatrol.Create(em, sourcePos, faction, sourceBuilding, destA, sourcePos, posA);

            // Clear waypoints — patrols follow caravans via CaravanFollowerTag, not PatrolSystem
            if (em.HasBuffer<PatrolWaypoint>(patrol))
                em.GetBuffer<PatrolWaypoint>(patrol).Clear();

            // Set destination to idle so UpdatePatrolFollowers picks up nearest caravan next frame
            em.SetComponentData(patrol, new DesiredDestination { Position = float3.zero, Has = 0 });
        }

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
