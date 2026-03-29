// FlowFieldGizmos.cs
// Debug visualization: draws the passability grid and flow field direction arrows.
// Attach to any GameObject or let GameBootstrap create it.
// Enable Gizmos in the Game view toolbar to see in play mode.
// Location: Assets/Scripts/Systems/Movement/FlowFieldGizmos.cs

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Editor/runtime gizmo drawer for the passability grid and flow field arrows.
    /// - Grid: colored squares per cell (green=passable, red=terrain, blue=building, orange=obstacle)
    /// - Grid lines: thin white outlines around each cell
    /// - Arrows: direction vectors from a cached flow field, drawn as lines with arrowheads
    /// Drawing is limited to a configurable radius around the camera for performance.
    /// </summary>
    public class FlowFieldGizmos : MonoBehaviour
    {
        // =====================================================================
        // CONFIGURATION (editable in Inspector)
        // =====================================================================

        [Header("Passability Grid")]
        [Tooltip("Draw colored squares showing passable/blocked cells")]
        public bool ShowGrid = true;

        [Tooltip("Draw thin white grid lines around cells")]
        public bool ShowGridLines = true;

        [Header("Flow Field Arrows")]
        [Tooltip("Draw direction arrows for cached flow fields")]
        public bool ShowArrows = true;

        [Tooltip("Which cached flow field to display (0 = first, -1 = all)")]
        public int FieldIndex = 0;

        [Header("View")]
        [Tooltip("Only draw gizmos within this radius of the camera (world units)")]
        public float ViewRadius = 40f;

        [Tooltip("Height offset above terrain for gizmo rendering")]
        public float HeightOffset = 0.5f;

        // =====================================================================
        // COLORS
        // =====================================================================

        private static readonly Color PassableColor       = new Color(0.2f, 0.8f, 0.2f, 0.12f);
        private static readonly Color TerrainBlockedColor = new Color(0.9f, 0.15f, 0.15f, 0.35f);
        private static readonly Color BuildingBlockedColor= new Color(0.2f, 0.2f, 0.9f, 0.45f);
        private static readonly Color ObstacleBlockedColor= new Color(0.9f, 0.6f, 0.1f, 0.45f);
        private static readonly Color GridLineColor       = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ArrowColor          = new Color(1f, 1f, 0f, 0.85f);
        private static readonly Color ArrowHeadColor      = new Color(1f, 0.6f, 0f, 0.9f);

        // =====================================================================
        // GIZMO DRAWING
        // =====================================================================

        void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            var grid = PassabilityGrid.Instance;
            if (grid == null || !grid.Cells.IsCreated) return;

            // Camera position for view culling
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 camWorldPos = cam.transform.position;
            float3 camGround = new float3(camWorldPos.x, 0f, camWorldPos.z);

            // Compute visible cell range
            int2 minCell = grid.WorldToCell(new float3(camGround.x - ViewRadius, 0f, camGround.z - ViewRadius));
            int2 maxCell = grid.WorldToCell(new float3(camGround.x + ViewRadius, 0f, camGround.z + ViewRadius));
            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(grid.Width - 1, grid.Height - 1));

            float cs = grid.CellSize;

            if (ShowGrid)
                DrawPassabilityGrid(grid, minCell, maxCell, cs);

            if (ShowGridLines)
                DrawGridLines(grid, minCell, maxCell, cs);

            if (ShowArrows)
                DrawFlowFieldArrows(grid, minCell, maxCell, cs);
        }

        // =====================================================================
        // PASSABILITY GRID — colored cubes per cell
        // =====================================================================

        private void DrawPassabilityGrid(PassabilityGrid grid, int2 minCell, int2 maxCell, float cs)
        {
            Vector3 cubeSize = new Vector3(cs * 0.94f, 0.08f, cs * 0.94f);

            for (int cy = minCell.y; cy <= maxCell.y; cy++)
            {
                for (int cx = minCell.x; cx <= maxCell.x; cx++)
                {
                    int idx = cy * grid.Width + cx;
                    byte cellVal = grid.Cells[idx];

                    Gizmos.color = cellVal switch
                    {
                        PassabilityGrid.Passable        => PassableColor,
                        PassabilityGrid.TerrainBlocked  => TerrainBlockedColor,
                        PassabilityGrid.BuildingBlocked => BuildingBlockedColor,
                        PassabilityGrid.ObstacleBlocked => ObstacleBlockedColor,
                        _ => Color.magenta
                    };

                    float3 worldPos = grid.CellToWorld(new int2(cx, cy));
                    Vector3 pos = new Vector3(worldPos.x, worldPos.y + HeightOffset, worldPos.z);
                    Gizmos.DrawCube(pos, cubeSize);
                }
            }
        }

        // =====================================================================
        // GRID LINES — thin white outlines
        // =====================================================================

        private void DrawGridLines(PassabilityGrid grid, int2 minCell, int2 maxCell, float cs)
        {
            Gizmos.color = GridLineColor;
            float3 origin = grid.Origin;
            float y = HeightOffset;

            // Try to get a reasonable Y from the center of view
            var cam = Camera.main;
            if (cam != null)
                y = TerrainUtility.GetHeight(cam.transform.position.x, cam.transform.position.z) + HeightOffset;

            // Vertical lines (along Z)
            for (int cx = minCell.x; cx <= maxCell.x + 1; cx++)
            {
                float x = origin.x + cx * cs;
                float z0 = origin.z + minCell.y * cs;
                float z1 = origin.z + (maxCell.y + 1) * cs;
                Gizmos.DrawLine(new Vector3(x, y, z0), new Vector3(x, y, z1));
            }

            // Horizontal lines (along X)
            for (int cy = minCell.y; cy <= maxCell.y + 1; cy++)
            {
                float z = origin.z + cy * cs;
                float x0 = origin.x + minCell.x * cs;
                float x1 = origin.x + (maxCell.x + 1) * cs;
                Gizmos.DrawLine(new Vector3(x0, y, z), new Vector3(x1, y, z));
            }
        }

        // =====================================================================
        // FLOW FIELD ARROWS — direction vectors from cached field
        // =====================================================================

        private void DrawFlowFieldArrows(PassabilityGrid grid, int2 minCell, int2 maxCell, float cs)
        {
            var ffm = FlowFieldManager.Instance;
            if (ffm == null) return;

            var lookup = ffm.CurrentLookup;
            if (!lookup.IsValid || !lookup.DirectionData.IsCreated) return;

            var cachedSlots = ffm.CachedSlots;
            if (cachedSlots == null || cachedSlots.Count == 0) return;

            // Collect slots to draw
            var slotsToDraw = new List<KeyValuePair<int, int>>();
            int index = 0;
            foreach (var kvp in cachedSlots)
            {
                if (FieldIndex < 0 || index == FieldIndex)
                    slotsToDraw.Add(kvp);
                index++;
            }

            if (slotsToDraw.Count == 0) return;

            float arrowLen = cs * 0.38f;
            float headLen = cs * 0.12f;
            int gw = lookup.GridWidth;
            int gh = lookup.GridHeight;
            int cellsPerField = lookup.CellsPerField;

            foreach (var kvp in slotsToDraw)
            {
                int slot = kvp.Value;

                for (int cy = minCell.y; cy <= maxCell.y; cy++)
                {
                    for (int cx = minCell.x; cx <= maxCell.x; cx++)
                    {
                        // ── 5×5 Gaussian kernel convolution (matches GetDirection) ──
                        float2 weightedSum = float2.zero;
                        float totalWeight = 0f;

                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int nz = cy + dy;
                            if (nz < 0 || nz >= gh) continue;

                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int nx = cx + dx;
                                if (nx < 0 || nx >= gw) continue;

                                int idx = slot * cellsPerField + nz * gw + nx;
                                if (idx < 0 || idx >= lookup.DirectionData.Length) continue;

                                float2 cellDir = lookup.DirectionData[idx];
                                if (math.lengthsq(cellDir) < 1e-6f) continue;

                                float w = GaussianWeight5x5(dx, dy);
                                weightedSum += cellDir * w;
                                totalWeight += w;
                            }
                        }

                        if (totalWeight < 1e-6f || math.lengthsq(weightedSum) < 1e-6f) continue;

                        float2 dirN = math.normalize(weightedSum);

                        float3 worldPos = grid.CellToWorld(new int2(cx, cy));
                        Vector3 center = new Vector3(worldPos.x, worldPos.y + HeightOffset + 0.1f, worldPos.z);

                        // Arrow shaft: from center toward convolved direction
                        Vector3 dir3 = new Vector3(dirN.x, 0f, dirN.y);
                        Vector3 tip = center + dir3 * arrowLen;

                        Gizmos.color = ArrowColor;
                        Gizmos.DrawLine(center, tip);

                        // Arrowhead: two small lines from the tip
                        Gizmos.color = ArrowHeadColor;
                        Vector3 perp = new Vector3(-dir3.z, 0f, dir3.x);
                        Vector3 back = -dir3 * headLen;
                        Gizmos.DrawLine(tip, tip + back + perp * headLen * 0.6f);
                        Gizmos.DrawLine(tip, tip + back - perp * headLen * 0.6f);
                    }
                }
            }
        }

        // =====================================================================
        // KERNEL (mirrors FlowFieldLookup.GaussianWeight5x5)
        // =====================================================================

        /// <summary>
        /// 5×5 Gaussian kernel weight (σ = 1.0).
        /// Must match FlowFieldLookup.GaussianWeight5x5 exactly.
        /// </summary>
        private static float GaussianWeight5x5(int dx, int dy)
        {
            int d2 = dx * dx + dy * dy;
            return d2 switch
            {
                0 => 1.0000f,
                1 => 0.6065f,
                2 => 0.3679f,
                4 => 0.1353f,
                5 => 0.0821f,
                8 => 0.0183f,
                _ => 0f,
            };
        }
    }
}
