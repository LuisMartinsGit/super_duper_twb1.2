// NavPassabilityOverlay.cs
// A real SURFACE visualisation of nav passability: builds a translucent mesh
// draped over the terrain, one coloured quad per cost-field cell —
//   red    = impassable (mountains, cliffs, water, building footprints)
//   orange = conditional (gate cells)
//   green  = walkable
// Unlike the Debug.DrawLine cost-field overlay (which can only draw X-marks),
// this is a filled surface you can read at a glance from a top-down view, and
// it shows in BOTH the Scene and Game views.
//
// Usage: drop this on an empty GameObject in Play mode and position it over the
// area you want to inspect (or tick Follow Scene Camera). It reads the live
// NavCostField from the default ECS world and rebuilds only when the field
// changes / you move it, so it's cheap to leave on.
//
// Location: Assets/Scripts/Systems/Navigation/NavPassabilityOverlay.cs

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Systems.Navigation
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NavPassabilityOverlay : MonoBehaviour
    {
        [Header("Area")]
        [Tooltip("Half-size (m) of the square window of cells to draw around the focus.")]
        public float radius = 150f;
        [Tooltip("Centre the window on the Scene-view camera pivot instead of this object.")]
        public bool followSceneCamera = false;

        [Header("Appearance")]
        [Tooltip("Lift above the terrain surface to avoid z-fighting.")]
        public float yOffset = 0.4f;
        [Range(0f, 1f)]
        [Tooltip("Opacity of impassable cells (passable cells draw at half this).")]
        public float alpha = 0.5f;
        [Tooltip("Also draw walkable cells (green). Off = only show blockers.")]
        public bool showPassable = true;

        public Color passableColor = new Color(0.15f, 0.85f, 0.2f);
        public Color conditionalColor = new Color(1f, 0.55f, 0f);
        public Color impassableColor = new Color(0.9f, 0.1f, 0.1f);

        [Header("Refresh / safety")]
        [Tooltip("Seconds between change checks.")]
        public float refreshInterval = 0.5f;
        [Tooltip("Rebuild when the focus has moved this far (m).")]
        public float moveRebuildDistance = 8f;
        [Tooltip("Hard cap on cells meshed (each cell = 4 verts).")]
        public int maxCells = 120000;

        private Mesh _mesh;
        private Material _mat;
        private MeshFilter _mf;
        private MeshRenderer _mr;

        private float _timer;
        private int _lastGen = int.MinValue;
        private Vector3 _lastCenter = new Vector3(float.MaxValue, 0, 0);
        private float _lastRadius = -1f;
        private bool _lastShowPassable;
        private EntityQuery _gridQuery;
        private EntityQuery _costQuery;
        private Unity.Entities.World _cachedWorld;

        // Reusable build buffers (avoid per-rebuild GC churn).
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _tris = new List<int>();

        private void OnEnable()
        {
            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "NavPassabilityOverlay" };
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _mesh.MarkDynamic();
            }
            _mf.sharedMesh = _mesh;

            if (_mat == null)
            {
                // Sprites/Default is always included, supports vertex colours +
                // alpha blending, and renders under URP. White base lets the
                // per-vertex colours show through.
                var sh = Shader.Find("Sprites/Default");
                _mat = new Material(sh) { name = "NavPassabilityOverlayMat", color = Color.white };
            }
            _mr.sharedMaterial = _mat;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;

            _lastGen = int.MinValue; // force a rebuild on enable
        }

        private void OnDisable()
        {
            if (_mesh != null) _mesh.Clear();
        }

        private void Update()
        {
            if (!Application.isPlaying) return; // needs the ECS world

            _timer += Time.deltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;

            if (!TryResolve(out var em)) return;
            if (_gridQuery.IsEmptyIgnoreFilter || _costQuery.IsEmptyIgnoreFilter) return;

            var grid = _gridQuery.GetSingleton<NavGridSingleton>();
            var cost = _costQuery.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated || grid.CellSize <= 0f) return;

            Vector3 center = FocusCenter();
            bool changed = cost.Generation != _lastGen
                || Mathf.Abs(radius - _lastRadius) > 0.01f
                || showPassable != _lastShowPassable
                || (center - _lastCenter).sqrMagnitude > moveRebuildDistance * moveRebuildDistance;
            if (!changed) return;

            Rebuild(em, grid, cost, center);

            _lastGen = cost.Generation;
            _lastCenter = center;
            _lastRadius = radius;
            _lastShowPassable = showPassable;
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

        private Vector3 FocusCenter()
        {
            if (followSceneCamera)
            {
#if UNITY_EDITOR
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null) return sv.pivot;
#endif
            }
            return transform.position;
        }

        private void Rebuild(EntityManager em, NavGridSingleton grid, NavCostField cost, Vector3 center)
        {
            _verts.Clear(); _colors.Clear(); _tris.Clear();

            float cell = grid.CellSize;
            float inv = 1f / cell;
            int cxc = Mathf.FloorToInt((center.x - grid.Origin.x) * inv);
            int czc = Mathf.FloorToInt((center.z - grid.Origin.z) * inv);
            int rad = Mathf.Max(1, Mathf.CeilToInt(radius * inv));
            int x0 = Mathf.Clamp(cxc - rad, 0, grid.Width - 1);
            int x1 = Mathf.Clamp(cxc + rad, 0, grid.Width - 1);
            int z0 = Mathf.Clamp(czc - rad, 0, grid.Height - 1);
            int z1 = Mathf.Clamp(czc + rad, 0, grid.Height - 1);

            // Auto-downsample so a large Radius still covers the WHOLE area
            // (just coarser) instead of truncating into a lopsided half. Each
            // drawn quad is step×step cells and samples the WORST cost in that
            // block, so a thin wall never vanishes at coarse resolution.
            long boxCells = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
            int step = 1;
            if (boxCells > maxCells)
                step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt((float)boxCells / maxCells)));
            float qsize = cell * step;

            float r2 = radius * radius;

            try
            {
                for (int z = z0; z <= z1; z += step)
                {
                    for (int x = x0; x <= x1; x += step)
                    {
                        float bcx = grid.Origin.x + (x + step * 0.5f) * cell;
                        float bcz = grid.Origin.z + (z + step * 0.5f) * cell;
                        float ddx = bcx - center.x, ddz = bcz - center.z;
                        if (ddx * ddx + ddz * ddz > r2) continue;

                        byte v = WorstCost(cost, grid, x, z, step);
                        bool blocked = v == NavCostField.CostImpassable || v == NavCostField.CostConditional;
                        if (!showPassable && !blocked) continue;

                        Color col;
                        float a;
                        if (v == NavCostField.CostImpassable) { col = impassableColor; a = alpha; }
                        else if (v == NavCostField.CostConditional) { col = conditionalColor; a = alpha; }
                        else { col = passableColor; a = alpha * 0.5f; }
                        col.a = a;

                        AddCell(grid.Origin.x + x * cell, grid.Origin.z + z * cell, qsize, col);
                    }
                }
            }
            catch
            {
                // Cost array was mid-write — bail and retry next refresh.
                return;
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0, true);
            _mesh.RecalculateBounds();
        }

        // Worst (most-blocking) cost in a step×step block: impassable beats
        // conditional beats the highest weighted value beats walkable. Keeps
        // walls visible when the overlay is downsampled at large radii.
        private static byte WorstCost(NavCostField cost, NavGridSingleton grid, int x0, int z0, int step)
        {
            byte worst = 0;
            for (int dz = 0; dz < step; dz++)
            {
                int zz = z0 + dz;
                if (zz >= grid.Height) break;
                for (int dx = 0; dx < step; dx++)
                {
                    int xx = x0 + dx;
                    if (xx >= grid.Width) break;
                    byte v = cost.Cost[zz * grid.Width + xx];
                    if (v == NavCostField.CostImpassable) return NavCostField.CostImpassable;
                    if (v == NavCostField.CostConditional) worst = NavCostField.CostConditional;
                    else if (worst != NavCostField.CostConditional && v > worst) worst = v;
                }
            }
            return worst;
        }

        // One terrain-draped quad covering the cell whose min corner is (mx,mz).
        private void AddCell(float mx, float mz, float cell, Color col)
        {
            int b = _verts.Count;
            // Corner heights sample the terrain so the quad drapes over slopes.
            _verts.Add(LocalAt(mx, mz));
            _verts.Add(LocalAt(mx + cell, mz));
            _verts.Add(LocalAt(mx + cell, mz + cell));
            _verts.Add(LocalAt(mx, mz + cell));
            _colors.Add(col); _colors.Add(col); _colors.Add(col); _colors.Add(col);
            _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 1);
            _tris.Add(b); _tris.Add(b + 3); _tris.Add(b + 2);
        }

        private Vector3 LocalAt(float wx, float wz)
        {
            float wy = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(wx, wz) + yOffset;
            return transform.InverseTransformPoint(new Vector3(wx, wy, wz));
        }
    }
}
