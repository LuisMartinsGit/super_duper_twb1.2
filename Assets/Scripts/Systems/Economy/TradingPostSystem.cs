// File: Assets/Scripts/Systems/Economy/TradingPostSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Manages the Runai trading post chain system.
    ///
    /// Responsibilities:
    /// 1. Post Numbering: Assigns sequential PostNumber to newly completed posts.
    /// 2. Lane Discovery: Every 2s, establishes TradeLane between consecutive posts.
    /// 3. Trader Spawning: 1st trader immediately on lane creation, 2nd after 4 minutes.
    /// 4. Patrol Spawning: 5 free patrol units per lane on creation.
    ///
    /// Posts are numbered in build order (1-based). Gaps stay on destruction.
    /// Traders traverse the full chain: Post 1→2→...→N then reverse.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TradingPostSystem : ISystem
    {
        private const float LaneDiscoveryInterval = 2f;
        private const float SecondTraderDelay = 240f; // 4 minutes
        private const int MaxTradersPerLane = 2;
        private const int PatrolUnitsPerLane = 5;
        private const float BaseIncome = 25f;
        private const float RouteLengthDivisor = 30f;

        private float _discoveryTimer;
        private EntityQuery _postQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _postQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<TradingPostTag>(),
                ComponentType.ReadOnly<TradingPostData>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================
            // PHASE 1: Assign PostNumber to newly completed posts (PostNumber == 0)
            // =============================================================
            AssignPostNumbers(em, ecb);

            // =============================================================
            // PHASE 2: Lane Discovery (every 2 seconds)
            // =============================================================
            _discoveryTimer -= dt;
            if (_discoveryTimer <= 0f)
            {
                _discoveryTimer = LaneDiscoveryInterval;
                DiscoverLanes(em, ecb);
            }

            // =============================================================
            // PHASE 3: Trader Spawning (per frame)
            // =============================================================
            SpawnTraders(ref state, em, ecb, dt);

            // =============================================================
            // PHASE 4: Patrol Unit Spawning (check each lane)
            // =============================================================
            SpawnPatrolUnits(ref state, em, ecb);
        }

        /// <summary>
        /// Assign sequential PostNumber to completed posts that still have PostNumber == 0.
        /// </summary>
        private void AssignPostNumbers(EntityManager em, EntityCommandBuffer ecb)
        {
            // Find max existing post number across all factions
            // (each faction has its own numbering, so we need per-faction max)
            using var allPosts = _postQuery.ToEntityArray(Allocator.Temp);
            using var allData = _postQuery.ToComponentDataArray<TradingPostData>(Allocator.Temp);
            using var allFactions = _postQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Build per-faction max post number
            var factionMax = new NativeHashMap<int, int>(8, Allocator.Temp);
            for (int i = 0; i < allPosts.Length; i++)
            {
                int fKey = (int)allFactions[i].Value;
                int num = allData[i].PostNumber;
                if (!factionMax.TryGetValue(fKey, out int curMax) || num > curMax)
                    factionMax[fKey] = num;
            }

            // Assign numbers to posts with PostNumber == 0
            for (int i = 0; i < allPosts.Length; i++)
            {
                if (allData[i].PostNumber != 0) continue;

                int fKey = (int)allFactions[i].Value;
                factionMax.TryGetValue(fKey, out int curMax);
                int newNumber = curMax + 1;
                factionMax[fKey] = newNumber;

                em.SetComponentData(allPosts[i], new TradingPostData { PostNumber = newNumber });
            }

            factionMax.Dispose();
        }

        /// <summary>
        /// For each faction, establish TradeLane components between consecutive numbered posts.
        /// </summary>
        private void DiscoverLanes(EntityManager em, EntityCommandBuffer ecb)
        {
            using var posts = _postQuery.ToEntityArray(Allocator.Temp);
            using var postData = _postQuery.ToComponentDataArray<TradingPostData>(Allocator.Temp);
            using var postFactions = _postQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Group posts by faction, sorted by PostNumber
            // Simple approach: for each post, find the next-higher-numbered post of same faction
            for (int i = 0; i < posts.Length; i++)
            {
                if (postData[i].PostNumber == 0) continue;

                Faction fac = postFactions[i].Value;
                int myNum = postData[i].PostNumber;

                // Find the post with the smallest PostNumber > myNum for same faction
                Entity nextPost = Entity.Null;
                int nextNum = int.MaxValue;

                for (int j = 0; j < posts.Length; j++)
                {
                    if (j == i) continue;
                    if (postFactions[j].Value != fac) continue;
                    int jNum = postData[j].PostNumber;
                    if (jNum > myNum && jNum < nextNum)
                    {
                        nextNum = jNum;
                        nextPost = posts[j];
                    }
                }

                if (nextPost != Entity.Null)
                {
                    // Ensure TradeLane exists on this post
                    if (em.HasComponent<TradeLane>(posts[i]))
                    {
                        var lane = em.GetComponentData<TradeLane>(posts[i]);
                        lane.NextPost = nextPost;
                        lane.LaneValid = 1;
                        em.SetComponentData(posts[i], lane);
                    }
                    else
                    {
                        ecb.AddComponent(posts[i], new TradeLane
                        {
                            NextPost = nextPost,
                            ActiveTraders = 0,
                            SecondTraderTimer = SecondTraderDelay,
                            PatrolUnitsSpawned = 0,
                            LaneValid = 1
                        });
                    }
                }
                else
                {
                    // This is the highest-numbered post — no outgoing lane
                    if (em.HasComponent<TradeLane>(posts[i]))
                    {
                        var lane = em.GetComponentData<TradeLane>(posts[i]);
                        lane.LaneValid = 0;
                        em.SetComponentData(posts[i], lane);
                    }
                }
            }
        }

        /// <summary>
        /// Spawn traders on lanes that need them (max 2 per lane).
        /// First trader spawns immediately, second after 4 minute timer.
        /// </summary>
        private void SpawnTraders(ref SystemState state, EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (lane, postData, transform, faction, entity) in SystemAPI
                .Query<RefRW<TradeLane>, RefRO<TradingPostData>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradingPostTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var l = ref lane.ValueRW;
                if (l.LaneValid == 0) continue;

                // Validate next post still exists
                if (!em.Exists(l.NextPost) || !em.HasComponent<TradingPostTag>(l.NextPost))
                {
                    l.LaneValid = 0;
                    continue;
                }

                // Spawn first trader immediately if none exist
                if (l.ActiveTraders < 1)
                {
                    SpawnTrader(em, entity, l.NextPost, faction.ValueRO.Value,
                        transform.ValueRO.Position, postData.ValueRO.PostNumber);
                    l.ActiveTraders = 1;
                }

                // Tick second trader timer
                if (l.ActiveTraders < MaxTradersPerLane)
                {
                    l.SecondTraderTimer -= dt;
                    if (l.SecondTraderTimer <= 0f)
                    {
                        SpawnTrader(em, entity, l.NextPost, faction.ValueRO.Value,
                            transform.ValueRO.Position, postData.ValueRO.PostNumber);
                        l.ActiveTraders = 2;
                    }
                }
            }
        }

        /// <summary>
        /// Spawn a single trader at the given post, heading toward nextPost.
        /// </summary>
        private static void SpawnTrader(EntityManager em, Entity lanePost, Entity nextPost,
            Faction faction, float3 spawnPos, int originPostNumber)
        {
            float3 destPos = em.GetComponentData<LocalTransform>(nextPost).Position;
            int destNum = em.GetComponentData<TradingPostData>(nextPost).PostNumber;

            float dist = math.distance(spawnPos, destPos);
            float maxCargo = BaseIncome * (dist / RouteLengthDivisor);

            Entity trader = Caravan.Create(em, spawnPos + new float3(2f, 0f, 0f), faction);

            // Replace CaravanState with TraderState
            if (em.HasComponent<CaravanState>(trader))
                em.RemoveComponent<CaravanState>(trader);

            em.AddComponentData(trader, new TraderState
            {
                CurrentDestPost = nextPost,
                CurrentCargo = maxCargo,
                MaxCargo = maxCargo,
                IsForward = 1,
                DestPostNumber = destNum,
                OwnerLanePost = lanePost
            });

            em.SetComponentData(trader, new DesiredDestination
            {
                Position = destPos,
                Has = 1
            });
        }

        /// <summary>
        /// Spawn patrol units for lanes that don't have their full complement yet.
        /// </summary>
        private void SpawnPatrolUnits(ref SystemState state, EntityManager em, EntityCommandBuffer ecb)
        {
            foreach (var (lane, transform, faction, entity) in SystemAPI
                .Query<RefRW<TradeLane>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TradingPostTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var l = ref lane.ValueRW;
                if (l.LaneValid == 0) continue;
                if (l.PatrolUnitsSpawned >= PatrolUnitsPerLane) continue;

                if (!em.Exists(l.NextPost) || !em.HasComponent<LocalTransform>(l.NextPost))
                    continue;

                float3 posA = transform.ValueRO.Position;
                float3 posB = em.GetComponentData<LocalTransform>(l.NextPost).Position;
                Faction fac = faction.ValueRO.Value;

                // Spawn remaining patrol units
                int toSpawn = PatrolUnitsPerLane - l.PatrolUnitsSpawned;
                for (int i = 0; i < toSpawn; i++)
                {
                    // Spread units along the lane
                    float t = (i + 1f) / (toSpawn + 1f);
                    float3 spawnPos = math.lerp(posA, posB, t);

                    Entity patrol = TradePatrol.Create(em, spawnPos, fac, entity, l.NextPost, posA, posB);
                    // TradePatrol.Create handles all components including PatrolWaypoint buffer
                }
                l.PatrolUnitsSpawned = PatrolUnitsPerLane;
            }
        }
    }
}
