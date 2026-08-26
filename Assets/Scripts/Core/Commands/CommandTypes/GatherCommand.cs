// GatherCommand.cs
// Gather/mining command component and execution logic

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Systems.Movement;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a gather command for a miner/worker unit.
    /// When attached to an entity, gathering systems will process it.
    /// </summary>
    public struct GatherCommand : IComponentData
    {
        /// <summary>The resource node to gather from (e.g., Iron Mine)</summary>
        public Entity ResourceNode;
    }

    /// <summary>
    /// Helper class for executing gather commands
    /// </summary>
    public static class GatherCommandHelper
    {
        /// <summary>
        /// Execute a gather command on a miner unit.
        /// Clears conflicting commands and sets up gathering state.
        /// </summary>
        public static void Execute(EntityManager em, Entity miner, Entity resourceNode)
        {
            if (!em.Exists(miner) || !em.Exists(resourceNode)) return;

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, miner);

            // Set up gather command
            SetupGather(em, miner, resourceNode);
        }

        /// <summary>
        /// Check if a gather command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity miner, Entity resourceNode)
        {
            if (!em.Exists(miner) || !em.Exists(resourceNode)) return false;

            // Check if miner has mining capability
            if (!em.HasComponent<MinerTag>(miner)) return false;

            // Check if resource is valid and has resources left
            // Could add more validation here

            return true;
        }

        private static void SetupGather(EntityManager em, Entity miner, Entity resourceNode)
        {
            var cmd = new GatherCommand
            {
                ResourceNode = resourceNode
            };

            if (!em.HasComponent<GatherCommand>(miner))
                em.AddComponentData(miner, cmd);
                else
                    em.SetComponentData(miner, cmd);

            // Destination = a spot BESIDE the node, not the node itself. A
            // resource node stamps its own build cell impassable, so a
            // destination at its transform can never be arrived at: the worker
            // grinds against it, and FlowFollowSystem's line-of-sight test
            // fails on an impassable goal cell so the approach is not even a
            // straight line. See MiningReach.
            if (em.HasComponent<Unity.Transforms.LocalTransform>(resourceNode)
                && em.HasComponent<Unity.Transforms.LocalTransform>(miner))
            {
                var minerPos = em.GetComponentData<Unity.Transforms.LocalTransform>(miner).Position;
                TheWaningBorder.Systems.Work.MiningReach.TryGetMiningStand(
                    em, resourceNode, minerPos, out var stand);

                if (em.HasComponent<DesiredDestination>(miner))
                {
                    em.SetComponentData(miner, new DesiredDestination
                    {
                        Position = stand,
                        Has = 1
                    });
                }
                else
                {
                    em.AddComponentData(miner, new DesiredDestination
                    {
                        Position = stand,
                        Has = 1
                    });
                }

                // (Pre-warm removed with the navmesh migration — PR3.
                // NavMeshPathRequestSystem picks up the new DesiredDestination.)
            }
        }
    }
}