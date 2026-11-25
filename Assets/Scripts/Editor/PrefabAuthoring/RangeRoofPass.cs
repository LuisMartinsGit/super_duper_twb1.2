// Archery Range roofs wear the Garrison's materials (2026-11-25): the same
// tile, and the gold that carries the baked glow (GoldGlowPass). Dobei walls
// and the tiled torii are not NAMED roof, so the game does not slate them
// (BuildingFactionColorMarker rule 3); their roof slots, the Garrison_3 gate's
// included, wear Roof_Gold_04_Slate, which carries that tint itself.
//   Waning Border > Art > Garrison + Range > Harmonize Range Roofs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class RangeRoofPass
    {
        const string R = "Assets/Synty/PolygonSamuraiEmpire/Materials/Building/";

        [MenuItem("Waning Border/Art/Garrison + Range/Harmonize Range Roofs")]
        public static void Run()
        {
            var plain = AssetDatabase.LoadAssetAtPath<Material>(R + "Roof_04.mat");
            var gold = AssetDatabase.LoadAssetAtPath<Material>(R + "Roof_Gold_04.mat");
            var trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synty/PolygonSamuraiEmpire/Materials/Alts/PolygonSamuraiEmpire_02_C.mat");
            var log = new StringBuilder();
            const string B = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/";
            var slate = AssetDatabase.LoadAssetAtPath<Material>(B + "Art/Roof_Gold_04_Slate.mat");
            if (slate == null) { slate = new Material(gold); AssetDatabase.CreateAsset(slate, B + "Art/Roof_Gold_04_Slate.mat"); }
            slate.CopyPropertiesFromMaterial(gold);
            slate.SetColor("_BaseColor", TheWaningBorder.Rendering.BuildingFactionColorMarker.RoofSlate);
            EditorUtility.SetDirty(slate);
            for (int k = 1; k <= 4; k++)
            {
                int n = k == 4 ? 3 : k;
                string path = k == 4 ? B + "Garrison/Garrison_3.prefab" : B + "ArcheryRange/ArcheryRange_" + n + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int swapped = 0;
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var m = r.sharedMaterials; bool hit = false;
                        for (int i = 0; i < m.Length; i++)
                        {
                            if (m[i] == null) continue;
                            string s = m[i].name;
                            Material to = null;
                            bool untinted = r.name.IndexOf("roof", System.StringComparison.OrdinalIgnoreCase) < 0;
                            if (s.StartsWith("Roof_") && untinted && (r.name.Contains("Dobei") || r.name.Contains("Torii"))) to = slate;
                            else if (k == 4) continue;
                            else if (s.StartsWith("Roof_Gold_")) to = gold;
                            else if (s.StartsWith("Roof_0")) to = plain;
                            else if (n > 1 && r.name.Contains("Roof_") && s.StartsWith("PolygonSamuraiEmpire_0")) to = trim;
                            if (to == null || to == m[i]) continue;
                            m[i] = to; hit = true; swapped++;
                        }
                        if (hit) r.sharedMaterials = m;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"{path}: {swapped} slots");
                    foreach (var g in root.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(x => x.name.Contains("Roof") || x.name.Contains("Dobei") || x.name.Contains("Torii"))
                        .GroupBy(x => x.name.Split(' ')[0] + " : " + string.Join(",", x.sharedMaterials.Select(y => y == null ? "-" : y.name))))
                        log.AppendLine("   " + g.Count() + " " + g.Key);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/range-roofs.txt", log.ToString());
            Debug.Log("[RangeRoofs]\n" + log);
        }
    }
}
