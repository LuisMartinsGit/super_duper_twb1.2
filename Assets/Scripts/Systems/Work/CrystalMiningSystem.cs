// File: Assets/Scripts/Systems/Work/CrystalMiningSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles miners gathering crystal from crystal nodes (creature cadavers).
    ///
    /// Miners assigned to crystal (GatheringResource == 1):
    /// - Move to crystal node
    /// - Gather 1 crystal every 1.5 seconds, accumulating up to 10
    /// - When carrying 10 (or node depleted), walk to nearest Hall or GathererHut to deposit
    /// - Return to crystal node for more, or go idle if node is depleted
    ///
    /// State machine: MovingToDeposit -> Gathering -> ReturningToBase -> (loop or Idle)
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MiningSystem))]
    public partial struct CrystalMiningSystem : ISystem
    {
        private const float GatherInterval = 1.5f;    // Mine 1 crystal every 1.5 seconds
        private const int CrystalPerGather = 1;        // 1 crystal per gather action
        private const int MaxCarryAmount = 10;          // Deliver after accumulating 10
        private const float GatherRange = 5f;
        private const float DropoffRange = 6f;

        // Cached queries — created once in OnCreate, reused every frame
        private EntityQuery _hallDropoffQuery;
        private EntityQuery _hutDropoffQuery;
        private EntityQuery _cadaverQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinerTag>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _cadaverQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<CadaverState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _hallDropoffQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );

            _hutDropoffQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
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

            foreach (var (minerState, transform, faction, entity) in SystemAPI
                .Query<RefRW<MinerState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<MinerTag>()
                .WithNone<ForgeSupplyOrder>()
                .WithEntityAccess())
            {
                ref var miner = ref minerState.ValueRW;

                // Only handle crystal miners
                if (miner.GatheringResource != 1) continue;

                var pos = transform.ValueRO.Position;
                var fac = faction.ValueRO.Value;

                // --- UserMoveOrder interrupt ---
                // Player issued a move command: stop mining, keep load, go idle
                if (em.HasComponent<UserMoveOrder>(entity))
                {
                    if (miner.State != MinerWorkState.Idle)
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                        miner.DropoffTarget = Entity.Null;
                        // Keep CurrentLoad and GatheringResource — miner carries crystal while moving
                        // Will be reset to iron (0) when going idle below if no GatherCommand follows
                    }
                    // Don't reset GatheringResource here — let the Idle case handle it
                    // so MiningSystem picks them up next frame
                    continue;
                }

                switch (miner.State)
                {
                    case MinerWorkState.MovingToDeposit:
                        ProcessMovingToCadaver(ref miner, em, ref ecb, entity, pos);
                        break;

                    case MinerWorkState.Gathering:
                        ProcessGatheringCrystal(ref miner, em, ref ecb, entity, fac, dt);
                        break;

                    case MinerWorkState.ReturningToBase:
                        ProcessReturningToBase(ref miner, em, ref ecb, entity, pos, fac);
                        break;

                    case MinerWorkState.Idle:
                        // Idle crystal miners get reset - MiningSystem will reassign
                        miner.GatheringResource = 0;
                        break;
                }
            }
        }

        private void ProcessMovingToCadaver(ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity entity, float3 pos)
        {
            // Check if cadaver still exists or is depleted — auto-find next in LOS
            bool needNewTarget = false;
            if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
            {
                needNewTarget = true;
            }
            else if (em.HasComponent<CadaverState>(miner.AssignedDeposit))
            {
                var cadaverState = em.GetComponentData<CadaverState>(miner.AssignedDeposit);
                if (cadaverState.Depleted == 1)
                    needNewTarget = true;
            }

            if (needNewTarget)
            {
                if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, pos))
                {
                    miner.State = MinerWorkState.Idle;
                    miner.GatheringResource = 0;
                    miner.AssignedDeposit = Entity.Null;
                }
                return;
            }

            var cadaverPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
            float dist = DistXZ(pos, cadaverPos);

            if (dist <= GatherRange)
            {
                // Reached cadaver - start gathering
                miner.State = MinerWorkState.Gathering;
                miner.GatherTimer = 0f;

                // Stop moving
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    em.SetComponentData(entity, new DesiredDestination { Has = 0 });
                }
            }
        }

        private void ProcessGatheringCrystal(ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity entity, Faction fac, float dt)
        {
            miner.GatherTimer += dt;

            // Effective gather interval: faster when GatherSpeedMultiplier > 1
            float effectiveInterval = miner.GatherSpeedMultiplier > 0f
                ? GatherInterval / miner.GatherSpeedMultiplier
                : GatherInterval;

            if (miner.GatherTimer >= effectiveInterval)
            {
                miner.GatherTimer = 0f;

                // Check crystal node still exists and has crystal
                if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
                {
                    // Node gone - deposit what we have or go idle
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, ref ecb, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.GatheringResource = 0;
                    }
                    return;
                }

                if (!em.HasComponent<CadaverState>(miner.AssignedDeposit))
                {
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, ref ecb, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.GatheringResource = 0;
                    }
                    return;
                }

                var cadaverState = em.GetComponentData<CadaverState>(miner.AssignedDeposit);

                // Extract crystal from node (1 crystal per gather action)
                int toGather = math.min(CrystalPerGather, cadaverState.RemainingCrystal);
                cadaverState.RemainingCrystal -= toGather;
                miner.CurrentLoad += toGather;

                bool justDepleted = false;
                if (cadaverState.RemainingCrystal <= 0)
                {
                    cadaverState.RemainingCrystal = 0;
                    cadaverState.Depleted = 1;
                    justDepleted = true;
                }

                em.SetComponentData(miner.AssignedDeposit, cadaverState);

                // Destroy depleted cadaver via ECB (structural changes not allowed during iteration)
                if (justDepleted && em.Exists(miner.AssignedDeposit))
                {
                    if (em.HasComponent<PresentationId>(miner.AssignedDeposit))
                        ecb.RemoveComponent<PresentationId>(miner.AssignedDeposit);
                    ecb.DestroyEntity(miner.AssignedDeposit);
                    miner.AssignedDeposit = Entity.Null;
                }

                // Only return to base when carrying max load or node is depleted (carry capacity includes tech bonus)
                int effectiveMaxCarry = MaxCarryAmount + miner.CarryCapacityBonus;
                bool isFull = miner.CurrentLoad >= effectiveMaxCarry;
                bool nodeDepleted = cadaverState.Depleted == 1;

                if (isFull || nodeDepleted)
                {
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, ref ecb, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        // Node depleted and nothing to carry — try to find next cadaver in LOS
                        var minerPos = em.GetComponentData<LocalTransform>(entity).Position;
                        if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, minerPos))
                        {
                            miner.State = MinerWorkState.Idle;
                            miner.GatheringResource = 0;
                            miner.AssignedDeposit = Entity.Null;
                        }
                    }
                }
                // else: keep gathering (stay in Gathering state)
            }
        }

        private void ProcessReturningToBase(ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity entity, float3 pos, Faction fac)
        {
            // Check if dropoff target still exists
            if (miner.DropoffTarget == Entity.Null || !em.Exists(miner.DropoffTarget))
            {
                // Try to find a new dropoff
                SetDropoffDestination(ref miner, em, ref ecb, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                if (miner.DropoffTarget == Entity.Null)
                {
                    // No dropoff available - go idle
                    miner.State = MinerWorkState.Idle;
                    miner.GatheringResource = 0;
                    return;
                }
            }

            var dropoffPos = em.GetComponentData<LocalTransform>(miner.DropoffTarget).Position;
            float dist = DistXZ(pos, dropoffPos);

            if (dist <= DropoffRange)
            {
                // Reached dropoff - deposit crystal to faction economy
                if (FactionEconomy.TryGetBank(em, fac, out var bank))
                {
                    var resources = em.GetComponentData<FactionResources>(bank);
                    resources.Crystal += miner.CurrentLoad;
                    resources.Clamp();
                    em.SetComponentData(bank, resources);
                }

                miner.CurrentLoad = 0;

                // Stop moving
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    em.SetComponentData(entity, new DesiredDestination { Has = 0 });
                }

                // Check if cadaver still has crystal
                bool cadaverHasCrystal = false;
                if (miner.AssignedDeposit != Entity.Null && em.Exists(miner.AssignedDeposit))
                {
                    if (em.HasComponent<CadaverState>(miner.AssignedDeposit))
                    {
                        var cadaverState = em.GetComponentData<CadaverState>(miner.AssignedDeposit);
                        cadaverHasCrystal = cadaverState.Depleted == 0;
                    }
                }

                if (cadaverHasCrystal)
                {
                    // Go back for more crystal
                    miner.State = MinerWorkState.MovingToDeposit;
                    var cadaverPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;

                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        em.SetComponentData(entity, new DesiredDestination
                        {
                            Position = cadaverPos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = cadaverPos,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // Cadaver depleted — try to find next cadaver in LOS
                    miner.DropoffTarget = Entity.Null;
                    if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, pos))
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.GatheringResource = 0;
                        miner.AssignedDeposit = Entity.Null;
                    }
                }
            }
        }

        /// <summary>
        /// Find the nearest Hall or GathererHut of the miner's faction and set it as dropoff target.
        /// </summary>
        private static void SetDropoffDestination(
            ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity minerEntity,
            Faction fac, EntityQuery hallQuery, EntityQuery hutQuery)
        {
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;
            float3 minerPos = em.GetComponentData<LocalTransform>(minerEntity).Position;

            // Search for Halls (exclude under-construction)
            using var halls = hallQuery.ToEntityArray(Allocator.Temp);
            using var hallFactions = hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < halls.Length; i++)
            {
                if (hallFactions[i].Value != fac) continue;
                float dist = DistXZ(minerPos, hallTransforms[i].Position);
                if (dist < nearestDist)
                {
                    nearest = halls[i];
                    nearestDist = dist;
                }
            }

            // Search for GathererHuts (exclude under-construction)
            using var huts = hutQuery.ToEntityArray(Allocator.Temp);
            using var hutFactions = hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hutTransforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < huts.Length; i++)
            {
                if (hutFactions[i].Value != fac) continue;
                float dist = DistXZ(minerPos, hutTransforms[i].Position);
                if (dist < nearestDist)
                {
                    nearest = huts[i];
                    nearestDist = dist;
                }
            }

            miner.DropoffTarget = nearest;

            // Set move destination to dropoff
            if (nearest != Entity.Null)
            {
                var dropoffPos = em.GetComponentData<LocalTransform>(nearest).Position;
                if (em.HasComponent<DesiredDestination>(minerEntity))
                {
                    em.SetComponentData(minerEntity, new DesiredDestination
                    {
                        Position = dropoffPos,
                        Has = 1
                    });
                }
                else
                {
                    ecb.AddComponent(minerEntity, new DesiredDestination
                    {
                        Position = dropoffPos,
                        Has = 1
                    });
                }
            }
        }

        /// <summary>
        /// Find nearest non-depleted cadaver within the miner's line-of-sight range.
        /// Assigns the miner to it and sets movement destination.
        /// Returns true if a new cadaver was found.
        /// </summary>
        private bool TryAssignNearestCadaver(ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity entity, float3 pos)
        {
            float los = 10f;
            if (em.HasComponent<LineOfSight>(entity))
                los = em.GetComponentData<LineOfSight>(entity).Radius;

            using var cadavers = _cadaverQuery.ToEntityArray(Allocator.Temp);
            using var cadaverStates = _cadaverQuery.ToComponentDataArray<CadaverState>(Allocator.Temp);
            using var cadaverTransforms = _cadaverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity bestCadaver = Entity.Null;
            float bestDist = float.MaxValue;
            float3 bestPos = float3.zero;

            for (int i = 0; i < cadavers.Length; i++)
            {
                if (cadaverStates[i].Depleted == 1) continue;
                if (cadaverStates[i].RemainingCrystal <= 0) continue;

                float dist = DistXZ(pos, cadaverTransforms[i].Position);
                if (dist <= los && dist < bestDist)
                {
                    bestDist = dist;
                    bestCadaver = cadavers[i];
                    bestPos = cadaverTransforms[i].Position;
                }
            }

            if (bestCadaver == Entity.Null) return false;

            // Assign miner to new cadaver
            miner.AssignedDeposit = bestCadaver;
            miner.GatheringResource = 1;
            miner.State = MinerWorkState.MovingToDeposit;

            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Position = bestPos, Has = 1 });
                else
                    ecb.AddComponent(entity, new DesiredDestination { Position = bestPos, Has = 1 });

            return true;
        }

        /// <summary>
        /// XZ-only (horizontal) distance — ignores Y so terrain height doesn't break range checks.
        /// </summary>
        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}
