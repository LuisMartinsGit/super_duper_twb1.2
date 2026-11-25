// The two kinds of player-colour material the hand-built Garrison and Archery
// Range use (2026-11-25):
//   Stucco_White_Pattern_0N_Striped  the Synty stucco with a painted
//       *PlayerColorMask* (BuildingFactionColorMarker rule 0): its blue stripes
//       take the owner's colour, vertical ones included, with no mesh cut.
//   Banner_Samurai_Player            the Samurai pack's wind-driven cloth
//       (Foliage graph), neutral, tinted by rule 2 (*_Player).
//   Waning Border > Art > Garrison + Range > Build Materials
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class GarrisonRangeMaterials
    {
        const string Art = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/";
        public const string BannerMat = Art + "Banner_Samurai_Player.mat";
        public static string StuccoMat(int n) => Art + "StripeBand/Stucco_White_Pattern_0" + n + "_Striped.mat";

        [MenuItem("Waning Border/Art/Garrison + Range/Build Materials")]
        public static void Build()
        {
            for (int n = 1; n <= 2; n++)
            {
                string maskPath = Art + "StripeBand/Stucco_0" + n + "_PlayerColorMask.png";
                AssetDatabase.ImportAsset(maskPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(maskPath);
                imp.sRGBTexture = false;
                imp.SaveAndReimport();
                var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synty/PolygonAncientEmpire/Materials/BuildingAlts/Stucco_White_Pattern_0" + n + ".mat");
                var tex = TheWaningBorder.Rendering.BuildingFactionColorMarker.AlbedoOf(src, out _);
                var nrm = TheWaningBorder.Rendering.BuildingFactionColorMarker.NormalMapOf(src, out float ns);
                var mat = Asset(StuccoMat(n), Shader.Find(TheWaningBorder.Rendering.BuildingFactionColorMarker.BuildingLitShaderName));
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", ns); mat.EnableKeyword("_NORMALMAP"); }
                mat.SetFloat("_Smoothness", 0.1f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetTexture("_DetailMask", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
                EditorUtility.SetDirty(mat);
            }
            var kite = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synty/PolygonSamuraiEmpire/Materials/Misc/Kites/Kite_01_A.mat");
            var b = Asset(BannerMat, kite.shader);
            b.CopyPropertiesFromMaterial(kite);
            var tex2 = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Banner_Samurai_Player.png");
            b.SetTexture("_Leaf_Texture", tex2);
            b.SetTexture("_Trunk_Texture", tex2);
            b.SetFloat("_Use_Color_Noise", 1f);      // the switch that lets the base colours multiply the texture
            TheWaningBorder.Rendering.BuildingFactionColorMarker.TintFoliage(b, Color.white);
            EditorUtility.SetDirty(b);
            AssetDatabase.SaveAssets();
            Debug.Log("[GarrisonRange] materials built");
        }

        static Material Asset(string path, Shader s)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
            m.shader = s;
            return m;
        }
    }
}
