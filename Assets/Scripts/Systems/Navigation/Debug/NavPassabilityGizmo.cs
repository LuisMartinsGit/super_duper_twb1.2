// NavPassabilityGizmo.cs
// Editor-only Scene-view gizmo for inspecting NAV PASSABILITY cell-by-cell.
//
// Drop this component on an empty GameObject and position it near a problem
// area (e.g. the mountain / cliff that border units keep snagging on). In Play
// mode it reads the LIVE NavCostField — the exact per-cell passability the
// flow-field + steering stack uses — and draws a filled, terrain-following
// colour cell for every grid cell within Radius:
//
//   red     = impassable (255)  — mountains, cliffs, water, building footprints
//   orange  = conditional (254) — gate cells (passable only for the owner)
//   green   = walkable (0)
//   green→yellow = weighted (1..253) — incl. the wall-CLEARANCE band that bows
//             flow paths away from obstacle edges (see FlowSegmentSystem).
//
// Use it to answer "is the way around this cliff actually open?" — a one-cell
// red pinch or a fully-blocked detour shows up immediately. Complements the
// whole-map overview in NavDebugDrawSystem (which samples sparsely at flat y);
// this one is dense + terrain-following for close inspection.
//
// Cost-field reads happen on the main thread in OnDrawGizmos; the field is
// only written on the rare ticks a building/obstacle is (de)stamped, and the
// loop is guarded so a same-frame write can't throw into the editor.

using Unity.Entities;
using UnityEngine;
// NOTE: the bare identifier `World` resolves to the TheWaningBorder.World
// namespace here (found in the enclosing TheWaningBorder scope), not the ECS
// type — and a file-level using-alias does NOT override that. So every ECS
// World reference below is fully qualified as Unity.Entities.World.

namespace TheWaningBorder.Systems.Navigation
{
    [DisallowMultipleComponent]
    public class NavPassabilityGizmo : MonoBehaviour
    {
        [Header("Area")]
        [Tooltip("Draw cells within this XZ radius (metres) of the focus point.")]
        public float radius = 40f;
        [Tooltip("Centre the window on the Scene-view camera pivot instead of " +
                 "this object's position (so you don't have to move the object).")]
        public bool followSceneCamera = false;

        [Header("Filter")]
        [Tooltip("Hide walkable cells; show only impassable / conditional ones.")]
        public bool onlyBlocked = false;

        [Header("Appearance")]
        [Tooltip("Place each cell marker on the terrain surface.")]
        public bool followTerrain = true;
        [Tooltip("Height offset above the surface for the markers.")]
        public float yOffset = 0.25f;
        [Range(0f, 1f)]
        [Tooltip("Fill transparency of the cell quads.")]
        public float fillAlpha = 0.35f;
        [Tooltip("Also draw a wire outline per cell (sharper but busier).")]
        public bool drawWire = false;
        [Tooltip("Only draw when this object is selected (less Scene clutter).")]
        public bool onlyWhenSelected = false;

        [Header("Safety")]
        [Tooltip("Hard cap on cells drawn per frame.")]
        public int maxCells = 30000;

#if UNITY_EDITOR
        // Cached so we don't allocate a fresh EntityQuery every gizmo frame.
        private EntityQuery _gridQuery;
        private EntityQuery _costQuery;
        private Unity.Entities.World _cachedWorld;

        private void OnDrawGizmos()
        {
            if (!onlyWhenSelected) Draw();
        }

        private void OnDrawGizmosSelected()
        {
            if (onlyWhenSelected) Draw();
        }

        private bool TryResolve(out EntityManager em)
        {
            em = default;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { _cachedWorld = null; return false; }
            em = world.EntityManager;
            if (_cachedWorld != world)
            {
                _gridQuery = em.CreateEntityQuery(typeof(NavGridSingleton));
                _costQuery = em.CreateEntityQuery(typeof(NavCostField));
                _cachedWorld = world;
            }
            return true;
        }

        private void Draw()
        {
            if (!TryResolve(out var em)) return;
            if (_gridQuery.IsEmptyIgnoreFilter || _costQuery.IsEmptyIgnoreFilter) return;

            var grid = _gridQuery.GetSingleton<NavGridSingleton>();
            var cost = _costQuery.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated || grid.CellSize <= 0f) return;

            // Focus point.
            Vector3 c = transform.position;
            if (followSceneCamera)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null) c = sv.pivot;
            }

            // Cell window around the focus, clamped to the grid bounds.
            float inv = 1f / grid.CellSize;
            int cx = Mathf.FloorToInt((c.x - grid.Origin.x) * inv);
            int cz = Mathf.FloorToInt((c.z - grid.Origin.z) * inv);
            int rad = Mathf.Max(1, Mathf.CeilToInt(radius * inv));
            int x0 = Mathf.Clamp(cx - rad, 0, grid.Width - 1);
            int x1 = Mathf.Clamp(cx + rad, 0, grid.Width - 1);
            int z0 = Mathf.Clamp(cz - rad, 0, grid.Height - 1);
            int z1 = Mathf.Clamp(cz + rad, 0, grid.Height - 1);

            float r2 = radius * radius;
            float cell = grid.CellSize;
            var size = new Vector3(cell * 0.9f, 0.05f, cell * 0.9f);
            int drawn = 0;

            // The cost array is only written on (rare) stamp ticks; guard the
            // read so a same-frame write can't throw an editor exception.
            try
            {
                for (int z = z0; z <= z1 && drawn < maxCells; z++)
                {
                    for (int x = x0; x <= x1 && drawn < maxCells; x++)
                    {
                        float wx = grid.Origin.x + (x + 0.5f) * cell;
                        float wz = grid.Origin.z + (z + 0.5f) * cell;
                        float ddx = wx - c.x, ddz = wz - c.z;
                        if (ddx * ddx + ddz * ddz > r2) continue;

                        byte v = cost.Cost[z * grid.Width + x];

                        bool blocked = v == NavCostField.CostImpassable
                            || v == NavCostField.CostConditional;
                        if (onlyBlocked && !blocked) continue;

                        Color col;
                        if (v == NavCostField.CostImpassable) col = Color.red;
                        else if (v == NavCostField.CostConditional) col = new Color(1f, 0.55f, 0f);
                        else if (v == 0) col = new Color(0.15f, 0.85f, 0.2f);
                        else col = Color.Lerp(new Color(0.6f, 0.9f, 0.2f), Color.yellow,
                            Mathf.Clamp01(v / 200f));

                        float wy = (followTerrain
                            ? TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(wx, wz)
                            : grid.Origin.y) + yOffset;
                        var centre = new Vector3(wx, wy, wz);

                        Gizmos.color = new Color(col.r, col.g, col.b, fillAlpha);
                        Gizmos.DrawCube(centre, size);
                        if (drawWire)
                        {
                            Gizmos.color = new Color(col.r, col.g, col.b, 0.9f);
                            Gizmos.DrawWireCube(centre, size);
                        }
                        drawn++;
                    }
                }
            }
            catch
            {
                // Cost array was mid-write this frame — skip, draw next frame.
                return;
            }

            // Outline the query area so the focus radius is obvious.
            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            DrawRing(new Vector3(c.x, c.y, c.z), radius);
        }

        private static void DrawRing(Vector3 centre, float r)
        {
            const int seg = 48;
            Vector3 prev = centre + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif // UNITY_EDITOR
    }
}
