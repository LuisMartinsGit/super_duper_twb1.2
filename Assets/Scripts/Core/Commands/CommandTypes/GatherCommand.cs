// GatherCommand.cs
// Gather/mining command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/GatherCommand.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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
        
        /// <summary>Where to deposit gathered resources (e.g., GatherersHut, Hall)</summary>
        public Entity DepositLocation;
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
        public static void Execute(EntityManager em, Entity miner, Entity resourceNode, Entity depositLocation)
        {
            if (!em.Exists(miner) || !em.Exists(resourceNode)) return;

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, miner);

            // Set up gather command
            SetupGather(em, miner, resourceNode, depositLocation);
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

        /// <summary>
        /// Find nearest non-depleted resource deposit (iron mine or cadaver) within the
        /// miner's LineOfSight radius. Returns Entity.Null if nothing is in range.
        /// </summary>
        public static Entity FindNearestDeposit(EntityManager em, Entity miner, Faction faction)
        {
            if (!em.Exists(miner)) return Entity.Null;
            if (!em.HasComponent<LocalTransform>(miner)) return Entity.Null;

            float3 minerPos = em.GetComponentData<LocalTransform>(miner).Position;

            // Use LineOfSight radius if available, otherwise a sensible default
            float searchRadius = em.HasComponent<LineOfSight>(miner)
                ? em.GetComponentData<LineOfSight>(miner).Radius
                : 30f;

            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            // Search iron deposits
            var ironQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using (var ironEntities = ironQuery.ToEntityArray(Allocator.Temp))
            using (var ironStates = ironQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp))
            using (var ironTransforms = ironQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ironEntities.Length; i++)
                {
                    if (ironStates[i].Depleted == 1) continue;

                    float dist = DistXZ(minerPos, ironTransforms[i].Position);
                    if (dist <= searchRadius && dist < nearestDist)
                    {
                        nearest = ironEntities[i];
                        nearestDist = dist;
                    }
                }
            }

            // Search cadavers (crystal sources)
            var cadaverQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<CadaverState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using (var cadaverEntities = cadaverQuery.ToEntityArray(Allocator.Temp))
            using (var cadaverStates = cadaverQuery.ToComponentDataArray<CadaverState>(Allocator.Temp))
            using (var cadaverTransforms = cadaverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < cadaverEntities.Length; i++)
                {
                    if (cadaverStates[i].Depleted == 1) continue;

                    float dist = DistXZ(minerPos, cadaverTransforms[i].Position);
                    if (dist <= searchRadius && dist < nearestDist)
                    {
                        nearest = cadaverEntities[i];
                        nearestDist = dist;
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// Find the nearest non-depleted resource deposit (iron mine or cadaver) within
        /// a radius of a given world position. Used by input system for click-near-deposit.
        /// </summary>
        public static Entity FindNearestDepositNearPosition(EntityManager em, float3 position, float searchRadius)
        {
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            // Search iron deposits
            var ironQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using (var ironEntities = ironQuery.ToEntityArray(Allocator.Temp))
            using (var ironStates = ironQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp))
            using (var ironTransforms = ironQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ironEntities.Length; i++)
                {
                    if (ironStates[i].Depleted == 1) continue;

                    float dist = DistXZ(position, ironTransforms[i].Position);
                    if (dist <= searchRadius && dist < nearestDist)
                    {
                        nearest = ironEntities[i];
                        nearestDist = dist;
                    }
                }
            }

            // Search cadavers (crystal sources)
            var cadaverQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<CadaverTag>(),
                ComponentType.ReadOnly<CadaverState>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using (var cadaverEntities = cadaverQuery.ToEntityArray(Allocator.Temp))
            using (var cadaverStates = cadaverQuery.ToComponentDataArray<CadaverState>(Allocator.Temp))
            using (var cadaverTransforms = cadaverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < cadaverEntities.Length; i++)
                {
                    if (cadaverStates[i].Depleted == 1) continue;

                    float dist = DistXZ(position, cadaverTransforms[i].Position);
                    if (dist <= searchRadius && dist < nearestDist)
                    {
                        nearest = cadaverEntities[i];
                        nearestDist = dist;
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// XZ-only (horizontal) distance -- ignores Y so terrain height doesn't break range checks.
        /// </summary>
        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }

        private static void SetupGather(EntityManager em, Entity miner, Entity resourceNode, Entity depositLocation)
        {
            var cmd = new GatherCommand
            {
                ResourceNode = resourceNode,
                DepositLocation = depositLocation
            };

            if (!em.HasComponent<GatherCommand>(miner))
                em.AddComponentData(miner, cmd);
                else
                    em.SetComponentData(miner, cmd);

            // Set destination to resource node position
            if (em.HasComponent<Unity.Transforms.LocalTransform>(resourceNode))
            {
                var nodePos = em.GetComponentData<Unity.Transforms.LocalTransform>(resourceNode).Position;

                if (em.HasComponent<DesiredDestination>(miner))
                {
                    em.SetComponentData(miner, new DesiredDestination
                    {
                        Position = nodePos,
                        Has = 1
                    });
                }
                else
                {
                    em.AddComponentData(miner, new DesiredDestination
                    {
                        Position = nodePos,
                        Has = 1
                    });
                }

                // (Pre-warm removed with the navmesh migration — PR3.
                // NavMeshPathRequestSystem picks up the new DesiredDestination.)
            }
        }
    }
}