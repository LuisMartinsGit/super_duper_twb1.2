// ShaderErrorReport.cs
// Waning Border > Debug > Report Terrain Shader Errors — logs every compile
// message of the terrain shader and the two ground-look shaders. Unity's
// import log only says "All subshaders removed" for a shader that failed;
// this prints WHY (file, line, message).

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class ShaderErrorReport
    {
        static readonly string[] Shaders =
            { "TWB/Terrain/Lit", "TWB/Terrain/Blood Look", "TWB/Terrain/Curse Look", "TWB/Terrain/Curse Shelf" };

        [MenuItem("Waning Border/Debug/Report Terrain Shader Errors")]
        public static void Report()
        {
            foreach (var name in Shaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) { Debug.LogError($"[ShaderErrors] {name}: not found"); continue; }
                var msgs = ShaderUtil.GetShaderMessages(shader);
                Debug.Log($"[ShaderErrors] {name}: {msgs.Length} message(s), supported={shader.isSupported}");
                foreach (var m in msgs)
                    Debug.Log($"[ShaderErrors] {name} {m.severity} {m.file}:{m.line} {m.message} ({m.platform})");
            }
        }

        /// <summary>Play mode: move the camera to the world "x z" written in
        /// Temp/agent-bridge/focus.txt (for repeatable screenshots).</summary>
        [MenuItem("Waning Border/Debug/Focus Camera On Point (Temp focus.txt)")]
        public static void FocusOnPoint()
        {
            var path = System.IO.Path.Combine("Temp", "agent-bridge", "focus.txt");
            if (!EditorApplication.isPlaying || !System.IO.File.Exists(path)) { Debug.LogWarning("[FocusOnPoint] needs play mode and " + path); return; }
            var parts = System.IO.File.ReadAllText(path).Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            float x = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            float z = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            float y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(x, z);
            TheWaningBorder.CameraRig.CameraController.FocusOn(new Vector3(x, y, z), instant: true);
            // Optional third value: the zoom distance (clamped by the rig to its config range).
            if (parts.Length > 2)
            {
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var t = typeof(TheWaningBorder.CameraRig.CameraController);
                var rig = t.GetField("_instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
                float zoom = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                if (rig != null) { t.GetField("_targetZoom", F)?.SetValue(rig, zoom); t.GetField("_currentZoom", F)?.SetValue(rig, zoom); }
                // Optional fourth value: the heading in degrees (0 = looking north).
                if (rig != null && parts.Length > 3)
                {
                    float yaw = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                    t.GetField("_targetRotation", F)?.SetValue(rig, yaw);
                    t.GetField("_currentRotation", F)?.SetValue(rig, yaw);
                    t.GetField("_rotationVelocity", F)?.SetValue(rig, 0f);
                }
            }
            Debug.Log($"[FocusOnPoint] camera on ({x:0},{z:0}), cursed={TheWaningBorder.Systems.Border.CurseGroundSystem.IsCursed(x, z)}, " +
                      $"fieldReady={TheWaningBorder.Systems.Border.CurseGroundSystem.Ready}");
        }

        /// <summary>Set properties on a material asset from
        /// Temp/agent-bridge/matprops.txt: first line the asset path, then one
        /// "name value" per line — a float, "r g b a" for a colour, or
        /// "+KEYWORD" / "-KEYWORD". Works in play mode (live tuning).</summary>
        [MenuItem("Waning Border/Debug/Set Material Props (Temp matprops.txt)")]
        public static void SetMaterialProps()
        {
            var path = System.IO.Path.Combine("Temp", "agent-bridge", "matprops.txt");
            var lines = System.IO.File.ReadAllLines(path);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(lines[0].Trim());
            if (mat == null) { Debug.LogError("[MatProps] no material at " + lines[0]); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0][0] == '+') { mat.EnableKeyword(p[0].Substring(1)); mat.SetFloat(p[0].Substring(1), 1f); continue; }
                if (p[0][0] == '-') { mat.DisableKeyword(p[0].Substring(1)); mat.SetFloat(p[0].Substring(1), 0f); continue; }
                if (p.Length >= 4)
                    mat.SetColor(p[0], new Color(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv),
                                                 p.Length > 4 ? float.Parse(p[4], inv) : 1f));
                else mat.SetFloat(p[0], float.Parse(p[1], inv));
            }
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[MatProps] applied " + (lines.Length - 1) + " values to " + lines[0]);
        }

        /// <summary>Play mode, Fiendstone Pass: walk the nav grid north-south
        /// across the river at each ford and at two deep reaches, and log how
        /// many 1 m steps are impassable. A ford must read 0; deep water must
        /// not (the river line is layout.py's river_y = 34 sin(x / 160)).</summary>
        [MenuItem("Waning Border/Debug/Report Ford Passability (Fiendstone Pass)")]
        public static void ReportFordPassability()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Fords] needs a running match"); return; }
            var sb = new System.Text.StringBuilder("[Fords] impassable 1 m steps across the river:\n");
            foreach (float x in new[] { -380f, 0f, 380f, -200f, 200f })
            {
                float zc = 34f * Mathf.Sin(x / 160f);
                int blocked = 0, steps = 0, under = 0;
                float water = TheWaningBorder.World.Terrain.WaterPlane.Instance != null
                    ? TheWaningBorder.World.Terrain.WaterPlane.Instance.waterLevel : float.MinValue;
                for (float z = zc - 30f; z <= zc + 30f; z += 1f, steps++)
                {
                    var p = new Unity.Mathematics.float3(x, 0f, z);
                    var cell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(p);
                    if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell)) blocked++;
                    if (TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(x, z) < water) under++;
                }
                sb.AppendLine($"  x={x,5:0}: {blocked}/{steps} blocked, {under} under water");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>Play mode: log each territory's resource type and the nodes
        /// actually laid in it (supply / iron / veilstone) — finds the
        /// territories too small for their type's node set.</summary>
        [MenuItem("Waning Border/Debug/Report Nodes Per Territory")]
        public static void ReportNodesPerTerritory()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Nodes] needs a running match"); return; }
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            int n = TheWaningBorder.World.Regions.RegionMap.Count;
            var counts = new int[n, 3];
            void Count<T>(int k) where T : unmanaged, Unity.Entities.IComponentData
            {
                var q = em.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<T>(),
                                             Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
                foreach (var x in xfs)
                {
                    int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(x.Position.x, x.Position.z);
                    if (r >= 0 && r < n) counts[r, k]++;
                }
                q.Dispose();   // one-shot editor query, created and disposed here
            }
            Count<SupplyNodeTag>(0); Count<IronMineTag>(1); Count<VeilstoneOutcroppingTag>(2);
            var sb = new System.Text.StringBuilder("[Nodes] per territory (supply/iron/veilstone):\n");
            for (int r = 0; r < n; r++)
                sb.AppendLine($"  {r:00} {TheWaningBorder.World.Regions.RegionMap.NameOf(r),-22} " +
                              $"{TheWaningBorder.World.Regions.TerritoryResources.TypeOf(r),-16} " +
                              $"{counts[r, 0]}/{counts[r, 1]}/{counts[r, 2]}");
            Debug.Log(sb.ToString());
        }

        /// <summary>Play mode: write Temp/agent-bridge/curse_field.png — the
        /// cursed-ground FIELD (left) beside the terrain mask's curse coverage
        /// (right), both over the same world square, north up.</summary>
        [MenuItem("Waning Border/Debug/Dump Curse Field vs Mask")]
        public static void DumpCurseField()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[CurseDump] needs play mode"); return; }
            Vector2 min = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
            Vector2 size = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
            const int R = 256;
            var tex = new Texture2D(R * 2, R, TextureFormat.RGB24, false);
            int f = 0, m = 0;
            for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float wx = min.x + (x + 0.5f) / R * size.x, wz = min.y + (y + 0.5f) / R * size.y;
                bool c = TheWaningBorder.Systems.Border.CurseGroundSystem.IsCursed(wx, wz);
                float k = TheWaningBorder.Influence.InfluenceMaskTexture.CurseCoverage(wx, wz);
                if (c) f++; if (k > 0.1f) m++;
                tex.SetPixel(x, y, c ? new Color(0.7f, 0.3f, 1f) : new Color(0.15f, 0.15f, 0.15f));
                tex.SetPixel(R + x, y, new Color(k * 0.7f, k * 0.3f, k));
            }
            var path = System.IO.Path.Combine("Temp", "agent-bridge", "curse_field.png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Debug.Log($"[CurseDump] field px {f}, mask px {m}, world min {min} size {size}, " +
                      $"field origin {TheWaningBorder.Systems.Border.CurseGroundSystem.Origin}, version " +
                      $"{TheWaningBorder.Systems.Border.CurseGroundSystem.Version}, camera {cam} -> {path}");
        }

        /// <summary>Play mode: move the camera onto the most cursed ground on
        /// the map, to look at the curse overlay and its crystal shelf.</summary>
        /// <summary>Play mode: how much curse sits on UNWALKABLE ground — the
        /// simulation field's cursed cells and the drawn coverage (> 0.1) on
        /// terrain-blocked passability cells. Both must be 0 (2026-10-17).</summary>
        [MenuItem("Waning Border/Debug/Report Curse On Unwalkable Ground")]
        public static void ReportCurseOnUnwalkable()
        {
            var grid = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (!EditorApplication.isPlaying || grid == null || !grid.IsMaskReady
                || !TheWaningBorder.Systems.Border.CurseGroundSystem.Ready)
            { Debug.LogWarning("[CurseWalk] needs a running match with the curse field"); return; }
            var o = TheWaningBorder.Systems.Border.CurseGroundSystem.Origin;
            float cs = TheWaningBorder.Systems.Border.CurseGroundSystem.CellSize;
            int w = TheWaningBorder.Systems.Border.CurseGroundSystem.Width, h = TheWaningBorder.Systems.Border.CurseGroundSystem.Height;
            int cursed = 0, cursedBlocked = 0, drawn = 0, drawnBlocked = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float wx = o.x + (x + 0.5f) * cs, wz = o.y + (y + 0.5f) * cs;
                bool blocked = grid.GetCell(grid.WorldToCell(new Unity.Mathematics.float3(wx, 0f, wz)))
                               == TheWaningBorder.World.Terrain.PassabilityGrid.TerrainBlocked;
                if (TheWaningBorder.Systems.Border.CurseGroundSystem.CursedCell(x, y)) { cursed++; if (blocked) cursedBlocked++; }
                if (TheWaningBorder.Influence.InfluenceMaskTexture.Live
                    && TheWaningBorder.Influence.InfluenceMaskTexture.CurseCoverage(wx, wz) > 0.1f) { drawn++; if (blocked) drawnBlocked++; }
            }
            Debug.Log($"[CurseWalk] field: {cursed} cursed cells, {cursedBlocked} on unwalkable ground; " +
                      $"look: {drawn} cells drawn cursed, {drawnBlocked} on unwalkable ground.");
        }

        /// <summary>Play mode, a map running: write the minimap's terrain image
        /// as "&lt;Map&gt; Minimap.png" beside the map's lobby image — the picture
        /// tools/twb-match-data.py puts under the Muster Rolls replay.</summary>
        [MenuItem("Waning Border/Maps/Bake Minimap Image (play mode)")]
        public static void BakeMinimapImage()
        {
            var mm = TheWaningBorder.UI.Ingame.MinimapPanelBinder.Active;
            byte[] png = mm != null ? mm.BakeTerrainPng() : null;
            if (!EditorApplication.isPlaying || png == null)
            { Debug.LogWarning("[MinimapBake] needs a running match with its minimap built"); return; }
            string folder = null;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                string p = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path;
                const string root = "Assets/GameData/Scenes/Maps/";
                if (p.StartsWith(root)) { folder = System.IO.Path.GetDirectoryName(p).Replace('\\', '/'); break; }
            }
            if (folder == null) { Debug.LogWarning("[MinimapBake] no map scene loaded"); return; }
            string name = System.IO.Path.GetFileName(folder);
            string path = $"{folder}/{name} Minimap.png";
            System.IO.File.WriteAllBytes(path, png);
            Debug.Log($"[MinimapBake] wrote {path} ({png.Length / 1024} KB)");
        }

        [MenuItem("Waning Border/Debug/Focus Camera On Curse")]
        public static void FocusOnCurse()
        {
            if (!EditorApplication.isPlaying || !TheWaningBorder.Influence.InfluenceMaskTexture.Live)
            {
                Debug.LogWarning("[FocusOnCurse] needs a running match");
                return;
            }
            Vector2 min = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
            Vector2 size = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
            // The EDGE of the curse is the interesting part: aim for coverage
            // near the front rather than the deepest point.
            float bestScore = -1f; Vector2 best = min + size * 0.5f;
            for (int j = 0; j < 96; j++)
            for (int i = 0; i < 96; i++)
            {
                float x = min.x + size.x * (i + 0.5f) / 96f, z = min.y + size.y * (j + 0.5f) / 96f;
                float c = TheWaningBorder.Influence.InfluenceMaskTexture.CurseCoverage(x, z);
                float score = c > 0.15f ? 1f - Mathf.Abs(c - 0.6f) : -1f;
                if (score > bestScore) { bestScore = score; best = new Vector2(x, z); }
            }
            float y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(best.x, best.y);
            TheWaningBorder.CameraRig.CameraController.FocusOn(new Vector3(best.x, y, best.y), instant: true);
            Debug.Log($"[FocusOnCurse] camera on ({best.x:0},{best.y:0}), score {bestScore:0.00}");
        }
    }
}
