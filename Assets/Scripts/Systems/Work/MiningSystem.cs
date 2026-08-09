// File: Assets/Scripts/Systems/Work/MiningSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles miners gathering iron from deposits. Mined resources are
    /// credited straight to the faction bank on every gather tick — miners
    /// never carry resources and never walk anything back to a building.
    ///
    /// Iron miners (GatheringResource == 0):
    /// - Move to assigned iron deposit
    /// - Gather 1 iron every 2 seconds, credited directly to the bank
    /// - When the deposit depletes, auto-find another one nearby or go idle
    ///
    /// Veilsteel miners (GatheringResource == 2) run the exact same state
    /// machine — a veilsteel "Sharp Crystals" node carries VeilsteelDepositTag +
    /// the shared IronDepositState; only the resource credited to the bank
    /// differs.
    ///
    /// State machine: Idle -> MovingToDeposit -> Gathering -> (loop or Idle)
    ///
    /// Interrupts:
    /// - UserMoveOrder: stops mining, goes idle
    /// - GatherCommand for different resource type: reassigns
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
        private const float GatherRange = 5f;           // How close miner needs to be to mine
        private const float AutoFindRadius = 10f;       // Radius to auto-find next same-type deposit around a depleted node

        // Cached queries — created once in OnCreate, reused every frame
        private EntityQuery _ironDepositQuery;
        private EntityQuery _veilsteelDepositQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinerTag>();

            _ironDepositQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            _veilsteelDepositQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<VeilsteelDepositTag>(),
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
                     .WithEntityAccess())
            {
                ref var miner = ref minerState.ValueRW;

                // Veilstone miners are handled by VeilstoneMiningSystem
                if (miner.GatheringResource == 1) continue;

                var pos = transform.ValueRO.Position;
                var fac = faction.ValueRO.Value;

                // --- UserMoveOrder / construction interrupt ---
                // Player issued a move command, OR the worker was drafted to
                // build/repair (BuildCommand doesn't carry UserMoveOrder):
                // stop mining, go idle. Without the build/repair branch the
                // mining state machine kept driving the worker's
                // DesiredDestination toward the deposit every tick while
                // BuildingConstructionSystem drove it toward the site — a
                // tug-of-war that made workers walk AWAY from their shown
                // destination.
                if (em.HasComponent<UserMoveOrder>(entity)
                    || em.HasComponent<TheWaningBorder.Core.Commands.Types.BuildCommand>(entity)
                    || em.HasComponent<BuildOrder>(entity)
                    || em.HasComponent<RepairOrder>(entity))
                {
                    if (miner.State != MinerWorkState.Idle)
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                    }
                    continue;
                }

                switch (miner.State)
                {
                    case MinerWorkState.Idle:
                        ProcessIdleState(ref miner, em, ecb, entity, pos, fac);
                        break;

                    case MinerWorkState.MovingToDeposit:
                        ProcessMovingState(ref miner, em, entity, pos, dt);
                        break;

                    case MinerWorkState.Gathering:
                        ProcessGatheringState(ref miner, em, ecb, entity, pos, fac, dt);
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

                // Determine resource type (outcropping = veilstone, sharp
                // crystals = veilsteel, otherwise iron)
                byte newResourceType =
                    em.HasComponent<VeilstoneOutcroppingTag>(gatherCmd.ResourceNode) ? (byte)1 :
                    em.HasComponent<VeilsteelDepositTag>(gatherCmd.ResourceNode) ? (byte)2 : (byte)0;

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
            // veilstone). The AI brain in SimpleAISystem.AssignIdleMiners issues
            // those commands; the player issues them via right-click.
        }

        private void ProcessMovingState(ref MinerState miner, EntityManager em, Entity entity, float3 pos, float dt)
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

                    // Look for another same-type deposit within AutoFindRadius
                    // of where the depleted node was.
                    TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                    return;
                }
            }

            var depositPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;
            // Reach is measured to the node's SURFACE, so a physically large
            // deposit isn't harder to mine than a small one.
            float dist = TargetGeometry.SurfaceDistXZ(em, pos, miner.AssignedDeposit);

            if (dist <= GatherRange)
            {
                // Reached deposit - start gathering
                miner.State = MinerWorkState.Gathering;
                miner.GatherTimer = 0f;

                // Plant and turn to face the deposit — miners used to swing at
                // whatever heading they arrived on.
                TargetGeometry.StopAndFace(em, entity, depositPos, dt);
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
            // Keep facing the node for the whole gather, not just on arrival:
            // a turn takes several frames at DefaultTurnSpeed, and the miner may
            // have been rotated by its approach on the final step.
            if (miner.AssignedDeposit != Entity.Null && em.Exists(miner.AssignedDeposit)
                && em.HasComponent<LocalTransform>(miner.AssignedDeposit))
            {
                TargetGeometry.Face(em, entity,
                    em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position, dt);
            }

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
                    TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                    return;
                }

                var depState = em.GetComponentData<IronDepositState>(miner.AssignedDeposit);

                // Extract from the deposit and credit the bank directly —
                // the resource goes straight to the player's inventory.
                int toGather = math.min(IronPerGather, depState.RemainingIron);
                depState.RemainingIron -= toGather;

                if (toGather > 0 && FactionEconomy.TryGetBank(em, fac, out var bank))
                {
                    var resources = em.GetComponentData<FactionResources>(bank);
                    if (miner.GatheringResource == 2)
                        resources.Veilsteel += toGather;
                    else
                        resources.Iron += toGather;
                    resources.Clamp();
                    em.SetComponentData(bank, resources);
                }

                bool justDepleted = false;
                if (depState.RemainingIron <= 0)
                {
                    depState.RemainingIron = 0;
                    depState.Depleted = 1;
                    justDepleted = true;
                }

                em.SetComponentData(miner.AssignedDeposit, depState);

                if (justDepleted)
                {
                    // Capture the node's position before destroying it so
                    // auto-find can search around the depleted node's location.
                    miner.LastDepositPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;

                    // Destroy the depleted entity so the visual (mesh +
                    // ObstacleTag + collider) collapses off the map. Mirrors
                    // VeilstoneMiningSystem's outcropping-cleanup pattern.
                    // Other miners holding a stale AssignedDeposit reference
                    // will fall through the !em.HasComponent<IronDepositState>
                    // guard above and re-route to a new deposit on their next
                    // tick.
                    if (em.HasComponent<PresentationId>(miner.AssignedDeposit))
                        ecb.RemoveComponent<PresentationId>(miner.AssignedDeposit);
                    ecb.DestroyEntity(miner.AssignedDeposit);
                    miner.AssignedDeposit = Entity.Null;

                    // Look for another same-type deposit within AutoFindRadius
                    // of the depleted node.
                    TryAssignNearestIronDeposit(ref miner, em, entity, miner.LastDepositPos);
                }
                // else: keep gathering (stay in Gathering state)
            }
        }

        /// <summary>
        /// Look for the nearest non-depleted same-type deposit (iron for
        /// GatheringResource 0, veilsteel for 2) within AutoFindRadius of
        /// <paramref name="searchCenter"/> (typically the depleted node's
        /// position). If found, assign the miner to it, set the move destination,
        /// and transition to MovingToDeposit. Otherwise transition to Idle.
        /// </summary>
        private void TryAssignNearestIronDeposit(ref MinerState miner, EntityManager em, Entity entity, float3 searchCenter)
        {
            var depositQuery = miner.GatheringResource == 2 ? _veilsteelDepositQuery : _ironDepositQuery;
            using var deposits = depositQuery.ToEntityArray(Allocator.Temp);
            using var states = depositQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var transforms = depositQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

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
    }
}
