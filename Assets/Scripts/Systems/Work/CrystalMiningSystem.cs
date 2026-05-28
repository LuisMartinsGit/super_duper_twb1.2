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
        private const float AutoFindRadius = 10f;       // Radius to auto-find next cadaver around a depleted node

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
                // Player issued a move command: stop mining, keep load, go idle.
                // GatheringResource stays 1 — explicit move commands don't switch
                // the miner's resource type. The player can issue a fresh
                // GatherCommand on iron to convert intent.
                if (em.HasComponent<UserMoveOrder>(entity))
                {
                    if (miner.State != MinerWorkState.Idle)
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                        miner.DropoffTarget = Entity.Null;
                    }
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
                        // Idle crystal miners do nothing on their own. AI miners
                        // are reassigned by SimpleAISystem.AssignIdleMiners; the
                        // player issues a fresh GatherCommand via right-click.
                        // Crystal flow stays independent — never revert to iron.
                        break;
                }
            }
        }

        private void ProcessMovingToCadaver(ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity entity, float3 pos)
        {
            // Check if cadaver still exists or is depleted — auto-find next nearby
            bool needNewTarget = false;
            float3 searchCenter = pos;
            if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
            {
                // Cadaver was destroyed (likely depleted by another miner) — fall
                // back to the last known position captured in LastDepositPos.
                needNewTarget = true;
                if (!miner.LastDepositPos.Equals(float3.zero))
                    searchCenter = miner.LastDepositPos;
            }
            else if (em.HasComponent<CadaverState>(miner.AssignedDeposit))
            {
                var cadaverState = em.GetComponentData<CadaverState>(miner.AssignedDeposit);
                if (cadaverState.Depleted == 1)
                {
                    needNewTarget = true;
                    searchCenter = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
                    miner.LastDepositPos = searchCenter;
                }
            }

            if (needNewTarget)
            {
                // No cadaver in range — go idle as a crystal miner. We keep
                // GatheringResource=1 so the miner stays committed to crystal.
                if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, searchCenter))
                {
                    miner.State = MinerWorkState.Idle;
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
                return;
            }

            // Tier-3 stuck recovery: if our destination got cleared while we
            // were trying to reach the cadaver, drop to Idle so the next tick
            // assigns a fresh target. Stays a crystal miner — flows are
            // independent. Same caveats as MiningSystem — only fire when the
            // component EXISTS but its Has==0 (otherwise we race the
            // structural-change deferral and ping-pong forever).
            if (em.HasComponent<DesiredDestination>(entity)
                && em.GetComponentData<DesiredDestination>(entity).Has == 0)
            {
                miner.State = MinerWorkState.Idle;
                miner.AssignedDeposit = Entity.Null;
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

                // Check crystal node still exists and has crystal. If it was
                // destroyed by another miner exhausting it, the current miner
                // is still standing where the cadaver was (within GatherRange)
                // — use that as the search center for the next cadaver.
                if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit)
                    || !em.HasComponent<CadaverState>(miner.AssignedDeposit))
                {
                    var minerPos = em.GetComponentData<LocalTransform>(entity).Position;
                    miner.LastDepositPos = minerPos;
                    miner.AssignedDeposit = Entity.Null;

                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, ref ecb, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, miner.LastDepositPos))
                    {
                        miner.State = MinerWorkState.Idle;
                    }
                    return;
                }

                var cadaverState = em.GetComponentData<CadaverState>(miner.AssignedDeposit);

                // Extract crystal from node (1 crystal per gather action).
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

                // Capture the cadaver's position before destruction so auto-find
                // can search around where the depleted node was.
                if (justDepleted)
                    miner.LastDepositPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;

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
                        // Node depleted and nothing to carry — look for another
                        // cadaver within AutoFindRadius of the depleted node.
                        if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, miner.LastDepositPos))
                        {
                            miner.State = MinerWorkState.Idle;
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
                    // No dropoff available — go idle but keep crystal intent.
                    miner.State = MinerWorkState.Idle;
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
                    // Cadaver depleted — auto-find another cadaver within
                    // AutoFindRadius of where the depleted node was (not the
                    // miner's current position at the dropoff).
                    miner.DropoffTarget = Entity.Null;
                    float3 searchCenter = miner.LastDepositPos.Equals(float3.zero) ? pos : miner.LastDepositPos;
                    if (!TryAssignNearestCadaver(ref miner, em, ref ecb, entity, searchCenter))
                    {
                        miner.State = MinerWorkState.Idle;
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
        /// Look for the nearest non-depleted cadaver within AutoFindRadius of
        /// <paramref name="searchCenter"/> (typically the depleted node's
        /// position). If found, assign the miner to it and set the move
        /// destination. Returns true if a new cadaver was found.
        /// </summary>
        private bool TryAssignNearestCadaver(ref MinerState miner, EntityManager em, ref EntityCommandBuffer ecb, Entity entity, float3 searchCenter)
        {
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

                float dist = DistXZ(searchCenter, cadaverTransforms[i].Position);
                if (dist <= AutoFindRadius && dist < bestDist)
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
