// BuildingArtWiringTool.cs
// Turns the exported *Variants.fbx building art into wired, team-colorable
// multi-variant prefabs.
//
// The FBX files are produced from Blender_assets/Buildings.blend and already
// carry the hierarchy BuildingVariantVisual expects:
//
//   <Building>
//     Lv0                  numbered NN_Rise children (ordered construction rise)
//     Alanthor
//       Lv1 / Lv2 / Lv3    (Hut levels additionally hold LvN_A / LvN_B shapes)
//
// This tool does the four Unity-side steps that cannot be baked into an FBX:
//
//   1. Import the shared Synty atlases uncompressed + readable, so the
//      faction recolor's hue test sees exact authored pixels.
//   2. Create one URP/Lit material per atlas with the texture on _BaseMap.
//      This is the whole ball game for team color: BuildingFactionColorMarker
//      only ever swaps _BaseMap / _MainTex. The pre-existing Hall materials
//      put the atlas on _DetailAlbedoMap instead, which is exactly why the
//      Hall has never shown a player color.
//   3. Remap each FBX's embedded materials onto those shared assets.
//   4. Build/refresh the prefab (adding the empty Runai + Feraldis branches
//      the FBX deliberately omits) and assign it to the building's
//      BuildingDefSO along with its presentationId.
//
// Re-runnable: every step is idempotent, so re-exporting the FBX and running
// this again refreshes the art without recreating GUIDs.

// This folder lives inside the TheWaningBorder.Runtime asmref scope, so an
// "Editor" folder does NOT get its own assembly here — these files compile
// into the runtime assembly and the guard is what keeps UnityEditor out of
// player builds. Same pattern as BuildingFactionColorAudit.cs beside it.
#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.Presentation.EditorTools
{
    public static class BuildingArtWiringTool
    {
        private const string AtlasFolder =
            "Assets/GameData/TechTree/Presentation/Buildings/Atlases";

        private sealed class BuildingArt
        {
            public string Name;            // prefab + root node name
            public string FbxPath;
            public string SoPath;
            public int    PresentationId;
        }

        private static readonly BuildingArt[] Art =
        {
            new BuildingArt {
                Name = "Hall", PresentationId = 100,
                FbxPath = "Assets/GameData/TechTree/Buildings/Age 0/Hall/HallVariants.fbx",
                SoPath  = "Assets/GameData/TechTree/Buildings/Age 0/Hall/Hall.asset" },
            new BuildingArt {
                Name = "Hut", PresentationId = 102,
                FbxPath = "Assets/GameData/TechTree/Buildings/Age 0/Hut/HutVariants.fbx",
                SoPath  = "Assets/GameData/TechTree/Buildings/Age 0/Hut/Hut.asset" },
            new BuildingArt {
                Name = "Barracks", PresentationId = 510,
                FbxPath = "Assets/GameData/TechTree/Buildings/Age 0/Barracks/BarracksVariants.fbx",
                SoPath  = "Assets/GameData/TechTree/Buildings/Age 0/Barracks/Barracks.asset" },
            new BuildingArt {
                Name = "ArcheryRange", PresentationId = 511,
                FbxPath = "Assets/GameData/TechTree/Buildings/Age 0/ArcheryRange/ArcheryRangeVariants.fbx",
                SoPath  = "Assets/GameData/TechTree/Buildings/Age 0/ArcheryRange/ArcheryRange.asset" },
            new BuildingArt {
                Name = "RoyalStable", PresentationId = 356,
                FbxPath = "Assets/GameData/TechTree/Buildings/Alanthor/RoyalStable/RoyalStableVariants.fbx",
                SoPath  = "Assets/GameData/TechTree/Buildings/Alanthor/RoyalStable/RoyalStable.asset" },
        };

        // Culture branches BuildingVariantVisual looks for. Alanthor comes in
        // on the FBX (it holds the authored art); the other two are created
        // empty so ResolveTarget falls back to Lv0 for those cultures rather
        // than showing Alanthor art under a Runai flag.
        private static readonly string[] EmptyCultureBranches = { "Runai", "Feraldis" };

        [MenuItem("Waning Border/Buildings/Wire Building Art (FBX -> Prefab -> SO)")]
        public static void Run()
        {
            int ok = 0, failed = 0;
            var log = new System.Text.StringBuilder();

            var materials = BuildAtlasMaterials(log);

            foreach (var art in Art)
            {
                try
                {
                    WireOne(art, materials, log);
                    ok++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    log.AppendLine($"  FAILED {art.Name}: {e.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BuildingArtWiring] {ok} wired, {failed} failed.\n{log}");
        }

        /// <summary>Batch entry point: Unity.exe -batchmode -executeMethod
        /// TheWaningBorder.Presentation.EditorTools.BuildingArtWiringTool.RunBatch</summary>
        public static void RunBatch()
        {
            Run();
            EditorApplication.Exit(0);
        }

        // ─────────────────────────────────────────────────────────────────
        // 1 + 2 — atlases and their materials
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, Material> BuildAtlasMaterials(System.Text.StringBuilder log)
        {
            var result = new Dictionary<string, Material>();
            if (!Directory.Exists(AtlasFolder))
            {
                log.AppendLine($"  atlas folder missing: {AtlasFolder}");
                return result;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                // Not fatal for the wiring, but the recolor needs _BaseMap.
                log.AppendLine("  URP/Lit shader not found — falling back to Standard.");
                shader = Shader.Find("Standard");
            }

            foreach (var texPath in Directory.GetFiles(AtlasFolder, "*.png"))
            {
                string assetPath = texPath.Replace('\\', '/');
                ConfigureAtlasImport(assetPath, log);

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null) continue;

                string matName = MaterialNameFor(Path.GetFileName(assetPath));
                string matPath = $"{AtlasFolder}/{matName}.mat";

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader) { name = matName };
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                if (mat.shader != shader) mat.shader = shader;

                // THE team-color contract: the atlas must sit on the main
                // albedo slot. _MainTex is set too because the marker walks
                // both aliases and URP materials expose them independently.
                if (mat.HasProperty("_BaseMap"))  mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex", tex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color", Color.white);
                // Flat low-poly art: kill the default gloss.
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);

                EditorUtility.SetDirty(mat);
                result[matName] = mat;
            }

            log.AppendLine($"  atlas materials: {result.Count}");
            return result;
        }

        // Mirrors the naming the Blender exporter writes into the FBX, so the
        // material remap below matches by name.
        private static string MaterialNameFor(string pngFileName)
        {
            switch (pngFileName)
            {
                case "Texture_01.png":                    return "Atlas_Texture_01";
                case "Texture_Alt_02.png":                return "Atlas_Texture_Alt_02";
                case "Texture_Alt_03.png":                return "Atlas_Texture_Alt_03";
                case "Texture_01_Swap_Snow_To_Grass.png": return "Atlas_Texture_01_SnowToGrass";
                case "PolyAdventureTexture_01.png":       return "Atlas_PolyAdventure_01";
                case "PolyAdventureTexture_Dark_01.png":  return "Atlas_PolyAdventure_Dark_01";
                case "PolyAdventureTexture_Snow_01.png":  return "Atlas_PolyAdventure_Snow_01";
                default: return "Atlas_" + Path.GetFileNameWithoutExtension(pngFileName);
            }
        }

        private static void ConfigureAtlasImport(string assetPath, System.Text.StringBuilder log)
        {
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) return;

            bool changed = false;

            // Readable = the recolor's fast GetPixels32 path instead of a
            // per-atlas GPU blit-and-readback.
            if (!ti.isReadable) { ti.isReadable = true; changed = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
            if (ti.textureType != TextureImporterType.Default)
            { ti.textureType = TextureImporterType.Default; changed = true; }

            // Point filtering + clamp: these are tightly packed swatch
            // atlases. Bilinear sampling across a swatch boundary invents
            // in-between colors, and near the blue block those blends drift
            // out of the 190-260 deg hue window — leaving hairline seams that
            // keep the authored blue on an otherwise recolored building.
            if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }

            // Uncompressed for the same reason: DXT quantisation shifts hues
            // by a few degrees, which is enough to push edge pixels of the
            // blue swatch out of the match window.
            var settings = ti.GetDefaultPlatformTextureSettings();
            if (settings.textureCompression != TextureImporterCompression.Uncompressed)
            {
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SetPlatformTextureSettings(settings);
                changed = true;
            }

            if (changed)
            {
                ti.SaveAndReimport();
                log.AppendLine($"  reimported atlas {Path.GetFileName(assetPath)}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3 + 4 — model import, prefab, SO
        // ─────────────────────────────────────────────────────────────────

        private static void WireOne(BuildingArt art, Dictionary<string, Material> materials,
                                    System.Text.StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(art.FbxPath) as ModelImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException("no FBX at " + art.FbxPath);

            ConfigureModelImport(importer, materials);

            var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(art.FbxPath);
            if (fbxRoot == null) throw new System.Exception("FBX failed to load");

            string prefabPath = Path.GetDirectoryName(art.FbxPath).Replace('\\', '/')
                                + "/" + art.Name + ".prefab";

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot);
            try
            {
                instance.name = art.Name;

                Transform container = FindLv0Container(instance.transform);
                if (container == null)
                    throw new System.Exception("no Lv0 node — the FBX hierarchy is wrong");

                foreach (var culture in EmptyCultureBranches)
                {
                    if (container.Find(culture) != null) continue;
                    var go = new GameObject(culture);
                    go.transform.SetParent(container, false);
                }

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
                if (!success || saved == null)
                    throw new System.Exception("SaveAsPrefabAsset failed");

                AssignToSo(art, saved, log);

                int abGroups = CountAbGroups(saved.transform);
                log.AppendLine(
                    $"  {art.Name}: prefab={prefabPath} pid={art.PresentationId}" +
                    (abGroups > 0 ? $" abGroups={abGroups}" : ""));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void ConfigureModelImport(ModelImporter importer,
                                                 Dictionary<string, Material> materials)
        {
            importer.importCameras = false;
            importer.importLights  = false;
            importer.importAnimation = false;
            // The exporter writes deliberate child order (NN_Rise ascending);
            // alphabetical re-sorting is harmless for the rise (it reads the
            // number out of the name) but makes the hierarchy harder to read.
            importer.sortHierarchyByName = false;
            // Keep every authored node: preserveHierarchy off lets Unity strip
            // a single-child root, which would eat the Lv0 / culture empties.
            importer.preserveHierarchy = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            // InPrefab, not External: External makes Unity extract a .mat per
            // FBX material into the building's folder, which would scatter
            // duplicate atlas materials across five directories and defeat the
            // point of sharing them. The remaps below override the embedded
            // materials with the shared assets.
            importer.materialLocation   = ModelImporterMaterialLocation.InPrefab;

            // Bind each embedded FBX material to the shared atlas asset.
            foreach (var kvp in materials)
            {
                importer.AddRemap(
                    new AssetImporter.SourceAssetIdentifier(typeof(Material), kvp.Key),
                    kvp.Value);
            }

            importer.SaveAndReimport();
        }

        private static Transform FindLv0Container(Transform root)
        {
            // Same rule as BuildingVariantVisual.FindContainerWithLv0.
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name.Equals("Lv0", System.StringComparison.OrdinalIgnoreCase))
                    return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                for (int c = 0; c < child.childCount; c++)
                    if (child.GetChild(c).name.Equals("Lv0", System.StringComparison.OrdinalIgnoreCase))
                        return child;
            }
            return null;
        }

        private static int CountAbGroups(Transform root)
        {
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.Length > 2 && t.name.EndsWith("_A", System.StringComparison.Ordinal))
                    n++;
            return n;
        }

        private static void AssignToSo(BuildingArt art, GameObject prefab,
                                       System.Text.StringBuilder log)
        {
            var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(art.SoPath);
            if (so == null)
            {
                log.AppendLine($"  {art.Name}: NO SO at {art.SoPath} — prefab built but unwired");
                return;
            }

            var serialized = new SerializedObject(so);
            serialized.FindProperty("prefab").objectReferenceValue = prefab;

            // Only stamp the id when the SO has none; an existing non-zero id
            // is the live contract between the ECS factory and the catalog and
            // must not be rewritten from this table.
            var pid = serialized.FindProperty("presentationId");
            if (pid.intValue == 0) pid.intValue = art.PresentationId;
            else if (pid.intValue != art.PresentationId)
                log.AppendLine($"  {art.Name}: SO presentationId {pid.intValue} " +
                               $"differs from expected {art.PresentationId} — left as authored");

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(so);
        }
    }
}

#endif // UNITY_EDITOR
