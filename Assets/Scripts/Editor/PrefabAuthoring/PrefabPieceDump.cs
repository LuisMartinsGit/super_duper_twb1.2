// PrefabPieceDump.cs
// One line per PIECE of a composed building prefab - each outermost nested
// prefab instance (or bare renderer) under the root: its group, source asset,
// position / yaw / scale in the root's frame (root at scale 1) and its
// bounds. For planning additions to a hand-authored prefab without opening it.
// Paths come from Temp/piece-dump.txt (one asset path a line); output goes to
// Temp/piece-dump/<prefab name>.txt.
//   Waning Border > Art > Dump Prefab Pieces

using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class PrefabPieceDump
    {
        [MenuItem("Waning Border/Art/Dump Prefab Pieces")]
        public static void Run()
        {
            const string jobs = "Temp/piece-dump.txt";
            if (!File.Exists(jobs)) { Debug.LogError("[PieceDump] no job file at " + jobs); return; }
            Directory.CreateDirectory("Temp/piece-dump");
            foreach (var raw in File.ReadAllLines(jobs))
            {
                var path = raw.Trim();
                if (path.Length == 0 || path.StartsWith("#")) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.transform.localScale = Vector3.one;
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var sb = new StringBuilder();
                    Walk(root.transform, root.transform, "", sb);
                    File.WriteAllText("Temp/piece-dump/" + Path.GetFileNameWithoutExtension(path) + ".txt", sb.ToString());
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("[PieceDump] done");
        }

        static void Walk(Transform t, Transform root, string group, StringBuilder sb)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                bool isInstance = PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject);
                bool hasRenderer = c.GetComponent<Renderer>() != null;
                if (isInstance || hasRenderer || c.childCount == 0)
                {
                    string src = isInstance ? Path.GetFileNameWithoutExtension(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(c.gameObject)) : "-";
                    var p = c.position; var e = c.eulerAngles; var s = c.lossyScale;
                    string line = string.Format(CultureInfo.InvariantCulture,
                        "{0} | {1} | src {2} | pos {3:F2} {4:F2} {5:F2} | rot {6:F0} {7:F0} {8:F0} | scale {9:F2} {10:F2} {11:F2}",
                        group, c.name, src, p.x, p.y, p.z, e.x, e.y, e.z, s.x, s.y, s.z);
                    bool any = false; Bounds b = default;
                    foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r is ParticleSystemRenderer) continue;
                        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    }
                    if (any) line += string.Format(CultureInfo.InvariantCulture, " | x {0:F2}..{1:F2} y {2:F2}..{3:F2} z {4:F2}..{5:F2}", b.min.x, b.max.x, b.min.y, b.max.y, b.min.z, b.max.z);
                    if (!c.gameObject.activeInHierarchy) line += " | INACTIVE";
                    sb.AppendLine(line);
                }
                else Walk(c, root, group.Length == 0 ? c.name : group + "/" + c.name, sb);
            }
        }
    }
}
