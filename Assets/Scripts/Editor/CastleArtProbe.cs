// CastleArtProbe.cs
// Measures the Synty castle pieces the castle authoring uses: renderer bounds
// relative to each prefab's pivot, written to Temp/castle_bounds.txt. A
// measuring tool for CastleArtAuthor, so pieces are placed by their real
// sizes rather than guessed ones.

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class CastleArtProbe
    {
        const string Root = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";

        static readonly string[] Pieces =
        {
            "Castle/SM_Bld_Castle_Wall_01", "Castle/SM_Bld_Castle_Wall_02", "Castle/SM_Bld_Castle_Wall_03",
            "Castle/SM_Bld_Castle_Wall_Block_01", "Castle/SM_Bld_Castle_Wall_Bottom_01",
            "Castle/SM_Bld_Castle_Battlements_01", "Castle/SM_Bld_Castle_Battlements_02",
            "Castle/SM_Bld_Castle_Battlements_Half_01", "Castle/SM_Bld_Castle_Battlements_S_Corner_01",
            "Castle/SM_Bld_Castle_Hoarding_01", "Castle/SM_Bld_Castle_Hoarding_Half_01",
            "Castle/SM_Bld_Castle_Wall_Tower_S_01", "Castle/SM_Bld_Castle_Wall_Tower_M_01",
            "Castle/SM_Bld_Castle_Wall_Tower_L_01",
            "Castle/SM_Bld_Castle_Wall_Gate_01", "Castle/SM_Bld_Castle_Wall_Gate_L_01",
            "Castle/SM_Bld_Castle_Wall_Archway_01", "Castle/SM_Bld_Castle_Wall_Door_Big_01",
            "Castle/SM_Bld_Castle_Roof_Cap_Round_01", "Castle/SM_Bld_Castle_Roof_Cap_Round_05",
            "Castle/SM_Bld_Castle_Roof_Cap_Square_01",
            "Castle/SM_Bld_Keep_Pillar_01", "Castle/SM_Bld_Keep_Pillar_02", "Castle/SM_Bld_Keep_Pillar_Cap_01",
            "Castle/SM_Bld_Castle_Wall_Corbels_01", "Castle/SM_Bld_Castle_Wall_Arrowslit_01",
            "Castle/SM_Bld_Castle_Wood_Floor_01", "Castle/SM_Bld_Castle_Wall_Panel_Wood_01",
            "Buildings/House/SM_Bld_House_Tower_Stone_Small_01", "Buildings/House/SM_Bld_House_Tower_Stone_01",
            "Buildings/House/SM_Bld_House_Tower_Stone_02", "Buildings/Presets/SM_Bld_Preset_Tower_01_Optimized",
            "Buildings/SM_Bld_Wooden_Tower_01",
            "Props/Banners/SM_Prop_Flag_01", "Props/Banners/SM_Prop_Banner_01", "Props/Banners/SM_Prop_Flag_Pole_01",
            "Props/Banners/SM_Prop_Battle_Banner_01",
            "Props/SM_Prop_Torch_01", "Props/SM_Prop_Bracket_Lamp_01",
            "SiegeEngines/SM_Wep_Ballista_Mounted_01", "SiegeEngines/SM_Wep_Trebuchet_01",
        };

        [MenuItem("Waning Border/Art/Probe Castle Piece Bounds")]
        public static void Probe()
        {
            var sb = new StringBuilder();
            sb.AppendLine("piece | size x y z | centre x y z (relative to pivot) | children");
            foreach (var p in Pieces)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + p + ".prefab");
                if (prefab == null) { sb.AppendLine($"{p} | MISSING"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = Vector3.zero;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { sb.AppendLine($"{p} | no renderers"); Object.DestroyImmediate(go); continue; }
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var kids = new StringBuilder();
                foreach (Transform c in go.transform) kids.Append(c.name).Append(',');
                sb.AppendLine($"{p} | {b.size.x:F2} {b.size.y:F2} {b.size.z:F2} | {b.center.x:F2} {b.center.y:F2} {b.center.z:F2} | {kids}");
                Object.DestroyImmediate(go);
            }
            File.WriteAllText("Temp/castle_bounds.txt", sb.ToString());
            Debug.Log("[CastleArtProbe] wrote Temp/castle_bounds.txt");
        }
    }
}
