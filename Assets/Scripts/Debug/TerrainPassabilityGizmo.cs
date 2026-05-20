// File: Assets/Scripts/Debug/TerrainPassabilityGizmo.cs
// Debug overlay that visualises the PassabilityGrid — what units actually
// experience at runtime. Colour-codes each impassable cell by reason
// (terrain / building / obstacle / unreachable) and toggles in-game with F9.

using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.Maps;

public class TerrainPassabilityGizmo : MonoBehaviour
{
    public enum SourceMode
    {
        // Read directly from PassabilityGrid.Cells — what pathing actually
        // sees. Recommended once the grid has finished baking.
        PassabilityGrid,
        // Recompute slope/water on the fly from the terrain itself. Useful
        // before PassabilityGrid is ready, or to compare the two.
        TerrainSampling,
    }

    [Header("Mode")]
    [Tooltip("Where the overlay reads passability from.")]
    public SourceMode source = SourceMode.PassabilityGrid;

    [Tooltip("Toggle visibility. F9 flips this at runtime.")]
    public bool visible = true;

    [Tooltip("Draw even when this GameObject is not selected in the editor.")]
    public bool drawAlways = true;

    [Header("Grid (TerrainSampling fallback only)")]
    [Tooltip("World-space distance between sample points.")]
    [Range(0.5f, 8f)]
    public float cellSize = 2f;

    [Tooltip("Half-extent of the square area to visualise (centred on this transform).")]
    [Range(16f, 512f)]
    public float halfExtent = 128f;

    [Tooltip("Slopes above this value are impassable.")]
    public float maxWalkableSlope = 0.55f;

    [Tooltip("Distance between height samples for slope estimation.")]
    public float slopeCheckStep = 1.5f;

    [Tooltip("World-space water level. Terrain below this is impassable.")]
    public float waterHeight = 20f;

    [Header("Performance")]
    [Tooltip("Draw every Nth grid cell. 1 = every cell (heavy on a 1m grid). " +
             "Auto-tuned each frame so total draws stay near drawBudget.")]
    [Range(1, 8)]
    public int decimation = 1;

    [Tooltip("Soft cap on cells drawn per frame — decimation auto-rises to " +
             "stay near this number so the editor stays responsive.")]
    public int drawBudget = 8000;

    [Header("Appearance")]
    [Tooltip("Height offset above terrain so quads don't z-fight.")]
    public float yOffset = 0.3f;

    public Color terrainBlockedColor   = new(0.95f, 0.20f, 0.20f, 0.45f); // red
    public Color buildingBlockedColor  = new(0.30f, 0.55f, 1.00f, 0.45f); // blue
    public Color obstacleBlockedColor  = new(0.95f, 0.75f, 0.10f, 0.45f); // amber
    public Color unreachableColor      = new(0.80f, 0.20f, 0.85f, 0.45f); // magenta — outside common reachable region
    public Color passableColor         = new(0.20f, 0.95f, 0.30f, 0.10f);

    [Header("Region zones (from ProceduralMapGen)")]
    [Tooltip("Overlay each tagged region in its own colour. Zones draw on " +
             "top of passability, so a magenta-passability cell inside an " +
             "Expansion still reads as the Expansion colour.")]
    public bool showZones = true;

    public Color zonePlayerStart = new(0.00f, 0.95f, 0.95f, 0.55f); // cyan
    public Color zoneExpansion   = new(0.30f, 0.85f, 0.55f, 0.40f); // teal-green
    public Color zoneTravelLane  = new(0.95f, 0.90f, 0.35f, 0.45f); // yellow
    public Color zoneResource    = new(1.00f, 0.55f, 0.10f, 0.55f); // orange
    public Color zoneChokepoint  = new(1.00f, 0.30f, 0.55f, 0.55f); // pink-red
    public Color zoneCurseSpawn  = new(0.60f, 0.20f, 0.90f, 0.55f); // purple

    [Tooltip("Also fill passable cells in green (heavier in the editor).")]
    public bool showPassable = false;

    [Tooltip("Tint cells outside the common reachable region (after halls spawn).")]
    public bool showUnreachable = true;

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.F9;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    void OnDrawGizmos()
    {
        if (drawAlways) Draw();
    }

    void OnDrawGizmosSelected()
    {
        if (!drawAlways) Draw();
    }

    void Draw()
    {
        if (!visible) return;
        var grid = PassabilityGrid.Instance;
        if (source == SourceMode.PassabilityGrid && grid != null && grid.Cells.IsCreated)
            DrawFromGrid(grid);
        else
            DrawFromTerrain();
    }

    void DrawFromGrid(PassabilityGrid grid)
    {
        int w = grid.Width;
        int h = grid.Height;
        float cs = grid.CellSize;
        var origin = grid.Origin;
        var cells = grid.Cells;
        bool reachabilityReady = grid.IsReachabilityReady && showUnreachable;
        var regionSet = (showZones && ProceduralMapGen.IsActive) ? ProceduralMapGen.Current : null;

        // Auto-tune decimation so the cell count stays under the budget.
        // Rendering 60k cubes every gizmo pass stalls the editor; sampling
        // every Nth keeps the overlay readable at any grid size.
        int total = w * h;
        int wantStep = Mathf.Max(decimation, Mathf.CeilToInt(Mathf.Sqrt(total / (float)Mathf.Max(1, drawBudget))));
        int step = Mathf.Max(1, wantStep);
        float drawSize = cs * step;

        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                byte v = cells[y * w + x];
                float wx = origin.x + (x + 0.5f) * cs;
                float wz = origin.z + (y + 0.5f) * cs;
                float wy = TerrainUtility.GetHeight(wx, wz) + yOffset;

                Color? c = null;
                // Zones take priority over passability so a passable cell
                // inside an Expansion / Lane / Resource shows its zone tint.
                if (regionSet != null)
                    c = ZoneColorAt(regionSet, new Vector2(wx, wz));

                if (!c.HasValue)
                {
                    if (v == PassabilityGrid.TerrainBlocked)       c = terrainBlockedColor;
                    else if (v == PassabilityGrid.BuildingBlocked) c = buildingBlockedColor;
                    else if (v == PassabilityGrid.ObstacleBlocked) c = obstacleBlockedColor;
                    else if (reachabilityReady &&
                             !grid.IsReachableByAllPlayers(new float3(wx, 0f, wz)))
                        c = unreachableColor;
                    else if (showPassable) c = passableColor;
                }

                if (c.HasValue)
                {
                    Gizmos.color = c.Value;
                    Gizmos.DrawCube(new Vector3(wx, wy, wz),
                                    new Vector3(drawSize, 0.01f, drawSize));
                }
            }
        }
    }

    // Region-tag priority. PlayerStart > Chokepoint > Resource > CurseSpawn
    // > TravelLane > Expansion. Higher-priority tags overpaint lower-priority
    // ones so a Resource sitting inside an Expansion reads as Resource.
    Color? ZoneColorAt(MapRegionSet set, Vector2 p)
    {
        Color? best = null;
        int bestPriority = -1;
        for (int i = 0; i < set.regions.Count; i++)
        {
            var r = set.regions[i];
            if (r.SignedDistance(p) > 0f) continue; // not inside

            int prio;
            Color col;
            switch (r.tag)
            {
                case RegionTag.PlayerStart: prio = 6; col = zonePlayerStart; break;
                case RegionTag.Chokepoint:  prio = 5; col = zoneChokepoint;  break;
                case RegionTag.Resource:    prio = 4; col = zoneResource;    break;
                case RegionTag.CurseSpawn:  prio = 3; col = zoneCurseSpawn;  break;
                case RegionTag.TravelLane:  prio = 2; col = zoneTravelLane;  break;
                case RegionTag.Expansion:   prio = 1; col = zoneExpansion;   break;
                default: continue;
            }
            if (prio > bestPriority)
            {
                bestPriority = prio;
                best = col;
            }
        }
        return best;
    }

    // Fallback path — same slope/water math the grid uses, but sampled
    // directly so the overlay works even before PassabilityGrid bakes.
    void DrawFromTerrain()
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
                if (IsImpassableFromTerrain(x, z, h))
                {
                    Gizmos.color = terrainBlockedColor;
                    Gizmos.DrawCube(new Vector3(x, h + yOffset, z),
                                    new Vector3(cellSize, 0.01f, cellSize));
                }
                else if (showPassable)
                {
                    Gizmos.color = passableColor;
                    Gizmos.DrawCube(new Vector3(x, h + yOffset, z),
                                    new Vector3(cellSize, 0.01f, cellSize));
                }
            }
        }
    }

    bool IsImpassableFromTerrain(float x, float z, float heightAtCenter)
    {
        if (heightAtCenter <= waterHeight) return true;

        float hL = TerrainUtility.GetHeight(x - slopeCheckStep, z);
        float hR = TerrainUtility.GetHeight(x + slopeCheckStep, z);
        float hD = TerrainUtility.GetHeight(x, z - slopeCheckStep);
        float hU = TerrainUtility.GetHeight(x, z + slopeCheckStep);
        float dX = (hR - hL) / (slopeCheckStep * 2f);
        float dZ = (hU - hD) / (slopeCheckStep * 2f);
        float slope = Mathf.Sqrt(dX * dX + dZ * dZ);
        return slope > maxWalkableSlope;
    }
}
