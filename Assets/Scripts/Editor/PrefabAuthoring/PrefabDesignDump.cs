// Dumps the composition of hand-authored building prefabs (2026-11-02): every
// nested kit piece with its source prefab, placement, scale and material
// overrides, plus bounds and piece / material histograms — the reference an
// author reads before composing a sibling building in the same language.
//   Waning Border > Art > Dump Reference Prefabs  ->  Temp/prefab-dump/<name>.txt

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class PrefabDesignDump
    {
        static readonly string[] Paths =
        {
            "Assets/GameData/TechTree/Age0/Buildings/Barracks/Art/Barracks_Age_0.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Fortress/Fortress 1.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Fortress/Fortress 2.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Fortress/Fortress 3.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/Tower 1.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/Tower 2.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Tower/Tower 3.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Garrison_3 1.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Garrison_3 2.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Garrison_3 3.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/ArcheryRange_3 2.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/ArcheryRange_3.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/ArcheryRange_3 1.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House 1.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House 2.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House 3.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House_1.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House_2.prefab",
            "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/Art/alanthor_House_lv3.prefab",
        };

        [MenuItem("Waning Border/Art/Dump Reference Prefabs")]
        public static void Dump()
        {
            // Unity empties Temp/ when a batch run quits, so an agent run sets TWB_OUT.
            string outRoot = System.Environment.GetEnvironmentVariable("TWB_OUT");
            string outDir = Path.Combine(string.IsNullOrEmpty(outRoot) ? "Temp" : outRoot, "prefab-dump");
            Directory.CreateDirectory(outDir);
            foreach (var path in Paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[PrefabDesignDump] missing " + path); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(path);
                    var pieces = new Dictionary<string, int>();
                    var mats = new Dictionary<string, int>();
                    Bounds all = default; bool any = false;
                    var seen = new HashSet<GameObject>();
                    int rows = 0;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == go.transform) continue;
                        var root = PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject);
                        bool isPieceRoot = root == t.gameObject;
                        bool hasRenderer = t.GetComponent<Renderer>() != null;
                        if (!isPieceRoot && !(hasRenderer && root == null)) continue;
                        if (!seen.Add(t.gameObject)) continue;
                        string src = "(loose)";
                        if (root != null)
                        {
                            var srcObj = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
                            src = srcObj != null ? srcObj.name : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                        }
                        pieces[src] = pieces.TryGetValue(src, out int c) ? c + 1 : 1;
                        var rends = t.GetComponentsInChildren<Renderer>(true);
                        Bounds pb = default; bool pany = false;
                        var matNames = new List<string>();
                        foreach (var r in rends)
                        {
                            if (!pany) { pb = r.bounds; pany = true; } else pb.Encapsulate(r.bounds);
                            foreach (var m in r.sharedMaterials)
                            {
                                string mn = m != null ? m.name : "null";
                                matNames.Add(mn);
                                mats[mn] = mats.TryGetValue(mn, out int mc) ? mc + 1 : 1;
                            }
                        }
                        if (pany) { if (!any) { all = pb; any = true; } else all.Encapsulate(pb); }
                        var lp = go.transform.InverseTransformPoint(t.position);
                        var le = (Quaternion.Inverse(go.transform.rotation) * t.rotation).eulerAngles;
                        var ls = t.lossyScale;
                        string parentPath = Parent(t, go.transform);
                        sb.AppendLine($"  {src,-44} name={t.name,-40} under={parentPath,-28} pos=({lp.x:F2},{lp.y:F2},{lp.z:F2}) rot=({le.x:F0},{le.y:F0},{le.z:F0}) scale=({ls.x:F2},{ls.y:F2},{ls.z:F2})" +
                                      (pany ? $" bounds=[{pb.min.x:F1},{pb.min.y:F1},{pb.min.z:F1}]-[{pb.max.x:F1},{pb.max.y:F1},{pb.max.z:F1}]" : "") +
                                      $" mats={string.Join("|", matNames.Distinct())}");
                        rows++;
                    }
                    sb.AppendLine($"-- {rows} pieces; bounds min=({all.min.x:F2},{all.min.y:F2},{all.min.z:F2}) max=({all.max.x:F2},{all.max.y:F2},{all.max.z:F2}) size=({all.size.x:F2},{all.size.y:F2},{all.size.z:F2}) centre=({all.center.x:F2},{all.center.z:F2})");
                    sb.AppendLine("-- pieces: " + string.Join(", ", pieces.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}")));
                    sb.AppendLine("-- materials: " + string.Join(", ", mats.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}")));
                    // Root children (the author's grouping)
                    sb.AppendLine("-- root children: " + string.Join(", ", Enumerable.Range(0, go.transform.childCount).Select(i => go.transform.GetChild(i).name)));
                    File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path).Replace(' ', '_') + ".txt"), sb.ToString());
                }
                finally { Object.DestroyImmediate(go); }
            }
            Debug.Log("[PrefabDesignDump] wrote " + outDir);
        }

        static string Parent(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var p = t.parent; p != null && p != root; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
