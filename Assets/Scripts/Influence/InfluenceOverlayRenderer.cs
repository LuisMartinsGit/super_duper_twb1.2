// InfluenceOverlayRenderer.cs
// In-world influence BORDERS — the visible outline of every territory.
//
// Design (docs/Design/Overview.md § The influence map): "in-world overlay =
// border lines only, traced as smooth splines along the 0.5 contour".
// For every channel that holds ground (the 8 players + the curse) the 0.5
// iso-contour is traced with marching squares over the influence grid, the
// segments are chained into polylines and Chaikin-smoothed into clean
// splines, then emitted as terrain-draped ribbon meshes — players in their
// banner colour, the curse in purple, blood in dark red at its own level.
// No fill, no texture, no pixelation: the FILLS are the terrain shader's job
// (InfluenceMaskTexture), this is only the outline.
//
// Restored 2026-08-18: the tracer was written for the old IMGUI HUD's
// INFLUENCE button, lost its only caller in the UI redesign, and was then
// dropped as dead code in the scripts-layout refactor — so the ground had
// coloured territory with no borders at all. It now mounts itself on
// gameplay scenes and is ON by default; Toggle()/SetVisible() remain for a
// HUD hook.
//
// Fog of war applies (as on the minimap): border vertices in unexplored
// ground are invisible, in explored-but-unseen ground they dim.

using System.Collections.Generic;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Systems.Visibility;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Influence
{
    [DefaultExecutionOrder(2001)] // just after InfluenceMaskTexture (2000)
    public sealed class InfluenceOverlayRenderer : MonoBehaviour
    {
        // The influence field ticks at 0.1 s; retracing every other tick is
        // indistinguishable in motion and halves the cost.
        private const float RebuildInterval = 0.2f;
        private const float Threshold = 0.5f;        // the border contour level
        private const float HeightOffset = 0.6f;     // drape height above terrain
        // Ribbon half-width. 0.24 was tuned for the old always-off debug
        // overlay; at 0.4 the line reads as a border from normal RTS camera
        // height, which is the point of having it on permanently.
        private const float HalfWidth = 0.4f;
        private const int ChaikinIterations = 2;     // corner-cutting passes
        private const byte VisibleAlpha = 230;
        private const byte RevealedAlpha = 110;

        // Blood layer — same spline treatment, its own contour + colour.
        private const float BloodThreshold = 0.35f;
        private static readonly Color32 BloodColor = new Color32(140, 20, 20, 255);

        private static InfluenceOverlayRenderer _instance;

        public static bool IsVisible => _instance != null && _instance._visible;

        /// <summary>Toggle the in-world border overlay (HUD hook). Creates
        /// the renderer on first use; geometry builds lazily once terrain +
        /// influence map exist.</summary>
        public static void Toggle() => SetVisible(!IsVisible);

        /// <summary>Show / hide the in-world border overlay.</summary>
        public static void SetVisible(bool on)
        {
            if (_instance == null)
            {
                if (!on) return;
                _instance = new GameObject("[Influence Overlay]")
                    .AddComponent<InfluenceOverlayRenderer>();
            }
            _instance.ApplyVisible(on);
        }

        // ─── Auto-mount on gameplay scenes ────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!MapRegistry.IsGameplayScene(scene.name)) return;
            if (Object.FindFirstObjectByType<InfluenceOverlayRenderer>() != null) return;
            _instance = new GameObject("[Influence Overlay]")
                .AddComponent<InfluenceOverlayRenderer>();
            _instance.ApplyVisible(true);
        }

        // ─── State ────────────────────────────────────────────────────────
        private bool _visible = true;
        private bool _built;
        private GameObject _meshGo;
        private Mesh _mesh;
        private Material _mat;
        private float _nextRebuild;

        // Terrain height cache — the ribbon has thousands of vertices and
        // Terrain.SampleHeight per vertex per rebuild is milliseconds of main
        // thread. One GetHeights snapshot (re-taken only when the terrain
        // object itself changes) plus bilinear lookup is free by comparison.
        private float[,] _heights;
        private int _heightRes;
        private Vector3 _terrainPos, _terrainSize;
        private int _terrainVersion = -1;

        // Reused scratch — this runs five times a second, so nothing here
        // may allocate after warm-up.
        private readonly List<Vector3> _verts = new();
        private readonly List<Color32> _colors = new();
        private readonly List<int> _tris = new();
        private readonly List<Vector2> _segA = new();
        private readonly List<Vector2> _segB = new();
        private readonly List<Vector2> _pts = new();
        private readonly List<Vector2> _smoothScratch = new();
        private readonly Dictionary<long, int> _endA = new();
        private readonly Dictionary<long, int> _endB = new();
        private bool[] _used = new bool[256];
        private sbyte[] _dominant;   // strongest channel per cell
        private float[] _field;      // scalar field currently being marched

        private void ApplyVisible(bool on)
        {
            _visible = on;
            if (on && !_built) TryBuild();
            if (_meshGo != null) _meshGo.SetActive(on && _built);
            if (on) _nextRebuild = 0f;
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

            double t0 = Time.realtimeSinceStartupAsDouble;
            RefreshTerrainCache();
            RebuildBorders();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("InfluenceBorders",
                (Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
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
            if (!RefreshTerrainCache()) return;

            // Sprites/Default: vertex-colour, alpha-blended, double-sided —
            // renders fine under URP, and it is in the project's
            // always-included shader list, so Shader.Find still resolves it
            // in a player build (unreferenced shaders get stripped).
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[InfluenceOverlay] Sprites/Default missing — " +
                                 "influence borders will not render.");
                return;
            }

            _mesh = new Mesh
            {
                name = "InfluenceBorders",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            _mesh.MarkDynamic();

            _mat = new Material(shader)
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

            int cells = PlayerInfluenceMap.Resolution * PlayerInfluenceMap.Resolution;
            _dominant = new sbyte[cells];
            _field = new float[cells];

            _built = true;
            TWBLog.Log("[InfluenceOverlay] territory borders online.");
        }

        /// <summary>Snapshot the terrain heightmap once per terrain object.
        /// False while no terrain exists yet (MapMagic generates it late).</summary>
        private bool RefreshTerrainCache()
        {
            var terrain = TheWaningBorder.World.Terrain.TerrainUtility.GetActiveTerrain();
            if (terrain == null || terrain.terrainData == null) return false;
            int version = TheWaningBorder.World.Terrain.TerrainUtility.TerrainVersion;
            if (_heights != null && version == _terrainVersion) return true;

            var data = terrain.terrainData;
            _heightRes = data.heightmapResolution;
            _heights = data.GetHeights(0, 0, _heightRes, _heightRes);
            _terrainPos = terrain.transform.position;
            _terrainSize = data.size;
            _terrainVersion = version;
            return true;
        }

        // ─── Rebuild ──────────────────────────────────────────────────────

        private void RebuildBorders()
        {
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();

            // Dominance for every cell, once. The per-channel contour below
            // is ownership-clipped against it, and re-deriving that inside
            // each channel's march would be nine passes instead of one.
            PlayerInfluenceMap.FillDominantChannels(_dominant);

            for (int ch = 0; ch < PlayerInfluenceMap.ChannelCount; ch++)
            {
                if (!BuildClippedField(ch)) continue;

                _segA.Clear();
                _segB.Clear();
                MarchField(Threshold);
                if (_segA.Count == 0) continue;

                ChainAndEmit(PlayerInfluenceMap.ChannelColor(ch));
            }

            // Blood — an independent field with its own contour level.
            if (BloodMap.Ready && BloodMap.HasPresence(BloodThreshold))
            {
                int res = PlayerInfluenceMap.Resolution;
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                        _field[y * res + x] = BloodMap.CellValue(x, y);

                _segA.Clear();
                _segB.Clear();
                MarchField(BloodThreshold);
                if (_segA.Count > 0) ChainAndEmit(BloodColor);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// Load one channel's field into <see cref="_field"/>, OWNERSHIP-CLIPPED:
        /// a cell only counts as this channel's territory while it is the
        /// STRONGEST channel there. Where two players' fields meet, each
        /// border stops at the seam — the meeting reads as a clean DOUBLE
        /// line in the two players' colours, instead of each contour
        /// wandering deep into the other's ground (both fields sit over 0.5
        /// across the whole overlap). Returns false when nothing crosses the
        /// threshold, so an empty channel costs one pass and no tracing.
        /// </summary>
        private bool BuildClippedField(int channel)
        {
            PlayerInfluenceMap.FillChannel(channel, _field);

            bool any = false;
            for (int i = 0; i < _field.Length; i++)
            {
                if (_field[i] < Threshold) continue;
                if (_dominant[i] != channel) _field[i] = Threshold - 0.01f; // outranked — seam cell
                else any = true;
            }
            return any;
        }

        /// <summary>Marching squares over <see cref="_field"/> at the given
        /// contour level — emits raw contour segments in grid space
        /// (cell-centre coordinates). Influence channels and the blood map
        /// share the same grid layout.</summary>
        private void MarchField(float t)
        {
            int res = PlayerInfluenceMap.Resolution;
            var f = _field;

            for (int y = 0; y < res - 1; y++)
            {
                int row = y * res, next = row + res;
                for (int x = 0; x < res - 1; x++)
                {
                    float v00 = f[row + x];
                    float v10 = f[row + x + 1];
                    float v11 = f[next + x + 1];
                    float v01 = f[next + x];

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
            if (_used.Length < n) _used = new bool[Mathf.NextPowerOfTwo(n)];
            else System.Array.Clear(_used, 0, n);

            _endA.Clear();
            _endB.Clear();
            for (int i = 0; i < n; i++)
            {
                Register(Key(_segA[i]), i);
                Register(Key(_segB[i]), i);
            }

            for (int s = 0; s < n; s++)
            {
                if (_used[s]) continue;
                _used[s] = true;

                _pts.Clear();
                _pts.Add(_segA[s]);
                _pts.Add(_segB[s]);
                Extend(_pts);        // grow from the tail
                _pts.Reverse();
                Extend(_pts);        // grow from the (old) head

                bool closed = _pts.Count > 3 && Key(_pts[0]) == Key(_pts[_pts.Count - 1]);
                if (closed) _pts.RemoveAt(_pts.Count - 1);

                for (int it = 0; it < ChaikinIterations; it++)
                    Chaikin(_pts, closed);

                EmitRibbon(_pts, closed, color);
            }
        }

        /// <summary>A contour endpoint sits on a cell EDGE shared by exactly
        /// two cells, so it carries at most two segments — two slots is the
        /// whole adjacency structure, and the dictionaries are reused.</summary>
        private void Register(long key, int seg)
        {
            if (!_endA.ContainsKey(key)) _endA[key] = seg;
            else if (!_endB.ContainsKey(key)) _endB[key] = seg;
        }

        private void Extend(List<Vector2> pts)
        {
            while (true)
            {
                long key = Key(pts[pts.Count - 1]);

                int next = -1;
                if (_endA.TryGetValue(key, out int a) && !_used[a]) next = a;
                else if (_endB.TryGetValue(key, out int b) && !_used[b]) next = b;
                if (next < 0) return;

                _used[next] = true;
                Vector2 pa = _segA[next], pb = _segB[next];
                pts.Add(Key(pa) == key ? pb : pa);

                // Stop when the loop closes.
                if (Key(pts[pts.Count - 1]) == Key(pts[0])) return;
            }
        }

        /// <summary>One Chaikin corner-cutting pass, in place.</summary>
        private void Chaikin(List<Vector2> pts, bool closed)
        {
            int count = pts.Count;
            if (count < 3) return;

            _smoothScratch.Clear();
            if (!closed) _smoothScratch.Add(pts[0]);

            int last = closed ? count : count - 1;
            for (int i = 0; i < last; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % count];
                _smoothScratch.Add(a * 0.75f + b * 0.25f);
                _smoothScratch.Add(a * 0.25f + b * 0.75f);
            }
            if (!closed) _smoothScratch.Add(pts[count - 1]);

            pts.Clear();
            pts.AddRange(_smoothScratch);
        }

        private void EmitRibbon(List<Vector2> pts, bool closed, Color32 color)
        {
            int count = pts.Count;
            if (count < 2) return;

            bool fogged = GameSettings.FogOfWarEnabled;
            // Observer perspective: outline what the VIEWED player can see;
            // an observer with nothing selected sees every border.
            var viewFaction = GameSettings.ViewFactionOrLocal;
            bool fullReveal = GameSettings.IsObserver && GameSettings.ViewFaction == null;
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
                if (fogged && !fullReveal)
                {
                    var p3 = new float3(w.x, 0f, w.y);
                    if (FogOfWarSystem.IsVisibleToFaction(viewFaction, p3))
                        alpha = VisibleAlpha;
                    else if (FogOfWarSystem.IsRevealedToFaction(viewFaction, p3))
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
            _verts.Add(new Vector3(xz.x, HeightAt(xz.x, xz.y) + HeightOffset, xz.y));
            _colors.Add(c);
        }

        /// <summary>Bilinear terrain height from the cached heightmap.</summary>
        private float HeightAt(float worldX, float worldZ)
        {
            if (_heights == null || _heightRes < 2) return _terrainPos.y;

            float u = Mathf.Clamp01((worldX - _terrainPos.x) / Mathf.Max(0.001f, _terrainSize.x))
                      * (_heightRes - 1);
            float v = Mathf.Clamp01((worldZ - _terrainPos.z) / Mathf.Max(0.001f, _terrainSize.z))
                      * (_heightRes - 1);
            int x0 = (int)u, y0 = (int)v;
            int x1 = Mathf.Min(x0 + 1, _heightRes - 1);
            int y1 = Mathf.Min(y0 + 1, _heightRes - 1);
            float tx = u - x0, ty = v - y0;

            // GetHeights is indexed [z, x] and normalized 0..1 over size.y.
            float h00 = _heights[y0, x0], h10 = _heights[y0, x1];
            float h01 = _heights[y1, x0], h11 = _heights[y1, x1];
            float top = h00 + (h10 - h00) * tx;
            float bot = h01 + (h11 - h01) * tx;
            return _terrainPos.y + (top + (bot - top) * ty) * _terrainSize.y;
        }
    }
}
