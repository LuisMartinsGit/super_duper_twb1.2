// PrefabDumpTool.cs
// Writes the full hierarchy (name, local position / rotation / scale, mesh and
// material names) of every prefab or model listed one per line in
// Temp/dump_list.txt to Temp/prefab_dump.txt — so an authoring pass can read
// a prefab's real layout (nested instances included) instead of guessing it
// from the YAML.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class PrefabDumpTool
    {
        [MenuItem("Waning Border/Art/Dump Prefab Hierarchies")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            foreach (var raw in File.ReadAllLines("Temp/dump_list.txt"))
            {
                string path = raw.Trim();
                if (path.Length == 0) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine("MISSING " + path); continue; }
                sb.AppendLine("=== " + path);
                var rs = prefab.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    sb.AppendLine($"bounds size {b.size.x:F2} {b.size.y:F2} {b.size.z:F2} min {b.min.x:F2} {b.min.y:F2} {b.min.z:F2} max {b.max.x:F2} {b.max.y:F2} {b.max.z:F2}");
                }
                Walk(prefab.transform, 0, sb);
            }
            File.WriteAllText("Temp/prefab_dump.txt", sb.ToString());
            Debug.Log("[PrefabDumpTool] wrote Temp/prefab_dump.txt");
        }

        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<Renderer>();
            string mesh = mf != null && mf.sharedMesh != null ? " mesh=" + mf.sharedMesh.name : "";
            string mats = "";
            if (mr != null)
            {
                var ms = mr.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) mats += (i == 0 ? " mats=" : ",") + (ms[i] != null ? ms[i].name : "null");
            }
            var p = t.localPosition; var e = t.localEulerAngles; var s = t.localScale;
            string xf = $" p=({p.x:F2},{p.y:F2},{p.z:F2}) r=({e.x:F0},{e.y:F0},{e.z:F0}) s=({s.x:F2},{s.y:F2},{s.z:F2})";
            sb.AppendLine(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [off]") + xf + mesh + mats);
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }
    }
}
