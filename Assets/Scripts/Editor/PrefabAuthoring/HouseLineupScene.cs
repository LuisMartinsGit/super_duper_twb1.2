// HouseLineupScene — the Hut line side by side (2026-10-27): the Age 0 Hut
// and the Alanthor House at levels 1-3, each on its own 2 x 2-cell plot
// (4 m, Build_Grid.md), in one scene with a 2 m grid underfoot so the plot
// edges can be read, and a capture of every model from four orientations
// (and the whole row) so every face is checked for walls.
//
//   Waning Border > Art > Build House Lineup Scene
//
// The models are the four FBX written by tools/blender/alanthor/
// build_house_family.py, built 100 % from Synty Fantasy Kingdom meshes. Their
// six materials (Syn_Atlas, Age0_Mud under the Hut; Al_Stucco, Al_Stone,
// Al_Tile, Al_Red under the House) are made here — BuildingLit over one Synty
// texture each, tinted — and every FBX is remapped onto them by name. The scene
// is built ADDITIVELY beside whatever the developer has open (never closing
// it), saved to Assets/GameData/Scenes/Debug/HouseLineup.unity and closed
// again; captures go to Temp/house-lineup/.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.EditorTools
{
    public static class HouseLineupScene
    {
        const string ScenePath = "Assets/GameData/Scenes/Debug/HouseLineup.unity";
        const string HutArt = "Assets/GameData/TechTree/Age0/Buildings/Hut/Art/";
        const string HouseArt = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/Art/";
        const string SyntyTex = "Assets/Synty/PolygonFantasyKingdom/Textures/";
        const string BuildingLitShader = "TheWaningBorder/BuildingLit";
        // name, folder (Hut or House Art/Materials), Synty texture, tint
        static readonly (string name, bool hut, string tex, Color tint)[] Mats =
        {
            ("Syn_Atlas", true,  "Alts/PolygonFantasyKingdom_01_A.png", Color.white),
            ("Age0_Mud",  true,  "Ground/Mud_01.png",                   Color.white),
            ("Al_Stucco", false, "Castle/Wall_Stucco_01.png",           new Color(1.28f, 1.25f, 1.18f)),
            ("Al_Stone",  false, "Castle/Wall_Brick_01.png",            Color.white),
            ("Al_Tile",   false, "Castle/Roof_Tile_04.png",             Color.white),
            ("Al_Red",    false, "Castle/Wall_Stucco_01.png",           new Color(0.85f, 0.13f, 0.10f)),
        };
        static readonly (string label, string fbx)[] Models =
        {
            ("Hut", HutArt + "Hut_Age_0.prefab"),
            ("HouseLv1", HouseArt + "alanthor_House_lv1.prefab"),
            ("HouseLv2", HouseArt + "alanthor_House_lv2.prefab"),
            ("HouseLv3", HouseArt + "alanthor_House_lv3.prefab"),
        };
        const float Plot = 4f;        // 2 x 2 cells of 2 m
        const float Spacing = 6f;     // plot centres 6 m apart: a 2 m lane between plots

        [MenuItem("Waning Border/Art/Build House Lineup Scene")]
        public static void Build()
        {
            var report = new StringBuilder();
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "house-lineup");
            Directory.CreateDirectory(outDir);
            AssetDatabase.Refresh();

            EnsureHutMaterials(report);
            foreach (var (label, fbx) in Models)
                if (fbx.EndsWith(".fbx")) Remap(fbx, label == "Hut", report);   // the prefabs carry their materials

            var keepActive = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var stage = new Vector3(0f, -4000f, 0f);          // out of the developer's scene while capturing
                var root = new GameObject("HouseLineup");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = stage;

                // Ground: 40 x 20 m with 2 m cells; shifted 1 m in x so the
                // plot edges (odd metres) fall on grid lines.
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground"; ground.transform.SetParent(root.transform, false);
                ground.transform.localPosition = new Vector3(1f, 0f, 0f);
                ground.transform.localScale = new Vector3(4f, 1f, 2f);
                Object.DestroyImmediate(ground.GetComponent<Collider>());
                ground.GetComponent<Renderer>().sharedMaterial = GridMaterial(20, 10);

                var sunGo = new GameObject("Sun"); sunGo.transform.SetParent(root.transform, false);
                var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.6f;
                sun.color = new Color(1f, 0.93f, 0.84f); sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
                var fillGo = new GameObject("Fill"); fillGo.transform.SetParent(root.transform, false);
                var fill = fillGo.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.4f;
                fill.color = new Color(0.72f, 0.78f, 1f); fillGo.transform.rotation = Quaternion.Euler(30f, 142f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.40f, 0.45f, 0.55f);
                RenderSettings.fog = false;

                var placed = new List<(string label, GameObject go)>();
                for (int i = 0; i < Models.Length; i++)
                {
                    var (label, fbx) = Models[i];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                    if (prefab == null) { report.AppendLine("MISSING " + fbx); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    go.name = label; go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(-9f + i * Spacing, 0f, 0f);
                    var b = Bounds(go);
                    report.AppendLine($"{label}: size {b.size.x:F2} x {b.size.z:F2} x {b.size.y:F2} m, " +
                                      $"fit {Mathf.Min(Plot / b.size.x, Plot / b.size.z):F3}");
                    placed.Add((label, go));
                }

                // Captures: the row from four sides, then every model from four sides and above.
                var camGo = new GameObject("CaptureCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 40f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.11f, 0.13f, 0.19f, 1f);
                try
                {
                    var dirs = new (string tag, Vector3 from)[]
                    {
                        ("S", new Vector3(0f, 0f, -1f)), ("E", new Vector3(1f, 0f, 0f)),
                        ("N", new Vector3(0f, 0f, 1f)), ("W", new Vector3(-1f, 0f, 0f)),
                    };
                    var rowFocus = stage + new Vector3(0f, 1.6f, 0f);
                    foreach (var (tag, from) in dirs)
                    {
                        float dist = (tag == "S" || tag == "N") ? 30f : 36f;
                        Shoot(cam, rowFocus, from, dist, 42f, Path.Combine(outDir, "Lineup_" + tag + ".png"), 2400, 1100);
                    }
                    foreach (var (label, go) in placed)
                    {
                        var b = Bounds(go);
                        var focus = new Vector3(go.transform.position.x, b.min.y + b.size.y * 0.42f, go.transform.position.z);
                        float dist = Mathf.Max(8.5f, b.size.y * 1.9f);
                        foreach (var (tag, from) in dirs)
                            Shoot(cam, focus, from, dist, 38f, Path.Combine(outDir, label + "_" + tag + ".png"), 1100, 900);
                        Shoot(cam, new Vector3(focus.x, b.min.y, focus.z), new Vector3(0f, 0f, -0.001f), 20f, 89.9f,
                              Path.Combine(outDir, label + "_Top.png"), 900, 900);
                    }
                }
                finally { Object.DestroyImmediate(camGo); }

                root.transform.position = Vector3.zero;
                EditorSceneManager.SaveScene(scene, ScenePath);
                report.AppendLine("scene " + ScenePath);
            }
            finally
            {
                if (keepActive.IsValid()) SceneManager.SetActiveScene(keepActive);
                EditorSceneManager.CloseScene(scene, true);
            }
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            Debug.Log("[HouseLineup]\n" + report);
        }

        // ── materials and the FBX remap ────────────────────────────────
        static string MatPath((string name, bool hut, string tex, Color tint) m)
            => (m.hut ? HutArt : HouseArt) + "Materials/" + m.name + ".mat";

        internal static void EnsureHutMaterials(StringBuilder report)
        {
            var shader = Shader.Find(BuildingLitShader);
            if (shader == null) { report.AppendLine("shader missing: " + BuildingLitShader); return; }
            foreach (var art in new[] { HutArt, HouseArt })
                if (!AssetDatabase.IsValidFolder(art + "Materials")) AssetDatabase.CreateFolder(art.TrimEnd('/'), "Materials");
            foreach (var md in Mats)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(SyntyTex + md.tex);
                if (t == null) { report.AppendLine("missing Synty texture " + md.tex); continue; }
                string path = MatPath(md);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader) { name = md.name };
                    AssetDatabase.CreateAsset(m, path);
                    report.AppendLine("created " + path);
                }
                m.shader = shader;
                m.SetTexture("_BaseMap", t); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
                m.SetColor("_BaseColor", md.tint); if (m.HasProperty("_Color")) m.SetColor("_Color", md.tint);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", md.name == "Al_Tile" ? 0.2f : 0.08f);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        static void Remap(string fbx, bool hut, StringBuilder report)
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { report.AppendLine("no importer for " + fbx); return; }
            bool changed = false;
            var existing = imp.GetExternalObjectMap();
            foreach (var md in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath(md));
                if (m == null) { report.AppendLine("no material " + MatPath(md)); continue; }
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), md.name);
                if (existing.TryGetValue(key, out var cur) && cur == m) continue;
                imp.AddRemap(key, m); changed = true;
            }
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; changed = true; }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (changed) { imp.SaveAndReimport(); report.AppendLine("remapped " + fbx); }
        }

        // ── scene helpers ──────────────────────────────────────────────
        static Material GridMaterial(int cellsX, int cellsZ)
        {
            const int px = 64;
            var tex = new Texture2D(cellsX * px, cellsZ * px, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp };
            var a = new Color(0.30f, 0.38f, 0.27f); var b = new Color(0.27f, 0.35f, 0.24f); var line = new Color(0.62f, 0.66f, 0.55f);
            var cols = new Color[tex.width * tex.height];
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                {
                    bool onLine = x % px < 2 || y % px < 2 || x % px > px - 3 || y % px > px - 3;
                    bool odd = ((x / px) + (y / px)) % 2 == 1;
                    cols[y * tex.width + x] = onLine ? line : (odd ? a : b);
                }
            tex.SetPixels(cols); tex.Apply();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader);
            m.SetTexture("_BaseMap", tex); m.SetFloat("_Smoothness", 0.04f);
            return m;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in rs) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        static void Shoot(Camera cam, Vector3 focus, Vector3 from, float dist, float tilt, string file, int w, int h)
        {
            if (tilt > 85f)
            {   // straight down, north up
                cam.transform.position = focus + Vector3.up * dist;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            else
            {
                var flat = new Vector3(from.x, 0f, from.z).normalized;
                var back = flat * Mathf.Cos(tilt * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(tilt * Mathf.Deg2Rad);
                cam.transform.position = focus + back * dist;
                cam.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);
            }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
