// The Alanthor oriental kit (2026-11-10): Japanese roof pieces and red lacquer
// columns re-shaped from Synty's castle kit by
// tools/blender/alanthor/build_oriental_kit.py, which writes the FBX files,
// Al_Lacquer_Palette.png and kit_manifest.json into
// Civs/Alanthor/Buildings/Art/Oriental/.
//
//   Waning Border > Art > Bind Oriental Kit
//
// makes one prefab beside each FBX with its materials on, by slot NAME from the
// manifest - an FBX carries geometry, UVs and material names only, and its
// .fbx.meta is not versioned, so the prefab is what holds the binding:
//   Tile    -> Castle_Roof_Player (takes the player colour)
//   Wall    -> Synty's Castle_Wall_01 (override per building, as on the kit's own walls)
//   Lacquer -> Al_Lacquer (the palette: lacquer red, gilt, black, plaster)
//   Atlas   -> Synty's atlas material
//   Paper   -> Al_Shoji_Paper (the shoji panes: the palette's paper colour,
//              glowing at EmissiveLadder.Window so they light up at dusk)
// Re-run after every re-export. A piece's GameObject is never named *roof*:
// BuildingFactionColorMarker paints every non-tile slot of such a node slate.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class OrientalKitBinder
    {
        public const string Dir = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Oriental";
        const string TilePath = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Castle_Roof_Player.mat";
        const string WallPath = "Assets/Synty/PolygonFantasyKingdom/Materials/Walls/Castle_Wall_01.mat";
        const string AtlasPath = "Assets/Synty/PolygonFantasyKingdom/Materials/Alts/PolygonFantasyKingdom_Mat_01_A.mat";
        const string LacquerSource = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Castle_Wall_golden.mat";
        public const string LacquerPath = Dir + "/Al_Lacquer.mat";
        const string PalettePath = Dir + "/Al_Lacquer_Palette.png";
        public const string PaperPath = Dir + "/Al_Shoji_Paper.mat";

        public static string PiecePath(string piece) => $"{Dir}/{piece}.prefab";

        [MenuItem("Waning Border/Art/Bind Oriental Kit")]
        public static void Bind()
        {
            var log = new StringBuilder();
            AssetDatabase.Refresh();

            if (AssetImporter.GetAtPath(PalettePath) is TextureImporter ti)
            {
                bool dirty = false;
                if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; dirty = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            if (palette == null) { Debug.LogError($"[OrientalKit] no palette at {PalettePath} - run the Blender script first"); return; }

            var lacquer = AssetDatabase.LoadAssetAtPath<Material>(LacquerPath);
            if (lacquer == null)
            {
                lacquer = new Material(AssetDatabase.LoadAssetAtPath<Material>(LacquerSource)) { name = "Al_Lacquer" };
                AssetDatabase.CreateAsset(lacquer, LacquerPath);
                log.AppendLine("created Al_Lacquer.mat");
            }
            lacquer.SetTexture("_Albedo_Map", palette);
            if (lacquer.HasProperty("_Normal_Map")) lacquer.SetTexture("_Normal_Map", null);
            if (lacquer.HasProperty("_BaseColor")) lacquer.SetColor("_BaseColor", Color.white);
            if (lacquer.HasProperty("_Smoothness")) lacquer.SetFloat("_Smoothness", 0.55f);
            if (lacquer.HasProperty("_Glossiness")) lacquer.SetFloat("_Glossiness", 0.55f);
            EditorUtility.SetDirty(lacquer);

            var paper = AssetDatabase.LoadAssetAtPath<Material>(PaperPath);
            if (paper == null)
            {
                paper = new Material(lacquer) { name = "Al_Shoji_Paper" };
                AssetDatabase.CreateAsset(paper, PaperPath);
                log.AppendLine("created Al_Shoji_Paper.mat");
            }
            paper.SetTexture("_Albedo_Map", palette);
            if (paper.HasProperty("_Smoothness")) paper.SetFloat("_Smoothness", 0.1f);
            if (paper.HasProperty("_Glossiness")) paper.SetFloat("_Glossiness", 0.1f);
            if (paper.HasProperty("_Enable_Emission")) paper.SetFloat("_Enable_Emission", 1f);
            if (paper.HasProperty("_Emission_Map")) paper.SetTexture("_Emission_Map", palette);
            if (paper.HasProperty("_Emission_Color")) paper.SetColor("_Emission_Color", TheWaningBorder.Rendering.EmissiveLadder.Window);
            EditorUtility.SetDirty(paper);

            var slotMat = new Dictionary<string, Material>
            {
                { "Tile", AssetDatabase.LoadAssetAtPath<Material>(TilePath) },
                { "Wall", AssetDatabase.LoadAssetAtPath<Material>(WallPath) },
                { "Atlas", AssetDatabase.LoadAssetAtPath<Material>(AtlasPath) },
                { "Lacquer", lacquer },
                { "Paper", paper },
            };
            foreach (var kv in slotMat) if (kv.Value == null) { Debug.LogError($"[OrientalKit] material for slot {kv.Key} is missing"); return; }

            string manifest = File.ReadAllText($"{Dir}/kit_manifest.json");
            int made = 0;
            foreach (Match m in Regex.Matches(manifest, "\"(SM_[A-Za-z0-9_]+)\"\\s*:\\s*\\[([^\\]]*)\\]"))
            {
                string piece = m.Groups[1].Value;
                var slots = new List<string>();
                foreach (Match s in Regex.Matches(m.Groups[2].Value, "\"(\\w+)\"")) slots.Add(s.Groups[1].Value);
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
                if (mesh.subMeshCount != slots.Count) { log.AppendLine($"FAIL {piece}: {mesh.subMeshCount} submeshes, manifest lists {slots.Count}"); continue; }

                var mats = new Material[slots.Count];
                for (int i = 0; i < slots.Count; i++) mats[i] = slotMat[slots[i]];

                string path = PiecePath(piece);
                var go = new GameObject(piece);
                try
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    PrefabUtility.SaveAsPrefabAsset(go, path);
                }
                finally { Object.DestroyImmediate(go); }
                made++;
                var b = mesh.bounds;
                log.AppendLine($"OK {piece}: [{string.Join(", ", slots)}] min {b.min} max {b.max}");
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"-- {made} prefabs -> {Dir}");
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/oriental-kit-binder.txt", log.ToString());
            Debug.Log("[OrientalKit]\n" + log);
        }
    }
}
