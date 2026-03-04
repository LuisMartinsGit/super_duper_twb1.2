// File: Assets/Scripts/Debug/TerrainPassabilityGizmo.cs
// Debug gizmo that visualises impassable terrain (red overlay).
// Uses the EXACT same slope formula as MovementSystem so what you
// see is what the units actually experience at runtime.

using UnityEngine;
using TheWaningBorder.World.Terrain;

public class TerrainPassabilityGizmo : MonoBehaviour
{
    // ─── Tunables ────────────────────────────────────────────
    [Header("Grid")]
    [Tooltip("World-space distance between sample points. Lower = more detail, more cost.")]
    [Range(0.5f, 8f)]
    public float cellSize = 2f;

    [Tooltip("Half-extent of the square area to visualise (centered on this transform).")]
    [Range(16f, 512f)]
    public float halfExtent = 128f;

    [Header("Slope (must match MovementSystem)")]
    [Tooltip("Slopes above this value are impassable.")]
    public float maxWalkableSlope = 0.55f;

    [Tooltip("Distance between height samples for slope estimation.")]
    public float slopeCheckStep = 1.5f;

    [Header("Water")]
    [Tooltip("World-space water level. Terrain below this is impassable.")]
    public float waterHeight = 20f;

    [Header("Appearance")]
    [Tooltip("Colour drawn over impassable cells.")]
    public Color impassableColor = new Color(1f, 0f, 0f, 0.45f);

    [Tooltip("Height offset above terrain so quads don't z-fight.")]
    public float yOffset = 0.25f;

    [Tooltip("Toggle to also show passable cells (green).")]
    public bool showPassable = false;

    [Tooltip("Colour for passable cells (only when Show Passable is on).")]
    public Color passableColor = new Color(0f, 1f, 0f, 0.12f);

    // ─── Gizmo drawing ──────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        // Only draw when this object is selected in the hierarchy
        DrawPassabilityGrid();
    }

    void DrawPassabilityGrid()
    {
        if (!TerrainUtility.IsReady()) return;

        Vector3 center = transform.position;
        float minX = center.x - halfExtent;
        float maxX = center.x + halfExtent;
        float minZ = center.z - halfExtent;
        float maxZ = center.z + halfExtent;

        float half = cellSize * 0.5f;

        for (float x = minX; x <= maxX; x += cellSize)
        {
            for (float z = minZ; z <= maxZ; z += cellSize)
            {
                float h = TerrainUtility.GetHeight(x, z);
                bool impassable = IsImpassable(x, z, h);

                if (impassable)
                {
                    Gizmos.color = impassableColor;
                    DrawFlatQuad(x, z, h + yOffset, half);
                }
                else if (showPassable)
                {
                    Gizmos.color = passableColor;
                    DrawFlatQuad(x, z, h + yOffset, half);
                }
            }
        }
    }

    /// <summary>
    /// Replicates MovementSystem slope check + water check.
    /// </summary>
    bool IsImpassable(float x, float z, float heightAtCenter)
    {
        // 1. Under water → impassable
        if (heightAtCenter <= waterHeight)
            return true;

        // 2. Slope too steep → impassable  (same math as MovementSystem)
        float hL = TerrainUtility.GetHeight(x - slopeCheckStep, z);
        float hR = TerrainUtility.GetHeight(x + slopeCheckStep, z);
        float hD = TerrainUtility.GetHeight(x, z - slopeCheckStep);
        float hU = TerrainUtility.GetHeight(x, z + slopeCheckStep);

        float dX = (hR - hL) / (slopeCheckStep * 2f);
        float dZ = (hU - hD) / (slopeCheckStep * 2f);
        float slope = Mathf.Sqrt(dX * dX + dZ * dZ);

        return slope > maxWalkableSlope;
    }

    /// <summary>
    /// Draw a flat quad at the given world position using Gizmos.
    /// </summary>
    static void DrawFlatQuad(float cx, float cz, float y, float half)
    {
        Vector3 a = new Vector3(cx - half, y, cz - half);
        Vector3 b = new Vector3(cx + half, y, cz - half);
        Vector3 c = new Vector3(cx + half, y, cz + half);
        Vector3 d = new Vector3(cx - half, y, cz + half);

        // Solid fill (uses current Gizmos.color)
        // Gizmos.DrawMesh is unavailable for procedural quads,
        // so we draw two triangles via a small mesh helper —
        // but the simplest approach that Just Works is a cube
        // scaled flat.  The slight thickness is invisible at 0.01.
        Vector3 center = new Vector3(cx, y, cz);
        Gizmos.DrawCube(center, new Vector3(half * 2f, 0.01f, half * 2f));
    }
}
