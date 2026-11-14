// PropDressingPass.cs
// Dresses a hand-authored building prefab with vendor props (Synty), from a
// layout that is DATA (2026-11-13). The props go into ONE group of their own
// under the prefab root, as nested prefab instances the developer can move,
// swap or delete in the Editor; nothing else in the prefab is touched, and a
// re-run rebuilds only that group.
//
// Layout files: tools/art/dressing/*.txt. Lines:
//   prefab <asset path of the building prefab>
//   group  <name of the props group>            e.g. 15 [fade-in] - Props
//   <prop prefab name> | <x> | <z> | <yaw> [| key=value ...]
// A prop is placed by its MEASURED bounds: its middle on (x, z) and its lowest
// point on y (default 0), in the building root's frame at root scale 1.
//   y=<m>        height of its lowest point
//   s=<f>        uniform scale
//   pivot        place by its pivot instead of its bounds (wall-mounted things)
//   player       its Flag_* material slots take Flag_Symbol_Player (player cloth)
//   tilt=<deg>   lean about its local X after the yaw
// '#' starts a comment.
//   Waning Border > Art > Dressing > Apply Layouts
//   Waning Border > Art > Dressing > Prop Info   (sizes of the props named in
//       Temp/prop-info.txt -> Temp/prop-info-report.txt)

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class PropDressingPass
    {
        const string LayoutDir = "tools/art/dressing";
        const string PlayerFlag = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/Flag_Symbol_Player.mat";
        static readonly string[] PropRoots = { "Assets/Synty" };

        static Dictionary<string, string> _byName;

        static string Find(string name)
        {
            if (_byName == null)
            {
                _byName = new Dictionary<string, string>();
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PropRoots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string n = Path.GetFileNameWithoutExtension(path);
                    if (!_byName.ContainsKey(n)) _byName[n] = path;
                }
            }
            return _byName.TryGetValue(name, out var p) ? p : null;
        }

        static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);

        [MenuItem("Waning Border/Art/Dressing/Prop Info")]
        public static void Info()
        {
            _byName = null;
            var sb = new StringBuilder();
            foreach (var raw in File.ReadAllLines("Temp/prop-info.txt"))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                string path = Find(name);
                if (path == null) { sb.AppendLine("MISSING " + name); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var mats = new HashSet<string>();
                bool any = false; Bounds b = default;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m.name);
                }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:F2} x {2:F2} x {3:F2} (x z y)  x {4:F2}..{5:F2} y {6:F2}..{7:F2} z {8:F2}..{9:F2}  lights {10} particles {11}  mats {12}",
                    name, b.size.x, b.size.z, b.size.y, b.min.x, b.max.x, b.min.y, b.max.y, b.min.z, b.max.z,
                    go.GetComponentsInChildren<Light>(true).Length, go.GetComponentsInChildren<ParticleSystem>(true).Length,
                    string.Join(",", mats)));
                Object.DestroyImmediate(go);
            }
            File.WriteAllText("Temp/prop-info-report.txt", sb.ToString());
            Debug.Log("[PropDressing] info\n" + sb);
        }

        [MenuItem("Waning Border/Art/Dressing/Apply Layouts")]
        public static void Apply()
        {
            _byName = null;
            var sb = new StringBuilder();
            if (!Directory.Exists(LayoutDir)) { Debug.LogError("[PropDressing] no " + LayoutDir); return; }
            foreach (var file in Directory.GetFiles(LayoutDir, "*.txt"))
            {
                try { ApplyOne(file, sb); }
                catch (System.Exception e) { sb.AppendLine("FAIL " + file + ": " + e); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/prop-dressing-report.txt", sb.ToString());
            Debug.Log("[PropDressing]\n" + sb);
        }

        static void ApplyOne(string file, StringBuilder sb)
        {
            string prefabPath = null, groupName = null;
            var lines = new List<string>();
            foreach (var raw in File.ReadAllLines(file))
            {
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("prefab ")) prefabPath = line.Substring(7).Trim();
                else if (line.StartsWith("group ")) groupName = line.Substring(6).Trim();
                else lines.Add(line);
            }
            if (prefabPath == null || groupName == null) { sb.AppendLine("BAD HEADER " + file); return; }

            var playerFlag = AssetDatabase.LoadAssetAtPath<Material>(PlayerFlag);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Vector3 keep = root.transform.localScale;
                root.transform.localScale = Vector3.one;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var old = root.transform.Find(groupName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var group = new GameObject(groupName).transform;
                group.SetParent(root.transform, false);

                int placed = 0;
                foreach (var line in lines)
                {
                    var p = line.Split('|');
                    if (p.Length < 4) { sb.AppendLine("  BAD " + line); continue; }
                    string name = p[0].Trim();
                    string path = Find(name);
                    if (path == null) { sb.AppendLine("  MISSING " + name); continue; }
                    float x = F(p[1]), z = F(p[2]), yaw = F(p[3]);
                    float y = 0f, s = 1f, tilt = 0f; bool pivot = false, player = false;
                    for (int i = 4; i < p.Length; i++)
                    {
                        string o = p[i].Trim();
                        if (o == "pivot") pivot = true;
                        else if (o == "player") player = true;
                        else if (o.StartsWith("y=")) y = F(o.Substring(2));
                        else if (o.StartsWith("s=")) s = F(o.Substring(2));
                        else if (o.StartsWith("tilt=")) tilt = F(o.Substring(5));
                    }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), group);
                    go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                    go.transform.localScale = Vector3.one * s;
                    go.transform.localPosition = new Vector3(x, y, z);
                    if (!pivot)
                    {
                        bool any = false; Bounds b = default;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                        {
                            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                        }
                        if (any) go.transform.position += new Vector3(x - b.center.x, y - b.min.y, z - b.center.z);
                    }
                    // No light sources: this set is lit by hand (BuildingLanternLight exclusions).
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
                    if (player && playerFlag != null)
                    {
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            var mats = r.sharedMaterials; bool changed = false;
                            for (int m = 0; m < mats.Length; m++)
                                if (mats[m] != null && mats[m].name.StartsWith("Flag_")) { mats[m] = playerFlag; changed = true; }
                            if (changed) r.sharedMaterials = mats;
                        }
                    }
                    placed++;
                }
                root.transform.localScale = keep;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                sb.AppendLine($"{Path.GetFileName(prefabPath)}: {placed} props in \"{groupName}\"");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
