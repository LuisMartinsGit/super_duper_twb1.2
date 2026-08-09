// File: Assets/Scripts/Systems/Work/GatheringSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
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
    ///
    /// Veilstone patches: a worker sent to a veilstone node mines the closest
    /// node of that PATCH to the worker's current position, not necessarily
    /// the clicked node. Candidates must be discovered by the worker's
    /// faction and belong to the same patch (BFS cluster over hops of
    /// VeilstoneOutcropping.PatchClusterRadius — the map-wide patch
    /// definition).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GatheringSystem : ISystem
    {
        private const float GatherRange = 5f;

        private EntityQuery _outcroppingQuery;

        // NOT Burst-compiled: the params-array GetEntityQuery overload
        // allocates a managed ComponentType[] (Burst error BC1028).
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _outcroppingQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<VeilstoneOutcroppingState>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        // NOT Burst-compiled: the veilstone-patch retarget consults the managed
        // FogOfWarManager (via FogOfWarSystem.IsRevealedToFaction) for the
        // "node must be discovered" rule.
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

                // Detect resource type: veilstone node (VeilstoneOutcroppingTag),
                // veilsteel sharp-crystals node (VeilsteelDepositTag), or iron mine
                bool isBorderNode = em.HasComponent<VeilstoneOutcroppingTag>(resourceNode);
                byte resourceType = isBorderNode ? (byte)1
                    : em.HasComponent<VeilsteelDepositTag>(resourceNode) ? (byte)2 : (byte)0;

                // Determine action based on miner state
                if (minerState.State == MinerWorkState.Idle ||
                    minerState.State == MinerWorkState.MovingToDeposit)
                {
                    // Veilstone patch: retarget to the closest discovered node of
                    // the clicked node's patch relative to THIS worker. Gated on
                    // AssignedDeposit != resourceNode so the scan runs once per
                    // command, not every frame while walking (the command is
                    // rewritten below to point at the chosen node).
                    if (isBorderNode && minerState.AssignedDeposit != resourceNode)
                    {
                        var best = FindClosestDiscoveredPatchNode(em, resourceNode, myPos, entity);
                        if (best != Entity.Null && best != resourceNode)
                        {
                            resourceNode = best;
                            // Rewrite the command IN PLACE (SetComponentData is
                            // non-structural, legal during iteration, and the
                            // component is guaranteed present here). Deferring
                            // this via ECB raced against same-frame command
                            // clears — a move order removes GatherCommand on
                            // the main thread, and playback then threw on the
                            // missing component (unbalanced profiler samples
                            // in EntityCommandBuffer.Playback).
                            em.SetComponentData(entity, new GatherCommand
                            {
                                ResourceNode = best
                            });
                        }
                    }

                    // Move to resource node
                    var nodePos = em.GetComponentData<LocalTransform>(resourceNode).Position;
                    var dist = DistXZ(myPos, nodePos);

                    if (dist > GatherRange)
                    {
                        // Update state and set destination
                        minerState.State = MinerWorkState.MovingToDeposit;
                        minerState.AssignedDeposit = resourceNode;
                        minerState.GatheringResource = resourceType;
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
                        minerState.GatheringResource = resourceType;
                        minerState.GatherTimer = 0f;
                        ecb.SetComponent(entity, minerState);

                        // Plant and face the node
                        TheWaningBorder.Core.TargetGeometry.StopAndFace(
                            ecb, em, entity, nodePos, SystemAPI.Time.DeltaTime);

                        // Remove GatherCommand - MiningSystem takes over from here
                        ecb.RemoveComponent<GatherCommand>(entity);
                    }
                }
                else if (minerState.State == MinerWorkState.Gathering)
                {
                    // Already gathering - remove command, MiningSystem handles the rest
                    ecb.RemoveComponent<GatherCommand>(entity);
                }
            }
        }

        /// <summary>
        /// Among the veilstone nodes of <paramref name="clickedNode"/>'s patch,
        /// return the one closest to <paramref name="workerPos"/> that still
        /// holds veilstone and has been discovered by the worker's faction.
        /// Patch membership = BFS flood from the clicked node with hops of at
        /// most VeilstoneOutcropping.PatchClusterRadius. Returns Entity.Null
        /// when no node qualifies — the caller then keeps the clicked node.
        /// </summary>
        private Entity FindClosestDiscoveredPatchNode(
            EntityManager em, Entity clickedNode, float3 workerPos, Entity worker)
        {
            using var nodes = _outcroppingQuery.ToEntityArray(Allocator.Temp);
            using var states = _outcroppingQuery.ToComponentDataArray<VeilstoneOutcroppingState>(Allocator.Temp);
            using var transforms = _outcroppingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int n = nodes.Length;
            int clickedIdx = -1;
            for (int i = 0; i < n; i++)
            {
                if (nodes[i] == clickedNode) { clickedIdx = i; break; }
            }
            if (clickedIdx < 0) return Entity.Null;

            float cluster = TheWaningBorder.Entities.VeilstoneOutcropping.PatchClusterRadius;
            float clusterSqr = cluster * cluster;

            // Flood-fill the patch. Depleted nodes still act as connectors so
            // a half-mined patch doesn't split into disconnected islands.
            var inPatch = new NativeArray<bool>(n, Allocator.Temp);
            var frontier = new NativeList<int>(n, Allocator.Temp);
            inPatch[clickedIdx] = true;
            frontier.Add(clickedIdx);
            while (frontier.Length > 0)
            {
                int cur = frontier[frontier.Length - 1];
                frontier.RemoveAt(frontier.Length - 1);
                float3 curPos = transforms[cur].Position;

                for (int i = 0; i < n; i++)
                {
                    if (inPatch[i]) continue;
                    float dx = transforms[i].Position.x - curPos.x;
                    float dz = transforms[i].Position.z - curPos.z;
                    if (dx * dx + dz * dz <= clusterSqr)
                    {
                        inPatch[i] = true;
                        frontier.Add(i);
                    }
                }
            }

            bool hasFaction = em.HasComponent<FactionTag>(worker);
            Faction workerFaction = hasFaction
                ? em.GetComponentData<FactionTag>(worker).Value
                : default;

            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!inPatch[i]) continue;
                if (states[i].Depleted == 1 || states[i].RemainingVeilstone <= 0) continue;
                if (hasFaction && !Visibility.FogOfWarSystem.IsRevealedToFaction(
                        workerFaction, transforms[i].Position)) continue;

                float dist = DistXZ(workerPos, transforms[i].Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = nodes[i];
                }
            }

            inPatch.Dispose();
            frontier.Dispose();
            return best;
        }
    }
}