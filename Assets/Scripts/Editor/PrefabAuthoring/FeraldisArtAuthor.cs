// FeraldisArtAuthor.cs
// Wires the ORIGINAL Feraldis building art (modelled from scratch in Blender
// by tools/blender/feraldis/*.py, exported as FBX under each building's Art/
// folder) into the prefabs and level SOs the game reads (2026-10-13).
//
// Feraldis shares no art with Alanthor: no Synty / ExplosiveLLC meshes, no
// Alanthor prefab, branch or FBX is referenced anywhere below. Everything
// this pass produces lives under Civs/Feraldis/ (the edition strip's unit).
//
// What it does, per building:
//   * Fortress                   Age0/Buildings/Fortress/Fortress.prefab is
//                                (re)built as the capital's multi-variant
//                                prefab: Lv0 = the NEW culture-neutral Shelter
//                                (tools/blender/feraldis/shelter.py, parts
//                                numbered 1..6 for the construction rise),
//                                Alanthor/Lv1..3 = Alanthor's existing branch
//                                carried over from Fortress_old.prefab,
//                                Feraldis/Lv1..3 = the new Feraldis Fortress,
//                                Runai empty. Both cultures' Fortress level
//                                SOs drop their `prefab` so only the in-prefab
//                                branch switch is ever consulted.
//   * Gatherer's Hut             the Age 0 multi-variant prefab: fills the
//                                existing "Feraldis" branch with Lv1/Lv2/Lv3
//                                (BuildingVariantVisual switches them in place)
//   * Barracks -> War Hall,       the legacy per-level ladder: one prefab per
//     Hut -> Slave Pens           level under <Building>/Art/, referenced by
//                                the Feraldis BuildingLevelDefSOs' `prefab`
//                                (BuildingPrefabSwapSystem.ResolvePrefab)
//   * Great Forge, Hall of Axes, culture-only levelled buildings: the SO
//     Rig Yard, Engine Works      prefab is rebuilt IN PLACE (same GUID) as a
//                                multi-variant prefab — Lv0 construction frame
//                                + Feraldis/Lv1..3; the Forge's four bays are
//                                named Bay1..Bay4
//   * War Totem, Longhouse,       single-form buildings: the SO prefab is
//     Hunting Lodge, Logging St.  rebuilt in place from the new model
//   * Mine                        the Feraldis Mine_Lvl1..3 SOs get one prefab
//                                per level (BuildingPrefabSwapSystem swaps the
//                                procedural pithead for it at level 1)
//   * Temple of Ridan ->          the Age 0 Temple SO gets a multi-variant
//     Blood Altar                 prefab: Lv0 is the Age 0 temple model
//                                (a NESTED reference to the Resources prefab,
//                                never a copy) + Feraldis/Lv1 the Blood Altar
//   * the Feraldis Fortress level SOs stop pointing at Alanthor's Hall_al_N
//
// Materials: one BuildingLit .mat per palette entry, created from the
// generator's palette.json (the generator is the single source of every
// Feraldis colour). FBX material NAMES are the contract; a mesh slot whose
// name has no .mat is reported, never silently left on the import default.
//
// Run: Waning Border > Art > Author Feraldis Building Art. Idempotent.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class FeraldisArtAuthor
    {
        const string TT  = "Assets/GameData/TechTree/";
        const string FER = TT + "Civs/Feraldis/Buildings/";
        const string ArtRoot = FER + "Art/";
        const string MatDir  = ArtRoot + "Materials/";
        const string PalettePath = ArtRoot + "palette.json";
        const string Age0ArtRoot = TT + "Age0/Buildings/Fortress/Art/";
        const string Age0MatDir  = Age0ArtRoot + "Materials/";
        const string Age0PalettePath = Age0ArtRoot + "palette.json";
        const string FortressPrefabPath = TT + "Age0/Buildings/Fortress/Fortress.prefab";
        const string FortressOldPrefabPath = TT + "Age0/Buildings/Fortress/Fortress_old.prefab";
        const string BuildingLitShader = "TheWaningBorder/BuildingLit";
        const string ReportPath = "Temp/feraldis-art-report.txt";

        static readonly List<string> Report = new List<string>();
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static int _failures;

        [Serializable] class PaletteEntry { public string name; public float[] @base; public float metallic; public float roughness; public float[] emission; public float strength; }
        [Serializable] class PaletteFile { public PaletteEntry[] materials; }

        [MenuItem("Waning Border/Art/Author Feraldis Building Art")]
        public static void AuthorAll()
        {
            Report.Clear(); Mats.Clear(); _failures = 0;
            try
            {
                EnsureMaterials(PalettePath, MatDir);       // Feraldis (edition-stripped with Civs/Feraldis/)
                EnsureMaterials(Age0PalettePath, Age0MatDir); // the culture-neutral Shelter, beside the Age 0 Fortress

                // Age 0 buildings: the prefab is whatever the building's SO references
                // (the Fortress SO was repointed at a new FBX on 2026-10-13, so no
                // path is hard-coded). A multi-variant prefab gets its Feraldis branch
                // filled; a model without the layout gets per-level prefabs on the
                // Feraldis level SOs (the legacy ladder BuildingPrefabSwapSystem.
                // ResolvePrefab reads) — which also stops those SOs pointing at
                // Alanthor's Hall_al_N.
                // SUPERSEDED 2026-10-13 ("use Synty as much as you can"): every
                // building this pass used to build from the Blender FBXs —
                // Fortress (Shelter + Feraldis Fortress), Barracks (Longhouse),
                // Work Camp, Slave Pens, Great Forge, Hall of Axes, Rig Yard,
                // Engine Works, Mine, Blood Altar, War Totem, Hunting Lodge,
                // Logging Station — is a Synty kit composition now, authored
                // by FeraldisKitAuthor (Waning Border > Art > Author Feraldis
                // Kit Art). Re-running THIS pass must not put the Blender
                // models back, so it only refreshes the palette materials
                // (Fer_PlayerColor_Cloth and the Age 0 cloth are still the
                // player-colour slots). The Blender generators and FBXs stay
                // on disk for the stand-alone Longhouse building
                // (Civs/Feraldis/Buildings/Longhouse/, untouched here).
                Report.Add("NOTE buildings: authored by Waning Border > Art > Author Feraldis Kit Art (Synty kit); this pass only refreshes materials");
                Plain(FER + "Longhouse/Longhouse.prefab", FER + "Longhouse/Art/Longhouse_Model.fbx", "Longhouse", FER + "Longhouse/Longhouse.asset");
            }
            catch (Exception e) { Fail("AuthorAll", e.ToString()); }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string text = $"[FeraldisArt] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { File.WriteAllText(ReportPath, text); } catch (Exception) { }
        }

        // ═════════════════════════════════════════════════════════════════
        // Previews — the prefabs as UNITY renders them (import scale, material
        // binding, emission, the player-colour slot), one PNG per level, into
        // Temp/feraldis-previews/. Objects are created HideAndDontSave far
        // below the open scene and destroyed again, so the scene is untouched.
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Render Feraldis Prefab Previews")]
        public static void RenderPreviews()
        {
            string outDir = Path.GetFullPath("Temp/feraldis-previews");
            Directory.CreateDirectory(outDir);
            string fortress = SoPrefabPath(TT + "Age0/Buildings/Fortress/Fortress.asset");
            var jobs = new List<(string label, string prefab, int level, byte culture)>
            {
                ("Shelter", fortress, 0, Cultures.None),
                ("FortressFeraldis", fortress, 1, Cultures.Feraldis),
                ("FortressFeraldis", fortress, 2, Cultures.Feraldis),
                ("FortressFeraldis", fortress, 3, Cultures.Feraldis),
                ("FortressAlanthor", fortress, 1, Cultures.Alanthor),
                ("WorkCamp", SoPrefabPath(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset"), 2, Cultures.Feraldis),
                ("TempleOfRidan", SoPrefabPath(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset"), 0, Cultures.Feraldis),
                ("BloodAltar", SoPrefabPath(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset"), 1, Cultures.Feraldis),
                ("GreatForge", FER + "GreatForge/GreatForge.prefab", 0, Cultures.Feraldis),
                ("GreatForge", FER + "GreatForge/GreatForge.prefab", 3, Cultures.Feraldis),
                ("HallOfAxes", FER + "HallOfAxes/HallOfAxes.prefab", 2, Cultures.Feraldis),
                ("RigYard", FER + "Pasture/RigYard.prefab", 1, Cultures.Feraldis),
                ("EngineWorks", FER + "SiegeYard/EngineWorks.prefab", 3, Cultures.Feraldis),
                ("WarHall", FER + "WarHall/Art/WarHall_2.prefab", -1, Cultures.Feraldis),
                ("SlavePens", FER + "SlavePens/Art/SlavePens_1.prefab", -1, Cultures.Feraldis),
                ("Mine", FER + "Mine/Art/Mine_3.prefab", -1, Cultures.Feraldis),
                ("WarTotem", FER + "WarTotem/WarTotem.prefab", -1, Cultures.Feraldis),
                ("Longhouse", FER + "Longhouse/Longhouse.prefab", -1, Cultures.Feraldis),
                ("HuntingLodge", FER + "HuntingLodge/HuntingLodge.prefab", -1, Cultures.Feraldis),
                ("LoggingStation", FER + "LoggingStation/LoggingStation.prefab", -1, Cultures.Feraldis),
            };
            if (File.Exists(FER + "WorkCamp/Art/WorkCamp_3.prefab")) jobs.Add(("WorkCamp", FER + "WorkCamp/Art/WorkCamp_3.prefab", -1, Cultures.Feraldis));

            var report = new List<string>();
            foreach (var j in jobs)
            {
                try { report.Add(RenderOne(j.label, j.prefab, j.level, j.culture, outDir)); }
                catch (Exception e) { report.Add($"FAIL {j.label} L{j.level}: {e.Message}"); }
            }
            string text = "[FeraldisArt] previews\n" + string.Join("\n", report);
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(outDir, "report.txt"), text); } catch (Exception) { }
        }

        static string SoPrefabPath(string soPath)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) return null;
            var p = new SerializedObject(so).FindProperty("prefab");
            var go = p != null ? p.objectReferenceValue as GameObject : null;
            return go != null ? AssetDatabase.GetAssetPath(go) : null;
        }

        /// <summary>level: -1 = plain prefab, 0 = Lv0 branch, N = Feraldis/LvN.</summary>
        static string RenderOne(string label, string prefabPath, int level, byte culture, string outDir)
        {
            if (string.IsNullOrEmpty(prefabPath)) return $"SKIP {label}: no prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return $"SKIP {label}: not loadable {prefabPath}";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(0f, -4000f, 0f);
            var created = new List<GameObject> { go };
            try
            {
                if (level >= 0)
                {
                    var v = TheWaningBorder.Rendering.BuildingVariantVisual.TrySetup(go);
                    if (v == null) return $"SKIP {label}: no variant layout in {prefabPath}";
                    if (level > 0) v.ShowVariant(culture, level);
                }
                TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.85f, 0.15f, 0.15f));

                var rends = go.GetComponentsInChildren<Renderer>(false);
                if (rends.Length == 0) return $"FAIL {label} L{level}: no renderers";
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);

                var camGo = new GameObject("FerPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                float diag = Mathf.Max(b.size.x, b.size.y, b.size.z);
                cam.orthographicSize = diag * 0.62f;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = diag * 12f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.09f, 0.11f, 0.17f, 1f);
                var dir = new Vector3(1f, 0.85f, -1f).normalized;
                camGo.transform.position = b.center + dir * diag * 4f;
                camGo.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                var sunGo = new GameObject("FerPreviewSun") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 2.0f; sun.color = new Color(1f, 0.93f, 0.85f);
                sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                // a cool fill from the far side so the shadow side is not a void
                var fillGo = new GameObject("FerPreviewFill") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.7f; fill.color = new Color(0.65f, 0.75f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

                var rt = new RenderTexture(960, 760, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                string file = Path.Combine(outDir, $"{label}_{(level < 0 ? "model" : "Lv" + level)}.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                return $"OK {label} L{level}: bounds {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} m, {rends.Length} renderers -> {file}";
            }
            finally
            {
                foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // Materials
        // ═════════════════════════════════════════════════════════════════

        static void EnsureMaterials(string palettePath, string matDir)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(palettePath);
            string raw = json != null ? json.text : (File.Exists(palettePath) ? File.ReadAllText(palettePath) : null);
            if (raw == null) throw new Exception("palette.json missing at " + palettePath + " — run tools/blender/feraldis/build_all.py");
            var palette = JsonUtility.FromJson<PaletteFile>(raw);
            if (palette == null || palette.materials == null || palette.materials.Length == 0)
                throw new Exception("palette.json parsed to nothing");

            var shader = Shader.Find(BuildingLitShader);
            if (shader == null) throw new Exception("shader missing: " + BuildingLitShader);
            Directory.CreateDirectory(matDir);

            foreach (var e in palette.materials)
            {
                string path = matDir + e.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = false;
                if (m == null) { m = new Material(shader) { name = e.name }; AssetDatabase.CreateAsset(m, path); created = true; }
                else if (m.shader != shader) m.shader = shader;

                var baseCol = new Color(e.@base[0], e.@base[1], e.@base[2], 1f);
                m.SetColor("_BaseColor", baseCol);
                if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
                m.SetFloat("_Metallic", e.metallic);
                m.SetFloat("_Smoothness", Mathf.Clamp01(1f - e.roughness));
                if (e.strength > 0f)
                {
                    var em = new Color(e.emission[0] * e.strength, e.emission[1] * e.strength, e.emission[2] * e.strength, 1f);
                    m.SetColor("_EmissionColor", em);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                    m.DisableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
                EditorUtility.SetDirty(m);
                Mats[e.name] = m;
                Report.Add((created ? "OK material created " : "OK material refreshed ") + path);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // Models
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Import settings for a generated FBX: a static mesh, nothing else.</summary>
        static void ImportSettings(string fbxPath)
        {
            var imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null) throw new Exception("not a model: " + fbxPath);
            bool dirty = false;
            if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (imp.importBlendShapes) { imp.importBlendShapes = false; dirty = true; }
            if (imp.isReadable) { imp.isReadable = false; dirty = true; }
            if (imp.addCollider) { imp.addCollider = false; dirty = true; }
            if (imp.meshCompression != ModelImporterMeshCompression.Off) { imp.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
            if (imp.importNormals != ModelImporterNormals.Import) { imp.importNormals = ModelImporterNormals.Import; dirty = true; }
            if (imp.materialImportMode == ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }

        /// <summary>
        /// A fully unpacked instance of the generated model with every
        /// material slot bound to the palette .mat of the same name and no
        /// colliders. Returns the instance root (named after the file).
        /// </summary>
        static GameObject InstantiateModel(string fbxPath)
        {
            ImportSettings(fbxPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new Exception("FBX missing or not imported: " + fbxPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var col in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "<null>";
                    if (Mats.TryGetValue(n, out var pal)) mats[i] = pal;
                    else Fail(Path.GetFileName(fbxPath), $"renderer '{r.name}' slot {i} material '{n}' has no palette entry");
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            return inst;
        }

        /// <summary>Move the model's children (Model, Banner, Bay1..) under `parent`, drop the file root.</summary>
        static void AdoptChildren(GameObject modelRoot, Transform parent)
        {
            var kids = new List<Transform>();
            for (int i = 0; i < modelRoot.transform.childCount; i++) kids.Add(modelRoot.transform.GetChild(i));
            foreach (var k in kids) k.SetParent(parent, true);
            UnityEngine.Object.DestroyImmediate(modelRoot);
        }

        // ═════════════════════════════════════════════════════════════════
        // Modes
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// The capital's multi-variant prefab, (re)built at Fortress.prefab:
        /// starts as a copy of Fortress_old.prefab when it does not exist yet
        /// (that is where Alanthor's own branch lives — it is carried over
        /// untouched), then Lv0 becomes the new Shelter, Feraldis/Lv1..3 the
        /// new Feraldis Fortress, Runai stays empty. Fortress.asset is pointed
        /// at it (presentationId untouched; the user's loose Fortress FBX is
        /// simply no longer referenced, not deleted), both cultures' Fortress
        /// level SOs drop their prefab, and the interim per-level Feraldis
        /// prefabs are removed.
        /// </summary>
        static void FortressVariant()
        {
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(FortressPrefabPath) == null)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(FortressOldPrefabPath) == null)
                        throw new Exception("neither Fortress.prefab nor Fortress_old.prefab exists — nothing to carry Alanthor's branch from");
                    if (!AssetDatabase.CopyAsset(FortressOldPrefabPath, FortressPrefabPath))
                        throw new Exception("CopyAsset failed: " + FortressOldPrefabPath + " -> " + FortressPrefabPath);
                    Report.Add("OK Fortress.prefab created as a copy of Fortress_old.prefab (Alanthor branch carried over)");
                }

                var contents = PrefabUtility.LoadPrefabContents(FortressPrefabPath);
                try
                {
                    contents.name = "Fortress";
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + FortressPrefabPath);

                    // Lv0: the Shelter, parts numbered 1..6 for the construction rise.
                    Transform lv0 = FindChild(container, "Lv0");
                    for (int i = lv0.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(lv0.GetChild(i).gameObject);
                    var shelter = InstantiateModel(Age0ArtRoot + "Shelter_Lv0.fbx");
                    shelter.transform.SetParent(lv0, false);
                    AdoptChildren(shelter, lv0);
                    lv0.gameObject.SetActive(true);

                    // Alanthor: carried over as is — just report what is there.
                    Transform al = FindChild(container, "Alanthor");
                    Report.Add(al != null
                        ? $"OK Fortress: Alanthor branch kept ({al.childCount} level nodes: {Names(al)})"
                        : "WARN Fortress: no Alanthor branch in the prefab — Alanthor's art was not in Fortress_old.prefab");

                    // Feraldis: Lv1..3 from the new models.
                    Transform fer = FindChild(container, "Feraldis") ?? new GameObject("Feraldis").transform;
                    fer.SetParent(container, false);
                    for (int i = fer.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(fer.GetChild(i).gameObject);
                    for (int l = 1; l <= 3; l++)
                    {
                        var lv = new GameObject("Lv" + l).transform;
                        lv.SetParent(fer, false);
                        var model = InstantiateModel(FER + $"Fortress/Art/Fortress_Lv{l}.fbx");
                        model.transform.SetParent(lv, false);
                        AdoptChildren(model, lv);
                    }
                    fer.gameObject.SetActive(true);

                    // Runai: present and empty.
                    Transform ru = FindChild(container, "Runai") ?? new GameObject("Runai").transform;
                    ru.SetParent(container, false);
                    for (int i = ru.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(ru.GetChild(i).gameObject);

                    PrefabUtility.SaveAsPrefabAsset(contents, FortressPrefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + FortressPrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FortressPrefabPath);
                WireBuildingSo(TT + "Age0/Buildings/Fortress/Fortress.asset", prefab);
                Report.Add($"OK Fortress: multi-variant prefab (Lv0 Shelter + Alanthor + Feraldis/Lv1..3 + Runai) -> {FortressPrefabPath}; Fortress.asset repointed");

                // One path only: the in-prefab branch switch. The level SOs of
                // BOTH cultures stop carrying a model (Alanthor's pointed at the
                // legacy Resources Hall_al_N, Feraldis's at interim per-level prefabs).
                for (int l = 1; l <= 3; l++)
                {
                    ClearLevelPrefab(FER + $"Fortress/Fortress_Lvl{l}.asset");
                    ClearLevelPrefab(TT + $"Civs/Alanthor/Buildings/Fortress/Fortress_Lvl{l}.asset");
                    string interim = FER + $"Fortress/Art/Fortress_{l}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(interim) != null && AssetDatabase.DeleteAsset(interim))
                        Report.Add("OK removed interim " + interim);
                }
            }
            catch (Exception e) { Fail("Fortress", e.ToString()); }
        }

        static Transform FindChild(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
                if (string.Equals(t.GetChild(i).name, name, StringComparison.OrdinalIgnoreCase)) return t.GetChild(i);
            return null;
        }

        static string Names(Transform t)
        {
            var n = new List<string>();
            for (int i = 0; i < t.childCount; i++) n.Add(t.GetChild(i).name);
            return string.Join(", ", n);
        }

        /// <summary>
        /// An Age 0 building: fill the Feraldis branch of its SO prefab when that
        /// prefab carries the multi-variant layout (Lv0 + culture branches),
        /// else author one prefab per level and reference them from the
        /// Feraldis level SOs. Either way the level SOs never point at another
        /// culture's model.
        /// </summary>
        static void Age0Building(string soPath, string fbxDir, string stem, string levelSoFormat)
        {
            try
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
                if (so == null) throw new Exception("SO missing " + soPath);
                var prop = new SerializedObject(so).FindProperty("prefab");
                var soPrefab = prop != null ? prop.objectReferenceValue as GameObject : null;
                string prefabPath = soPrefab != null ? AssetDatabase.GetAssetPath(soPrefab) : null;
                bool variant = soPrefab != null && prefabPath != null
                               && prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                               && TheWaningBorder.Rendering.BuildingVariantVisual.HasVariantLayout(soPrefab);
                if (variant)
                {
                    VariantBranch(prefabPath, fbxDir, stem);
                    for (int l = 1; l <= 3; l++) ClearLevelPrefab(string.Format(levelSoFormat, l));
                }
                else
                {
                    Report.Add($"NOTE {stem}: SO prefab '{prefabPath ?? "<none>"}' has no Lv0 variant layout - per-level prefabs on the level SOs");
                    LevelPrefabs(fbxDir, stem, levelSoFormat);
                }
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>Fill the "Feraldis" culture branch of an existing multi-variant prefab with Lv1..Lv3.</summary>
        static void VariantBranch(string prefabPath, string fbxDir, string stem)
        {
            try
            {
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + prefabPath);
                    Transform fer = null;
                    for (int i = 0; i < container.childCount; i++)
                        if (string.Equals(container.GetChild(i).name, "Feraldis", StringComparison.OrdinalIgnoreCase)) { fer = container.GetChild(i); break; }
                    if (fer == null)
                    {
                        fer = new GameObject("Feraldis").transform;
                        fer.SetParent(container, false);
                    }
                    for (int i = fer.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(fer.GetChild(i).gameObject);
                    for (int l = 1; l <= 3; l++)
                    {
                        var lv = new GameObject("Lv" + l).transform;
                        lv.SetParent(fer, false);
                        var model = InstantiateModel($"{fbxDir}{stem}_Lv{l}.fbx");
                        model.transform.SetParent(lv, false);
                        AdoptChildren(model, lv);
                    }
                    fer.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
                    Report.Add($"OK {stem}: Feraldis/Lv1..3 written into {prefabPath}");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>One prefab per level beside the FBX, referenced by the level SOs.</summary>
        static void LevelPrefabs(string fbxDir, string stem, string soPathFormat)
        {
            for (int l = 1; l <= 3; l++)
            {
                try
                {
                    var root = new GameObject($"{stem}_{l}");
                    var model = InstantiateModel($"{fbxDir}{stem}_Lv{l}.fbx");
                    model.transform.SetParent(root.transform, false);
                    AdoptChildren(model, root.transform);
                    string prefabPath = $"{fbxDir}{stem}_{l}.prefab";
                    var prefab = Save(root, prefabPath);
                    WireLevelSo(string.Format(soPathFormat, l), prefab);
                    Report.Add($"OK {stem} L{l} -> {prefabPath}");
                }
                catch (Exception e) { Fail($"{stem} L{l}", e.ToString()); }
            }
        }

        /// <summary>A culture-only building's SO prefab as a multi-variant prefab: Lv0 frame + Feraldis/Lv1..3. Rebuilt in place.</summary>
        static void VariantPrefab(string prefabPath, string fbxDir, string stem, string soPath)
        {
            try
            {
                var root = new GameObject(stem);
                var lv0 = new GameObject("Lv0").transform;
                lv0.SetParent(root.transform, false);
                var frame = InstantiateModel($"{fbxDir}{stem}_Lv0.fbx");
                frame.transform.SetParent(lv0, false);
                AdoptChildren(frame, lv0);

                var fer = new GameObject("Feraldis").transform;
                fer.SetParent(root.transform, false);
                for (int l = 1; l <= 3; l++)
                {
                    var lv = new GameObject("Lv" + l).transform;
                    lv.SetParent(fer, false);
                    var model = InstantiateModel($"{fbxDir}{stem}_Lv{l}.fbx");
                    model.transform.SetParent(lv, false);
                    AdoptChildren(model, lv);
                }
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: multi-variant prefab -> {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>
        /// The Age 0 Temple of Ridan as a multi-variant prefab: Lv0 is the
        /// existing Age 0 temple (nested Resources prefab instance — referenced,
        /// not copied, so the Age 0 art stays where it is), Feraldis/Lv1 the
        /// Blood Altar. Written beside the Temple's SO and set as its prefab.
        /// </summary>
        static void TempleVariant()
        {
            const string prefabPath = TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.prefab";
            const string soPath     = TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset";
            const string lv0Source  = "Assets/Resources/Prefabs/Buildings/TempleOfRidan.prefab";
            try
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(lv0Source);
                if (src == null) throw new Exception("Age 0 temple prefab missing: " + lv0Source);
                var root = new GameObject("TempleOfRidan");
                var lv0 = new GameObject("Lv0").transform;
                lv0.SetParent(root.transform, false);
                var nested = (GameObject)PrefabUtility.InstantiatePrefab(src);
                nested.transform.SetParent(lv0, false);
                nested.transform.localPosition = Vector3.zero;
                nested.transform.localRotation = Quaternion.identity;

                var fer = new GameObject("Feraldis").transform;
                fer.SetParent(root.transform, false);
                var lv1 = new GameObject("Lv1").transform;
                lv1.SetParent(fer, false);
                var model = InstantiateModel(FER + "BloodAltar/Art/BloodAltar_Model.fbx");
                model.transform.SetParent(lv1, false);
                AdoptChildren(model, lv1);

                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK TempleOfRidan: multi-variant prefab (Lv0 = Age 0 temple, Feraldis/Lv1 = Blood Altar) -> {prefabPath}");
            }
            catch (Exception e) { Fail("TempleOfRidan", e.ToString()); }
        }

        /// <summary>A single-form building's SO prefab, rebuilt in place from the new model.</summary>
        static void Plain(string prefabPath, string fbxPath, string stem, string soPath)
        {
            try
            {
                var root = new GameObject(stem);
                var model = InstantiateModel(fbxPath);
                model.transform.SetParent(root.transform, false);
                AdoptChildren(model, root.transform);
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: prefab -> {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        // ═════════════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════════════

        static Transform FindContainerWithLv0(Transform t, int maxDepth)
        {
            for (int i = 0; i < t.childCount; i++)
                if (string.Equals(t.GetChild(i).name, "Lv0", StringComparison.OrdinalIgnoreCase)) return t;
            if (maxDepth <= 1) return null;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindContainerWithLv0(t.GetChild(i), maxDepth - 1);
                if (f != null) return f;
            }
            return null;
        }

        static GameObject Save(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);
            if (!ok || asset == null) throw new Exception("SaveAsPrefabAsset failed for " + path);
            return asset;
        }

        /// <summary>`prefab` on a BuildingDefSO (presentationId untouched).</summary>
        static void WireBuildingSo(string soPath, GameObject prefab)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) throw new Exception("SO missing " + soPath);
            var s = new SerializedObject(so);
            var p = s.FindProperty("prefab");
            if (p == null) throw new Exception("no prefab field on " + soPath);
            p.objectReferenceValue = prefab;
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
        }

        /// <summary>`prefab` on a BuildingLevelDefSO.</summary>
        static void WireLevelSo(string soPath, GameObject prefab) => WireBuildingSo(soPath, prefab);

        static void ClearLevelPrefab(string soPath)
        {
            try
            {
                WireBuildingSo(soPath, null);
                Report.Add("OK cleared prefab on " + soPath);
            }
            catch (Exception e) { Fail(soPath, e.ToString()); }
        }

        static void Fail(string id, string why)
        {
            _failures++;
            Report.Add("FAIL " + id + ": " + why);
            Debug.LogError("[FeraldisArt] " + id + ": " + why);
        }
    }
}
