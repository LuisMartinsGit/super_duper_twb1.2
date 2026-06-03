// NavDebugDrawSystem.cs
// task-112 M7 (S11) -- EDITOR-ONLY visualisation overlay for the
// navigation stack. Reads the nav singletons read-only and renders the
// requested layer via UnityEngine.Debug.DrawLine + Gizmos. Wrapped in
// #if UNITY_EDITOR so the entire file is excised from player builds
// (no possibility of affecting determinism).
//
// Toggle via GameSettings.NavDebugVisualization:
//   * None              -- system does nothing.
//   * CostField         -- per-cell heatmap (impassable = red, blocked
//                          ~= orange, walkable = green) on layer 0.
//   * PortalGraph       -- tile boundaries (grey) + portal cells (cyan
//                          for inter-tile, magenta for climb, yellow
//                          for gates) + inter-portal edges (white).
//   * FlowVectors       -- per-cell arrow from cell-centre along the
//                          M3 cached flow direction (only the cached
//                          slabs, not the whole map).
//   * AbstractAStarPath -- chain of portal-cell midpoints for every
//                          unit's currently-active NavPathResult.
//   * All               -- everything above. Slow on large grids; use
//                          on small test scenarios only.
//
// DR-16: lives in PresentationSystemGroup (NOT SimulationSystemGroup)
// so the draw passes never modify sim state and never participate in
// determinism. ComponentSystemBase (not SystemBase) so we can opt out
// of Burst entirely -- Debug.DrawLine is managed.
//
// Location: Assets/Scripts/Systems/Navigation/Editor/NavDebugDrawSystem.cs

#if UNITY_EDITOR

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Systems.Navigation.Editor
{
    /// <summary>
    /// Editor-only debug-draw system for the nav stack. Runs once per
    /// presentation frame in <see cref="PresentationSystemGroup"/> --
    /// completely outside the sim, so any work done here cannot affect
    /// lockstep determinism.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NavDebugDrawSystem : SystemBase
    {
        // Configurable upper bound on cells drawn per frame for the
        // cost-field heatmap. 512x512 = 262k cells; drawing them all
        // every frame stalls the editor at 5 FPS. Cap at ~5000 random
        // (deterministic stride) samples so the heatmap still gives
        // a sense of the field without melting Unity's gizmo pipeline.
        private const int CostFieldMaxDrawSamples = 5000;
        // Same cap for the flow-vector field (per-cache-slab arrows).
        private const int FlowVectorMaxDrawSamples = 5000;

        // Lifetime for one-frame debug lines.
        private const float DrawDuration = 0f;

        protected override void OnCreate()
        {
            // Don't gate on RequireForUpdate -- the toggle is a static
            // field so we want to react to it being flipped at runtime.
        }

        protected override void OnUpdate()
        {
            var mode = GameSettings.NavDebugVisualization;
            if (mode == NavDebugVisualization.None) return;

            // Cost-field draw.
            if (mode == NavDebugVisualization.CostField
                || mode == NavDebugVisualization.All)
            {
                DrawCostField();
            }

            if (mode == NavDebugVisualization.PortalGraph
                || mode == NavDebugVisualization.All)
            {
                DrawPortalGraph();
            }

            if (mode == NavDebugVisualization.FlowVectors
                || mode == NavDebugVisualization.All)
            {
                DrawFlowVectors();
            }

            if (mode == NavDebugVisualization.AbstractAStarPath
                || mode == NavDebugVisualization.All)
            {
                DrawAStarPaths();
            }
        }

        // ── Cost-field heatmap ──────────────────────────────────────

        private void DrawCostField()
        {
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;
            if (!SystemAPI.HasSingleton<NavCostField>()) return;
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var cost = SystemAPI.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated) return;

            int total = grid.Width * grid.Height;
            int stride = math.max(1, total / CostFieldMaxDrawSamples);
            for (int idx = 0; idx < total; idx += stride)
            {
                int x = idx % grid.Width;
                int z = idx / grid.Width;
                byte c = cost.Cost[idx];
                Color colour;
                if (c == NavCostField.CostImpassable) colour = Color.red;
                else if (c == NavCostField.CostConditional) colour = new Color(1f, 0.5f, 0f); // orange
                else if (c == 0) colour = new Color(0f, 0.7f, 0f); // dim green
                else colour = Color.Lerp(Color.green, Color.yellow, c / 200f);

                // Draw an X-shape at the cell centre (4 short lines).
                float wx = grid.Origin.x + (x + 0.5f) * grid.CellSize;
                float wz = grid.Origin.z + (z + 0.5f) * grid.CellSize;
                float half = grid.CellSize * 0.25f;
                Vector3 c0 = new Vector3(wx, 0.1f, wz);
                Debug.DrawLine(c0 + new Vector3(-half, 0, -half), c0 + new Vector3(half, 0, half),
                    colour, DrawDuration);
                Debug.DrawLine(c0 + new Vector3(-half, 0, half), c0 + new Vector3(half, 0, -half),
                    colour, DrawDuration);
            }
        }

        // ── Portal graph: tile boundaries + portals + edges ─────────

        private void DrawPortalGraph()
        {
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;
            if (!SystemAPI.HasSingleton<PortalGraphSingleton>()) return;
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var graphSingle = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (graphSingle.Built == 0) return;
            ref var graph = ref graphSingle.Graph.Value;

            // Tile boundaries (dim grey grid).
            Color tileColour = new Color(0.3f, 0.3f, 0.3f);
            int tileSize = graph.TileSize;
            int worldWidth = graph.TilesX * tileSize;
            int worldHeight = graph.TilesZ * tileSize;
            for (int tz = 0; tz <= graph.TilesZ; tz++)
            {
                float z = grid.Origin.z + tz * tileSize * grid.CellSize;
                Debug.DrawLine(
                    new Vector3(grid.Origin.x, 0.05f, z),
                    new Vector3(grid.Origin.x + worldWidth * grid.CellSize, 0.05f, z),
                    tileColour, DrawDuration);
            }
            for (int tx = 0; tx <= graph.TilesX; tx++)
            {
                float x = grid.Origin.x + tx * tileSize * grid.CellSize;
                Debug.DrawLine(
                    new Vector3(x, 0.05f, grid.Origin.z),
                    new Vector3(x, 0.05f, grid.Origin.z + worldHeight * grid.CellSize),
                    tileColour, DrawDuration);
            }

            // Portal nodes (cyan / magenta / yellow per kind).
            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                var node = graph.Nodes[i];
                int cx = node.CellIndex % grid.Width;
                int cz = node.CellIndex / grid.Width;
                float wx = grid.Origin.x + (cx + 0.5f) * grid.CellSize;
                float wz = grid.Origin.z + (cz + 0.5f) * grid.CellSize;
                Color colour;
                if (node.PortalKind == PortalNode.KindClimb) colour = Color.magenta;
                else if (node.PortalKind == PortalNode.KindGateGround
                      || node.PortalKind == PortalNode.KindGateRampart) colour = Color.yellow;
                else colour = Color.cyan;
                float y = node.Layer == 1 ? 4f : 0.2f; // rampart at DeckY
                float r = grid.CellSize * 0.35f;
                Debug.DrawLine(new Vector3(wx - r, y, wz), new Vector3(wx + r, y, wz),
                    colour, DrawDuration);
                Debug.DrawLine(new Vector3(wx, y, wz - r), new Vector3(wx, y, wz + r),
                    colour, DrawDuration);
            }

            // Portal edges (white). Skip self-loops / virtual nodes.
            Color edgeColour = new Color(0.9f, 0.9f, 0.9f, 0.3f);
            for (int e = 0; e < graph.Edges.Length; e++)
            {
                var edge = graph.Edges[e];
                if (edge.FromPortalId < 0 || edge.FromPortalId >= graph.Nodes.Length) continue;
                if (edge.ToPortalId < 0 || edge.ToPortalId >= graph.Nodes.Length) continue;
                var n0 = graph.Nodes[edge.FromPortalId];
                var n1 = graph.Nodes[edge.ToPortalId];
                int x0 = n0.CellIndex % grid.Width;
                int z0 = n0.CellIndex / grid.Width;
                int x1 = n1.CellIndex % grid.Width;
                int z1 = n1.CellIndex / grid.Width;
                Vector3 a = new Vector3(
                    grid.Origin.x + (x0 + 0.5f) * grid.CellSize,
                    n0.Layer == 1 ? 4f : 0.15f,
                    grid.Origin.z + (z0 + 0.5f) * grid.CellSize);
                Vector3 b = new Vector3(
                    grid.Origin.x + (x1 + 0.5f) * grid.CellSize,
                    n1.Layer == 1 ? 4f : 0.15f,
                    grid.Origin.z + (z1 + 0.5f) * grid.CellSize);
                Debug.DrawLine(a, b, edgeColour, DrawDuration);
            }
        }

        // ── Flow vectors (per cached slab) ──────────────────────────

        private void DrawFlowVectors()
        {
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;
            if (!SystemAPI.HasSingleton<NavFlowCache>()) return;
            if (!SystemAPI.HasSingleton<DirectionTableSingleton>()) return;
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var cache = SystemAPI.GetSingleton<NavFlowCache>();
            if (!cache.DirPool.IsCreated || !cache.Slots.IsCreated) return;
            var dirTable = SystemAPI.GetSingleton<DirectionTableSingleton>();
            if (!dirTable.Table.IsCreated) return;
            ref var dirs = ref dirTable.Table.Value.Dirs;
            int tileSize = PortalGraphSingleton.TileSize;

            int slabsDrawn = 0;
            for (int s = 0; s < cache.Slots.Length && slabsDrawn < FlowVectorMaxDrawSamples; s++)
            {
                var slot = cache.Slots[s];
                if (slot.Valid == 0) continue;
                var key = cache.SlotKeys[s];
                int tileIndex = key.TileIndex;
                int tilesX = (grid.Width + tileSize - 1) / tileSize;
                int tileTX = tileIndex % tilesX;
                int tileTZ = tileIndex / tilesX;
                int cell0X = tileTX * tileSize;
                int cell0Z = tileTZ * tileSize;

                int dirOffset = slot.DirOffset;
                for (int dz = 0; dz < tileSize; dz++)
                {
                    for (int dx = 0; dx < tileSize; dx++)
                    {
                        int localIdx = dz * tileSize + dx;
                        byte b = cache.DirPool[dirOffset + localIdx];
                        if (b == NavFlowConstants.NoDirection) continue;
                        var dxz = dirs[b];
                        int wx = cell0X + dx;
                        int wz = cell0Z + dz;
                        float fx = grid.Origin.x + (wx + 0.5f) * grid.CellSize;
                        float fz = grid.Origin.z + (wz + 0.5f) * grid.CellSize;
                        Vector3 start = new Vector3(fx, 0.2f, fz);
                        Vector3 end = start + new Vector3(dxz.x, 0, dxz.y) * (grid.CellSize * 0.4f);
                        Debug.DrawLine(start, end, Color.cyan, DrawDuration);
                    }
                }
                slabsDrawn++;
            }
        }

        // ── Abstract A* path per unit ────────────────────────────────

        private void DrawAStarPaths()
        {
            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;
            if (!SystemAPI.HasSingleton<PortalGraphSingleton>()) return;
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var graphSingle = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (graphSingle.Built == 0) return;
            ref var graph = ref graphSingle.Graph.Value;

            // Per-unit path draw. Walk every unit that has a
            // NavPathResult + NavPathPortal buffer, draw cell-centre
            // segments between successive portals.
            foreach (var (xform, result, buffer) in SystemAPI.Query<
                RefRO<LocalTransform>, RefRO<NavPathResult>, DynamicBuffer<NavPathPortal>>())
            {
                if (result.ValueRO.Status != NavPathRequest.StatusSuccess) continue;
                if (buffer.Length < 2) continue;

                // Start segment: unit position -> first portal.
                Vector3 prev = new Vector3(
                    xform.ValueRO.Position.x, 0.5f, xform.ValueRO.Position.z);
                for (int i = 0; i < buffer.Length; i++)
                {
                    int portalId = buffer[i].PortalId;
                    if (portalId < 0 || portalId >= graph.Nodes.Length) continue;
                    var node = graph.Nodes[portalId];
                    int cx = node.CellIndex % grid.Width;
                    int cz = node.CellIndex / grid.Width;
                    Vector3 next = new Vector3(
                        grid.Origin.x + (cx + 0.5f) * grid.CellSize,
                        node.Layer == 1 ? 4f : 0.5f,
                        grid.Origin.z + (cz + 0.5f) * grid.CellSize);
                    Debug.DrawLine(prev, next, Color.green, DrawDuration);
                    prev = next;
                }
            }
        }
    }
}

#endif // UNITY_EDITOR
