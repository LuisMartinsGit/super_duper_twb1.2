// CurseShelfRenderer.cs
// The crystal SHELF the curse raises over its ground (2026-10-16): cursed
// ground stands up as crystal, up to Curse.mat's "Shelf Height" (0.5 m by
// default) above the terrain.
//
// The map is cut into 16 m chunks. A chunk gets a 0.5 m grid mesh, laid on
// the terrain, the first time any curse coverage reaches it, and is simply
// switched on and off after that. The mesh itself is flat; the shelf shader
// (TWB/Terrain/Curse Shelf) lifts each vertex by the SAME jagged front the
// terrain overlay draws from the live curse mask, so the shelf grows with
// the curse and its edge is the crystal ground's edge — nothing here has to
// rebuild as the curse spreads.
//
// Its look is Resources/Curse.mat (copied onto the shelf's material — live
// in the Editor), plus the terrain material's overlay edge noise.
//
// Presentation only: the simulation never sees the shelf. Units and
// buildings standing on cursed ground stand INSIDE it (the terrain height
// they walk on is unchanged).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Influence
{
    [DefaultExecutionOrder(2002)]   // after InfluenceMaskTexture (2000)
    public sealed class CurseShelfRenderer : MonoBehaviour
    {
        private const float ChunkSize = 16f;
        private const int Cells = 32;                // 0.5 m between vertices
        private const float CheckInterval = 0.5f;
        private const float ActiveThreshold = 0.03f; // coverage that may raise any shelf
        private const float GroundLift = 0.04f;      // clear of the terrain, no z-fight
        private const string ShelfMaterial = "CurseShelf";
        private const string CurseLook = "Curse";

        private Material _material;   // runtime copy of Resources/CurseShelf
        private Material _curse;      // Resources/Curse — the look
        private readonly Dictionary<int, GameObject> _chunks = new Dictionary<int, GameObject>();
        private int _cols, _rows;
        private Vector2 _origin;
        private float _nextCheck;
        private static readonly int OverlayNoiseScale = Shader.PropertyToID("_OverlayNoiseScale");
        private static readonly int ShelfHeight = Shader.PropertyToID("_CurseShelfHeight");

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
            if (!Application.isPlaying) return;
            if (!MapRegistry.IsGameplayScene(scene.name)) return;
            if (Object.FindFirstObjectByType<CurseShelfRenderer>() != null) return;
            new GameObject("[Curse Crystal Shelf]").AddComponent<CurseShelfRenderer>();
        }

        private void Awake()
        {
            var template = Resources.Load<Material>(ShelfMaterial);
            _curse = Resources.Load<Material>(CurseLook);
            if (template == null || _curse == null)
            {
                Debug.LogError("[CurseShelfRenderer] Resources/CurseShelf.mat or Resources/Curse.mat missing — " +
                               "no crystal shelf this match.");
                enabled = false;
                return;
            }
            _material = new Material(template) { name = "CurseShelf (runtime)" };
            ApplyLook();
        }

        private void OnDestroy()
        {
            foreach (var go in _chunks.Values)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(go);
            }
            _chunks.Clear();
            if (_material != null) Destroy(_material);
        }

        private void ApplyLook()
        {
            TerrainOverlayMaterialBinder.CopyProperties(_curse, _material);
            var terrain = TerrainOverlayMaterialBinder.Current;
            if (terrain != null && terrain.HasProperty(OverlayNoiseScale))
                _material.SetFloat(OverlayNoiseScale, terrain.GetFloat(OverlayNoiseScale));
        }

        private void Update()
        {
            if (_material == null || !InfluenceMaskTexture.Live) return;
#if UNITY_EDITOR
            ApplyLook();   // live: tweak Curse.mat in Play mode and see it
#endif
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;

            if (_cols == 0) LayOut();
            if (_cols == 0) return;
            ApplyLook();

            for (int cz = 0; cz < _rows; cz++)
            for (int cx = 0; cx < _cols; cx++)
            {
                float x0 = _origin.x + cx * ChunkSize, z0 = _origin.y + cz * ChunkSize;
                bool cursed = MaxCoverage(x0, z0) >= ActiveThreshold;
                int key = cz * _cols + cx;
                _chunks.TryGetValue(key, out var go);
                if (cursed && go == null) _chunks[key] = go = BuildChunk(x0, z0);
                if (go != null && go.activeSelf != cursed) go.SetActive(cursed);
            }
        }

        private void LayOut()
        {
            Vector2 min = PlayerInfluenceMap.WorldMin, size = PlayerInfluenceMap.WorldSize;
            if (size.x <= 0f || size.y <= 0f) return;
            _origin = min;
            _cols = Mathf.CeilToInt(size.x / ChunkSize);
            _rows = Mathf.CeilToInt(size.y / ChunkSize);
        }

        /// <summary>Highest curse coverage over the chunk, sampled on a 4x4
        /// grid that overhangs it by a few metres (the mask is coarse, and the
        /// shelf's jagged front reaches a little past the coverage).</summary>
        private static float MaxCoverage(float x0, float z0)
        {
            const float pad = 4f;
            float best = 0f;
            for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                float x = x0 - pad + (ChunkSize + 2f * pad) * i / 3f;
                float z = z0 - pad + (ChunkSize + 2f * pad) * j / 3f;
                float c = InfluenceMaskTexture.CurseCoverage(x, z);
                if (c > best) best = c;
            }
            return best;
        }

        private GameObject BuildChunk(float x0, float z0)
        {
            int n = Cells + 1;
            float step = ChunkSize / Cells;
            var verts = new Vector3[n * n];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = x0 + i * step, z = z0 + j * step;
                float y = TerrainUtility.GetHeight(x, z) + GroundLift;
                verts[j * n + i] = new Vector3(x - x0, y, z - z0);
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            var tris = new int[Cells * Cells * 6];
            int t = 0;
            for (int j = 0; j < Cells; j++)
            for (int i = 0; i < Cells; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = "CurseShelfChunk" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            // The shader lifts the vertices: make room for the shelf in the bounds.
            float lift = (_material.HasProperty(ShelfHeight) ? _material.GetFloat(ShelfHeight) : 0.5f) + 1f;
            mesh.bounds = new Bounds(new Vector3(ChunkSize * 0.5f, (minY + maxY) * 0.5f + lift * 0.5f, ChunkSize * 0.5f),
                                     new Vector3(ChunkSize, maxY - minY + lift, ChunkSize));

            var go = new GameObject($"Shelf {x0:0},{z0:0}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(x0, 0f, z0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }
    }
}
