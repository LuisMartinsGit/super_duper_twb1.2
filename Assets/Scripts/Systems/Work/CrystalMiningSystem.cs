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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MinerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

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
                        ProcessMovingToCadaver(ref miner, em, entity, pos);
                        break;

                    case MinerWorkState.Gathering:
                        ProcessGatheringCrystal(ref miner, em, entity, fac, dt);
                        break;

                    case MinerWorkState.ReturningToBase:
                        ProcessReturningToBase(ref miner, em, entity, pos, fac);
                        break;

                    case MinerWorkState.Idle:
                        // Idle crystal miners get reset - MiningSystem will reassign
                        miner.GatheringResource = 0;
                        break;
                }
            }
        }

        private void ProcessMovingToCadaver(ref MinerState miner, EntityManager em, Entity entity, float3 pos)
        {
            // Check if cadaver still exists
            if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
            {
                miner.State = MinerWorkState.Idle;
                miner.GatheringResource = 0;
                miner.AssignedDeposit = Entity.Null;
                return;
            }

            // Check if cadaver is depleted
            if (em.HasComponent<CadaverState>(miner.AssignedDeposit))
            {
                var cadaverState = em.GetComponentData<CadaverState>(miner.AssignedDeposit);
                if (cadaverState.Depleted == 1)
                {
                    miner.State = MinerWorkState.Idle;
                    miner.GatheringResource = 0;
                    miner.AssignedDeposit = Entity.Null;
                    return;
                }
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

        private void ProcessGatheringCrystal(ref MinerState miner, EntityManager em, Entity entity, Faction fac, float dt)
        {
            miner.GatherTimer += dt;

            if (miner.GatherTimer >= GatherInterval)
            {
                miner.GatherTimer = 0f;

                // Check crystal node still exists and has crystal
                if (miner.AssignedDeposit == Entity.Null || !em.Exists(miner.AssignedDeposit))
                {
                    // Node gone - deposit what we have or go idle
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac);
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
                        SetDropoffDestination(ref miner, em, entity, fac);
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

                if (cadaverState.RemainingCrystal <= 0)
                {
                    cadaverState.RemainingCrystal = 0;
                    cadaverState.Depleted = 1;
                }

                em.SetComponentData(miner.AssignedDeposit, cadaverState);

                // Propagate mining noise to nearest crystal main node
                PropagateNoise(em, em.GetComponentData<LocalTransform>(miner.AssignedDeposit).Position);

                // Only return to base when carrying max load or node is depleted
                bool isFull = miner.CurrentLoad >= MaxCarryAmount;
                bool nodeDepleted = cadaverState.Depleted == 1;

                if (isFull || nodeDepleted)
                {
                    if (miner.CurrentLoad > 0)
                    {
                        miner.State = MinerWorkState.ReturningToBase;
                        SetDropoffDestination(ref miner, em, entity, fac);
                    }
                    else
                    {
                        // Node depleted and nothing to carry - go idle
                        miner.State = MinerWorkState.Idle;
                        miner.GatheringResource = 0;
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
                // Try to find a new dropoff
                SetDropoffDestination(ref miner, em, entity, fac);
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
                        em.AddComponentData(entity, new DesiredDestination
                        {
                            Position = cadaverPos,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // Cadaver depleted - go idle, MiningSystem will find next target
                    miner.State = MinerWorkState.Idle;
                    miner.GatheringResource = 0;
                    miner.AssignedDeposit = Entity.Null;
                    miner.DropoffTarget = Entity.Null;
                }
            }
        }

        /// <summary>
        /// Find the nearest Hall or GathererHut of the miner's faction and set it as dropoff target.
        /// </summary>
        private static void SetDropoffDestination(ref MinerState miner, EntityManager em, Entity minerEntity, Faction fac)
        {
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;
            float3 minerPos = em.GetComponentData<LocalTransform>(minerEntity).Position;

            // Search for Halls (exclude under-construction)
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );

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
            var hutQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );

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
                    em.AddComponentData(minerEntity, new DesiredDestination
                    {
                        Position = dropoffPos,
                        Has = 1
                    });
                }
            }
        }

        /// <summary>
        /// Propagate mining noise to the nearest CrystalMainNode within range.
        /// Alerts the crystal curse faction when players mine cadavers near nodes.
        /// </summary>
        private const float NoiseRange = 40f;

        private static void PropagateNoise(EntityManager em, float3 cadaverPos)
        {
            var nodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<CrystalMiningNoise>()
            );

            using var nodeEntities = nodeQuery.ToEntityArray(Allocator.Temp);
            using var nodeTransforms = nodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity nearestNode = Entity.Null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < nodeEntities.Length; i++)
            {
                float dist = math.distance(
                    new float2(cadaverPos.x, cadaverPos.z),
                    new float2(nodeTransforms[i].Position.x, nodeTransforms[i].Position.z));

                if (dist <= NoiseRange && dist < nearestDist)
                {
                    nearestNode = nodeEntities[i];
                    nearestDist = dist;
                }
            }

            if (nearestNode != Entity.Null)
            {
                var noise = em.GetComponentData<CrystalMiningNoise>(nearestNode);
                noise.LocalNoise += 1;
                em.SetComponentData(nearestNode, noise);
            }
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
