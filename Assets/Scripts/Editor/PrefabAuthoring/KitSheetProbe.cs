// KitSheetProbe.cs
// Contact sheets of Synty pieces, for choosing what a unit is built from.
// Each line of Temp/kit-sheets.txt is one sheet:
//   <out png>|<yaw>|<columns>|<cell px>|<prefab or folder prefix>;<prefab>;...
// An entry ending in * lists every prefab whose path starts with it. The order
// is written to <out png>.txt (row by row).
//   Waning Border > Art > Units > Render Kit Sheets

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class KitSheetProbe
    {
        [MenuItem("Waning Border/Art/Units/Render Kit Sheets")]
        public static void Run()
        {
            foreach (var raw in File.ReadAllLines("Temp/kit-sheets.txt"))
            {
                var p = raw.Trim().Split('|');
                if (p.Length < 5) continue;
                float yaw = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                int cols = int.Parse(p[2]), cell = int.Parse(p[3]);
                var paths = new List<string>();
                foreach (var e in p[4].Split(';'))
                {
                    if (!e.EndsWith("*")) { paths.Add(e); continue; }
                    string prefix = e.TrimEnd('*'), dir = prefix.Substring(0, prefix.LastIndexOf('/'));
                    paths.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { dir })
                        .Select(AssetDatabase.GUIDToAssetPath).Where(x => x.StartsWith(prefix)).Distinct().OrderBy(x => x));
                }
                var stage = new GameObject("KitSheet");
                var sb = new StringBuilder();
                try
                {
                    int rows = (paths.Count + cols - 1) / cols;
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    for (int i = 0; i < paths.Count; i++)
                    {
                        var a = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                        if (a == null) { sb.AppendLine($"{i} MISSING {paths[i]}"); continue; }
                        var go = Object.Instantiate(a, stage.transform);
                        var b = LegionaryShot.BoundsOf(go);
                        // Bodies are drawn on one scale, pieces fitted to their cell.
                        float k = 0.9f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                        if (p.Length > 5) k = float.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture);
                        go.transform.localScale *= k;
                        b = LegionaryShot.BoundsOf(go);
                        Vector3 at = rot * new Vector3(i % cols, -(i / cols), 0f);
                        go.transform.position += at - b.center;
                        sb.AppendLine($"r{i / cols + 1}c{i % cols + 1} {Path.GetFileNameWithoutExtension(paths[i])} size {b.size / k}");
                    }
                    var c = rot * new Vector3((cols - 1) * 0.5f, -(rows - 1) * 0.5f, 0f);
                    var camB = new Bounds(c, rot * new Vector3(cols, rows / 1.1f, 0f));
                    camB.size = new Vector3(Mathf.Abs(camB.size.x), Mathf.Abs(camB.size.y), Mathf.Abs(camB.size.z));
                    LegionaryShot.Shoot(camB, yaw, 0f, 64, 64, "Temp/kit_warm.png", true);
                    LegionaryShot.Shoot(camB, yaw, 0f, cols * cell, rows * cell, p[0], true);
                }
                finally { Object.DestroyImmediate(stage); }
                File.WriteAllText(p[0] + ".txt", sb.ToString());
            }
        }
    }
}
