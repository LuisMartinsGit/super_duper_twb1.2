// HouseMaterialAuthor.cs
// Rebuilds the house's materials from its per-part texture folders and binds
// them to House_test_project.fbx through the importer's material remap (the
// FBX carries only material NAMES — the textures live in .mat assets bound by
// name, and a re-export wipes the .fbx.meta, so this is re-runnable).
//
// Each FBX material maps to one folder under Hut/Textures/ holding
//   <Part>_texture_{AlbedoTransparency,MetallicSmoothness,Normal,AO,Height}.png
// plus optionally _Emission and _PlayerColorMask. The materials are URP Lit,
// metallic workflow (MetallicSmoothness: metal in R, smoothness in A), double
// sided, with the height map in the parallax slot. The player-colour mask goes
// in _DetailMask, where BuildingFactionColorMarker rule 0 picks it up by name.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class HouseMaterialAuthor
    {
        const string Root = "Assets/GameData/TechTree/Age0/Buildings/Hut/";
        const string Model = Root + "House_test_project.fbx";
        const string TexRoot = Root + "Textures/";
        const string MatRoot = Root + "Materials/";
        const string LitShader = "Universal Render Pipeline/Lit";

        /// <summary>FBX material name → texture folder, file prefix, parallax strength.</summary>
        static readonly (string fbx, string folder, string prefix, float parallax)[] Parts =
        {
            ("Base_texture", "Foundation (Base)", "Foundation", 0.005f),
            ("Buissons_texture", "Bushes (Buissons)", "Bushes", 0.005f),
            ("Chambranle_fenetres_et_lumiere_texture",
             "Window_Frames_and_Light (Chambranle_fenetres_et_lumiere)", "Window_Frames_and_Light", 0.005f),
            ("Lanterne_texture", "Lantern (Lanterne)", "Lantern", 0.005f),
            ("Muret_texture", "Low_Wall (Muret)", "Low_Wall", 0.005f),
            ("Murs_texture", "Wall (Murs)", "Wall", 0.005f),
            ("Piliers_texture", "Pillars (Piliers)", "Pillars", 0.005f),
            ("Porte_et_lumiere_porte_texture", "Door_and_light_door(Porte_et_lumiere_porte)", "Door_and_light_door", 0.005f),
            ("Toit_texture", "Roof (Toit)", "Roof", 0.04f),
        };

        [MenuItem("Waning Border/Art/Rebuild House Materials")]
        public static void Rebuild()
        {
            if (!AssetDatabase.IsValidFolder(MatRoot.TrimEnd('/')))
                AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Materials");

            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var report = new System.Text.StringBuilder();
            foreach (var p in Parts)
            {
                string dir = TexRoot + p.folder + "/" + p.prefix + "_texture_";
                var albedo = Tex(dir + "AlbedoTransparency.png", srgb: true, readable: true);
                var gloss = Tex(dir + "MetallicSmoothness.png", srgb: false);
                var normal = Tex(dir + "Normal.png", srgb: false, normal: true);
                var ao = Tex(dir + "AO.png", srgb: false);
                var height = Tex(dir + "Height.png", srgb: false);
                var emission = Tex(dir + "Emission.png", srgb: true);
                var mask = Tex(dir + "PlayerColorMask.png", srgb: false, readable: true);

                string matPath = MatRoot + p.fbx + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(Shader.Find(LitShader)) { name = p.fbx };
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else mat.shader = Shader.Find(LitShader);

                mat.SetFloat("_WorkflowMode", 1f);          // metallic
                mat.SetFloat("_Cull", 0f);                  // double sided, as authored
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetColor("_BaseColor", Color.white);
                Bind(mat, "_BaseMap", albedo); Bind(mat, "_MainTex", albedo);
                Bind(mat, "_MetallicGlossMap", gloss); mat.SetFloat("_Metallic", 0f);
                Bind(mat, "_BumpMap", normal); mat.SetFloat("_BumpScale", 1f);
                Bind(mat, "_OcclusionMap", ao); mat.SetFloat("_OcclusionStrength", 1f);
                Bind(mat, "_ParallaxMap", height); mat.SetFloat("_Parallax", p.parallax);
                Bind(mat, "_DetailMask", mask);
                Bind(mat, "_EmissionMap", emission);
                mat.SetColor("_EmissionColor", emission != null ? Color.white : Color.black);
                mat.globalIlluminationFlags = emission != null
                    ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                    : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

                Keyword(mat, "_SPECULAR_SETUP", false);
                Keyword(mat, "_METALLICSPECGLOSSMAP", gloss != null);
                Keyword(mat, "_NORMALMAP", normal != null);
                Keyword(mat, "_OCCLUSIONMAP", ao != null);
                Keyword(mat, "_PARALLAXMAP", height != null);
                Keyword(mat, "_EMISSION", emission != null);
                EditorUtility.SetDirty(mat);

                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), p.fbx), mat);
                report.AppendLine($"{p.fbx}: albedo {Ok(albedo)} gloss {Ok(gloss)} normal {Ok(normal)} " +
                                  $"AO {Ok(ao)} height {Ok(height)} emission {Ok(emission)} mask {Ok(mask)}");
            }
            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();
            Debug.Log("[HouseMaterialAuthor] rebuilt house materials\n" + report);
        }

        /// <summary>Mesh density per material — vertices, triangles and the
        /// mesh's size — to Temp/house_mesh.txt.</summary>
        [MenuItem("Waning Border/Art/Probe House Mesh")]
        public static void ProbeMesh()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Model))
            {
                if (!(o is Mesh m)) continue;
                sb.AppendLine($"mesh {m.name}: {m.vertexCount} verts, size {m.bounds.size}");
                var mr = FindRenderer(m);
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var d = m.GetSubMesh(s);
                    string matName = mr != null && s < mr.sharedMaterials.Length && mr.sharedMaterials[s] != null
                        ? mr.sharedMaterials[s].name : "?";
                    sb.AppendLine($"  sub {s} {matName}: {d.indexCount / 3} tris, {d.vertexCount} verts");
                }
            }
            File.WriteAllText("Temp/house_mesh.txt", sb.ToString());
        }

        static Renderer FindRenderer(Mesh m)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh == m) return mf.GetComponent<Renderer>();
            return null;
        }

        static string Ok(Texture t) => t != null ? "ok" : "-";

        static Texture2D Tex(string path, bool srgb, bool normal = false, bool readable = false)
        {
            if (!File.Exists(path)) return null;
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.sRGBTexture != srgb || ti.isReadable != readable)
            {
                ti.textureType = type;
                ti.sRGBTexture = srgb;
                ti.isReadable = readable;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void Bind(Material m, string prop, Texture t)
        {
            if (m.HasProperty(prop)) m.SetTexture(prop, t);
        }

        static void Keyword(Material m, string k, bool on)
        {
            if (on) m.EnableKeyword(k); else m.DisableKeyword(k);
        }
    }
}
