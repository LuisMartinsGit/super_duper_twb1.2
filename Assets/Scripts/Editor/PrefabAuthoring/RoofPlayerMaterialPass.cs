// The castle kit's roofs and the player colour (2026-11-02).
//
// Every roof the player colour multiplies into has to be the NEUTRAL tile
// (Castle_Roof_Player: Roof_Tile_Player.png, luminance only) - the kit's own
// Castle_Roof_01 is teal, and teal x a swatch keeps the teal's hue: Blue looked
// right, Red went purple, Yellow went green (the Fortress, 2026-11-02).
// The 2026-11-01 pass patched the YAML of a few prefabs by hand and missed the
// hand-authored "Fortress 1..3" / "Garrison_3 n" / "Tower n" prefabs the level
// SOs now bind, and 38 of the 40 caps on the Fortress Art prefabs.
//
// This pass goes through the prefab API instead: every .prefab under the Age 0,
// Alanthor and sect folders (the user-confirmed art scope; Feraldis and Runai
// have their own looks) has each renderer slot that holds the source material
// pointed at Castle_Roof_Player, saved as a normal override on the nested Synty
// instance - what the Inspector would write. Re-runnable; a second run changes
// nothing. Report in Temp/roof-player-pass-<tag>.txt.
//
// The probe renders a row of a building in six player colours to
// Temp/roof-colour-probe/<Building>.png with BuildingFactionColorMarker.Apply
// (walls: WallModuleArt.ApplyOwnerColor), so the tint can be judged without a
// match - the in-match art-probe camera is useless on the layout maps.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class RoofPlayerMaterialPass
    {
        const string TealRoofPath   = "Assets/Synty/PolygonFantasyKingdom/Materials/Misc/Castle_Roof_01.mat";
        const string AlTilePath     = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/Art/Materials/Al_Tile.mat";
        const string PlayerRoofPath = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Castle_Roof_Player.mat";

        static readonly string[] Scope =
        {
            "Assets/GameData/TechTree/Age0",
            "Assets/GameData/TechTree/Civs/Alanthor",
            "Assets/GameData/TechTree/Sects",
        };

        [MenuItem("Waning Border/Art/Roofs/Castle_Roof_01 -> Castle_Roof_Player (Age 0 + Alanthor + Sects)")]
        public static void SwapTealRoofs() => Swap(TealRoofPath, "castle-roof");

        /// <summary>The Alanthor theme's teal Roof_Tile_04 pyramids. NOT run by
        /// default: those roofs were authored teal on purpose; this is here so
        /// the choice is one click if every roof should carry the colour.</summary>
        [MenuItem("Waning Border/Art/Roofs/Al_Tile -> Castle_Roof_Player (Age 0 + Alanthor + Sects)")]
        public static void SwapAlTile() => Swap(AlTilePath, "al-tile");

        static void Swap(string fromPath, string tag)
        {
            var from = AssetDatabase.LoadAssetAtPath<Material>(fromPath);
            var to = AssetDatabase.LoadAssetAtPath<Material>(PlayerRoofPath);
            if (from == null || to == null)
            {
                Debug.LogError($"[RoofPlayerMaterialPass] missing material: from={(from != null)} ({fromPath}) to={(to != null)} ({PlayerRoofPath})");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"Roof material pass: {from.name} -> {to.name}   {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            int prefabsChanged = 0, slotsChanged = 0, prefabsSeen = 0, failed = 0;
            var guids = AssetDatabase.FindAssets("t:Prefab", Scope);
            var paths = new List<string>();
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) paths.Add(p);
            }
            paths.Sort();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string path = paths[i];
                    EditorUtility.DisplayProgressBar("Roof material pass", path, (float)i / paths.Count);
                    prefabsSeen++;
                    GameObject root;
                    try { root = PrefabUtility.LoadPrefabContents(path); }
                    catch (System.Exception e) { report.AppendLine($"FAIL load {path}: {e.Message}"); failed++; continue; }
                    int changed = 0;
                    try
                    {
                        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                            var mats = r.sharedMaterials;
                            bool any = false;
                            for (int m = 0; m < mats.Length; m++)
                            {
                                if (mats[m] != from) continue;
                                mats[m] = to; any = true; changed++;
                            }
                            if (any) r.sharedMaterials = mats;
                        }
                        if (changed > 0)
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                            if (!ok) { report.AppendLine($"FAIL save {path}"); failed++; }
                            else { prefabsChanged++; slotsChanged += changed; report.AppendLine($"{changed,4} slots  {path}"); }
                        }
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            report.AppendLine($"-- {prefabsSeen} prefabs scanned, {prefabsChanged} changed ({slotsChanged} slots), {failed} failed");
            Directory.CreateDirectory("Temp");
            string file = Path.Combine("Temp", $"roof-player-pass-{tag}.txt");
            File.WriteAllText(file, report.ToString());
            Debug.Log($"[RoofPlayerMaterialPass] {from.name} -> {to.name}: {prefabsChanged} prefabs / {slotsChanged} slots changed of {prefabsSeen} scanned, {failed} failed. {file}");
        }

        // ------------------------------------------------------------------
        // PROBE
        // ------------------------------------------------------------------

        struct Subject { public string Label, Path; public bool Wall; }

        /// <summary>Buildings whose levels come from their level SOs
        /// (`<Folder>/<Name>_Lvl1..3.asset`, field `prefab`): the probe then shows
        /// exactly the prefab the game shows at that level, whatever it is bound to.</summary>
        static readonly (string Label, string SoStem)[] LevelledSubjects =
        {
            ("Fortress",     "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Fortress/Fortress"),
            ("WatchTower",   "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/WatchTower"),
            ("Garrison",     "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Garrison"),
            ("ArcheryRange", "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/ArcheryRange"),
            ("House",        "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House"),
            ("Guild",        "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Guild/Guild"),
        };

        /// <summary>Prefabs shown by path (no level SO): a stone-wall tower, for
        /// the wall colour path.</summary>
        static readonly Subject[] ExtraSubjects =
        {
            new Subject { Label = "StoneWall_Tower_L2", Path = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/StoneWall/Lvl2/Tower/alanthor_StoneWall_Tower_L2.prefab", Wall = true },
        };

        const string ShelterSoPath = "Assets/GameData/TechTree/Age0/Buildings/Fortress/Fortress.asset";

        /// <summary>The prefab an SO's `prefab` field points at, or null.</summary>
        static string SoPrefabPath(string soPath)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) return null;
            var prop = new SerializedObject(so).FindProperty("prefab");
            var go = prop != null ? prop.objectReferenceValue as GameObject : null;
            return go != null ? AssetDatabase.GetAssetPath(go) : null;
        }

        static List<Subject> BuildSubjects(StringBuilder log)
        {
            var list = new List<Subject>();
            foreach (var (label, stem) in LevelledSubjects)
                for (int l = 1; l <= 3; l++)
                {
                    string so = $"{stem}_Lvl{l}.asset";
                    string path = SoPrefabPath(so);
                    if (path == null) { log.AppendLine($"SKIP {label}_L{l}: no prefab on {so}"); continue; }
                    list.Add(new Subject { Label = $"{label}_L{l}", Path = path });
                }
            string shelter = SoPrefabPath(ShelterSoPath);
            if (shelter != null) list.Add(new Subject { Label = "Shelter_Age0", Path = shelter });
            else log.AppendLine($"SKIP Shelter_Age0: no prefab on {ShelterSoPath}");
            list.AddRange(ExtraSubjects);
            return list;
        }

        /// <summary>Blue, Red, Green, Yellow, Purple, Orange: the first six lobby swatches.</summary>
        const int ProbeColours = 6;

        /// <summary>Preview only: the probe swaps Al_Tile for the player tile ON
        /// THE INSTANCES it renders, so the teal pyramids can be judged as player
        /// roofs without touching a prefab. Output in a sub-folder.</summary>
        static bool _alTileAsPlayer;

        [MenuItem("Waning Border/Art/Roofs/Render Roof Colour Probe (Al_Tile as player, preview only)")]
        public static void RenderProbeAlTileAsPlayer()
        {
            _alTileAsPlayer = true;
            try { RenderProbe(); } finally { _alTileAsPlayer = false; }
        }

        [MenuItem("Waning Border/Art/Roofs/Render Roof Colour Probe")]
        public static void RenderProbe()
        {
            string outDir = Path.Combine("Temp", "roof-colour-probe");
            if (_alTileAsPlayer) outDir = Path.Combine(outDir, "al-tile-as-player");
            Directory.CreateDirectory(outDir);
            var log = new StringBuilder();
            foreach (var s in BuildSubjects(log))
            {
                try { log.AppendLine($"{RenderRow(s, outDir)}   [{s.Path}]"); }
                catch (System.Exception e) { log.AppendLine($"FAIL {s.Label}: {e}"); }
            }
            File.WriteAllText(Path.Combine(outDir, "probe.txt"), log.ToString());
            Debug.Log("[RoofPlayerMaterialPass] probe\n" + log);
        }

        static string RenderRow(Subject s, string outDir)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(s.Path);
            if (prefab == null) return $"SKIP {s.Label}: not loadable {s.Path}";
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4000f, 0f);
            try
            {
                // One copy per colour, side by side on their bounds.
                var copies = new List<GameObject>();
                float width = 0f;
                for (int c = 0; c < ProbeColours; c++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.position = stage;
                    created.Add(go); copies.Add(go);
                    if (_alTileAsPlayer) SwapOnInstance(go, AlTilePath, PlayerRoofPath);
                    if (!MeshBounds(go, out var b)) return $"FAIL {s.Label}: no renderers";
                    width = Mathf.Max(width, b.size.x);
                }
                float spacing = width + 3f;
                Bounds all = default; bool anyB = false;
                for (int c = 0; c < copies.Count; c++)
                {
                    var go = copies[c];
                    MeshBounds(go, out var b);
                    // Stand it on the stage at its slot, by its measured bounds.
                    go.transform.position += new Vector3((c - (ProbeColours - 1) * 0.5f) * spacing - b.center.x, stage.y - b.min.y, stage.z - b.center.z);
                    var colour = FactionColors.ColorPool[c];
                    BuildingFactionColorMarker.Apply(go, colour);
                    if (s.Wall) WallModuleArt.ApplyOwnerColor(go, colour);
                    MeshBounds(go, out b);
                    if (!anyB) { all = b; anyB = true; } else all.Encapsulate(b);
                }

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.hideFlags = HideFlags.HideAndDontSave;
                created.Add(ground);
                ground.transform.position = new Vector3(all.center.x, all.min.y - 0.02f, all.center.z);
                ground.transform.localScale = new Vector3(all.size.x * 0.25f, 1f, Mathf.Max(all.size.z, 10f) * 0.5f);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                gm.SetColor("_BaseColor", new Color(0.30f, 0.40f, 0.28f));
                gm.SetFloat("_Smoothness", 0.05f);
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                Object.DestroyImmediate(ground.GetComponent<Collider>());

                // RTS-angle orthographic shot framed on the projected corners.
                const int W = 2400, H = 700;
                var camGo = new GameObject("RoofProbeCam") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                float diag = Mathf.Max(all.size.x, all.size.y, all.size.z);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = diag * 12f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
                var dir = new Vector3(0.25f, 0.72f, -0.65f).normalized;
                camGo.transform.position = all.center + dir * diag * 4f;
                camGo.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                float ex = 0f, ey = 0f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? all.min.x : all.max.x, (i & 2) == 0 ? all.min.y : all.max.y, (i & 4) == 0 ? all.min.z : all.max.z);
                    var v = camGo.transform.InverseTransformPoint(corner);
                    ex = Mathf.Max(ex, Mathf.Abs(v.x)); ey = Mathf.Max(ey, Mathf.Abs(v.y));
                }
                cam.orthographicSize = Mathf.Max(ey, ex * H / (float)W) * 1.08f;

                var sunGo = new GameObject("RoofProbeSun") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.7f; sun.color = new Color(1f, 0.94f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
                var fillGo = new GameObject("RoofProbeFill") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.75f, 0.8f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(35f, 140f, 0f);

                bool fog = RenderSettings.fog;
                var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.48f, 0.58f);
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;
                string file = Path.Combine(outDir, $"{s.Label}.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                rt.Release(); Object.DestroyImmediate(rt);
                Object.DestroyImmediate(gm);
                return $"OK {s.Label}: {all.size.x:F1} x {all.size.y:F1} x {all.size.z:F1} m -> {file}";
            }
            finally { foreach (var c in created) if (c != null) Object.DestroyImmediate(c); }
        }

        /// <summary>Replace one material with another on a scene INSTANCE
        /// (sharedMaterials on the instance's renderers; the prefab asset is
        /// untouched and the instance is destroyed after the shot).</summary>
        static void SwapOnInstance(GameObject go, string fromPath, string toPath)
        {
            var from = AssetDatabase.LoadAssetAtPath<Material>(fromPath);
            var to = AssetDatabase.LoadAssetAtPath<Material>(toPath);
            if (from == null || to == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool any = false;
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] == from) { mats[m] = to; any = true; }
                if (any) r.sharedMaterials = mats;
            }
        }

        static bool MeshBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
