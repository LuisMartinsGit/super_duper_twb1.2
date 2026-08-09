// AlanthorRampartRenderer.cs
// Instanced rampart props over Alanthor-influenced cliffs: where the culture's
// ground coverage reaches steep terrain, the cliff face grows flat WHITE-SLATE
// RETAINING-WALL panels and the cliff crest grows a parapet strip with
// crenellated merlons — real geometry crowning the shader's masonry terraces
// (TWBTerrainOverlays), so influenced cliffs read as built fortification, not
// just re-textured rock.
//
// Purely presentational and fully derived: no sim state, no scene wiring.
//   * Coverage comes from InfluenceMaskTexture.AlanthorCoverage — the SAME
//     eased value the terrain shader paints with, so props grow out of the
//     ground in lockstep with the advancing slate front.
//   * Cliff shape comes from the terrain (interpolated normal + height).
//   * All props are one shared built-in cube mesh, GPU-instanced with
//     per-prop TRS — panels, parapets and merlons differ only by scale.
//   * Instance lists rebuild on a slow cadence (coverage moves slowly);
//     drawing is a few RenderMeshInstanced batches per frame.
//
// Location: Assets/Scripts/Presentation/

using System.Collections.Generic;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Influence;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Presentation
{
    [DefaultExecutionOrder(2100)]
    public sealed class AlanthorRampartRenderer : MonoBehaviour
    {
        // ── Sampling / cadence ────────────────────────────────────────────
        private const float SampleStep = 2f;      // m between cliff samples
        private const float RebuildInterval = 0.4f;
        private const int MaxInstances = 12000;   // hard cap across all lists

        // ── Cliff classification (matches the shader's slope thresholds:
        // steep ≈ 1-normal.y > 0.25..0.45) ────────────────────────────────
        private const float SteepNormalY = 0.78f; // below this = cliff face
        private const float FlatNormalY = 0.90f;  // above this = walkable top
        private const float CrestDrop = 0.6f;     // m a neighbour must fall for a crest

        // ── Growth (driven by the eased coverage, like the paint) ─────────
        private const float CoverageMin = 0.25f;  // props start rising here
        private const float CoverageFull = 0.6f;  // full height at/above this

        // ── Prop dimensions (m) — white slate retaining wall + battlements ─
        private static readonly Vector3 PanelSize = new(2.3f, 2.8f, 0.45f);
        private static readonly Vector3 ParapetSize = new(1.2f, 0.55f, 0.35f);
        private static readonly Vector3 MerlonSize = new(0.55f, 0.75f, 0.4f);

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
            if (Object.FindFirstObjectByType<AlanthorRampartRenderer>() != null) return;
            new GameObject("[Alanthor Rampart Renderer]")
                .AddComponent<AlanthorRampartRenderer>();
        }

        // ─── State ────────────────────────────────────────────────────────
        private UnityEngine.Terrain _terrain;
        private Mesh _cube;
        private Material _material;
        private Bounds _drawBounds;
        private float _rebuildTimer;
        private readonly List<Matrix4x4> _instances = new();
        private Matrix4x4[] _drawArray = System.Array.Empty<Matrix4x4>();
        private int _drawCount;

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void Update()
        {
            // MapMagic destroys/recreates the tile terrain (and its data)
            // while generating — revalidate both every frame, not just once.
            if (_terrain == null || _terrain.terrainData == null)
            {
                _terrain = null;
                if (!TryInit()) return;
            }

            _rebuildTimer -= Time.deltaTime;
            if (_rebuildTimer <= 0f)
            {
                _rebuildTimer = RebuildInterval;
                Rebuild();
            }

            Draw();
        }

        private bool TryInit()
        {
            _terrain = UnityEngine.Terrain.activeTerrain;
            if (_terrain == null || _terrain.terrainData == null) return false;

            if (_material != null) Destroy(_material);

            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            // White slate stone: the terrace masonry texture from the terrain
            // material, tinted cool white so the walls read as dressed slate.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            _material = new Material(shader)
            {
                name = "AlanthorRampart(Runtime)",
                enableInstancing = true,
            };
            var template = _terrain.materialTemplate;
            if (template != null && template.HasTexture("_TerraceAlbedo"))
                _material.SetTexture("_BaseMap", template.GetTexture("_TerraceAlbedo"));
            _material.SetColor("_BaseColor", new Color(0.93f, 0.94f, 0.97f, 1f));
            _material.SetFloat("_Smoothness", 0.35f);

            Vector3 tPos = _terrain.GetPosition();
            Vector3 tSize = _terrain.terrainData.size;
            _drawBounds = new Bounds(
                tPos + tSize * 0.5f,
                tSize + new Vector3(8f, 40f, 8f));
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // BUILD — scan covered ground, classify cliff faces + crests
        // ─────────────────────────────────────────────────────────────────

        private void Rebuild()
        {
            _instances.Clear();
            _crest.Clear();
            _crestByCell.Clear();

            var data = _terrain.terrainData;
            Vector3 tPos = _terrain.GetPosition();
            Vector3 tSize = data.size;

            int iz = 0;
            for (float wz = tPos.z; wz < tPos.z + tSize.z; wz += SampleStep, iz++)
            {
                int ix = 0;
                for (float wx = tPos.x; wx < tPos.x + tSize.x; wx += SampleStep, ix++)
                {
                    // Cheap gate first: no Alanthor ground, no props.
                    float cov = InfluenceMaskTexture.AlanthorCoverage(wx, wz);
                    if (cov < CoverageMin) continue;
                    float grow = Mathf.Clamp01((cov - CoverageMin) / (CoverageFull - CoverageMin));

                    float u = (wx - tPos.x) / tSize.x;
                    float v = (wz - tPos.z) / tSize.z;
                    Vector3 n = data.GetInterpolatedNormal(u, v);
                    float h = tPos.y + data.GetInterpolatedHeight(u, v);

                    if (n.y < SteepNormalY)
                    {
                        AddWallPanel(wx, h, wz, n, grow);
                        if (_instances.Count >= MaxInstances) goto capped;
                    }
                    else if (n.y > FlatNormalY && IsCrest(data, tPos, tSize, wx, h, wz))
                    {
                        _crestByCell[CellKey(ix, iz)] = _crest.Count;
                        _crest.Add(new CrestPoint
                        { Pos = new Vector3(wx, h, wz), Ix = ix, Iz = iz });
                    }
                }
            }

            // Chain the crest cells into contour polylines and lay the
            // battlements along them as one continuous wall.
            BuildBattlementChains(data, tPos, tSize);
        capped:

            _drawCount = _instances.Count;
            if (_drawArray.Length < _drawCount)
                _drawArray = new Matrix4x4[Mathf.NextPowerOfTwo(_drawCount)];
            _instances.CopyTo(_drawArray, 0);
        }

        /// <summary>Flat cell whose neighbourhood drops away steeply.</summary>
        private bool IsCrest(TerrainData data, Vector3 tPos, Vector3 tSize,
            float wx, float h, float wz)
        {
            for (int d = 0; d < 4; d++)
            {
                float nx = wx + CardinalDX[d] * SampleStep;
                float nz = wz + CardinalDZ[d] * SampleStep;
                float nu = Mathf.Clamp01((nx - tPos.x) / tSize.x);
                float nv = Mathf.Clamp01((nz - tPos.z) / tSize.z);
                float nh = tPos.y + data.GetInterpolatedHeight(nu, nv);
                if (h - nh >= CrestDrop
                    && data.GetInterpolatedNormal(nu, nv).y < SteepNormalY)
                    return true;
            }
            return false;
        }

        /// <summary>A flat vertical retaining-wall panel set into the cliff
        /// face, facing outward (along the downhill direction). Adjacent
        /// samples up the slope stack into stepped retaining rows.</summary>
        private void AddWallPanel(float wx, float h, float wz, Vector3 normal, float grow)
        {
            var outDir = new Vector3(normal.x, 0f, normal.z);
            float len = outDir.magnitude;
            if (len < 1e-3f) return;
            outDir /= len;

            var rot = Quaternion.LookRotation(outDir, Vector3.up);
            float height = PanelSize.y * grow;
            Vector3 pos = new Vector3(wx, h + height * 0.5f - 0.6f, wz) + outDir * 0.2f;
            _instances.Add(Matrix4x4.TRS(pos, rot,
                new Vector3(PanelSize.x, height, PanelSize.z)));
        }

        // ─────────────────────────────────────────────────────────────────
        // BATTLEMENTS — crest cells chained into contour polylines, smoothed
        // and resampled at even arc-length so the wall reads as ONE
        // continuous crenellated line following the cliff edge.
        // ─────────────────────────────────────────────────────────────────

        private struct CrestPoint
        {
            public Vector3 Pos;
            public int Ix, Iz;
        }

        private static readonly int[] CardinalDX = { 1, -1, 0, 0 };
        private static readonly int[] CardinalDZ = { 0, 0, 1, -1 };
        // Cardinals first so chains prefer straight continuation, diagonals
        // let them turn corners without breaking.
        private static readonly int[] NeighborDX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighborDZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private readonly List<CrestPoint> _crest = new();
        private readonly Dictionary<long, int> _crestByCell = new();
        private readonly List<int> _chain = new();
        private readonly List<Vector3> _chainPts = new();

        private static long CellKey(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;

        private void BuildBattlementChains(TerrainData data, Vector3 tPos, Vector3 tSize)
        {
            if (_crest.Count < 2) return;
            var visited = new bool[_crest.Count];

            for (int seed = 0; seed < _crest.Count; seed++)
            {
                if (visited[seed]) continue;

                _chain.Clear();
                _chain.Add(seed);
                visited[seed] = true;

                // Grow forward from the tail, then flip and grow the other
                // way, so the seed can sit mid-line.
                for (int pass = 0; pass < 2; pass++)
                {
                    int next;
                    while ((next = FindUnvisitedNeighbor(_chain[_chain.Count - 1], visited)) >= 0)
                    {
                        visited[next] = true;
                        _chain.Add(next);
                    }
                    _chain.Reverse();
                }

                EmitChain(data, tPos, tSize);
                if (_instances.Count >= MaxInstances) return;
            }
        }

        private int FindUnvisitedNeighbor(int idx, bool[] visited)
        {
            var cp = _crest[idx];
            for (int d = 0; d < 8; d++)
            {
                if (_crestByCell.TryGetValue(
                        CellKey(cp.Ix + NeighborDX[d], cp.Iz + NeighborDZ[d]), out int ni)
                    && !visited[ni])
                    return ni;
            }
            return -1;
        }

        /// <summary>Smooth the chained crest into a soft spline and march it
        /// at parapet-length steps: abutting parapet segments aligned to the
        /// local tangent (so the wall is gap-free through corners) with a
        /// merlon on every other station — a continuous crenellation.</summary>
        private void EmitChain(TerrainData data, Vector3 tPos, Vector3 tSize)
        {
            if (_chain.Count < 2) return;

            _chainPts.Clear();
            for (int i = 0; i < _chain.Count; i++)
                _chainPts.Add(_crest[_chain[i]].Pos);

            // Two relaxation passes turn the grid staircase into a spline-like
            // curve (endpoints pinned).
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < _chainPts.Count - 1; i++)
                    _chainPts[i] = (_chainPts[i - 1] + _chainPts[i] + _chainPts[i + 1]) / 3f;

            float SampleH(float x, float z) => tPos.y + data.GetInterpolatedHeight(
                Mathf.Clamp01((x - tPos.x) / tSize.x), Mathf.Clamp01((z - tPos.z) / tSize.z));

            float segLen = ParapetSize.x;      // one parapet per station
            float carry = 0f;                  // arc-length remainder between edges
            int station = 0;

            for (int i = 0; i < _chainPts.Count - 1; i++)
            {
                Vector3 a = _chainPts[i], b = _chainPts[i + 1];
                Vector3 seg = b - a;
                seg.y = 0f;
                float len = seg.magnitude;
                if (len < 1e-4f) continue;
                Vector3 tangent = seg / len;

                float d = carry;
                for (; d < len; d += segLen, station++)
                {
                    Vector3 p = a + tangent * d;

                    float cov = InfluenceMaskTexture.AlanthorCoverage(p.x, p.z);
                    if (cov < CoverageMin) continue;
                    float grow = Mathf.Clamp01((cov - CoverageMin) / (CoverageFull - CoverageMin));

                    // Outward = horizontal perpendicular that points downhill.
                    Vector3 side = Vector3.Cross(Vector3.up, tangent);
                    float hHere = SampleH(p.x, p.z);
                    Vector3 probeA = p + side * 2f;
                    Vector3 probeB = p - side * 2f;
                    Vector3 outDir = SampleH(probeA.x, probeA.z) < SampleH(probeB.x, probeB.z)
                        ? side : -side;

                    var rot = Quaternion.LookRotation(outDir, Vector3.up);
                    Vector3 basePos = new Vector3(p.x, hHere, p.z) + outDir * 0.35f;

                    // Parapet segment — slightly overlong so neighbours abut
                    // through curvature with no visible seams.
                    float ph = ParapetSize.y * grow;
                    _instances.Add(Matrix4x4.TRS(
                        basePos + Vector3.up * (ph * 0.5f), rot,
                        new Vector3(segLen * 1.12f, ph, ParapetSize.z)));

                    // Merlon every other station: merlon-gap-merlon rhythm.
                    if ((station & 1) == 0)
                    {
                        float mh = MerlonSize.y * grow;
                        _instances.Add(Matrix4x4.TRS(
                            basePos + Vector3.up * (ph + mh * 0.5f), rot,
                            new Vector3(MerlonSize.x, mh, MerlonSize.z)));
                    }

                    if (_instances.Count >= MaxInstances) return;
                }
                carry = d - len; // arc-length remainder flows into the next edge
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DRAW
        // ─────────────────────────────────────────────────────────────────

        private void Draw()
        {
            if (_drawCount == 0 || _material == null) return;

            var rp = new RenderParams(_material)
            {
                worldBounds = _drawBounds,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
            };
            for (int start = 0; start < _drawCount; start += 1023)
            {
                int n = Mathf.Min(1023, _drawCount - start);
                Graphics.RenderMeshInstanced(rp, _cube, 0, _drawArray, n, start);
            }
        }
    }
}
