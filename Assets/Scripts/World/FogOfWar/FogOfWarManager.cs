using System;
using UnityEngine;

namespace TheWaningBorder.World.FogOfWar
{
    /// <summary>
    /// Core fog of war manager handling per-faction visibility grids.
    /// Maintains visible (current frame) and revealed (persistent) state for each cell.
    /// Updates an Alpha8 texture for the human player's FoW overlay.
    /// </summary>
    public class FogOfWarManager : MonoBehaviour
    {
        public static FogOfWarManager Instance { get; private set; }

        [Header("Grid")]
        public Vector2 WorldMin = new Vector2(-12.5f, -12.5f);
        public Vector2 WorldMax = new Vector2(12.5f, 12.5f);
        public float CellSize = 0.1f;

        [Header("Visuals (Human Player)")]
        public Faction HumanFaction = GameSettings.LocalPlayerFaction;
        [Tooltip("Material that uses the Unlit/FogOfWar shader.")]
        public Material FogMaterial;
        [Tooltip("Quad or plane that covers the playable area; its material will be set to FogMaterial.")]
        public MeshRenderer FogRenderer;
        [Range(0, 1)] public float ExploredAlpha = 0.65f; // explored-but-not-currently-visible
        [Range(0, 1)] public float HiddenAlpha = 0.98f;   // never seen

        // Internal
        int _w, _h;
        byte[] _visible;   // [faction][cell], 0/1 current frame
        byte[] _revealed;  // [faction][cell], 0/1 persistent
        Texture2D _tex;    // human overlay

        const int MaxFactions = 8;

        int Idx(int x, int y) => y * _w + x;

        // Map any enum to a safe slice [0..MaxFactions-1]
        int FOfs(Faction f)
        {
            int fi = (int)f;
            if (fi < 0) fi = -fi;
            fi %= MaxFactions;
            return fi * _w * _h;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _w = Mathf.CeilToInt((WorldMax.x - WorldMin.x) / CellSize);
            _h = Mathf.CeilToInt((WorldMax.y - WorldMin.y) / CellSize);
            _visible = new byte[MaxFactions * _w * _h];
            _revealed = new byte[MaxFactions * _w * _h];

            _tex = new Texture2D(_w, _h, TextureFormat.Alpha8, false, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            EnsureMaterialBound();
            ClearAll();
            PushHumanTexture();
        }

        /// <summary>Ensures FogMaterial, FogRenderer and shader params are bound to _tex.</summary>
        void EnsureMaterialBound()
        {
            if (FogMaterial == null && FogRenderer != null)
                FogMaterial = FogRenderer.sharedMaterial;

            if (FogMaterial == null) return;

            if (FogMaterial.mainTexture != _tex)
                FogMaterial.mainTexture = _tex;

            FogMaterial.SetVector("_WorldMin", new Vector4(WorldMin.x, 0, WorldMin.y, 0));
            FogMaterial.SetVector("_WorldMax", new Vector4(WorldMax.x, 0, WorldMax.y, 0));

            if (FogRenderer != null && FogRenderer.sharedMaterial != FogMaterial)
                FogRenderer.sharedMaterial = FogMaterial;
        }

        public void ClearAll()
        {
            Array.Clear(_visible, 0, _visible.Length);
            // NOTE: revealed persists across frames; do NOT clear here
        }

        /// <summary>Call once per frame before stamping to zero current visibility only.</summary>
        public void BeginFrame()
        {
            Array.Clear(_visible, 0, _visible.Length);
        }

        /// <summary>Stamp a circular LoS for a faction.</summary>
        public void Stamp(Faction f, Vector3 worldPos, float radius)
        {
            int fx = FOfs(f);

            float gx = (worldPos.x - WorldMin.x) / CellSize;
            float gy = (worldPos.z - WorldMin.y) / CellSize;
            float r = Mathf.Max(0.01f, radius / CellSize);
            int minx = Mathf.Clamp(Mathf.FloorToInt(gx - r), 0, _w - 1);
            int maxx = Mathf.Clamp(Mathf.CeilToInt(gx + r), 0, _w - 1);
            int miny = Mathf.Clamp(Mathf.FloorToInt(gy - r), 0, _h - 1);
            int maxy = Mathf.Clamp(Mathf.CeilToInt(gy + r), 0, _h - 1);
            float r2 = r * r;

            for (int y = miny; y <= maxy; y++)
            {
                for (int x = minx; x <= maxx; x++)
                {
                    float dx = (x + 0.5f) - gx;
                    float dy = (y + 0.5f) - gy;
                    if (dx * dx + dy * dy <= r2)
                    {
                        int i = fx + Idx(x, y);
                        _visible[i] = 1;
                        _revealed[i] = 1;
                    }
                }
            }
        }

        /// <summary>Update the human overlay texture after stamping.</summary>
        public void EndFrameAndBuild()
        {
            EnsureMaterialBound();
            PushHumanTexture();
        }

        void PushHumanTexture()
        {
            int ofs = FOfs(HumanFaction);

            if (_tex.width != _w || _tex.height != _h)
            {
                _tex.Reinitialize(_w, _h);
                _tex.filterMode = FilterMode.Point;
                _tex.wrapMode = TextureWrapMode.Clamp;
                EnsureMaterialBound();
            }

            var data = _tex.GetRawTextureData<byte>();
            int required = _w * _h;
            if (data.Length != required)
            {
                _tex.Reinitialize(_w, _h);
                data = _tex.GetRawTextureData<byte>();
                EnsureMaterialBound();
            }

            for (int i = 0; i < required; i++)
            {
                byte vis = _visible[ofs + i];
                byte rev = _revealed[ofs + i];

                byte a = 255;
                if (vis == 1) a = 0;
                else if (rev == 1) a = (byte)Mathf.RoundToInt(ExploredAlpha * 255f);
                else a = (byte)Mathf.RoundToInt(HiddenAlpha * 255f);

                data[i] = a;
            }

            _tex.Apply(false, false);
        }

        public bool IsVisible(Faction f, Vector3 worldPos)
        {
            if (!WorldToCell(worldPos, out int x, out int y)) return false;
            return _visible[FOfs(f) + Idx(x, y)] != 0;
        }

        public bool IsRevealed(Faction f, Vector3 worldPos)
        {
            if (!WorldToCell(worldPos, out int x, out int y)) return false;
            return _revealed[FOfs(f) + Idx(x, y)] != 0;
        }

        bool WorldToCell(Vector3 pos, out int x, out int y)
        {
            x = Mathf.FloorToInt((pos.x - WorldMin.x) / CellSize);
            y = Mathf.FloorToInt((pos.z - WorldMin.y) / CellSize);
            return (x >= 0 && x < _w && y >= 0 && y < _h);
        }

        public void ForceRebuildGrid(bool clearRevealed = false)
        {
            int newW = Mathf.CeilToInt((WorldMax.x - WorldMin.x) / CellSize);
            int newH = Mathf.CeilToInt((WorldMax.y - WorldMin.y) / CellSize);

            if (newW <= 0 || newH <= 0)
            {
                Debug.LogWarning("[FoW] Invalid grid size.");
                return;
            }

            _w = newW;
            _h = newH;

            int slice = _w * _h;
            _visible = new byte[MaxFactions * slice];
            _revealed = clearRevealed ? new byte[MaxFactions * slice] : new byte[MaxFactions * slice];

            if (_tex == null)
                _tex = new Texture2D(_w, _h, TextureFormat.Alpha8, false, true);
            else
                _tex.Reinitialize(_w, _h);

            _tex.wrapMode = TextureWrapMode.Clamp;

            EnsureMaterialBound();
            PushHumanTexture();
        }

        public void ApplyBounds(Vector2 newMin, Vector2 newMax, float? newCellSize = null, bool clearRevealed = false, int surfaceGrid = 128)
        {
            WorldMin = newMin;
            WorldMax = newMax;
            if (newCellSize.HasValue) CellSize = Mathf.Max(0.05f, newCellSize.Value);

            EnsureMaterialBound();
            ForceRebuildGrid(clearRevealed);

            if (FogRenderer != null)
            {
                var old = FogRenderer.gameObject;
                if (old != null) Destroy(old);
            }

            var mat = FogMaterial != null ? FogMaterial : new Material(Shader.Find("Unlit/FogOfWar"));
            GameObject surface = FogOfWarConformingMesh.Create(WorldMin, WorldMax, surfaceGrid, mat);
            surface.name = "FogSurface";
            surface.transform.SetParent(transform, false);
            FogRenderer = surface.GetComponent<MeshRenderer>();

            EnsureMaterialBound();
            PushHumanTexture();
        }

        /// <summary>
        /// Static helper to create and setup FogOfWar in a scene.
        /// </summary>
        public static void SetupFogOfWar()
        {
            if (FindObjectOfType<FogOfWarManager>() != null) return;

            int half = Mathf.Max(16, GameSettings.MapHalfSize);

            var root = new GameObject("FogOfWar");
            var mgr = root.AddComponent<FogOfWarManager>();
            mgr.WorldMin = new Vector2(-half, -half);
            mgr.WorldMax = new Vector2(half, half);
            mgr.CellSize = 1f;
            mgr.HumanFaction = GameSettings.LocalPlayerFaction;

            var mat = new Material(Shader.Find("Unlit/FogOfWar"));
            mat.renderQueue = 3000;
            mat.SetVector("_WorldMin", new Vector4(mgr.WorldMin.x, 0, mgr.WorldMin.y, 0));
            mat.SetVector("_WorldMax", new Vector4(mgr.WorldMax.x, 0, mgr.WorldMax.y, 0));

            GameObject fogSurface = FogOfWarConformingMesh.Create(mgr.WorldMin, mgr.WorldMax, 128, mat);
            fogSurface.name = "FogSurface";
            fogSurface.transform.SetParent(root.transform, false);

            var mr = fogSurface.GetComponent<MeshRenderer>();
            mgr.FogMaterial = mr.sharedMaterial;
            mgr.FogRenderer = mr;

            if (UnityEngine.Terrain.activeTerrain == null)
            {
                root.AddComponent<OneShotFoWRebuilder>().Init(mgr, 128);
            }
        }

        /// <summary>
        /// Helper component that rebuilds FoW mesh once terrain is available.
        /// </summary>
        private class OneShotFoWRebuilder : MonoBehaviour
        {
            FogOfWarManager _mgr;
            int _grid;

            public void Init(FogOfWarManager mgr, int grid) { _mgr = mgr; _grid = grid; }

            void LateUpdate()
            {
                var t = UnityEngine.Terrain.activeTerrain;
                if (t == null || t.terrainData == null) return;

                for (int i = transform.childCount - 1; i >= 0; i--)
                    Destroy(transform.GetChild(i).gameObject);

                var mat = _mgr.FogMaterial;
                GameObject fogSurface = FogOfWarConformingMesh.Create(_mgr.WorldMin, _mgr.WorldMax, _grid, mat);
                fogSurface.name = "FogSurface";
                fogSurface.transform.SetParent(transform, false);

                var mr = fogSurface.GetComponent<MeshRenderer>();
                _mgr.FogRenderer = mr;

                Destroy(this);
            }
        }
    }

    /// <summary>
    /// Creates a terrain-conforming mesh for the fog overlay to avoid z-fighting.
    /// </summary>
    public static class FogOfWarConformingMesh
    {
        public static GameObject Create(Vector2 worldMin, Vector2 worldMax, int grid = 128, Material mat = null)
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null) return CreateFlatQuad(worldMin, worldMax, mat);

            var td = terrain.terrainData;
            var tpos = terrain.transform.position;
            var tsize = td.size;

            int vertsX = Mathf.Max(2, grid + 1);
            int vertsZ = Mathf.Max(2, grid + 1);

            var verts = new Vector3[vertsX * vertsZ];
            var uvs = new Vector2[verts.Length];
            var tris = new int[(vertsX - 1) * (vertsZ - 1) * 6];

            for (int z = 0; z < vertsZ; z++)
            {
                float vz = Mathf.Lerp(worldMin.y, worldMax.y, z / (float)(vertsZ - 1));
                float vT = Mathf.InverseLerp(tpos.z, tpos.z + tsize.z, vz);
                for (int x = 0; x < vertsX; x++)
                {
                    float vx = Mathf.Lerp(worldMin.x, worldMax.x, x / (float)(vertsX - 1));
                    float uT = Mathf.InverseLerp(tpos.x, tpos.x + tsize.x, vx);

                    float y = td.GetInterpolatedHeight(uT, vT) + 0.03f;
                    int i = z * vertsX + x;
                    verts[i] = new Vector3(vx, y, vz);

                    float u = Mathf.InverseLerp(worldMin.x, worldMax.x, vx);
                    float v = Mathf.InverseLerp(worldMin.y, worldMax.y, vz);
                    uvs[i] = new Vector2(u, v);
                }
            }

            int ti = 0;
            for (int z = 0; z < vertsZ - 1; z++)
            {
                for (int x = 0; x < vertsX - 1; x++)
                {
                    int i0 = z * vertsX + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsX;
                    int i3 = i2 + 1;

                    tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                    tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
                }
            }

            var mesh = new Mesh { name = "FogConformMesh" };
            mesh.indexFormat = (verts.Length > 65000)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("FogConforming");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;

            if (mat == null) mat = new Material(Shader.Find("Unlit/FogOfWar"));
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
            return go;
        }

        static GameObject CreateFlatQuad(Vector2 min, Vector2 max, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "FogOfWar";
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            go.transform.position = new Vector3(0, 0.2f, 0);
            go.transform.localScale = new Vector3(max.x - min.x, max.y - min.y, 1);
            var mr = go.GetComponent<MeshRenderer>();
            if (mat == null) mat = new Material(Shader.Find("Unlit/FogOfWar"));
            mr.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col) UnityEngine.Object.Destroy(col);
            return go;
        }
    }
}