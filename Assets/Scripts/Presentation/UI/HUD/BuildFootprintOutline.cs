// BuildFootprintOutline.cs
// Draws a building's footprint as the exact 2 m build cells it occupies.
// Canonical spec: docs/Design/Build_Grid.md
// Location: Assets/Scripts/UI/HUD/BuildFootprintOutline.cs

using UnityEngine;
using Unity.Mathematics;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Footprint outline for building placement — the border of the occupied
    /// rect plus one interior line per cell seam, so the player reads the
    /// footprint in grid squares rather than guessing from a tinted mesh.
    ///
    /// Replaces nothing: before the build grid there was no footprint outline
    /// at all, only the Gatherer's Hut radius circle (which is a gather-range
    /// display, not a footprint) and the translucent ghost prefab.
    ///
    /// Lifetime: one pooled GameObject, created on first Show and reused. The
    /// LineRenderers are hidden rather than destroyed between placements —
    /// per the presentation perf contract, don't churn renderers per frame.
    /// </summary>
    public static class BuildFootprintOutline
    {
        // Ground clearance so the lines are not z-fought by the terrain.
        private const float GroundOffset = 0.12f;
        private const float LineWidth = 0.10f;
        private const float SeamLineWidth = 0.05f;

        // Samples along each edge so the outline follows terrain relief
        // instead of cutting through a slope.
        private const int SamplesPerMetre = 1;

        // Gold on white grid — the placement palette. Invalid stays red: the
        // blocked state must read instantly, palette or not.
        private static readonly Color ValidColor   = new Color(1f, 0.80f, 0.25f, 0.95f);
        private static readonly Color InvalidColor = new Color(1f, 0.32f, 0.3f, 0.9f);

        private static GameObject _root;
        private static LineRenderer _border;
        private static LineRenderer[] _seams = System.Array.Empty<LineRenderer>();
        private static int _activeSeams;
        private static Material _sharedMat;

        /// <summary>
        /// Draw the outline for a footprint. <paramref name="centre"/> is
        /// expected to be already snapped; the outline is drawn from the
        /// snapped rect regardless, so an unsnapped centre shows where the
        /// building will actually land rather than where the cursor is.
        /// </summary>
        public static void Show(float3 centre, int2 sizeMeters, bool valid)
        {
            EnsureRoot();

            float3 snapped = BuildGrid.Snap(centre, sizeMeters);
            BuildGrid.FootprintCells(snapped, sizeMeters, out int2 minCell, out int2 cellCount);

            float x0 = minCell.x * BuildGrid.CellSize;
            float z0 = minCell.y * BuildGrid.CellSize;
            float x1 = x0 + cellCount.x * BuildGrid.CellSize;
            float z1 = z0 + cellCount.y * BuildGrid.CellSize;

            Color c = valid ? ValidColor : InvalidColor;

            _root.SetActive(true);
            DrawBorder(x0, z0, x1, z1, c);
            DrawSeams(minCell, cellCount, x0, z0, x1, z1, c);
        }

        /// <summary>Hide the outline. Cheap to call every frame.</summary>
        public static void Hide()
        {
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
        }

        // ── Geometry ────────────────────────────────────────────────────

        private static void DrawBorder(float x0, float z0, float x1, float z1, Color c)
        {
            int perX = math.max(2, (int)((x1 - x0) * SamplesPerMetre) + 1);
            int perZ = math.max(2, (int)((z1 - z0) * SamplesPerMetre) + 1);

            // Walk the rect once, corner to corner, skipping the duplicated
            // corner sample at the start of each following edge.
            var pts = new System.Collections.Generic.List<Vector3>(2 * (perX + perZ));
            AppendEdge(pts, x0, z0, x1, z0, perX, true);
            AppendEdge(pts, x1, z0, x1, z1, perZ, false);
            AppendEdge(pts, x1, z1, x0, z1, perX, false);
            AppendEdge(pts, x0, z1, x0, z0, perZ, false);

            _border.loop = true;
            _border.positionCount = pts.Count;
            _border.SetPositions(pts.ToArray());
            Tint(_border, c);
        }

        private static void DrawSeams(int2 minCell, int2 cellCount,
                                      float x0, float z0, float x1, float z1, Color c)
        {
            // One interior line per cell seam: (cols - 1) + (rows - 1).
            int needed = math.max(0, cellCount.x - 1) + math.max(0, cellCount.y - 1);
            EnsureSeamCapacity(needed);
            _activeSeams = needed;

            int next = 0;
            for (int i = 1; i < cellCount.x; i++)
            {
                float x = (minCell.x + i) * BuildGrid.CellSize;
                SetSeam(_seams[next++], x, z0, x, z1,
                        math.max(2, (int)((z1 - z0) * SamplesPerMetre) + 1), c);
            }
            for (int j = 1; j < cellCount.y; j++)
            {
                float z = (minCell.y + j) * BuildGrid.CellSize;
                SetSeam(_seams[next++], x0, z, x1, z,
                        math.max(2, (int)((x1 - x0) * SamplesPerMetre) + 1), c);
            }

            for (int i = next; i < _seams.Length; i++)
                if (_seams[i] != null && _seams[i].gameObject.activeSelf)
                    _seams[i].gameObject.SetActive(false);
        }

        private static void SetSeam(LineRenderer lr, float ax, float az, float bx, float bz,
                                    int samples, Color c)
        {
            lr.gameObject.SetActive(true);
            lr.loop = false;
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
            // Seams sit a touch dimmer than the border so the outer edge of
            // the footprint stays the dominant read.
            Tint(lr, new Color(c.r, c.g, c.b, c.a * 0.45f));
        }

        private static void AppendEdge(System.Collections.Generic.List<Vector3> into,
                                       float ax, float az, float bx, float bz,
                                       int samples, bool includeFirst)
        {
            for (int i = includeFirst ? 0 : 1; i < samples; i++)
            {
                float t = samples == 1 ? 0f : i / (float)(samples - 1);
                float x = math.lerp(ax, bx, t);
                float z = math.lerp(az, bz, t);
                into.Add(new Vector3(x, TerrainUtility.GetHeight(x, z) + GroundOffset, z));
            }
        }

        // ── Plumbing ────────────────────────────────────────────────────

        private static void Tint(LineRenderer lr, Color c)
        {
            lr.startColor = c;
            lr.endColor = c;
            var m = lr.sharedMaterial;
            if (m == null) return;
            // Match the property pattern the Gatherer's Hut circle uses so
            // this works on both URP Lit and the Standard fallback.
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;

            _root = new GameObject("BuildFootprintOutline");
            Object.DontDestroyOnLoad(_root);

            _border = NewLine("Border", LineWidth);
        }

        private static void EnsureSeamCapacity(int needed)
        {
            if (_seams.Length >= needed) return;
            var grown = new LineRenderer[needed];
            System.Array.Copy(_seams, grown, _seams.Length);
            for (int i = _seams.Length; i < needed; i++)
                grown[i] = NewLine("Seam" + i, SeamLineWidth);
            _seams = grown;
        }

        private static LineRenderer NewLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var lr = go.AddComponent<LineRenderer>();

            // Shared shader via ProceduralMaterialHelper — a bare
            // Shader.Find here would be stripped out of a player build.
            if (_sharedMat == null)
            {
                _sharedMat = new Material(ProceduralMaterialHelper.LitShader)
                {
                    name = "BuildFootprintOutline_Shared"
                };
                // Transparent surface so the per-line alpha (dimmer seams)
                // actually blends — Opaque URP Lit discards it.
                PlacementOverlayMaterial.MakeTransparent(_sharedMat);
            }
            // Each line gets its own instance so the border and the dimmer
            // seams can carry different alphas.
            lr.material = new Material(_sharedMat);

            lr.startWidth = width;
            lr.endWidth = width;
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
