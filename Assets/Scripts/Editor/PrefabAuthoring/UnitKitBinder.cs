// The Alanthor UNIT KIT (2026-11-11): procedural, unit-scale pieces — thin
// kawara roofs with small tiles, slender marble columns, a battered stone
// base, thin plaster walls — written by
// tools/blender/alanthor/build_alanthor_unit_kit.py into
// Civs/Alanthor/Buildings/Art/UnitKit/ as one FBX per piece, UK_Atlas.png and
// uk_manifest.json.
//
//   Waning Border > Art > Bind Unit Kit
//
// makes the three materials and one prefab beside each FBX, by slot NAME from
// the manifest (an FBX carries geometry, UVs and material names only):
//   Atlas -> UK_Atlas.mat             URP Lit over UK_Atlas.png (U repeats).
//   Band  -> UK_Band_Playercolor.mat  the player colour. BuildingFactionColorMarker
//                                     paints any material named *playercolor* the
//                                     faction colour, and a model that HAS such a
//                                     slot takes the colour nowhere else — so the
//                                     slate-blue roof tiles are never hue-swapped.
//   Glow  -> UK_Glow.mat              lantern panes, emissive at EmissiveLadder.Window.
// Re-run after every re-export. No piece is named *roof* (the marker would
// paint it slate): the roofs are "Kawara".

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class UnitKitBinder
    {
        public const string Dir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/UnitKit";
        const string AtlasPath = Dir + "/UK_Atlas.png";
        public const string AtlasMatPath = Dir + "/UK_Atlas.mat";
        public const string BandMatPath = Dir + "/UK_Band_Playercolor.mat";
        public const string GlowMatPath = Dir + "/UK_Glow.mat";
        public const string ManifestPath = Dir + "/uk_manifest.json";

        public static string PiecePath(string piece) => $"{Dir}/{piece}.prefab";

        /// <summary>piece -> (slots, roof planes), read from the manifest.</summary>
        public static Dictionary<string, (string[] slots, int planes)> ReadManifest(out float eaveEdge, out Vector2 tile)
        {
            var result = new Dictionary<string, (string[], int)>();
            eaveEdge = 0f; tile = Vector2.zero;
            if (!File.Exists(ManifestPath)) return result;
            string text = File.ReadAllText(ManifestPath);
            var e = Regex.Match(text, "\"eave_edge\"\\s*:\\s*([0-9.]+)");
            if (e.Success) eaveEdge = float.Parse(e.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var t = Regex.Match(text, "\"tile\"\\s*:\\s*\\[\\s*([0-9.]+)\\s*,\\s*([0-9.]+)");
            if (t.Success) tile = new Vector2(float.Parse(t.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(t.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            foreach (Match m in Regex.Matches(text, "\"(UK_[A-Za-z0-9_]+)\"\\s*:\\s*\\{\\s*\"slots\"\\s*:\\s*\\[([^\\]]*)\\](.*?)\"planes\"\\s*:\\s*(\\d+)", RegexOptions.Singleline))
            {
                var slots = new List<string>();
                foreach (Match s in Regex.Matches(m.Groups[2].Value, "\"(\\w+)\"")) slots.Add(s.Groups[1].Value);
                result[m.Groups[1].Value] = (slots.ToArray(), int.Parse(m.Groups[4].Value));
            }
            return result;
        }

        [MenuItem("Waning Border/Art/Bind Unit Kit")]
        public static void Bind()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh();

            if (AssetImporter.GetAtPath(AtlasPath) is TextureImporter ti)
            {
                bool dirty = false;
                if (ti.wrapModeU != TextureWrapMode.Repeat) { ti.wrapModeU = TextureWrapMode.Repeat; dirty = true; }
                if (ti.wrapModeV != TextureWrapMode.Clamp) { ti.wrapModeV = TextureWrapMode.Clamp; dirty = true; }
                if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (ti.anisoLevel != 4) { ti.anisoLevel = 4; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            if (atlas == null) { Debug.LogError($"[UnitKit] no atlas at {AtlasPath} - run the Blender script first"); return; }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) { Debug.LogError("[UnitKit] URP Lit not found"); return; }

            Material Mat(string path, string name)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(lit) { name = name }; AssetDatabase.CreateAsset(m, path); log.AppendLine("created " + name); }
                return m;
            }
            var atlasMat = Mat(AtlasMatPath, "UK_Atlas");
            atlasMat.SetTexture("_BaseMap", atlas); atlasMat.SetColor("_BaseColor", Color.white);
            atlasMat.SetFloat("_Smoothness", 0.12f); atlasMat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(atlasMat);

            var band = Mat(BandMatPath, "UK_Band_Playercolor");
            band.SetTexture("_BaseMap", null);
            band.SetColor("_BaseColor", TheWaningBorder.Rendering.BuildingFactionColorMarker.Marker);
            band.SetFloat("_Smoothness", 0.08f); band.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(band);

            var glow = Mat(GlowMatPath, "UK_Glow");
            glow.SetTexture("_BaseMap", null);
            glow.SetColor("_BaseColor", new Color(0.98f, 0.86f, 0.60f));
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", TheWaningBorder.Rendering.EmissiveLadder.Window);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(glow);

            var slotMat = new Dictionary<string, Material> { { "Atlas", atlasMat }, { "Band", band }, { "Glow", glow } };
            var manifest = ReadManifest(out _, out _);
            int made = 0;
            foreach (var kv in manifest)
            {
                string piece = kv.Key; var slots = kv.Value.slots;
                string fbx = $"{Dir}/{piece}.fbx";
                if (AssetImporter.GetAtPath(fbx) is ModelImporter mi)
                {
                    bool dirty = false;
                    if (mi.importCameras) { mi.importCameras = false; dirty = true; }
                    if (mi.importLights) { mi.importLights = false; dirty = true; }
                    if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                    if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
                    if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
                    if (dirty) mi.SaveAndReimport();
                }
                Mesh mesh = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Mesh me) { mesh = me; break; }
                if (mesh == null) { log.AppendLine($"FAIL {piece}: no mesh in {fbx}"); continue; }
                if (mesh.subMeshCount != slots.Length) { log.AppendLine($"FAIL {piece}: {mesh.subMeshCount} submeshes, manifest lists {slots.Length}"); continue; }
                var mats = new Material[slots.Length];
                for (int i = 0; i < slots.Length; i++) mats[i] = slotMat[slots[i]];
                var go = new GameObject(piece);
                try
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    PrefabUtility.SaveAsPrefabAsset(go, PiecePath(piece));
                }
                finally { Object.DestroyImmediate(go); }
                made++;
                var b = mesh.bounds;
                log.AppendLine($"OK {piece}: [{string.Join(", ", slots)}] min {b.min.ToString("F2")} max {b.max.ToString("F2")}");
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"-- {made} prefabs -> {Dir}");
            string outDir = System.Environment.GetEnvironmentVariable("TWB_OUT") is { Length: > 0 } s ? s : "Temp";
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "unit-kit-binder.txt"), log.ToString());
            Debug.Log("[UnitKit]\n" + log);
        }
    }
}
