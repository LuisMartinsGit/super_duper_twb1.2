// File: Assets/Scripts/Systems/Work/MiningSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles miners gathering iron from deposits using carry-and-deposit model.
    ///
    /// Iron miners (GatheringResource == 0):
    /// - Move to assigned iron deposit
    /// - Gather 1 iron every 2 seconds, accumulating up to 10
    /// - When carrying 10 (or deposit depleted), walk to nearest Hall or GathererHut to deposit
    /// - Return to deposit for more, or find new one within LOS if depleted
    ///
    /// State machine: Idle -> MovingToDeposit -> Gathering -> ReturningToBase -> (loop or Idle)
    ///
    /// Interrupts:
    /// - UserMoveOrder: stops mining, keeps current load, goes idle
    /// - GatherCommand for different resource type: clears load, reassigns
    /// </summary>
    // GatheringSystem owns the player-command path: it sets the destination,
    // transitions the miner from Idle → MovingToDeposit / Gathering, and
    // removes the GatherCommand once the miner arrives. Running MiningSystem
    // AFTER guarantees that when ProcessIdleState fires on a still-Idle miner,
    // the command has either been consumed (race-free) or the destination is
    // already set (no double-write to MinerState). (task-062 Q-2)
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GatheringSystem))]
    public partial struct MiningSystem : ISystem
    {
        private const float GatherInterval = 2f;       // Seconds to gather one unit
        private const int IronPerGather = 1;            // Iron per gather action
        private const int MaxCarryAmount = 10;          // Deliver after accumulating 10
        private const float GatherRange = 5f;           // How close miner needs to be to mine
        private const float DropoffRange = 6f;          // How close to dropoff to deposit
        private const float AutoFindRadius = 10f;       // Radius to auto-find next same-type deposit around a depleted node

        // Cached queries — created once in OnCreate, reused every frame
        private EntityQuery _hallDropoffQuery;
        private EntityQuery _hutDropoffQuery;
        private EntityQuery _ironDepositQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinerTag>();

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

            _ironDepositQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Temp ECB for structural changes (RemoveComponent) during iteration
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (minerState, transform, faction, entity) in SystemAPI
                     .Query<RefRW<MinerState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                     .WithAll<MinerTag>()
                     .WithNone<ForgeSupplyOrder>()
                     .WithEntityAccess())
            {
                ref var miner = ref minerState.ValueRW;

                // Crystal miners are handled by CrystalMiningSystem
                if (miner.GatheringResource == 1) continue;

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
                        // Keep CurrentLoad — miner carries resources while moving
                    }
                    continue;
                }

                switch (miner.State)
                {
                    case MinerWorkState.Idle:
                        ProcessIdleState(ref miner, em, ecb, entity, pos, fac);
                        break;

                    case MinerWorkState.MovingToDeposit:
                        ProcessMovingState(ref miner, em, entity, pos);
                        break;

                    case MinerWorkState.Gathering:
                        ProcessGatheringState(ref miner, em, ecb, entity, pos, fac, dt);
                        break;

                    case MinerWorkState.ReturningToBase:
                        ProcessReturningToBase(ref miner, em, entity, pos, fac);
                        break;
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        private void ProcessIdleState(ref MinerState miner, EntityManager em, EntityCommandBuffer ecb, Entity entity, float3 pos, Faction fac)
        {
            // Check for explicit GatherCommand (from player right-click or AI)
            if (em.HasComponent<GatherCommand>(entity))
            {
                var gatherCmd = em.GetComponentData<GatherCommand>(entity);
                ecb.RemoveComponent<GatherCommand>(entity);

                if (gatherCmd.ResourceNode == Entity.Null || !em.Exists(gatherCmd.ResourceNode))
                    return;

                // Determine resource type (cadaver = crystal, otherwise iron)
                byte newResourceType = em.HasComponent<CadaverTag>(gatherCmd.ResourceNode) ? (byte)1 : (byte)0;

                // Switching resource type: clear existing load
                if (newResourceType != miner.GatheringResource && miner.CurrentLoad > 0)
                    miner.CurrentLoad = 0;

                miner.AssignedDeposit = gatherCmd.ResourceNode;
                miner.State = MinerWorkState.MovingToDeposit;
                miner.GatheringResource = newResourceType;

                // Move to the deposit. Miners always have DesiredDestination from the
                // factory, so SetComponentData is the only path; ecb.AddComponent is a
                // safety net for any external creator that forgot to add it.
                var depositPos = em.GetComponentData<LocalTransform>(gatherCmd.ResourceNode).Position;
                if (em.HasComponent<DesiredDestination>(entity))
                    em.SetComponentData(entity, new DesiredDestination { Position = depositPos, Has = 1 });
                else
                    ecb.AddComponent(entity, new DesiredDestination { Position = depositPos, Has = 1 });
                return;
            }

            // Idle miners do nothing on their own. Both player and AI miners
            // sit idle until commanded with an explicit GatherCommand (iron or
            // crystal). The AI brain in SimpleAISystem.AssignIdleMiners issues
            // those commands; the player issues them via right-click.
        }

        private void ProcessMovingState(ref MinerState miner, EntityManager em, Entity entity, float3 pos)
        {
            // Check if deposit still exists
            if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
            {
                miner.State = MinerWorkState.Idle;
                miner.AssignedDeposit = Entity.Null;
                return;
            }

            // Check if deposit is depleted
            if (em.HasComponent<IronDepositState>(miner.AssignedDeposit))
            {
                var depState = em.GetComponentData<IronDepositState>(miner.AssignedDeposit);
                if (depState.Depleted == 1)
                {
                    // Capture the depleted node's position so we can auto-find a
                    // replacement near it, regardless of where we are now.
                    miner.LastDepositPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
                    miner.AssignedDeposit = Entity.Null;

                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        var fac = em.GetComponentData<FactionTag>(entity).Value;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        // Nothing to deposit — look for another iron deposit
                        // within AutoFindRadius of where the depleted node was.
                        TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                    }
                    return;
                }
            }

            var depositPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
            float dist = DistXZ(pos, depositPos);

            if (dist <= GatherRange)
            {
                // Reached deposit - start gathering
                miner.State = MinerWorkState.Gathering;
                miner.GatherTimer = 0f;

                // Stop moving
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    em.SetComponentData(entity, new DesiredDestination { Has = 0 });
                }
                return;
            }

            // We are NOT at the deposit yet. If the destination component
            // exists and MovementSystem cleared it (tier-3 stuck cancel),
            // drop back to Idle so ProcessIdleState can re-find next frame.
            //
            // We deliberately treat "DesiredDestination component missing" as
            // "ProcessIdleState's AddComponentData hasn't synced yet" — NOT
            // as a stuck signal. Reading missing-then-Idle would race against
            // the structural-change deferral and trap the miner in an
            // Idle ↔ MovingToDeposit ping-pong that prevents any movement.
            if (em.HasComponent<DesiredDestination>(entity)
                && em.GetComponentData<DesiredDestination>(entity).Has == 0)
            {
                miner.State = MinerWorkState.Idle;
                miner.AssignedDeposit = Entity.Null;
            }
        }

        private void ProcessGatheringState(ref MinerState miner, EntityManager em, EntityCommandBuffer ecb, Entity entity, float3 pos, Faction fac, float dt)
        {
            miner.GatherTimer += dt;

            // Effective gather interval: faster when GatherSpeedMultiplier > 1
            float effectiveInterval = miner.GatherSpeedMultiplier > 0f
                ? GatherInterval / miner.GatherSpeedMultiplier
                : GatherInterval;

            if (miner.GatherTimer >= effectiveInterval)
            {
                miner.GatherTimer = 0f;

                // Check deposit still exists. If it was destroyed by another miner
                // exhausting it, the current miner is still standing where the
                // node was (within GatherRange) — use that as the search center.
                if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit)
                    || !em.HasComponent<IronDepositState>(miner.AssignedDeposit))
                {
                    miner.LastDepositPos = pos;
                    miner.AssignedDeposit = Entity.Null;

                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                    }
                    return;
                }

                var depState = em.GetComponentData<IronDepositState>(miner.AssignedDeposit);

                // Extract iron from deposit (1 iron per gather)
                int toGather = math.min(IronPerGather, depState.RemainingIron);
                depState.RemainingIron -= toGather;
                miner.CurrentLoad += toGather;

                bool justDepleted = false;
                if (depState.RemainingIron <= 0)
                {
                    depState.RemainingIron = 0;
                    depState.Depleted = 1;
                    justDepleted = true;
                }

                em.SetComponentData(miner.AssignedDeposit, depState);

                // Capture the node's position before destroying it so
                // auto-find can search around the depleted node's location.
                float3 depletedPos = float3.zero;
                if (justDepleted)
                    depletedPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;

                // When a deposit just depleted: destroy its entity so the
                // visual (mesh + ObstacleTag + collider) collapses off the
                // map. Mirrors CrystalMiningSystem's cadaver-cleanup pattern
                // (CrystalMiningSystem.cs:249-254). Other miners holding a
                // stale AssignedDeposit reference will fall through the
                // !em.HasComponent<IronDepositState> guard above and
                // re-route to a new deposit on their next tick.
                if (justDepleted && em.Exists(miner.AssignedDeposit))
                {
                    if (em.HasComponent<PresentationId>(miner.AssignedDeposit))
                        ecb.RemoveComponent<PresentationId>(miner.AssignedDeposit);
                    ecb.DestroyEntity(miner.AssignedDeposit);
                }

                // Check if full or deposit depleted (carry capacity includes tech bonus)
                int effectiveMaxCarry = MaxCarryAmount + miner.CarryCapacityBonus;
                bool isFull = miner.CurrentLoad >= effectiveMaxCarry;
                bool depositDepleted = depState.Depleted == 1;

                if (isFull || depositDepleted)
                {
                    if (depositDepleted)
                    {
                        miner.LastDepositPos = depletedPos;
                        miner.AssignedDeposit = Entity.Null;
                    }

                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        // Deposit depleted and nothing to carry — look for another
                        // iron deposit within AutoFindRadius of the depleted node.
                        TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                    }
                }
                // else: keep gathering (stay in Gathering state)
            }
        }

        private void ProcessReturningToBase(ref MinerState miner, EntityManager em, Entity entity, float3 pos, Faction fac)
        {
            // Check if dropoff target still exists
            if (miner.DropoffTarget == Entity.Null || !em.Exists(miner.DropoffTarget))
            {
                SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                if (miner.DropoffTarget == Entity.Null)
                {
                    // No dropoff available - go idle, keep load
                    miner.State = MinerWorkState.Idle;
                    return;
                }
            }

            var dropoffPos = em.GetComponentData<LocalTransform>(miner.DropoffTarget).Position;
            float dist = DistXZ(pos, dropoffPos);

            if (dist <= DropoffRange)
            {
                // Reached dropoff - deposit iron to faction economy
                if (FactionEconomy.TryGetBank(em, fac, out var bank))
                {
                    var resources = em.GetComponentData<FactionResources>(bank);
                    resources.Iron += miner.CurrentLoad;
                    resources.Clamp();
                    em.SetComponentData(bank, resources);
                }

                miner.CurrentLoad = 0;
                miner.DropoffTarget = Entity.Null;

                // Stop moving
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    em.SetComponentData(entity, new DesiredDestination { Has = 0 });
                }

                // Check if deposit still has iron - go back for more
                bool depositHasIron = false;
                if (miner.AssignedDeposit != Entity.Null && em.Exists(miner.AssignedDeposit))
                {
                    if (em.HasComponent<IronDepositState>(miner.AssignedDeposit))
                    {
                        var depState = em.GetComponentData<IronDepositState>(miner.AssignedDeposit);
                        depositHasIron = depState.Depleted == 0;
                    }
                }

                if (depositHasIron)
                {
                    // Go back for more iron. Miners always carry DesiredDestination from
                    // the factory, so SetComponentData is the only path used at runtime.
                    miner.State = MinerWorkState.MovingToDeposit;
                    var depPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
                    em.SetComponentData(entity, new DesiredDestination { Position = depPos, Has = 1 });
                }
                else
                {
                    // Deposit depleted — auto-find another iron deposit within
                    // AutoFindRadius of where the depleted node was (not the
                    // miner's current position at the dropoff).
                    TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                }
            }
        }

        /// <summary>
        /// Find the nearest Hall or GathererHut of the miner's faction and set it as dropoff target.
        /// </summary>
        private static void SetDropoffDestination(
            ref MinerState miner, EntityManager em, Entity minerEntity,
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

            // Set move destination to dropoff. Miner factory bakes in
            // DesiredDestination, so SetComponentData is always safe.
            if (nearest != Entity.Null)
            {
                var dropoffPos = em.GetComponentData<LocalTransform>(nearest).Position;
                em.SetComponentData(minerEntity, new DesiredDestination { Position = dropoffPos, Has = 1 });
            }
        }

        /// <summary>
        /// Look for the nearest non-depleted iron deposit within AutoFindRadius
        /// of <paramref name="searchCenter"/> (typically the depleted node's
        /// position). If found, assign the miner to it, set the move destination,
        /// and transition to MovingToDeposit. Otherwise transition to Idle.
        /// </summary>
        private void TryAssignNearestIronDeposit(ref MinerState miner, EntityManager em, Entity entity, float3 searchCenter)
        {
            using var deposits = _ironDepositQuery.ToEntityArray(Allocator.Temp);
            using var states = _ironDepositQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var transforms = _ironDepositQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity bestDeposit = Entity.Null;
            float bestDist = float.MaxValue;
            float3 bestPos = float3.zero;

            for (int i = 0; i < deposits.Length; i++)
            {
                if (states[i].Depleted == 1) continue;

                float dist = DistXZ(searchCenter, transforms[i].Position);
                if (dist <= AutoFindRadius && dist < bestDist)
                {
                    bestDeposit = deposits[i];
                    bestDist = dist;
                    bestPos = transforms[i].Position;
                }
            }

            if (bestDeposit == Entity.Null)
            {
                miner.State = MinerWorkState.Idle;
                miner.AssignedDeposit = Entity.Null;
                return;
            }

            miner.AssignedDeposit = bestDeposit;
            miner.State = MinerWorkState.MovingToDeposit;
            em.SetComponentData(entity, new DesiredDestination { Position = bestPos, Has = 1 });
        }

        /// <summary>
        /// XZ-only (horizontal) distance — ignores Y so terrain height doesn't break range checks.
        /// </summary>
    }
}
