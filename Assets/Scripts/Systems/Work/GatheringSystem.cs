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
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GatheringSystem : ISystem
    {
        // Range lives in MiningReach — see that file for why this system may
        // not carry its own.

        /// <summary>How far the iron / veilsteel divert looks for a minable
        /// replacement when the clicked node is walled in. Unlike the veilstone
        /// path (a BFS flood that walks a patch of any size one adjacency hop
        /// at a time) this is a flat radius scan, so it must span a whole patch
        /// on its own to reach the perimeter from a buried interior node.</summary>
        private const float DivertSearchRadius = 12f;

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

            // Read fresh each tick — never cache the struct. SpatialHashRebuildSystem
            // reallocates the map when the unit count outgrows it, so a stale
            // copy is a use-after-dispose. Lets the stand picker step around a
            // slot another worker already occupies.
            SystemAPI.TryGetSingleton<NavSpatialHash>(out var hash);

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
                    // Is this the tick the walk is SET UP, or one of the many
                    // ticks during it? The GatherCommand is not always consumed
                    // after one tick — VeilstoneMiningSystem never removes it —
                    // so on veilstone this block ran every frame of the walk.
                    //
                    // That matters because the stand point is derived from the
                    // worker's CURRENT position: recomputing it each tick made
                    // the goal slide sideways as the worker did, so lateral
                    // movement could never close the gap and the worker
                    // "stepped aside, then stopped". It also clobbered the
                    // mining systems' own re-pick, because this writes through
                    // the end-of-frame ECB and therefore lands last.
                    //
                    // The stand is LATCHED on assignment; during the walk the
                    // mining systems own the destination.
                    bool freshAssignment = minerState.State == MinerWorkState.Idle
                        || minerState.AssignedDeposit != resourceNode;
                    // YOU GET THE NODE YOU CLICKED. This used to retarget
                    // unconditionally to the closest node of the clicked node's
                    // patch relative to THIS worker — a spread heuristic that
                    // read as the game ignoring the click, because the patch is
                    // a BFS flood over PatchClusterRadius (12 m) hops, so it
                    // could hand the worker a node in a visibly different patch
                    // than the one whose resource bar the player was hovering.
                    //
                    // Now it diverts ONLY when the clicked node genuinely cannot
                    // be mined — walled in by other nodes, a building, or
                    // unwalkable ground — which is the one case where honouring
                    // the click would mean sending the worker to orbit forever.
                    // Gated on AssignedDeposit != resourceNode so the scan runs
                    // once per command, not every frame while walking.
                    if (minerState.AssignedDeposit != resourceNode
                        && !MiningReach.IsMinable(em, resourceNode, myPos))
                    {
                        var best = isBorderNode
                            ? FindClosestMinablePatchNode(em, resourceNode, myPos, entity)
                            : FindClosestMinableNeighbourNode(em, resourceNode, myPos);
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

                    // Move to resource node. Distance is measured to the node's
                    // SURFACE against the shared MiningReach.GatherRange — this
                    // used to be its own 5 m CENTRE-to-centre test, which is
                    // 2.5 build cells and, because this system runs before
                    // MiningSystem and flips the miner straight to Gathering,
                    // overrode the mining systems' tighter 2.5 m surface rule.
                    // A worker that happened to be within 5 m never took a step.
                    var nodePos = em.GetComponentData<LocalTransform>(resourceNode).Position;
                    float dist = TheWaningBorder.Core.TargetGeometry
                        .SurfaceDistXZ(em, myPos, resourceNode);

                    if (dist > MiningReach.GatherRange)
                    {
                        // Update state — only on the setup tick. Re-writing this
                        // every frame of the walk pushed a STALE snapshot of
                        // MinerState (captured before the mining systems ran)
                        // back over their work through the end-of-frame ECB,
                        // which lands last. GatherTimer progress and any state
                        // they had advanced went with it.
                        if (freshAssignment)
                        {
                            minerState.State = MinerWorkState.MovingToDeposit;
                            minerState.AssignedDeposit = resourceNode;
                            minerState.GatheringResource = resourceType;
                            ecb.SetComponent(entity, minerState);
                        }

                        // Walk to a spot BESIDE the node, never the node's own
                        // cell — that cell is impassable, so a destination
                        // inside it can never be arrived at and defeats
                        // FlowFollowSystem's straight-line check.
                        //
                        // Only on the tick the assignment is made: see
                        // freshAssignment above for why re-deriving this mid-walk
                        // makes the goal run away from the worker.
                        if (freshAssignment)
                        {
                            MiningReach.TryGetMiningStand(em, resourceNode, entity, myPos, in hash,
                                out float3 stand);

                            if (!em.HasComponent<DesiredDestination>(entity))
                            {
                                ecb.AddComponent(entity, new DesiredDestination
                                {
                                    Position = stand,
                                    Has = 1
                                });
                            }
                            else
                            {
                                ecb.SetComponent(entity, new DesiredDestination
                                {
                                    Position = stand,
                                    Has = 1
                                });
                            }
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
        /// Divert fallback for iron and veilsteel, which have no patch graph:
        /// the nearest node of the SAME kind, within one patch-cluster radius
        /// of the clicked one, that can actually be mined. Iron patches are
        /// solid blocks of build cells too, so an interior node is walled in by
        /// its neighbours exactly as a veilstone one is — without this, an iron
        /// click on the middle of a patch had no rescue at all and the worker
        /// orbited until stuck recovery unassigned it seconds later.
        /// </summary>
        private Entity FindClosestMinableNeighbourNode(
            EntityManager em, Entity clickedNode, float3 workerPos)
        {
            if (!em.HasComponent<LocalTransform>(clickedNode)) return Entity.Null;
            float3 clickedPos = em.GetComponentData<LocalTransform>(clickedNode).Position;

            bool wantVeilsteel = em.HasComponent<VeilsteelDepositTag>(clickedNode);
            var query = wantVeilsteel
                ? em.CreateEntityQuery(
                    ComponentType.ReadOnly<VeilsteelDepositTag>(),
                    ComponentType.ReadOnly<IronDepositState>(),
                    ComponentType.ReadOnly<LocalTransform>())
                : em.CreateEntityQuery(
                    ComponentType.ReadOnly<IronMineTag>(),
                    ComponentType.ReadOnly<IronDepositState>(),
                    ComponentType.ReadOnly<LocalTransform>());

            using var nodes = query.ToEntityArray(Allocator.Temp);
            using var states = query.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            query.Dispose();

            const float reach = DivertSearchRadius;
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == clickedNode) continue;
                if (states[i].Depleted == 1 || states[i].RemainingIron <= 0) continue;
                if (DistXZ(clickedPos, transforms[i].Position) > reach) continue;
                if (!MiningReach.IsMinable(em, nodes[i], workerPos)) continue;

                float dist = DistXZ(workerPos, transforms[i].Position);
                if (dist < bestDist) { bestDist = dist; best = nodes[i]; }
            }
            return best;
        }

        /// <summary>
        /// Fallback for a clicked node that cannot be mined: among the
        /// veilstone nodes of its patch, the one closest to
        /// <paramref name="workerPos"/> that still holds veilstone, has been
        /// discovered by the worker's faction, AND has somewhere legal to stand.
        /// Patch membership = BFS flood from the clicked node with hops of at
        /// most VeilstoneOutcropping.PatchClusterRadius. Returns Entity.Null
        /// when no node qualifies — the caller then keeps the clicked node.
        /// </summary>
        private Entity FindClosestMinablePatchNode(
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
                // Only offer somewhere the worker can actually work from —
                // diverting one unreachable node onto another is no rescue.
                if (!MiningReach.IsMinable(em, nodes[i], workerPos)) continue;

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