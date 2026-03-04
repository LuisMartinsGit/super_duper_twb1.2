// File: Assets/Scripts/Systems/Work/GatheringSystem.cs
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Processes GatherCommand components issued through CommandGateway.
    /// 
    /// This system handles player-initiated gather commands (right-click on resource).
    /// Works alongside MiningSystem which handles autonomous miner AI behavior.
    /// 
    /// Workflow:
    /// 1. Player right-clicks on resource node with miner selected
    /// 2. CommandGateway.IssueGather() adds GatherCommand component
    /// 3. This system moves miner to resource and updates MinerState
    /// 4. MiningSystem takes over once miner reaches Gathering state
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GatheringSystem : ISystem
    {
        private const float GatherRange = 5f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var em = state.EntityManager;

            foreach (var (transform, gatherCmd, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<GatherCommand>>()
                .WithEntityAccess())
            {
                var resourceNode = gatherCmd.ValueRO.ResourceNode;
                var depositLocation = gatherCmd.ValueRO.DepositLocation;

                // Validate resource node still exists
                if (!em.Exists(resourceNode))
                {
                    ecb.RemoveComponent<GatherCommand>(entity);
                    continue;
                }

                // Check if unit has MinerState component (required for gathering)
                if (!em.HasComponent<MinerState>(entity))
                {
                    ecb.RemoveComponent<GatherCommand>(entity);
                    continue;
                }

                var minerState = em.GetComponentData<MinerState>(entity);
                var myPos = transform.ValueRO.Position;

                // Detect resource type: crystal node (CadaverTag) or iron mine
                bool isCrystalNode = em.HasComponent<CadaverTag>(resourceNode);

                // Determine action based on miner state
                if (minerState.State == MinerWorkState.Idle ||
                    minerState.State == MinerWorkState.MovingToDeposit)
                {
                    // Move to resource node
                    var nodePos = em.GetComponentData<LocalTransform>(resourceNode).Position;
                    var dist = math.distance(myPos, nodePos);

                    if (dist > GatherRange)
                    {
                        // Update state and set destination
                        minerState.State = MinerWorkState.MovingToDeposit;
                        minerState.AssignedDeposit = resourceNode;
                        minerState.GatheringResource = isCrystalNode ? (byte)1 : (byte)0;
                        ecb.SetComponent(entity, minerState);

                        if (!em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.AddComponent(entity, new DesiredDestination
                            {
                                Position = nodePos,
                                Has = 1
                            });
                        }
                        else
                        {
                            ecb.SetComponent(entity, new DesiredDestination
                            {
                                Position = nodePos,
                                Has = 1
                            });
                        }
                    }
                    else
                    {
                        // Reached resource - start gathering
                        minerState.State = MinerWorkState.Gathering;
                        minerState.AssignedDeposit = resourceNode;
                        minerState.GatheringResource = isCrystalNode ? (byte)1 : (byte)0;
                        minerState.GatherTimer = 0f;
                        ecb.SetComponent(entity, minerState);

                        // Stop moving
                        if (em.HasComponent<DesiredDestination>(entity))
                        {
                            ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                        }

                        // Remove GatherCommand - MiningSystem takes over from here
                        ecb.RemoveComponent<GatherCommand>(entity);
                    }
                }
                else if (minerState.State == MinerWorkState.Gathering)
                {
                    // Already gathering - remove command, MiningSystem handles the rest
                    ecb.RemoveComponent<GatherCommand>(entity);
                }
                else if (minerState.State == MinerWorkState.ReturningToBase)
                {
                    // Let miner finish returning, then it will handle the new command
                    // Keep the GatherCommand for when they're done
                }
            }
        }
    }
}