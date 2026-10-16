// WallGateRegistrationSystem.cs
// task-112 M5 -- ensures every WallGateTag entity carries a
// GateRuntimeState component AND has its PortalNodeGround +
// PortalNodeRampart fields resolved against the current
// PortalGraphSingleton blob. Runs after WallPortalDetectionSystem so
// the spec list is up-to-date.
//
// Resolution strategy: find portal nodes whose PortalKind is
// KindGateGround / KindGateRampart AND whose CellIndex maps back to the
// gate's centre cell (matches the convention WallPortalDetectionSystem
// uses when emitting specs). Falls back to -1 if no node found (the
// next graph rebuild will resolve).
//
// Determinism: managed iteration on the main thread; portal scan walks
// blob node array in node-id ascending order (stable across machines).
//
// FINGERPRINT GATE (2026-10-15 perf): the resolution is a pure function of
// the graph generation, the nav grid layout and each gate's (entity,
// position, owner, has-state). This ran the full gates x portal-nodes scan
// on EVERY tick (2 ms a frame late game) although its inputs change only on
// a graph rebuild or a gate being built / converted / destroyed. It now
// hashes those inputs (O(gates)) and skips the scan when the hash matches
// the last pass — result-identical, since nothing else writes the node ids
// or OwnerId, and an unchanged input gives the same writes (none).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M5 gate registration. Adds <see cref="GateRuntimeState"/>
    /// to <c>WallGateTag</c> entities that lack one + resolves the two
    /// portal-node ids each gate links to.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WallPortalDetectionSystem))]
    [UpdateBefore(typeof(GateStateSystem))]
    public partial struct WallGateRegistrationSystem : ISystem
    {
        private EntityQuery _gateQuery;
        private ulong _lastFingerprint;
        private bool _haveFingerprint;

        public void OnCreate(ref SystemState state)
        {
            _gateQuery = SystemAPI.QueryBuilder()
                .WithAll<WallGateTag, LocalTransform, FactionTag>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_gateQuery.IsEmpty) return;
            if (!SystemAPI.HasSingleton<PortalGraphSingleton>()) return;
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;

            var graphSingleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (graphSingleton.Built == 0 || !graphSingleton.Graph.IsCreated) return;

            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var em = state.EntityManager;

            ref var graph = ref graphSingleton.Graph.Value;
            int nodeCount = graph.Nodes.Length;

            using var gateEntities = _gateQuery.ToEntityArray(Allocator.Temp);
            using var gateXfs = _gateQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var gateFactions = _gateQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Skip the scan when no input moved since the last pass.
            ulong fp = 1469598103934665603UL;
            fp = Mix(fp, (uint)SimCadence.Epoch);
            fp = Mix(fp, (uint)graphSingleton.Generation);
            fp = Mix(fp, (uint)nodeCount);
            fp = Mix(fp, (uint)grid.Width);
            fp = Mix(fp, math.asuint(grid.CellSize));
            fp = Mix(fp, math.asuint(grid.Origin.x));
            fp = Mix(fp, math.asuint(grid.Origin.z));
            fp = Mix(fp, (uint)gateEntities.Length);
            for (int gi = 0; gi < gateEntities.Length; gi++)
            {
                fp = Mix(fp, (uint)gateEntities[gi].Index);
                fp = Mix(fp, (uint)gateEntities[gi].Version);
                fp = Mix(fp, math.asuint(gateXfs[gi].Position.x));
                fp = Mix(fp, math.asuint(gateXfs[gi].Position.z));
                fp = Mix(fp, (uint)gateFactions[gi].Value);
                fp = Mix(fp, em.HasComponent<GateRuntimeState>(gateEntities[gi]) ? 1u : 0u);
            }
            if (_haveFingerprint && fp == _lastFingerprint) return;

            for (int gi = 0; gi < gateEntities.Length; gi++)
            {
                var gateEntity = gateEntities[gi];
                var gatePos = gateXfs[gi].Position;
                int ownerId = (int)gateFactions[gi].Value;

                // Centre cell + the two side cells (matches the convention
                // in WallPortalDetectionSystem.OnUpdate).
                int centreX = (int)math.floor((gatePos.x - grid.Origin.x) / grid.CellSize);
                int centreZ = (int)math.floor((gatePos.z - grid.Origin.z) / grid.CellSize);
                int insideX = centreX - 1;
                int outsideX = centreX + 1;

                int insideIdx = centreZ * grid.Width + insideX;
                int outsideIdx = centreZ * grid.Width + outsideX;

                int groundNode = -1;
                int rampartNode = -1;

                for (int n = 0; n < nodeCount; n++)
                {
                    var node = graph.Nodes[n];
                    if (node.OwnerId != ownerId) continue;
                    if (node.PortalKind == PortalNode.KindGateGround
                        && (node.CellIndex == insideIdx || node.CellIndex == outsideIdx)
                        && groundNode < 0)
                    {
                        groundNode = node.Id;
                    }
                    else if (node.PortalKind == PortalNode.KindGateRampart
                        && (node.CellIndex == insideIdx || node.CellIndex == outsideIdx)
                        && rampartNode < 0)
                    {
                        rampartNode = node.Id;
                    }
                }

                if (!em.HasComponent<GateRuntimeState>(gateEntity))
                {
                    em.AddComponentData(gateEntity, new GateRuntimeState
                    {
                        GateEntityId = gateEntity.Index,
                        OpenState = 1, // open by default; GateStateSystem closes when no friendly nearby
                        OwnerId = ownerId,
                        PortalNodeGround = groundNode,
                        PortalNodeRampart = rampartNode,
                        LastChangedTick = 0,
                    });
                }
                else
                {
                    var st = em.GetComponentData<GateRuntimeState>(gateEntity);
                    // Refresh portal-node ids on every rebuild generation.
                    bool changed = false;
                    if (st.PortalNodeGround != groundNode) { st.PortalNodeGround = groundNode; changed = true; }
                    if (st.PortalNodeRampart != rampartNode) { st.PortalNodeRampart = rampartNode; changed = true; }
                    if (st.OwnerId != ownerId) { st.OwnerId = ownerId; changed = true; }
                    if (changed) em.SetComponentData(gateEntity, st);
                }
            }

            // The pass above made every gate carry its state, so the input
            // the NEXT pass sees has has-state = 1 for each: hash that.
            ulong done = 1469598103934665603UL;
            done = Mix(done, (uint)SimCadence.Epoch);
            done = Mix(done, (uint)graphSingleton.Generation);
            done = Mix(done, (uint)nodeCount);
            done = Mix(done, (uint)grid.Width);
            done = Mix(done, math.asuint(grid.CellSize));
            done = Mix(done, math.asuint(grid.Origin.x));
            done = Mix(done, math.asuint(grid.Origin.z));
            done = Mix(done, (uint)gateEntities.Length);
            for (int gi = 0; gi < gateEntities.Length; gi++)
            {
                done = Mix(done, (uint)gateEntities[gi].Index);
                done = Mix(done, (uint)gateEntities[gi].Version);
                done = Mix(done, math.asuint(gateXfs[gi].Position.x));
                done = Mix(done, math.asuint(gateXfs[gi].Position.z));
                done = Mix(done, (uint)gateFactions[gi].Value);
                done = Mix(done, 1u);
            }
            _lastFingerprint = done;
            _haveFingerprint = true;
        }

        /// <summary>FNV-1a style fold of one 32-bit word.</summary>
        private static ulong Mix(ulong h, uint v)
        {
            for (int b = 0; b < 4; b++)
            {
                h ^= (v >> (b * 8)) & 0xFFu;
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
