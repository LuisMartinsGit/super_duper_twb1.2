// File: Assets/Scripts/Systems/Work/MiningSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MiningSystem : ISystem
    {
        private const float GatherInterval = 2f;       // Seconds to gather one unit
        private const int IronPerGather = 1;            // Iron per gather action
        private const int MaxCarryAmount = 10;          // Deliver after accumulating 10
        private const float GatherRange = 5f;           // How close miner needs to be to mine
        private const float DropoffRange = 6f;          // How close to dropoff to deposit
        private const float SearchRadius = 50f;         // How far AI miners search for deposits

        // Cached queries — created once in OnCreate, reused every frame
        private EntityQuery _hallDropoffQuery;
        private EntityQuery _hutDropoffQuery;
        private EntityQuery _ironDepositQuery;
        private EntityQuery _cadaverQuery;

        [BurstCompile]
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

            _cadaverQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<CadaverState>(),
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
                        ProcessGatheringState(ref miner, em, entity, pos, fac, dt);
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

                // Move to the deposit
                var depositPos = em.GetComponentData<LocalTransform>(gatherCmd.ResourceNode).Position;
                if (em.HasComponent<DesiredDestination>(entity))
                    em.SetComponentData(entity, new DesiredDestination { Position = depositPos, Has = 1 });
                else
                    em.AddComponentData(entity, new DesiredDestination { Position = depositPos, Has = 1 });
                return;
            }

            // Local player miners wait for commands — no auto-find.
            // AI miners auto-find the nearest deposit so they stay productive.
            if (fac == GameSettings.LocalPlayerFaction) return;

            // AI auto-find: nearest iron deposit or cadaver
            Entity nearestIron = FindNearestDeposit(em, pos, _ironDepositQuery);
            float ironDist = float.MaxValue;
            if (nearestIron != Entity.Null)
                ironDist = DistXZ(pos, em.GetComponentData<LocalTransform>(nearestIron).Position);

            Entity nearestCadaver = FindNearestCadaver(em, pos, _cadaverQuery);
            float cadaverDist = float.MaxValue;
            if (nearestCadaver != Entity.Null)
                cadaverDist = DistXZ(pos, em.GetComponentData<LocalTransform>(nearestCadaver).Position);

            Entity target;
            bool isCrystal;
            if (nearestIron != Entity.Null && ironDist <= cadaverDist)
            { target = nearestIron; isCrystal = false; }
            else if (nearestCadaver != Entity.Null)
            { target = nearestCadaver; isCrystal = true; }
            else if (nearestIron != Entity.Null)
            { target = nearestIron; isCrystal = false; }
            else
                return;

            miner.AssignedDeposit = target;
            miner.State = MinerWorkState.MovingToDeposit;
            miner.GatheringResource = isCrystal ? (byte)1 : (byte)0;

            var depPos = em.GetComponentData<LocalTransform>(target).Position;
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Position = depPos, Has = 1 });
            else
                em.AddComponentData(entity, new DesiredDestination { Position = depPos, Has = 1 });
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
                    // Deposit depleted while moving — drop off what we have or go idle
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        miner.AssignedDeposit = Entity.Null;
                        var fac = em.GetComponentData<FactionTag>(entity).Value;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
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
            }
        }

        private void ProcessGatheringState(ref MinerState miner, EntityManager em, Entity entity, float3 pos, Faction fac, float dt)
        {
            miner.GatherTimer += dt;

            // Effective gather interval: faster when GatherSpeedMultiplier > 1
            float effectiveInterval = miner.GatherSpeedMultiplier > 0f
                ? GatherInterval / miner.GatherSpeedMultiplier
                : GatherInterval;

            if (miner.GatherTimer >= effectiveInterval)
            {
                miner.GatherTimer = 0f;

                // Check deposit still exists
                if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
                {
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                    }
                    return;
                }

                if (!em.HasComponent<IronDepositState>(miner.AssignedDeposit))
                {
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
                    }
                    return;
                }

                var depState = em.GetComponentData<IronDepositState>(miner.AssignedDeposit);

                // Extract iron from deposit (1 iron per gather)
                int toGather = math.min(IronPerGather, depState.RemainingIron);
                depState.RemainingIron -= toGather;
                miner.CurrentLoad += toGather;

                if (depState.RemainingIron <= 0)
                {
                    depState.RemainingIron = 0;
                    depState.Depleted = 1;
                }

                em.SetComponentData(miner.AssignedDeposit, depState);

                // Check if full or deposit depleted (carry capacity includes tech bonus)
                int effectiveMaxCarry = MaxCarryAmount + miner.CarryCapacityBonus;
                bool isFull = miner.CurrentLoad >= effectiveMaxCarry;
                bool depositDepleted = depState.Depleted == 1;

                if (isFull || depositDepleted)
                {
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac, _hallDropoffQuery, _hutDropoffQuery);
                    }
                    else
                    {
                        // Deposit depleted and nothing to carry
                        miner.State = MinerWorkState.Idle;
                        miner.AssignedDeposit = Entity.Null;
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
                    // Go back for more iron
                    miner.State = MinerWorkState.MovingToDeposit;
                    var depPos = em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position;

                    if (em.HasComponent<DesiredDestination>(entity))
                        em.SetComponentData(entity, new DesiredDestination { Position = depPos, Has = 1 });
                    else
                        em.AddComponentData(entity, new DesiredDestination { Position = depPos, Has = 1 });
                }
                else
                {
                    // Deposit depleted - try to find new deposit within LOS
                    float los = em.HasComponent<LineOfSight>(entity)
                        ? em.GetComponentData<LineOfSight>(entity).Radius
                        : 10f;

                    Entity nearbyDeposit = FindNearestDepositWithinRange(em, pos, los, _ironDepositQuery);
                    if (nearbyDeposit != Entity.Null)
                    {
                        miner.AssignedDeposit = nearbyDeposit;
                        miner.State = MinerWorkState.MovingToDeposit;

                        var newPos = em.GetComponentData<LocalTransform>(nearbyDeposit).Position;
                        if (em.HasComponent<DesiredDestination>(entity))
                            em.SetComponentData(entity, new DesiredDestination { Position = newPos, Has = 1 });
                        else
                            em.AddComponentData(entity, new DesiredDestination { Position = newPos, Has = 1 });
                    }
                    else
                    {
                        // No deposit in LOS — go idle
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

            // Set move destination to dropoff
            if (nearest != Entity.Null)
            {
                var dropoffPos = em.GetComponentData<LocalTransform>(nearest).Position;
                if (em.HasComponent<DesiredDestination>(minerEntity))
                    em.SetComponentData(minerEntity, new DesiredDestination { Position = dropoffPos, Has = 1 });
                else
                    em.AddComponentData(minerEntity, new DesiredDestination { Position = dropoffPos, Has = 1 });
            }
        }

        /// <summary>
        /// Find the nearest non-depleted iron deposit within a specific range (for LOS-based auto-find).
        /// </summary>
        private static Entity FindNearestDepositWithinRange(EntityManager em, float3 pos, float maxRange, EntityQuery depositQuery)
        {
            using var deposits = depositQuery.ToEntityArray(Allocator.Temp);
            using var states = depositQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var transforms = depositQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < deposits.Length; i++)
            {
                if (states[i].Depleted == 1) continue;

                float dist = DistXZ(pos, transforms[i].Position);
                if (dist < nearestDist && dist <= maxRange)
                {
                    nearest = deposits[i];
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Find the nearest non-depleted iron deposit within search radius.
        /// </summary>
        private static Entity FindNearestDeposit(EntityManager em, float3 pos, EntityQuery depositQuery)
        {
            using var deposits = depositQuery.ToEntityArray(Allocator.Temp);
            using var states = depositQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var transforms = depositQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < deposits.Length; i++)
            {
                if (states[i].Depleted == 1) continue;

                float dist = DistXZ(pos, transforms[i].Position);
                if (dist < nearestDist && dist <= SearchRadius)
                {
                    nearest = deposits[i];
                    nearestDist = dist;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Find the nearest non-depleted creature cadaver within search radius.
        /// </summary>
        private static Entity FindNearestCadaver(EntityManager em, float3 pos, EntityQuery cadaverQuery)
        {
            using var cadavers = cadaverQuery.ToEntityArray(Allocator.Temp);
            using var states = cadaverQuery.ToComponentDataArray<CadaverState>(Allocator.Temp);
            using var transforms = cadaverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < cadavers.Length; i++)
            {
                if (states[i].Depleted == 1) continue;

                float dist = DistXZ(pos, transforms[i].Position);
                if (dist < nearestDist && dist <= SearchRadius)
                {
                    nearest = cadavers[i];
                    nearestDist = dist;
                }
            }

            return nearest;
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
