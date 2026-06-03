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
// Location: Assets/Scripts/Systems/Navigation/WallGateRegistrationSystem.cs

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
        }
    }
}
