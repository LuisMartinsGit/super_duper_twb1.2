// InfluenceOverlayRenderer.cs
// In-world influence visualization, toggled from the HUD's INFLUENCE
// button. BORDER-ONLY (design 2026-07-06 rev.4): for every channel with
// territory (players + curse), the 0.5 iso-contour is traced with marching
// squares over the influence grid, the segments are chained into polylines
// and Chaikin-smoothed into clean splines, then rendered as terrain-draped
// ribbon meshes — players in their banner colour, the curse in purple.
// No fill, no texture, no pixelation.
//
// Fog of war applies (as on the minimap): border vertices in unexplored
// ground are invisible, in explored-but-unseen ground they dim.
//
// Rebuilt every sim tick (0.1 s) — the contour only moves when the
// influence field does.

using System.Collections.Generic;
using TheWaningBorder.Systems.Visibility;
using TheWaningBorder.World.FogOfWar;
using Unity.Mathematics;
using UnityEngine;

namespace TheWaningBorder.Influence
{
    public sealed class InfluenceOverlayRenderer : MonoBehaviour
    {
        private const float RebuildInterval = 0.1f;  // matches InfluenceMapSystem's tick
        private const float Threshold = 0.5f;        // the border contour level
        private const float HeightOffset = 0.6f;     // drape height above terrain
        private const float HalfWidth = 0.24f;       // ribbon half-width (world units; −60 % 2026-07-06)
        private const int ChaikinIterations = 2;     // corner-cutting passes
        private const byte VisibleAlpha = 230;
        private const byte RevealedAlpha = 110;

        // Blood layer — same spline treatment, its own contour + colour.
        private const float BloodThreshold = 0.35f;
        private static readonly Color32 BloodColor = new Color32(140, 20, 20, 255);

        private static InfluenceOverlayRenderer _instance;

        public static bool IsVisible => _instance != null && _instance._visible;

        /// <summary>Toggle the in-world border overlay. Creates the renderer
        /// on first use; geometry builds lazily once terrain + influence map
        /// exist.</summary>
        public static void Toggle()
        {
            if (_instance == null)
            {
                var go = new GameObject("[Influence Overlay]");
                _instance = go.AddComponent<InfluenceOverlayRenderer>();
            }
            _instance.SetVisible(!_instance._visible);
        }

        private bool _visible;
        private bool _built;
        private GameObject _meshGo;
        private Mesh _mesh;
        private Material _mat;
        private Terrain _terrain;
        private float _terrainY;
        private float _nextRebuild;

        // Reused scratch buffers.
        private readonly List<Vector3> _verts = new();
        private readonly List<Color32> _colors = new();
        private readonly List<int> _tris = new();
        private readonly List<Vector2> _segA = new();
        private readonly List<Vector2> _segB = new();

        private void SetVisible(bool on)
        {
            _visible = on;
            if (on && !_built) TryBuild();
            if (_meshGo != null) _meshGo.SetActive(on && _built);
            if (on) { _nextRebuild = 0f; }
        }

        private void Update()
        {
            if (!_visible) return;

            if (!_built)
            {
                TryBuild();
                if (!_built) return;
                _meshGo.SetActive(true);
            }

            if (Time.time < _nextRebuild) return;
            _nextRebuild = Time.time + RebuildInterval;
            RebuildBorders();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_mat != null) Destroy(_mat);
            if (_instance == this) _instance = null;
        }

        // ─── Setup ────────────────────────────────────────────────────────

        private void TryBuild()
        {
            if (!PlayerInfluenceMap.Ready) return;
            _terrain = Terrain.activeTerrain;
            if (_terrain == null || _terrain.terrainData == null) return;
            _terrainY = _terrain.transform.position.y;

            _mesh = new Mesh
            {
                name = "InfluenceBorders",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };

            // Sprites/Default: vertex-colour, alpha-blended, double-sided —
            // and it renders fine under URP. No custom shader asset needed.
            _mat = new Material(Shader.Find("Sprites/Default"))
            {
                name = "InfluenceBorderMat",
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
            };

            _meshGo = new GameObject("InfluenceBorderMesh",
                typeof(MeshFilter), typeof(MeshRenderer));
            _meshGo.transform.SetParent(transform, false);
            _meshGo.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _meshGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _built = true;
        }

        // ─── Rebuild ──────────────────────────────────────────────────────

        private void RebuildBorders()
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();

            for (int ch = 0; ch < PlayerInfluenceMap.ChannelCount; ch++)
            {
                if (!PlayerInfluenceMap.ChannelHasPresence(ch, Threshold)) continue;

                int channel = ch;
                _segA.Clear();
                _segB.Clear();
                // OWNERSHIP-CLIPPED contour (2026-08-04): a cell only counts
                // as this channel's territory while it is the STRONGEST
                // channel there. Where two players' fields meet, each border
                // stops at the seam — the meeting reads as a clean DOUBLE
                // line in the two players' colours, instead of each contour
                // wandering deep into the other's ground (both fields sit
                // over 0.5 across the whole overlap).
                MarchField((x, y) =>
                {
                    float v = PlayerInfluenceMap.CellValue(x, y, channel);
                    if (v < Threshold) return v;
                    for (int o = 0; o < PlayerInfluenceMap.ChannelCount; o++)
                        if (o != channel && PlayerInfluenceMap.CellValue(x, y, o) > v)
                            return Threshold - 0.01f; // outranked — seam cell
                    return v;
                }, Threshold);
                if (_segA.Count == 0) continue;

                Color32 color = PlayerInfluenceMap.ChannelColor(ch);
                ChainAndEmit(color);
            }

            // Blood — an independent field with its own contour level.
            if (BloodMap.Ready && BloodMap.HasPresence(BloodThreshold))
            {
                _segA.Clear();
                _segB.Clear();
                MarchField(BloodMap.CellValue, BloodThreshold);
                if (_segA.Count > 0) ChainAndEmit(BloodColor);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>Marching squares over a scalar cell grid at the given
        /// contour level — emits raw contour segments in grid space
        /// (cell-centre coordinates). The getter is (x, y) → 0..1; both the
        /// influence channels and the blood map share the same grid layout.</summary>
        private void MarchField(System.Func<int, int, float> value, float t)
        {
            int res = PlayerInfluenceMap.Resolution;

            for (int y = 0; y < res - 1; y++)
            {
                for (int x = 0; x < res - 1; x++)
                {
                    float v00 = value(x, y);
                    float v10 = value(x + 1, y);
                    float v11 = value(x + 1, y + 1);
                    float v01 = value(x, y + 1);

                    int c = (v00 >= t ? 1 : 0) | (v10 >= t ? 2 : 0)
                          | (v11 >= t ? 4 : 0) | (v01 >= t ? 8 : 0);
                    if (c == 0 || c == 15) continue;

                    float Frac(float a, float b) => (t - a) / (b - a);

                    // Edge intersection points (grid space).
                    Vector2 E0() => new Vector2(x + Frac(v00, v10), y);          // bottom
                    Vector2 E1() => new Vector2(x + 1, y + Frac(v10, v11));      // right
                    Vector2 E2() => new Vector2(x + Frac(v01, v11), y + 1);      // top
                    Vector2 E3() => new Vector2(x, y + Frac(v00, v01));          // left

                    switch (c)
                    {
                        case 1:  AddSeg(E3(), E0()); break;
                        case 2:  AddSeg(E0(), E1()); break;
                        case 3:  AddSeg(E3(), E1()); break;
                        case 4:  AddSeg(E1(), E2()); break;
                        case 5:  AddSeg(E3(), E0()); AddSeg(E1(), E2()); break;
                        case 6:  AddSeg(E0(), E2()); break;
                        case 7:  AddSeg(E3(), E2()); break;
                        case 8:  AddSeg(E2(), E3()); break;
                        case 9:  AddSeg(E0(), E2()); break;
                        case 10: AddSeg(E0(), E1()); AddSeg(E2(), E3()); break;
                        case 11: AddSeg(E1(), E2()); break;
                        case 12: AddSeg(E1(), E3()); break;
                        case 13: AddSeg(E0(), E1()); break;
                        case 14: AddSeg(E3(), E0()); break;
                    }
                }
            }
        }

        private void AddSeg(Vector2 a, Vector2 b)
        {
            _segA.Add(a);
            _segB.Add(b);
        }

        // ─── Chaining + smoothing + ribbon emission ──────────────────────

        private static long Key(Vector2 p)
        {
            // Adjacent cells compute shared edge points from the same two
            // cell values, so matching endpoints are bit-identical; a
            // quantized key just guards against float noise.
            long ix = Mathf.RoundToInt(p.x * 64f);
            long iy = Mathf.RoundToInt(p.y * 64f);
            return (ix << 32) | (uint)iy;
        }

        private void ChainAndEmit(Color32 color)
        {
            int n = _segA.Count;
            var used = new bool[n];
            var map = new Dictionary<long, List<int>>(n * 2);

            for (int i = 0; i < n; i++)
            {
                Register(map, Key(_segA[i]), i);
                Register(map, Key(_segB[i]), i);
            }

            var pts = new List<Vector2>(64);
            for (int s = 0; s < n; s++)
            {
                if (used[s]) continue;
                used[s] = true;

                pts.Clear();
                pts.Add(_segA[s]);
                pts.Add(_segB[s]);
                Extend(map, used, pts, atHead: false); // grow from the tail
                pts.Reverse();
                Extend(map, used, pts, atHead: false); // grow from the (old) head

                bool closed = pts.Count > 3 && Key(pts[0]) == Key(pts[pts.Count - 1]);
                if (closed) pts.RemoveAt(pts.Count - 1);

                for (int it = 0; it < ChaikinIterations; it++)
                    Chaikin(pts, closed);

                EmitRibbon(pts, closed, color);
            }
        }

        private static void Register(Dictionary<long, List<int>> map, long key, int seg)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                map[key] = list;
            }
            list.Add(seg);
        }

        private void Extend(Dictionary<long, List<int>> map, bool[] used,
            List<Vector2> pts, bool atHead)
        {
            while (true)
            {
                long key = Key(pts[pts.Count - 1]);
                if (!map.TryGetValue(key, out var candidates)) return;

                int next = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!used[candidates[i]]) { next = candidates[i]; break; }
                }
                if (next < 0) return;

                used[next] = true;
                Vector2 a = _segA[next], b = _segB[next];
                pts.Add(Key(a) == key ? b : a);

                // Stop when the loop closes.
                if (Key(pts[pts.Count - 1]) == Key(pts[0])) return;
            }
        }

        /// <summary>One Chaikin corner-cutting pass, in place.</summary>
        private static void Chaikin(List<Vector2> pts, bool closed)
        {
            int count = pts.Count;
            if (count < 3) return;

            var outPts = new List<Vector2>(count * 2);
            if (!closed) outPts.Add(pts[0]);

            int last = closed ? count : count - 1;
            for (int i = 0; i < last; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % count];
                outPts.Add(a * 0.75f + b * 0.25f);
                outPts.Add(a * 0.25f + b * 0.75f);
            }
            if (!closed) outPts.Add(pts[count - 1]);

            pts.Clear();
            pts.AddRange(outPts);
        }

        private void EmitRibbon(List<Vector2> pts, bool closed, Color32 color)
        {
            int count = pts.Count;
            if (count < 2) return;

            bool fogged = GameSettings.FogOfWarEnabled;
            var localFaction = GameSettings.LocalPlayerFaction;
            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;
            float cellX = size.x / PlayerInfluenceMap.Resolution;
            float cellZ = size.y / PlayerInfluenceMap.Resolution;

            Vector2 GridToWorld(Vector2 p) => new Vector2(
                min.x + (p.x + 0.5f) * cellX,
                min.y + (p.y + 0.5f) * cellZ);

            int baseIndex = _verts.Count;

            for (int i = 0; i < count; i++)
            {
                Vector2 prev = pts[closed ? (i - 1 + count) % count : Mathf.Max(0, i - 1)];
                Vector2 next = pts[closed ? (i + 1) % count : Mathf.Min(count - 1, i + 1)];

                Vector2 w = GridToWorld(pts[i]);
                Vector2 dir = GridToWorld(next) - GridToWorld(prev);
                float len = dir.magnitude;
                dir = len > 1e-5f ? dir / len : Vector2.right;
                Vector2 normal = new Vector2(-dir.y, dir.x) * HalfWidth;

                // Fog of war: hidden → invisible, revealed → dim, visible → full.
                byte alpha = VisibleAlpha;
                if (fogged)
                {
                    var p3 = new float3(w.x, 0f, w.y);
                    if (FogOfWarSystem.IsVisibleToFaction(localFaction, p3))
                        alpha = VisibleAlpha;
                    else if (FogOfWarSystem.IsRevealedToFaction(localFaction, p3))
                        alpha = RevealedAlpha;
                    else
                        alpha = 0;
                }
                var c = new Color32(color.r, color.g, color.b, alpha);

                AddRibbonVert(w + normal, c);
                AddRibbonVert(w - normal, c);

                if (i > 0)
                {
                    int b = baseIndex + i * 2;
                    _tris.Add(b - 2); _tris.Add(b);     _tris.Add(b - 1);
                    _tris.Add(b - 1); _tris.Add(b);     _tris.Add(b + 1);
                }
            }

            if (closed)
            {
                int last = baseIndex + (count - 1) * 2;
                _tris.Add(last);     _tris.Add(baseIndex);     _tris.Add(last + 1);
                _tris.Add(last + 1); _tris.Add(baseIndex);     _tris.Add(baseIndex + 1);
            }
        }

        private void AddRibbonVert(Vector2 xz, Color32 c)
        {
            float y = _terrainY + _terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + HeightOffset;
            _verts.Add(new Vector3(xz.x, y, xz.y));
            _colors.Add(c);
        }
    }
}
