// GoldGlowPass.cs
// Gold trim that reads at RTS zoom (docs/Design/Art_Direction.md, gold glow).
// The Synty roof and prop textures paint gold as matte colour with no highlight,
// so this bakes an emission map from the GOLD TEXELS of each albedo and puts it
// on the material; everything else on the texture stays unlit by it. A material
// that already glowed keeps its own emission, added to the gold.
// Re-runnable: it always starts from the albedo and the vendor emission.
// Assets/Synty is not in git, so run it again after re-importing the packs.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class GoldGlowPass
    {
        const string OutDir = "Assets/GameData/Art/GoldGlow";
        const string Mats = "Assets/Synty/PolygonSamuraiEmpire/Materials/";

        // material, guid of the vendor emission it glows with (null: none)
        static readonly (string path, string emission)[] Targets =
        {
            (Mats + "Building/Roof_Gold_04.mat", null),
            (Mats + "Alts/PolygonSamuraiEmpire_02_C.mat", null),
            (Mats + "Alts/PolygonSamuraiEmpire_01_A.mat", "e7206ae774a79824c86178ecb6ac3a70"),
        };

        // Linear emission as a multiple of the gold texel.
        static readonly Color Tint = new Color(1.1f, 0.85f, 0.45f);

        [MenuItem("Waning Border/Art/Bake Gold Glow")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var (path, emission) in Targets)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { Debug.LogError("[GoldGlow] missing " + path); continue; }
                var albedo = Read(mat.GetTexture("_Albedo_Map"), out int w, out int h);
                var vendor = emission == null ? null : Read(
                    AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(emission)), w, h);

                var px = new Color[albedo.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    Color.RGBToHSV(albedo[i], out float hue, out float sat, out float val);
                    float k = hue > 0.06f && hue < 0.17f
                        ? Mathf.InverseLerp(0.35f, 0.55f, sat) * Mathf.InverseLerp(0.35f, 0.55f, val) : 0f;
                    Color e = (albedo[i] * k).linear * Tint;
                    if (vendor != null) e += vendor[i].linear;
                    e.a = 1f;
                    px[i] = e.gamma;
                }

                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.SetPixels(px);
                string png = OutDir + "/" + mat.name + "_GoldGlow.png";
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(png);

                mat.SetTexture("_Emission_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(png));
                mat.SetFloat("_Enable_Emission", 1f);
                mat.SetColor("_Emission_Color", Color.white);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
                Debug.Log("[GoldGlow] " + mat.name + " <- " + png);
            }
        }

        static Color[] Read(Texture src, out int w, out int h)
        {
            w = Mathf.Min(src.width, 2048);
            h = Mathf.Min(src.height, 2048);
            return Read(src, w, h);
        }

        static Color[] Read(Texture src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels();
            Object.DestroyImmediate(t);
            return px;
        }
    }
}
