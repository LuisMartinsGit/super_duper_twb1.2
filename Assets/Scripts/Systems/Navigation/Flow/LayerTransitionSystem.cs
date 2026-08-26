// LayerTransitionSystem.cs
// task-112 M5 -- replaces WallDoorAccessSystem. When a unit's
// NavPathPortal cursor points at a climb / gate portal AND the unit is
// within entry distance of the source cell, this system:
//   1. Validates eligibility (owner faction matches portal owner, open
//      state set on the PortalOwnerBitsMirror, climb-permission on the
//      unit's traversal profile). If ineligible, removes the path
//      portal entry + re-issues a NavPathRequest so the abstract
//      pathfinder re-routes (R4 backstop).
//   2. Adds a LayerTraversalState component recording the
//      start/end world position + the from/to layer.
//   3. Animates the unit's LocalTransform.Position along the link over
//      LayerTraversalDuration sim seconds. At Progress >= 0.5, flips
//      NavLayerIndex.CurrentLayer to the target layer. At Progress >=
//      1.0, removes the LayerTraversalState + advances
//      NavPathResult.CurrentPortalIndex.
//
// UnitIntegratorSystem checks for the presence of LayerTraversalState
// and skips per-unit integration while the transition is in flight (so
// the integrator and the layer-transition animator don't fight over
// LocalTransform.Position).
//
// Determinism (DR-11):
//   * Progress integrated by state.WorldUnmanaged.Time.DeltaTime --
//     the fixed-step constant in the sim group, NEVER Time.deltaTime.
//   * Eligibility checks integer-only: portal-owner == unit-owner +
//     PortalOwnerBitsMirror open-bit + TraversalProfile.CanClimb.
//   * Unit iteration uses entity.Index ascending order so concurrent
//     traversal starts on the same tick happen in a stable sequence.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M5 layer-transition / portal-traversal driver. Runs
    /// after the integrator so it can observe and override the unit's
    /// position after movement; runs before downstream wall-garrison
    /// behaviour so the layer flip is observable in the same tick.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UnitIntegratorSystem))]
    public partial struct LayerTransitionSystem : ISystem
    {
        /// <summary>Maximum world-space distance for "unit reached the
        /// portal source cell" check (matches the legacy
        /// <c>WallDoorAccessSystem.ArriveDoor</c>).</summary>
        public const float EntryDistance = 2.5f;
        /// <summary>Rate at which <see cref="LayerTraversalState.Progress"/>
        /// advances per second. 1/0.6 = ~1.67 -- 0.6s per traversal,
        /// matches the legacy door teleport's perceived duration but
        /// animated.</summary>
        public const float TransitionRate = 1.6666666f;
        /// <summary>Deck Y elevation (m). Layer-1 (rampart) units stand
        /// at this world Y; copied from
        /// <see cref="WallDoorAccessSystem"/> for parity.</summary>
        public const float DeckY = 4.0f;

        private EntityQuery _unitWithPathQuery;

        public void OnCreate(ref SystemState state)
        {
            _unitWithPathQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, NavPathResult, NavPathPortal, NavLayerIndex>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<PortalGraphSingleton>()) return;
            if (!SystemAPI.HasSingleton<PortalOwnerBitsMirror>()) return;
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;

            var graphSingleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (graphSingleton.Built == 0 || !graphSingleton.Graph.IsCreated) return;
            var mirror = SystemAPI.GetSingleton<PortalOwnerBitsMirror>();
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var em = state.EntityManager;
            // task-112 M5 DR-11: dt is the fixed-step constant in the sim
            // group. NEVER use Time.deltaTime / Time.realtimeSinceStartup.
            float dt = (float)state.WorldUnmanaged.Time.DeltaTime;

            ref var graph = ref graphSingleton.Graph.Value;
            int nodeCount = graph.Nodes.Length;

            // Snapshot units in entity.Index ascending order.
            using var unitEntities = _unitWithPathQuery.ToEntityArray(Allocator.Temp);
            var order = new NativeArray<int>(unitEntities.Length, Allocator.Temp);
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = 1; i < order.Length; i++)
            {
                int k = order[i];
                int j = i - 1;
                while (j >= 0 && unitEntities[order[j]].Index > unitEntities[k].Index)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = k;
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int oi = 0; oi < order.Length; oi++)
            {
                int srcIdx = order[oi];
                var entity = unitEntities[srcIdx];

                var xf = em.GetComponentData<LocalTransform>(entity);
                var nli = em.GetComponentData<NavLayerIndex>(entity);

                // ── Branch A: traversal already in progress -- advance it ──
                if (em.HasComponent<LayerTraversalState>(entity))
                {
                    var ts = em.GetComponentData<LayerTraversalState>(entity);

                    // Backstop eligibility re-check (R4 -- gate may have
                    // closed mid-transition; abort + drop the unit at the
                    // last good position).
                    if (!IsPortalAdmissible(ref graph, in mirror, ts.PortalId,
                            GetUnitOwnerId(em, entity), GetUnitCanClimb(em, entity)))
                    {
                        em.RemoveComponent<LayerTraversalState>(entity);
                        continue;
                    }

                    ts.Progress += TransitionRate * dt;
                    if (ts.Progress >= 1.0f) ts.Progress = 1.0f;

                    // Layer flip at midpoint.
                    if (ts.Progress >= 0.5f && nli.Layer != ts.ToLayer)
                    {
                        nli.Layer = ts.ToLayer;
                        em.SetComponentData(entity, nli);
                    }

                    // Animate position.
                    var lerped = math.lerp(ts.StartPos, ts.EndPos, ts.Progress);
                    xf.Position = lerped;
                    em.SetComponentData(entity, xf);

                    if (ts.Progress >= 1.0f)
                    {
                        // Complete traversal: clear state + advance the path
                        // cursor past this portal.
                        if (em.HasComponent<NavPathResult>(entity))
                        {
                            var res = em.GetComponentData<NavPathResult>(entity);
                            res.CurrentPortalIndex = math.min(res.CurrentPortalIndex + 1, res.Length);
                            em.SetComponentData(entity, res);
                        }
                        em.RemoveComponent<LayerTraversalState>(entity);
                    }
                    else
                    {
                        em.SetComponentData(entity, ts);
                    }
                    continue;
                }

                // ── Branch B: no traversal yet -- maybe start one ─────────
                if (!em.HasComponent<NavPathResult>(entity)) continue;
                if (!em.HasBuffer<NavPathPortal>(entity)) continue;

                var pathResult = em.GetComponentData<NavPathResult>(entity);
                if (pathResult.Status != NavPathRequest.StatusSuccess) continue;
                if (pathResult.Length <= pathResult.CurrentPortalIndex + 1) continue;
                if (pathResult.Generation != graphSingleton.Generation) continue;

                var buf = em.GetBuffer<NavPathPortal>(entity, true);
                int nextPortalIdx = pathResult.CurrentPortalIndex + 1;
                if (nextPortalIdx >= buf.Length) continue;
                int portalId = buf[nextPortalIdx].PortalId;
                if (portalId < 0 || portalId >= nodeCount) continue;

                var node = graph.Nodes[portalId];
                bool isLayerChanging = node.PortalKind == PortalNode.KindClimb
                    || node.PortalKind == PortalNode.KindGateGround
                    || node.PortalKind == PortalNode.KindGateRampart;
                if (!isLayerChanging)
                {
                    // Ground portal: nothing to animate, but the path cursor
                    // still has to advance as the unit walks its legs.
                    // Before this, NOTHING advanced the cursor for plain
                    // ground portals — FlowFollow kept sampling the slab of a
                    // leg the unit had already finished, so multi-leg routes
                    // around long blockers stalled after the first gap.
                    //
                    // Rule 1: within reach of the portal cell -> advance.
                    float3 portalCentre = CellCentre(in grid, node.CellIndex, node.Layer);
                    float pdx = xf.Position.x - portalCentre.x;
                    float pdz = xf.Position.z - portalCentre.z;
                    float reach = grid.CellSize * 3f;
                    if (pdx * pdx + pdz * pdz <= reach * reach)
                    {
                        pathResult.CurrentPortalIndex =
                            math.min(pathResult.CurrentPortalIndex + 1, pathResult.Length);
                        em.SetComponentData(entity, pathResult);
                        continue;
                    }

                    // Rule 2 (fast-forward): a portal SPAN can be many cells
                    // wide and the node's CellIndex is just one cell of it —
                    // a unit can slip through the gap far from that cell and
                    // never trip Rule 1. If the unit already stands in the
                    // tile of a LATER portal of the chain, jump the cursor
                    // there. Scan from the tail so the furthest match wins.
                    int ucx = (int)math.floor((xf.Position.x - grid.Origin.x) / grid.CellSize);
                    int ucz = (int)math.floor((xf.Position.z - grid.Origin.z) / grid.CellSize);
                    int tileSizeG = graph.TileSize;
                    int unitTile = (ucz / tileSizeG) * graph.TilesX + (ucx / tileSizeG);
                    for (int k = buf.Length - 1; k > nextPortalIdx; k--)
                    {
                        int pid = buf[k].PortalId;
                        if (pid < 0 || pid >= nodeCount) continue; // virtual start/goal
                        var pk = graph.Nodes[pid];
                        if (pk.Layer != nli.Layer) continue;
                        if (pk.TileIndex != unitTile) continue;
                        pathResult.CurrentPortalIndex = k - 1;
                        em.SetComponentData(entity, pathResult);
                        break;
                    }
                    continue;
                }

                // Resolve the portal's endpoint cells -- climb / gate nodes
                // come in pairs (source + target), the pair is adjacent in
                // the node list (Appender emits node 2k + 2k+1).
                int pairedId = (portalId % 2 == 0) ? portalId + 1 : portalId - 1;
                if (pairedId < 0 || pairedId >= nodeCount) continue;
                var paired = graph.Nodes[pairedId];
                if (paired.PortalKind != node.PortalKind) continue;

                // Source / target choice depends on the unit's current layer.
                PortalNode srcNode = nli.Layer == node.Layer ? node : paired;
                PortalNode dstNode = nli.Layer == node.Layer ? paired : node;
                if (srcNode.Layer != nli.Layer) continue; // can't enter from the wrong side

                // Within-entry-distance gate.
                float3 srcCellCentre = CellCentre(in grid, srcNode.CellIndex, nli.Layer);
                float dx = xf.Position.x - srcCellCentre.x;
                float dz = xf.Position.z - srcCellCentre.z;
                if (dx * dx + dz * dz > EntryDistance * EntryDistance) continue;

                // Eligibility: integer checks only.
                int unitOwnerId = GetUnitOwnerId(em, entity);
                byte canClimb = GetUnitCanClimb(em, entity);
                if (!IsPortalAdmissible(ref graph, in mirror, portalId, unitOwnerId, canClimb))
                {
                    // R4 backstop: enemy at gate, or gate closed -- skip the
                    // portal (advance the cursor) so the integrator stops
                    // bumping the unit against the wall. The pathfinder
                    // will re-issue a request next tick (caller can re-add
                    // NavPathRequest) but for M5 we just abort this path.
                    pathResult.CurrentPortalIndex = pathResult.Length;
                    em.SetComponentData(entity, pathResult);
                    continue;
                }

                // Begin traversal.
                float3 dstCellCentre = CellCentre(in grid, dstNode.CellIndex, dstNode.Layer);
                em.AddComponentData(entity, new LayerTraversalState
                {
                    InProgress = 1,
                    FromLayer = nli.Layer,
                    ToLayer = dstNode.Layer,
                    PortalId = portalId,
                    Progress = 0f,
                    StartPos = xf.Position,
                    EndPos = dstCellCentre,
                });
            }

            order.Dispose();
            ecb.Playback(em);
            ecb.Dispose();
        }

        // Read the unit's owner id from FactionTag (cast enum -> int). -1
        // when no faction component -- treat as neutral / any-owner.
        private static int GetUnitOwnerId(EntityManager em, Entity unit)
        {
            if (!em.HasComponent<FactionTag>(unit)) return -1;
            return (int)em.GetComponentData<FactionTag>(unit).Value;
        }

        // Read CanClimb from the unit's TraversalProfile, falling back to
        // 1 (climb-permitted) when no profile component (M5 default for
        // every unit factory).
        private static byte GetUnitCanClimb(EntityManager em, Entity unit)
        {
            if (!em.HasComponent<NavTraversalProfile>(unit)) return 1;
            byte profileId = em.GetComponentData<NavTraversalProfile>(unit).ProfileId;
            var profQ = em.CreateEntityQuery(typeof(TraversalProfileSingleton));
            if (profQ.IsEmptyIgnoreFilter) { profQ.Dispose(); return 1; }
            var profSingleton = profQ.GetSingleton<TraversalProfileSingleton>();
            profQ.Dispose();
            if (!profSingleton.Profiles.IsCreated) return 1;
            ref var profBlob = ref profSingleton.Profiles.Value;
            if (profileId >= profBlob.Profiles.Length) return 1;
            return profBlob.Profiles[profileId].CanClimb;
        }

        private static bool IsPortalAdmissible(
            ref PortalGraphBlob graph,
            in PortalOwnerBitsMirror mirror,
            int portalId,
            int unitOwnerId,
            byte canClimb)
        {
            if (portalId < 0 || portalId >= graph.Nodes.Length) return false;
            var node = graph.Nodes[portalId];
            bool isClimb = node.PortalKind == PortalNode.KindClimb;
            bool isGate = node.PortalKind == PortalNode.KindGateGround
                || node.PortalKind == PortalNode.KindGateRampart;

            // Climb portal: requires CanClimb on the unit profile.
            if (isClimb && canClimb == 0) return false;

            if (!mirror.Bits.IsCreated || portalId >= mirror.Bits.Length) return true;
            ushort slot = mirror.Bits[portalId];
            // Open bit check (gate may be closed).
            if (isGate && !PortalOwnerBitsMirror.UnpackOpen(slot)) return false;

            // Owner check: gate portals admit only matching owners. Climb
            // portals carry OwnerAny so any unit can use them.
            int portalOwner = PortalOwnerBitsMirror.UnpackOwner(slot);
            if (isGate && portalOwner >= 0 && portalOwner != unitOwnerId) return false;

            return true;
        }

        private static float3 CellCentre(in NavGridSingleton grid, int cellIndex, byte layer)
        {
            int cx = cellIndex % grid.Width;
            int cz = cellIndex / grid.Width;
            float y = layer == 1 ? DeckY : 0f;
            return new float3(
                grid.Origin.x + (cx + 0.5f) * grid.CellSize,
                y,
                grid.Origin.z + (cz + 0.5f) * grid.CellSize);
        }
    }
}
