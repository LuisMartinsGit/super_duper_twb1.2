// File: Assets/Scripts/Systems/Economy/CaravanMovementSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Manages caravan travel between TradeHub and Outpost endpoints.
    ///
    /// Responsibilities:
    /// 1. Detects when caravans arrive at their destination (DesiredDestination.Has == 0)
    /// 2. Deposits cargo on arrival at Outpost (credits faction Supplies)
    /// 3. Reverses direction: Outpost -> TradeHub (empty) -> Outpost (loaded)
    /// 4. Validates that route endpoints still exist; destroys orphaned caravans
    /// 5. Updates escort DesiredDestination to follow caravan position
    ///
    /// Runs after MovementSystem so arrival detection works on the same frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Movement.MovementSystem))]
    public partial struct CaravanMovementSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================
            // PHASE 1: Process caravan arrival and cargo operations
            // =============================================================
            foreach (var (caravan, dd, faction, transform, entity) in SystemAPI
                .Query<RefRW<CaravanState>, RefRW<DesiredDestination>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                ref var cs = ref caravan.ValueRW;
                ref var dest = ref dd.ValueRW;

                // Validate that origin and destination still exist
                bool originValid = em.Exists(cs.Origin) && em.HasComponent<TradeHubTag>(cs.Origin);
                bool destValid = em.Exists(cs.Destination) && em.HasComponent<OutpostTag>(cs.Destination);

                if (!originValid || !destValid)
                {
                    // Route endpoint destroyed - kill caravan and escort
                    var health = em.GetComponentData<Health>(entity);
                    health.Value = 0;
                    ecb.SetComponent(entity, health);

                    // Kill escort too
                    if (em.Exists(cs.EscortEntity) && em.HasComponent<Health>(cs.EscortEntity))
                    {
                        var escortHealth = em.GetComponentData<Health>(cs.EscortEntity);
                        escortHealth.Value = 0;
                        ecb.SetComponent(cs.EscortEntity, escortHealth);
                    }

                    // Decrement active caravan count on origin if it still exists
                    if (originValid && em.HasComponent<TradeRoute>(cs.Origin))
                    {
                        var route = em.GetComponentData<TradeRoute>(cs.Origin);
                        route.ActiveCaravans = math.max(0, route.ActiveCaravans - 1);
                        ecb.SetComponent(cs.Origin, route);
                    }

                    continue;
                }

                // Only process arrival when movement system says we've arrived
                if (dest.Has != 0) continue;

                if (cs.IsReturning == 0)
                {
                    // === ARRIVED AT OUTPOST ===
                    // Deposit cargo to faction bank
                    int suppliesDeposit = (int)cs.CurrentCargo;
                    if (suppliesDeposit > 0)
                    {
                        FactionEconomy.Add(em, faction.ValueRO.Value, Cost.Of(supplies: suppliesDeposit));
                    }

                    cs.CurrentCargo = 0f;
                    cs.IsReturning = 1;

                    // Set destination back to TradeHub (return trip, no cargo)
                    float3 originPos = em.GetComponentData<LocalTransform>(cs.Origin).Position;
                    dest.Position = originPos;
                    dest.Has = 1;
                }
                else
                {
                    // === ARRIVED BACK AT TRADEHUB ===
                    // Reload cargo for next trip
                    cs.CurrentCargo = cs.MaxCargo;
                    cs.IsReturning = 0;

                    // Set destination to Outpost
                    float3 destPos = em.GetComponentData<LocalTransform>(cs.Destination).Position;
                    dest.Position = destPos;
                    dest.Has = 1;
                }
            }

            // =============================================================
            // PHASE 2: Escort follow behavior
            // =============================================================
            foreach (var (escort, dd, entity) in SystemAPI
                .Query<RefRO<CaravanEscort>, RefRW<DesiredDestination>>()
                .WithAll<CaravanEscortTag>()
                .WithEntityAccess())
            {
                Entity caravanEntity = escort.ValueRO.CaravanEntity;

                // If caravan is gone, escort will be killed by CaravanDeathSystem
                if (!em.Exists(caravanEntity) || !em.HasComponent<LocalTransform>(caravanEntity))
                    continue;

                // Check if escort is currently engaged in combat (has an active target)
                bool inCombat = false;
                if (em.HasComponent<Target>(entity))
                {
                    var target = em.GetComponentData<Target>(entity);
                    if (target.Value != Entity.Null && em.Exists(target.Value))
                    {
                        inCombat = true;
                    }
                }

                // If not in combat, follow caravan position
                if (!inCombat)
                {
                    float3 caravanPos = em.GetComponentData<LocalTransform>(caravanEntity).Position;
                    float3 escortPos = em.GetComponentData<LocalTransform>(entity).Position;

                    // Only update destination if caravan is more than 3 units away
                    float distSq = math.distancesq(escortPos, caravanPos);
                    if (distSq > 9f) // 3^2
                    {
                        dd.ValueRW.Position = caravanPos;
                        dd.ValueRW.Has = 1;
                    }
                    else if (dd.ValueRO.Has == 0)
                    {
                        // Escort arrived near caravan, keep close
                        // Don't set new destination; just idle near caravan
                    }
                }
            }
        }
    }
}
