// KitIslandProbe.cs
// Which separate shells a Synty body is made of: one cell per shell, largest
// first, with its bounds, triangle count and colours in a text beside it.
//   Temp/kit-islands.txt: <prefab>|<out png>  per line
//   Waning Border > Art > Units > Render Kit Islands

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class KitIslandProbe
    {
        [MenuItem("Waning Border/Art/Units/Render Kit Islands")]
        public static void Run()
        {
            foreach (var raw in File.ReadAllLines("Temp/kit-islands.txt"))
            {
                var p = raw.Trim().Split('|');
                if (p.Length < 2) continue;
                var stage = new GameObject("Islands");
                var sb = new StringBuilder();
                try
                {
                    var part = KitPart.Load(p[0]);
                    var islands = part.Islands();
                    int n = Mathf.Min(islands.Count, 70), cols = 10, rows = (n + cols - 1) / cols;
                    var rot = Quaternion.Euler(0f, 205f, 0f);
                    for (int i = 0; i < islands.Count; i++)
                    {
                        var isl = islands[i];
                        var b = new Bounds(part.verts[isl[0]], Vector3.zero);
                        foreach (int v in isl) b.Encapsulate(part.verts[v]);
                        var colours = new Dictionary<int, int>();
                        foreach (int v in isl)
                        {
                            var c = part.ColourAt(part.uv[v]);
                            int key = (c.r << 16) | (c.g << 8) | c.b;
                            colours.TryGetValue(key, out int was);
                            colours[key] = was + 1;
                        }
                        string top = string.Join(" ", colours.OrderByDescending(k => k.Value).Take(5)
                            .Select(k => $"({k.Key >> 16},{(k.Key >> 8) & 255},{k.Key & 255})x{k.Value}"));
                        sb.AppendLine($"#{i} r{i / cols + 1}c{i % cols + 1} tris {isl.Count / 3} c {b.center.ToString("0.00")} s {b.size.ToString("0.00")} {top}");
                        if (i >= n) continue;
                        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        m.vertices = part.verts;
                        m.normals = part.normals;
                        m.uv = part.uv;
                        m.triangles = isl.ToArray();
                        var go = new GameObject("i" + i);
                        go.transform.SetParent(stage.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = m;
                        go.AddComponent<MeshRenderer>().sharedMaterial = part.material;
                        float fit = 0.85f / Mathf.Max(0.05f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                        go.transform.localScale = Vector3.one * fit;
                        go.transform.position = rot * new Vector3(i % cols, -(i / cols), 0f) - b.center * fit;
                    }
                    var size = rot * new Vector3(cols, rows / 1.1f, 0f);
                    var cb = new Bounds(rot * new Vector3((cols - 1) * 0.5f, -(rows - 1) * 0.5f, 0f),
                        new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
                    LegionaryShot.Shoot(cb, 205f, 0f, 64, 64, "Temp/kit_warm.png", true);
                    LegionaryShot.Shoot(cb, 205f, 0f, cols * 160, rows * 160, p[1], true);
                }
                finally { Object.DestroyImmediate(stage); }
                File.WriteAllText(p[1] + ".txt", sb.ToString());
            }
        }
    }
}
