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
    /// Handles Runai trader movement with distance-based resource generation.
    ///
    /// Each frame:
    /// 1. Calculate distance traveled since last frame
    /// 2. Accumulate supplies (1 per 2 distance) and crystal (1 per 15 distance)
    /// 3. On arrival: deposit integer amounts, keep fractional remainder
    /// 4. Pick new random destination and repeat
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct TraderMovementSystem : ISystem
    {
        private const float SuppliesPerDistance = 0.5f;   // 1 supply per 2 distance
        private const float CrystalPerDistance = 1f / 15f; // 1 crystal per 15 distance

        private uint _randomSeed;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _randomSeed = 7919;
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (trader, dd, faction, transform, entity) in SystemAPI
                .Query<RefRW<RunaiTraderState>, RefRW<DesiredDestination>, RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                ref var ts = ref trader.ValueRW;
                ref var dest = ref dd.ValueRW;
                float3 currentPos = transform.ValueRO.Position;

                // --- Validate destination still exists ---
                if (ts.CurrentDest != Entity.Null && !em.Exists(ts.CurrentDest))
                {
                    // Destination destroyed — find new one
                    if (!TryPickRandomNode(em, faction.ValueRO.Value, Entity.Null, out var newDest, out var newPos))
                    {
                        // No destinations — kill trader
                        var hp = em.GetComponentData<Health>(entity);
                        hp.Value = 0;
                        em.SetComponentData(entity, hp);
                        continue;
                    }

                    ts.CurrentDest = newDest;
                    dest.Position = newPos;
                    dest.Has = 1;
                    ts.PreviousPosition = currentPos;
                    FlowFieldManager.Instance?.RequestFlowField(newPos);
                    continue;
                }

                // --- Accumulate resources based on distance traveled ---
                float distMoved = math.distance(ts.PreviousPosition, currentPos);
                if (distMoved > 0.01f) // Ignore tiny movements
                {
                    ts.AccumulatedSupplies += distMoved * SuppliesPerDistance;
                    ts.AccumulatedCrystal += distMoved * CrystalPerDistance;
                }
                ts.PreviousPosition = currentPos;

                // --- Check arrival (movement system clears Has when arrived) ---
                if (dest.Has != 0) continue;

                // === ARRIVED AT DESTINATION ===

                // Deposit integer resources
                int supDep = (int)ts.AccumulatedSupplies;
                int cryDep = (int)ts.AccumulatedCrystal;

                if (supDep > 0 || cryDep > 0)
                {
                    // task-063 phase 1: sect TradeIncome multiplier removed with the
                    // FactionSectState bridge. Baseline 1.0× until Phase 2 reintroduces
                    // trade-related sect levers.
                    FactionEconomy.Add(em, faction.ValueRO.Value, Cost.Of(supplies: supDep, crystal: cryDep));
                }

                // Keep fractional remainder
                ts.AccumulatedSupplies -= supDep;
                ts.AccumulatedCrystal -= cryDep;

                // --- Pick new random destination ---
                if (TryPickRandomNode(em, faction.ValueRO.Value, ts.CurrentDest, out var next, out var nPos))
                {
                    ts.CurrentDest = next;
                    dest.Position = nPos;
                    dest.Has = 1;
                    FlowFieldManager.Instance?.RequestFlowField(nPos);
                }
                else
                {
                    // Only one node left — wait
                    dest.Has = 0;
                }
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
            position = transforms[idx].Position;

            candidates.Dispose();
            return true;
        }
    }
}
