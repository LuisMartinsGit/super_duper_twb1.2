// File: Assets/Scripts/Systems/Economy/TraderMovementSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Movement;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Handles trader chain traversal between numbered trading posts.
    ///
    /// On arrival at a post:
    /// 1. Deposits cargo to faction bank
    /// 2. Finds next post in chain (forward or backward)
    /// 3. Loads new cargo and sets destination
    /// 4. Reverses direction at chain endpoints
    ///
    /// If destination post is destroyed, skips to next valid post.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct TraderMovementSystem : ISystem
    {
        private const float BaseIncome = 25f;
        private const float RouteLengthDivisor = 30f;

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
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (trader, dd, faction, transform, entity) in SystemAPI
                .Query<RefRW<TraderState>, RefRW<DesiredDestination>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                ref var ts = ref trader.ValueRW;
                ref var dest = ref dd.ValueRW;

                // Validate destination post still exists
                if (ts.CurrentDestPost != Entity.Null && !em.Exists(ts.CurrentDestPost))
                {
                    // Destination destroyed — find next valid post in current direction
                    if (!TryFindNextPost(em, faction.ValueRO.Value, ts.DestPostNumber, ts.IsForward == 1, out var nextPost, out var nextNum, out var nextPos))
                    {
                        // Try reverse direction
                        if (!TryFindNextPost(em, faction.ValueRO.Value, ts.DestPostNumber, ts.IsForward == 0, out nextPost, out nextNum, out nextPos))
                        {
                            // No posts left — kill trader
                            var hp = em.GetComponentData<Health>(entity);
                            hp.Value = 0;
                            em.SetComponentData(entity, hp);
                            continue;
                        }
                        ts.IsForward = ts.IsForward == 1 ? (byte)0 : (byte)1;
                    }

                    ts.CurrentDestPost = nextPost;
                    ts.DestPostNumber = nextNum;
                    dest.Position = nextPos;
                    dest.Has = 1;
                    continue;
                }

                // Only process arrival when movement system says we've arrived
                if (dest.Has != 0) continue;

                // === ARRIVED AT POST ===
                // Deposit cargo
                int suppliesDeposit = (int)ts.CurrentCargo;
                if (suppliesDeposit > 0)
                {
                    FactionEconomy.Add(em, faction.ValueRO.Value, Cost.Of(supplies: suppliesDeposit));
                }
                ts.CurrentCargo = 0f;

                // Find next post in chain
                int currentNum = ts.DestPostNumber;
                bool forward = ts.IsForward == 1;

                // Sect trade income multiplier
                float tradeMult = 1f;
                if (FactionSectState.Instance != null)
                    tradeMult = FactionSectState.Instance.GetMultipliers(faction.ValueRO.Value).TradeIncome;

                if (TryFindNextPost(em, faction.ValueRO.Value, currentNum, forward, out var next, out var nNum, out var nPos))
                {
                    // Continue in same direction
                    float dist = math.distance(transform.ValueRO.Position, nPos);
                    ts.CurrentDestPost = next;
                    ts.DestPostNumber = nNum;
                    ts.CurrentCargo = BaseIncome * (dist / RouteLengthDivisor) * tradeMult;
                    ts.MaxCargo = ts.CurrentCargo;
                    dest.Position = nPos;
                    dest.Has = 1;
                }
                else
                {
                    // Reached end of chain — reverse direction
                    ts.IsForward = forward ? (byte)0 : (byte)1;

                    if (TryFindNextPost(em, faction.ValueRO.Value, currentNum, !forward, out next, out nNum, out nPos))
                    {
                        float dist = math.distance(transform.ValueRO.Position, nPos);
                        ts.CurrentDestPost = next;
                        ts.DestPostNumber = nNum;
                        ts.CurrentCargo = BaseIncome * (dist / RouteLengthDivisor) * tradeMult;
                        ts.MaxCargo = ts.CurrentCargo;
                        dest.Position = nPos;
                        dest.Has = 1;
                    }
                    else
                    {
                        // Only one post exists — just wait (no movement)
                        dest.Has = 0;
                    }
                }

                FlowFieldManager.Instance?.RequestFlowField(dest.Position);
            }
        }

        /// <summary>
        /// Find the next valid post in the chain from currentPostNumber in the given direction.
        /// Forward = find smallest PostNumber > current. Backward = find largest PostNumber < current.
        /// </summary>
        private bool TryFindNextPost(EntityManager em, Faction faction, int currentPostNumber, bool forward,
            out Entity nextPost, out int nextNum, out float3 nextPos)
        {
            nextPost = Entity.Null;
            nextNum = 0;
            nextPos = float3.zero;

            using var posts = _postQuery.ToEntityArray(Allocator.Temp);
            using var postData = _postQuery.ToComponentDataArray<TradingPostData>(Allocator.Temp);
            using var postFactions = _postQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var postTransforms = _postQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int bestNum = forward ? int.MaxValue : int.MinValue;

            for (int i = 0; i < posts.Length; i++)
            {
                if (postFactions[i].Value != faction) continue;
                int num = postData[i].PostNumber;
                if (num == 0) continue;

                if (forward && num > currentPostNumber && num < bestNum)
                {
                    bestNum = num;
                    nextPost = posts[i];
                    nextPos = postTransforms[i].Position;
                }
                else if (!forward && num < currentPostNumber && num > bestNum)
                {
                    bestNum = num;
                    nextPost = posts[i];
                    nextPos = postTransforms[i].Position;
                }
            }

            if (nextPost != Entity.Null)
            {
                nextNum = bestNum;
                return true;
            }
            return false;
        }
    }
}
