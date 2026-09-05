// BuildGridOverlay.cs
// Faint 2 m build-grid lines drawn on the ground around the placement cursor.
// Canonical spec: docs/Design/Build_Grid.md

using UnityEngine;
using Unity.Mathematics;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Shows the build grid itself while a building is being placed, so the
    /// player can read the cells a footprint will land on rather than inferring
    /// them from the footprint outline alone.
    ///
    /// Deliberately a LOCAL PATCH around the cursor, not the whole map: at 2 m
    /// cells a 400 m map is 200x200 cells, i.e. ~400 full-length lines and
    /// 80,000 crossings — far past what a LineRenderer overlay can carry. A
    /// patch of <see cref="RadiusCells"/> around the cursor costs ~42 lines and
    /// reads identically, because the grid is uniform everywhere.
    ///
    /// Rebuilt only when the cursor crosses a cell boundary (per the
    /// presentation perf contract — don't re-lay geometry every frame for a
    /// picture that has not changed).
    /// </summary>
    public static class BuildGridOverlay
    {
        /// <summary>Half-width of the drawn patch, in build cells.</summary>
        private const int RadiusCells = 10;

        /// <summary>Sits below the footprint outline so the outline reads on
        /// top of the grid rather than fighting it.</summary>
        private const float GroundOffset = 0.06f;
        private const float LineWidth = 0.035f;

        /// <summary>Samples per metre along a line, so it follows terrain
        /// relief instead of cutting through a slope.</summary>
        private const int SamplesPerCell = 1;

        // White, and slightly transparent by design: the grid is a reading aid
        // under the building, never a thing to look at. (The footprint outline
        // on top carries the gold — gold + white is the placement palette.)
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.30f);

        private static GameObject _root;
        private static LineRenderer[] _lines = System.Array.Empty<LineRenderer>();
        private static Material _sharedMat;
        private static int2 _lastCentreCell = new int2(int.MinValue, int.MinValue);
        private static bool _visible;

        /// <summary>
        /// Draw the grid patch centred on the cell containing
        /// <paramref name="world"/>. Cheap to call every frame.
        /// </summary>
        public static void Show(float3 world)
        {
            EnsureRoot();

            int2 centre = BuildGrid.WorldToCell(world);
            if (_visible && centre.Equals(_lastCentreCell)) return;   // nothing moved

            _lastCentreCell = centre;
            _visible = true;
            _root.SetActive(true);
            Rebuild(centre);
        }

        /// <summary>Hide the grid. Cheap to call every frame.</summary>
        public static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            _visible = false;
            _lastCentreCell = new int2(int.MinValue, int.MinValue);
        }

        // ── Geometry ────────────────────────────────────────────────────

        private static void Rebuild(int2 centre)
        {
            int min = -RadiusCells;
            int max = RadiusCells;
            int lineCount = (max - min + 1) * 2;   // verticals + horizontals
            EnsureCapacity(lineCount);

            float lo = (centre.x + min) * BuildGrid.CellSize;
            float hi = (centre.x + max) * BuildGrid.CellSize;
            float loZ = (centre.y + min) * BuildGrid.CellSize;
            float hiZ = (centre.y + max) * BuildGrid.CellSize;

            int samples = (max - min) * SamplesPerCell + 1;
            int next = 0;

            // Lines of constant X, running north-south.
            for (int i = min; i <= max; i++)
            {
                float x = (centre.x + i) * BuildGrid.CellSize;
                SetLine(_lines[next++], x, loZ, x, hiZ, samples);
            }
            // Lines of constant Z, running east-west.
            for (int j = min; j <= max; j++)
            {
                float z = (centre.y + j) * BuildGrid.CellSize;
                SetLine(_lines[next++], lo, z, hi, z, samples);
            }

            for (int i = next; i < _lines.Length; i++)
                if (_lines[i] != null && _lines[i].gameObject.activeSelf)
                    _lines[i].gameObject.SetActive(false);
        }

        private static void SetLine(LineRenderer lr, float ax, float az, float bx, float bz,
                                    int samples)
        {
            lr.gameObject.SetActive(true);
            var pts = new Vector3[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = samples == 1 ? 0f : i / (float)(samples - 1);
                float x = math.lerp(ax, bx, t);
                float z = math.lerp(az, bz, t);
                pts[i] = new Vector3(x, TerrainUtility.GetHeight(x, z) + GroundOffset, z);
            }
            lr.positionCount = samples;
            lr.SetPositions(pts);
        }

        // ── Plumbing ────────────────────────────────────────────────────

        private static void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("BuildGridOverlay");
            Object.DontDestroyOnLoad(_root);
        }

        private static void EnsureCapacity(int needed)
        {
            if (_lines.Length >= needed) return;
            var grown = new LineRenderer[needed];
            System.Array.Copy(_lines, grown, _lines.Length);
            for (int i = _lines.Length; i < needed; i++)
                grown[i] = NewLine("GridLine" + i);
            _lines = grown;
        }

        private static LineRenderer NewLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var lr = go.AddComponent<LineRenderer>();

            // Shared shader via ProceduralMaterialHelper — a bare Shader.Find
            // here would be stripped out of a player build.
            if (_sharedMat == null)
            {
                _sharedMat = new Material(ProceduralMaterialHelper.LitShader)
                {
                    name = "BuildGridOverlay_Shared"
                };
                // URP Lit defaults to Opaque, which discards the colour's
                // alpha — the grid must actually blend or it reads as solid
                // lit wire instead of a faint ground marking.
                PlacementOverlayMaterial.MakeTransparent(_sharedMat);
                if (_sharedMat.HasProperty("_BaseColor")) _sharedMat.SetColor("_BaseColor", GridColor);
                if (_sharedMat.HasProperty("_Color")) _sharedMat.SetColor("_Color", GridColor);
            }
            // One SHARED material across every line — the colour never varies,
            // so per-line instances would just be wasted draw state.
            lr.sharedMaterial = _sharedMat;

            lr.startColor = GridColor;
            lr.endColor = GridColor;
            lr.startWidth = LineWidth;
            lr.endWidth = LineWidth;
            lr.useWorldSpace = true;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return lr;
        }
    }
}
