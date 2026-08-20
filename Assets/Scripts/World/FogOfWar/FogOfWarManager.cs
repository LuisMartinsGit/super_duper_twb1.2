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
        /// <summary>
        /// Never-seen ground is FULLY opaque. At the old 0.98 a two-percent
        /// window let the terrain, and in particular the lit edge of the map,
        /// show faintly through unexplored fog — so the shape and extent of the
        /// map were readable before anyone had scouted it. The shader's _Tint
        /// is already black, so 1.0 is pure black.
        /// </summary>
        [Range(0, 1)] public float HiddenAlpha = 1f;      // never seen
        [Tooltip("Seconds between overlay texture rebuilds. The visibility GRID still " +
                 "updates every frame (gameplay queries stay exact); this only paces the " +
                 "per-cell repaint + GPU upload of the human player's fog texture.")]
        public float TextureUpdateInterval = 0.1f;

        // Internal
        float _nextTextureTime;
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

        /// <summary>Update the human overlay texture after stamping (throttled).</summary>
        public void EndFrameAndBuild()
        {
            // Shared line of sight. Merged BEFORE anything reads the grid, and
            // merged INTO each member's own slice, so every existing consumer
            // — IsVisible, IsRevealed, the overlay texture, the minimap, the
            // AI's intel scans — becomes team-aware without touching any of
            // them. docs/Design/Teams.md
            //
            // Deliberately NOT inside the texture throttle below: gameplay
            // queries run every frame and must see the merged result, while
            // the texture repaint is paced.
            MergeTeamVision();

            // Observer perspective: follow the viewed faction (the selected
            // asset's owner); no view faction = full reveal, overlay off.
            // Normal play resolves to LocalPlayerFaction and changes nothing.
            var view = GameSettings.ViewFaction;
            if (FogRenderer != null && FogRenderer.enabled != view.HasValue)
                FogRenderer.enabled = view.HasValue;
            if (view.HasValue && HumanFaction != view.Value)
            {
                HumanFaction = view.Value;
                _nextTextureTime = 0f; // repaint NOW — stale fog is the old player's vision
            }

            if (Time.unscaledTime < _nextTextureTime) return;
            _nextTextureTime = Time.unscaledTime + Mathf.Max(0f, TextureUpdateInterval);
            EnsureMaterialBound();
            PushHumanTexture();
        }

        // Scratch buffer for the team OR, kept alive between frames so the
        // merge does not allocate per frame.
        byte[] _teamVisible;
        byte[] _teamRevealed;

        /// <summary>
        /// OR every team member's visibility into a shared result and write it
        /// back to each member. Costs nothing in a free-for-all: if no team has
        /// two or more members the whole pass is skipped, which is the default
        /// lobby state.
        /// </summary>
        void MergeTeamVision()
        {
            if (_visible == null || _revealed == null) return;

            int cells = _w * _h;
            if (cells <= 0) return;

            for (byte team = 1; team <= Alliances.MaxTeams; team++)
            {
                // Collect this team's slice offsets.
                int memberCount = 0;
                int firstOfs = 0;
                Span<int> offsets = stackalloc int[MaxFactions];
                for (int f = 0; f < MaxFactions; f++)
                {
                    if (Alliances.TeamOf((Faction)f) != team) continue;
                    if (memberCount == 0) firstOfs = f * cells;
                    offsets[memberCount++] = f * cells;
                }
                if (memberCount < 2) continue;   // solo team == no sharing to do

                if (_teamVisible == null || _teamVisible.Length < cells)
                {
                    _teamVisible = new byte[cells];
                    _teamRevealed = new byte[cells];
                }

                // Seed from the first member, then OR the rest in.
                Array.Copy(_visible, firstOfs, _teamVisible, 0, cells);
                Array.Copy(_revealed, firstOfs, _teamRevealed, 0, cells);

                for (int m = 1; m < memberCount; m++)
                {
                    int ofs = offsets[m];
                    for (int i = 0; i < cells; i++)
                    {
                        if (_visible[ofs + i] != 0) _teamVisible[i] = 1;
                        if (_revealed[ofs + i] != 0) _teamRevealed[i] = 1;
                    }
                }

                // Write the union back to every member.
                for (int m = 0; m < memberCount; m++)
                {
                    Array.Copy(_teamVisible, 0, _visible, offsets[m], cells);
                    Array.Copy(_teamRevealed, 0, _revealed, offsets[m], cells);
                }
            }
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
                return;
            }

            int oldW = _w;
            int oldH = _h;
            byte[] oldRevealed = _revealed;

            _w = newW;
            _h = newH;

            int slice = _w * _h;
            _visible = new byte[MaxFactions * slice];

            if (clearRevealed || oldRevealed == null || oldW <= 0 || oldH <= 0)
            {
                _revealed = new byte[MaxFactions * slice];
            }
            else
            {
                // Fix #242: previously both branches of the ternary allocated a
                // fresh zero array, so clearRevealed=false still wiped
                // exploration progress. Now we copy the old revealed data into
                // the new grid, clipping to the overlap rectangle when the
                // dimensions change.
                _revealed = new byte[MaxFactions * slice];
                int copyW = Mathf.Min(oldW, _w);
                int copyH = Mathf.Min(oldH, _h);
                for (int f = 0; f < MaxFactions; f++)
                {
                    int oldBase = f * oldW * oldH;
                    int newBase = f * _w * _h;
                    for (int y = 0; y < copyH; y++)
                    {
                        System.Array.Copy(
                            oldRevealed, oldBase + y * oldW,
                            _revealed,   newBase + y * _w,
                            copyW);
                    }
                }
            }

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

            // Keep the enabled flag across surface rebuilds — observers run
            // with the overlay renderer hidden.
            bool rendererEnabled = FogRenderer == null || FogRenderer.enabled;
            if (FogRenderer != null)
            {
                var old = FogRenderer.gameObject;
                if (old != null) Destroy(old);
            }

            // Same stripped-shader trap as SetupFogOfWar: with no material and
            // no shader there is nothing to draw, so skip building the surface
            // rather than throwing on new Material(null). The fog GRID still
            // updates, so vision queries stay correct — only the overlay is gone.
            var mat = FogMaterial;
            if (mat == null)
            {
                var fogShader = ResolveFogShader();
                if (fogShader == null)
                {
                    // Nothing to draw. The fog GRID still updates, so vision
                    // queries stay correct — only the overlay is skipped.
                    FogRenderer = null;
                    PushHumanTexture();
                    return;
                }
                mat = new Material(fogShader);
            }

            GameObject surface = FogOfWarConformingMesh.Create(WorldMin, WorldMax, surfaceGrid, mat);
            surface.name = "FogSurface";
            surface.transform.SetParent(transform, false);
            FogRenderer = surface.GetComponent<MeshRenderer>();
            FogRenderer.enabled = rendererEnabled;

            EnsureMaterialBound();
            PushHumanTexture();
        }

        /// <summary>Shader file under a Resources/ folder, loaded by name.</summary>
        private const string FogShaderResource = "FogOfWarShader";

        /// <summary>
        /// Resolves the fog shader for BOTH the editor and a player build.
        ///
        /// Nothing in the project references Unlit/FogOfWar from a material or
        /// a scene, so it used to reach the player only if someone remembered
        /// to list it in Graphics > Always Included Shaders. Nobody did, so
        /// Shader.Find returned null in the build, `new Material(null)` threw
        /// out of GameBootstrap.InitializeWorld, the bootstrap coroutine died
        /// silently and the loading screen hung on "Building world..." forever.
        ///
        /// It now lives in a Resources/ folder — those are included in every
        /// build unconditionally — and is loaded explicitly rather than via
        /// Shader.Find, which is only reliable for shaders already loaded or
        /// pulled in by some other reference. Shader.Find stays as a fallback
        /// so a move or rename degrades instead of breaking.
        /// </summary>
        private static Shader ResolveFogShader()
        {
            var shader = Resources.Load<Shader>(FogShaderResource);
            if (shader != null) return shader;
            return Shader.Find("Unlit/FogOfWar");
        }

        /// <summary>
        /// Static helper to create and setup FogOfWar in a scene.
        /// </summary>
        public static void SetupFogOfWar()
        {
            if (FindFirstObjectByType<FogOfWarManager>() != null) return;

            var root = new GameObject("FogOfWar");
            var mgr = root.AddComponent<FogOfWarManager>();
            // Note: Awake() ran on AddComponent above and allocated the grid/texture
            // using the default WorldMin/Max (±12.5, CellSize 0.1). Setting the
            // fields directly afterwards used to leave _w/_h/_visible/_revealed
            // sized for that 25x25 default — every unit outside that box stamped
            // onto the clamped edge instead of revealing fog. ApplyBounds()
            // reallocates everything to the real map dimensions.
            mgr.HumanFaction = GameSettings.LocalPlayerFaction;

            var fogShader = ResolveFogShader();
            if (fogShader == null)
            {
                Debug.LogError(
                    "[FogOfWar] Shader \"Unlit/FogOfWar\" could not be resolved — "
                    + "continuing WITHOUT fog of war. Expected it at "
                    + "Assets/Scripts/World/FogOfWar/Resources/" + FogShaderResource + ".shader.");
            }
            else
            {
                var mat = new Material(fogShader);
                mat.renderQueue = 3000;
                mgr.FogMaterial = mat;
            }

            // Cover the ACTUAL playable rect. Baked terrains sit corner-at-
            // origin (0..size), so the old origin-centred ±MapHalfSize box
            // (with MapHalfSize snapped to the FARTHEST terrain coordinate)
            // built a surface and grid 2x the map per side — 4x the area,
            // burning 4x the per-frame fog work. Fall back to the centred
            // box only when no terrain exists yet (procedural maps are
            // origin-centred by construction).
            Vector2 min, max;
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
            {
                var tpos = terrain.transform.position;
                var tsize = terrain.terrainData.size;
                min = new Vector2(tpos.x, tpos.z);
                max = new Vector2(tpos.x + tsize.x, tpos.z + tsize.z);
            }
            else
            {
                int half = Mathf.Max(16, GameSettings.MapHalfSize);
                min = new Vector2(-half, -half);
                max = new Vector2(half, half);
            }

            mgr.ApplyBounds(
                min,
                max,
                newCellSize: 1f,
                clearRevealed: true,
                surfaceGrid: 128);

            // ApplyBounds creates the fog surface itself; parent it under our root
            // so the hierarchy stays tidy.
            if (mgr.FogRenderer != null)
                mgr.FogRenderer.transform.SetParent(root.transform, true);

            var active = UnityEngine.Terrain.activeTerrain;
            if (active == null || active.terrainData == null)
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

                // Re-derive the bounds from the terrain that just appeared —
                // grid, surface and revealed data all resize to the actual
                // playable rect (ApplyBounds keeps exploration progress).
                var tpos = t.transform.position;
                var tsize = t.terrainData.size;
                _mgr.ApplyBounds(
                    new Vector2(tpos.x, tpos.z),
                    new Vector2(tpos.x + tsize.x, tpos.z + tsize.z),
                    clearRevealed: false,
                    surfaceGrid: _grid);
                if (_mgr.FogRenderer != null)
                    _mgr.FogRenderer.transform.SetParent(transform, true);

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
            // MapMagic tiles load with a Terrain whose TerrainData is null
            // until the graph regenerates — treat that as no terrain at all.
            if (terrain == null || terrain.terrainData == null)
                return CreateFlatQuad(worldMin, worldMax, mat);

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