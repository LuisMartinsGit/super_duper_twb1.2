// InfluenceOverlayRenderer.cs
// In-world territory BORDERS — the visible outline of every holding,
// PROJECTED ONTO THE TERRAIN AS A DECAL (2026-08-31).
//
// For every owner (the 8 players + the curse) the contour of their ground is
// traced with marching squares over the ownership grid, chained into
// polylines and Chaikin-smoothed — and then RASTERIZED into one map-sized
// border texture that a single URP DecalProjector projects straight down
// onto the terrain. The old implementation emitted the same splines as a
// ribbon MESH floating 0.6 m above the ground, which read as a hovering
// tape on every slope; a decal hugs the terrain by construction, at the
// cost of one texture sample inside the projector volume. The texture is
// redrawn only when ownership changes (Regions.md §3b — no per-frame
// territory compute), so per frame this component is one integer compare.
//
// No per-vertex fog handling any more: the fog-of-war overlay plane renders
// above the ground and darkens the decal with everything else, which is the
// same statement the ground fills already make (territory is public
// information; the fog dims, it does not redact).
//
// The FILLS stay the terrain shader's job (InfluenceMaskTexture); this is
// only the outline.

using System.Collections.Generic;
using TheWaningBorder.Core.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Influence
{
    [DefaultExecutionOrder(2001)] // just after InfluenceMaskTexture (2000)
    public sealed class InfluenceOverlayRenderer : MonoBehaviour
    {
        // How often the version numbers are CHECKED; a rebuild only runs
        // when ownership actually changed.
        private const float RebuildInterval = 0.2f;
        private const float Threshold = 0.5f;        // the border contour level
        // Line half-width in metres, as rasterized into the decal. Wider than
        // the old mesh ribbon's 0.15: a projected line has no silhouette
        // against the sky, so it needs a little more ground to read.
        private const float HalfWidth = 0.5f;
        private const int ChaikinIterations = 2;     // corner-cutting passes
        private const byte BorderAlpha = 230;

        /// <summary>Border texture resolution. 2048 over a 1024 m map is
        /// 0.5 m per texel — a 1 m line lands ~2 texels wide, crisp under the
        /// projector's bilinear sample.</summary>
        private const int TexRes = 2048;

        /// <summary>Rendering-layer bit the border decal projects on. The
        /// terrain is the only renderer opted into it, so the borders land on
        /// the ground and never paint across units or buildings.</summary>
        private const uint TerrainDecalLayerBit = 1u << 1;

        // ── Territory outlines ────────────────────────────────────────────
        //
        // The player-coloured line is driven by TERRITORY OWNERSHIP, not by the
        // influence field. docs/Design/Regions.md §6 supersedes the influence
        // model for "who holds this ground", and the practical consequence was
        // that nobody could see their borders at all: influence is an AGE 1
        // thing (InfluenceMapSystem grants nothing to Age 0 buildings), so for
        // the whole opening every channel was flat zero and this renderer had
        // no contour to trace.
        //
        // Ownership is a per-REGION fact, so the field is binary — 1 in the
        // regions you hold, 0 everywhere else — and the marching squares below
        // trace the outline of the union. Regions are static for the match, so
        // the expensive half (which region is each grid cell in) is baked once.
        private const float TerritoryThreshold = 0.7f;
        // Above 0.5 on purpose. On a binary field the crossing interpolates to
        // `t` along the cell edge, so a 0.7 contour sits INSIDE the ground it
        // outlines. Where two players' regions touch, that separates their two
        // lines instead of stacking them on the same edge, where one colour
        // would simply paint over the other.
        // 0.75 -> 0.15 (2026-08-28): a 1.5 m band across the ground read as a
        // painted stripe rather than a border. A line marks where the ground
        // changes hands; it should not be wide enough to be ground itself.
        // 0.15 -> 0.4 (2026-08-31): as a DECAL the line lies flat on the
        // ground with no silhouette, and 0.15 m is under one border-texture
        // texel — 0.4 m keeps it a line, not a stripe, and actually visible.
        private const float TerritoryHalfWidth = 0.4f;

        /// <summary>Corner-cutting passes for the territory outline. More than
        /// the influence contour gets: the territory field is BINARY, so its
        /// marching-squares crossings all land at the same fraction of a cell
        /// edge and the raw polyline is a pure 45-degree staircase. Two passes
        /// leave that staircase visible as a zig-zag; four resolve it into the
        /// curve the partition actually describes.</summary>
        private const int TerritoryChaikinIterations = 4;

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
        private GameObject _decalGo;
        private UnityEngine.Rendering.Universal.DecalProjector _projector;
        private Material _decalMat;
        private Texture2D _tex;
        private Color32[] _pixels;
        private float _nextRebuild;

        // Reused scratch — nothing here may allocate after warm-up.
        private readonly List<Vector2> _segA = new();
        private readonly List<Vector2> _segB = new();
        private readonly List<Vector2> _pts = new();
        private readonly List<Vector2> _smoothScratch = new();
        private readonly Dictionary<long, int> _endA = new();
        private readonly Dictionary<long, int> _endB = new();
        private bool[] _used = new bool[256];
        private sbyte[] _dominant;   // strongest channel per cell
        private float[] _field;      // scalar field currently being marched
        /// <summary>Region id per grid cell, baked once — the partition never
        /// moves, only who owns it does.</summary>
        private short[] _regionCell;
        private bool _regionCellsBaked;
        private bool _territoryLogged;

        /// <summary>Grid content version this mesh was last built from.
        /// Regions.md §3b: border ribbons follow OWNERSHIP, which changes on
        /// events, so the marching-squares pass is gated on this instead of
        /// re-running on a timer forever (it was the single biggest measured
        /// per-frame territory cost — 325 frame spikes in ten minutes).</summary>
        private int _lastDataVersion = int.MinValue;

        private void ApplyVisible(bool on)
        {
            _visible = on;
            if (on && !_built) TryBuild();
            if (_decalGo != null) _decalGo.SetActive(on && _built);
            if (on) _nextRebuild = 0f;
        }

        private void Update()
        {
            if (!_visible) return;

            if (!_built)
            {
                TryBuild();
                if (!_built) return;
                _decalGo.SetActive(true);
            }

            if (Time.time < _nextRebuild) return;
            _nextRebuild = Time.time + RebuildInterval;

            // MapMagic regenerates the terrain OBJECT late (and rebinds can
            // replace it), which resets its rendering-layer opt-in — re-apply
            // whenever the terrain version moves. Integer compare otherwise.
            if (TheWaningBorder.World.Terrain.TerrainUtility.TerrainVersion != _terrainMaskVersion)
                ApplyTerrainDecalLayer();

            // EVENT-DRIVEN (Regions.md §3b): the borders can only change when
            // ownership does, so the interval above is merely how often the
            // version number is CHECKED. An unchanged version costs one
            // integer compare; the marching-squares + Chaikin + rasterize
            // pass runs only on a real change. (The decal needs no terrain
            // gate at all — projection follows the ground for free.)
            if (_lastDataVersion == PlayerInfluenceMap.DataVersion) return;
            _lastDataVersion = PlayerInfluenceMap.DataVersion;

            double t0 = Time.realtimeSinceStartupAsDouble;
            RebuildBorders();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("InfluenceBorders",
                (Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
        }

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_decalMat != null) Destroy(_decalMat);
            if (_instance == this) _instance = null;
        }

        private int _terrainMaskVersion = int.MinValue;

        /// <summary>Opt the active terrain into the border decal's rendering
        /// layer (keeping bit 0, which lights and everything else use).</summary>
        private void ApplyTerrainDecalLayer()
        {
            var terrain = TheWaningBorder.World.Terrain.TerrainUtility.GetActiveTerrain();
            if (terrain == null) return;
            terrain.renderingLayerMask |= TerrainDecalLayerBit;
            _terrainMaskVersion = TheWaningBorder.World.Terrain.TerrainUtility.TerrainVersion;
        }

        // ─── Setup ────────────────────────────────────────────────────────

        private void TryBuild()
        {
            if (!PlayerInfluenceMap.Ready) return;

            // The decal material lives in Resources so its URP Decal shader
            // ships in player builds as a dependency (Shader.Find on an
            // unreferenced shader would be stripped — see the fog shader
            // lesson). An instance is drawn on, never the asset.
            var src = Resources.Load<Material>("TWBTerritoryBorderDecal");
            if (src == null)
            {
                Debug.LogWarning("[InfluenceOverlay] Resources/TWBTerritoryBorderDecal.mat " +
                                 "missing — territory borders will not render.");
                return;
            }

            _tex = new Texture2D(TexRes, TexRes, TextureFormat.RGBA32, false)
            {
                name = "TerritoryBorders",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _pixels = new Color32[TexRes * TexRes];

            _decalMat = new Material(src) { name = "TerritoryBorderDecalMat" };
            _decalMat.SetTexture("Base_Map", _tex);
            _decalMat.SetTexture("_BaseMap", _tex);

            // ONE map-sized projector, firing straight down. Depth spans the
            // whole plausible height range; pivot half a depth forward so the
            // volume hangs below the transform. The projector's local X/Y
            // axes line up with world X/Z, which is exactly how the texture
            // is rasterized.
            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;
            const float Depth = 300f;

            _decalGo = new GameObject("TerritoryBorderDecal");
            _decalGo.transform.SetParent(transform, false);
            _decalGo.transform.position = new Vector3(
                min.x + size.x * 0.5f, Depth * 0.5f, min.y + size.y * 0.5f);
            _decalGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _projector = _decalGo.AddComponent<UnityEngine.Rendering.Universal.DecalProjector>();
            _projector.material = _decalMat;
            _projector.size = new Vector3(size.x, size.y, Depth);
            _projector.pivot = new Vector3(0f, 0f, Depth * 0.5f);
            // TERRAIN ONLY. Everything inside the volume would receive the
            // decal otherwise — units and buildings crossing a border got the
            // line painted across them. The projector emits on rendering
            // layer bit 1 (the renderer feature has decal layers enabled) and
            // only the terrain is opted into that bit below; every renderer
            // ships on bit 0 by default, so nothing else receives.
            _projector.renderingLayerMask = TerrainDecalLayerBit;
            ApplyTerrainDecalLayer();

            int cells = PlayerInfluenceMap.Resolution * PlayerInfluenceMap.Resolution;
            _dominant = new sbyte[cells];
            _field = new float[cells];

            _built = true;
            TWBLog.Log("[InfluenceOverlay] territory border decal online.");
        }

        // ─── Rebuild ──────────────────────────────────────────────────────

        private void RebuildBorders()
        {
            System.Array.Clear(_pixels, 0, _pixels.Length);

            // Dominance for every cell, once. The per-channel contour below
            // is ownership-clipped against it, and re-deriving that inside
            // each channel's march would be nine passes instead of one.
            PlayerInfluenceMap.FillDominantChannels(_dominant);

            // PLAYERS: outline the territory they hold, in their banner colour.
            // Falls back to the influence contour only where there is no
            // partition to outline (a scenario fixture, or a map shipped
            // without region seeds) — there the influence field is still the
            // only statement this renderer can make about ownership.
            bool byTerritory = BakeRegionCells();
            int outlined = 0;

            for (int ch = 0; ch < PlayerInfluenceMap.ChannelCount; ch++)
            {
                bool isPlayer = ch < PlayerInfluenceMap.PlayerChannels;

                if (isPlayer && byTerritory)
                {
                    // Cheap reject first: most factions hold nothing, and
                    // filling + marching an all-zero field to discover that
                    // costs a full grid pass per faction per rebuild.
                    if (!BuildTerritoryField(ch)) continue;

                    _segA.Clear();
                    _segB.Clear();
                    MarchField(TerritoryThreshold);
                    if (_segA.Count == 0) continue;

                    ChainAndEmit(PlayerInfluenceMap.ChannelColor(ch), TerritoryHalfWidth,
                        TerritoryChaikinIterations);
                    outlined++;
                    continue;
                }

                // The curse channel rides the same grid: since Regions.md §3
                // was implemented (2026-08-31) the rasterized ownership grid
                // carries curse-held territories at full strength, so its 0.5
                // contour IS the curse's territorial outline.
                if (!BuildClippedField(ch)) continue;

                _segA.Clear();
                _segB.Clear();
                MarchField(Threshold);
                if (_segA.Count == 0) continue;

                ChainAndEmit(PlayerInfluenceMap.ChannelColor(ch), HalfWidth, ChaikinIterations);
            }

            // Once, at the moment the first outline actually reaches the mesh.
            // Its ABSENCE is the useful half: no line here means ownership
            // never resolved, which is a different failure from a partition
            // that never loaded (that one leaves byTerritory false).
            if (byTerritory && outlined > 0 && !_territoryLogged)
            {
                _territoryLogged = true;
                Debug.Log($"[InfluenceOverlay] territory outlines online — " +
                          $"{TheWaningBorder.World.Regions.RegionMap.Count} region(s), " +
                          $"{outlined} faction(s) outlined.");
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
                if (_segA.Count > 0) ChainAndEmit(BloodColor, HalfWidth, ChaikinIterations);
            }

            _tex.SetPixels32(_pixels);
            _tex.Apply(false, false);
        }

        /// <summary>
        /// Bake which region each grid cell belongs to. Once per match: the
        /// seeds are authored and never move, and the domain warp is a pure
        /// function of position, so the partition is fixed for the whole match
        /// and only its OWNERS change.
        ///
        /// RegionAt, so ground no region can own is owned by NOBODY: Regions.md
        /// §1 — "mountains and cliffs are pure structure, scenery that divides
        /// territories and belongs to nobody, ever". A massif inside your
        /// holding is therefore a hole in your outline and the border runs
        /// around its foot, which is the whole point of excluding it.
        ///
        /// (This was NearestRegion first, to avoid exactly those holes. That
        /// was the wrong call: the holes are the design, not an artefact.)
        ///
        /// False while the partition does not exist yet (it is built during the
        /// loading coroutine, after this renderer comes alive) or the map ships
        /// none at all.
        /// </summary>
        private bool BakeRegionCells()
        {
            if (_regionCellsBaked) return true;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return false;

            int res = PlayerInfluenceMap.Resolution;
            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;
            if (_regionCell == null || _regionCell.Length != res * res)
                _regionCell = new short[res * res];

            for (int y = 0; y < res; y++)
            {
                float wz = min.y + (y + 0.5f) / res * size.y;
                int row = y * res;
                for (int x = 0; x < res; x++)
                {
                    float wx = min.x + (x + 0.5f) / res * size.x;
                    _regionCell[row + x] =
                        (short)TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);
                }
            }

            _regionCellsBaked = true;
            return true;
        }

        /// <summary>
        /// Load a binary "this faction holds it" field for the marching pass.
        /// False when the faction holds nothing, which is the common case and
        /// worth answering before touching the grid at all.
        /// </summary>
        private bool BuildTerritoryField(int faction)
        {
            // Ownership is normally derived by TerritoryIncomeSystem's 5 s
            // tick, whose first run is a full interval after the match starts.
            // Waiting for it would leave every player's border missing for the
            // opening five seconds — exactly when they are looking for it.
            if (!TheWaningBorder.World.Regions.TerritoryOwnership.Ready)
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w == null || !w.IsCreated) return false;
                TheWaningBorder.World.Regions.TerritoryOwnership.Recompute(w.EntityManager);
            }

            if (TheWaningBorder.World.Regions.TerritoryOwnership.CountOf((Faction)faction) == 0)
                return false;

            for (int i = 0; i < _field.Length; i++)
            {
                int region = _regionCell[i];
                // RegionMap.None (-1) is unclaimable ground — mountain, cliff,
                // water, the rim. It belongs to nobody, so it is 0 for every
                // faction and the contour closes around it.
                _field[i] = region >= 0
                    && TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(region) == faction
                        ? 1f : 0f;
            }
            return true;
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

        private void ChainAndEmit(Color32 color, float halfWidth, int smoothing)
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

                for (int it = 0; it < smoothing; it++)
                    Chaikin(_pts, closed);

                RasterizeChain(_pts, closed, color, halfWidth);
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

        /// <summary>
        /// Draw a smoothed polyline into the border texture as a thick line —
        /// the decal projector puts it on the ground. Cost is proportional to
        /// line LENGTH, not map area, and it only runs on ownership change.
        /// </summary>
        private void RasterizeChain(List<Vector2> pts, bool closed, Color32 color, float halfWidth)
        {
            int count = pts.Count;
            if (count < 2) return;

            Vector2 min = PlayerInfluenceMap.WorldMin;
            Vector2 size = PlayerInfluenceMap.WorldSize;
            float cellX = size.x / PlayerInfluenceMap.Resolution;
            float cellZ = size.y / PlayerInfluenceMap.Resolution;
            float texPerX = TexRes / size.x;
            float texPerZ = TexRes / size.y;

            // Grid space → texel space directly (world drops out of the
            // middle): decal UV (0,0) sits at (WorldMin.x, WorldMin.y) and
            // spans WorldSize, which is exactly this texture's mapping.
            Vector2 ToTex(Vector2 p) => new Vector2(
                (p.x + 0.5f) * cellX * texPerX,
                (p.y + 0.5f) * cellZ * texPerZ);

            float radius = Mathf.Max(1f, halfWidth * texPerX);
            var c = new Color32(color.r, color.g, color.b, BorderAlpha);

            int last = closed ? count : count - 1;
            for (int i = 0; i < last; i++)
                StampSegment(ToTex(pts[i]), ToTex(pts[(i + 1) % count]), radius, c);
        }

        /// <summary>Stamp a segment as overlapping discs, in texel space.</summary>
        private void StampSegment(Vector2 a, Vector2 b, float radius, Color32 c)
        {
            float len = (b - a).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (radius * 0.5f)));
            int ri = Mathf.CeilToInt(radius);
            float r2 = radius * radius;

            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                int px = Mathf.RoundToInt(p.x);
                int py = Mathf.RoundToInt(p.y);
                for (int dy = -ri; dy <= ri; dy++)
                {
                    int y = py + dy;
                    if (y < 0 || y >= TexRes) continue;
                    int row = y * TexRes;
                    for (int dx = -ri; dx <= ri; dx++)
                    {
                        int x = px + dx;
                        if (x < 0 || x >= TexRes) continue;
                        if (dx * dx + dy * dy > r2) continue;
                        _pixels[row + x] = c;
                    }
                }
            }
        }
    }
}
