// UnitSizeProbe.cs
// Measures every unit SO's prefab (2026-11-14): its root scale and the size of
// its body as authored, and the size it is drawn at in a match (authored x the
// one presentation factor, UnitWorldScale). The Spearman is the reference: a
// unit on foot should stand as tall as he does.
//   Waning Border > Art > Scale > Units: Measure Sizes   -> Temp/unit-sizes.txt

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class UnitSizeProbe
    {
        const string Report = "Temp/unit-sizes.txt";

        [MenuItem("Waning Border/Art/Scale/Units: Measure Sizes")]
        public static void Measure()
        {
            float k = TheWaningBorder.Rendering.UnitWorldScale.Value;
            var rows = new List<(string cls, string id, string line, float h)>();
            foreach (var guid in AssetDatabase.FindAssets("t:UnitDefSO", new[] { "Assets/GameData/TechTree" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<UnitDefSO>(path);
                if (so == null) continue;
                var prefabField = typeof(UnitDefSO).GetField("prefab");
                var prefab = prefabField != null ? prefabField.GetValue(so) as GameObject : null;
                if (prefab == null)
                {
                    rows.Add((so.unitClass, so.id, $"{so.unitClass,-18} {so.id,-28} NO PREFAB (procedural visual)", 0f));
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    bool any = false; Bounds b = default;
                    foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                    {
                        if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                        // A skinned mesh's world bounds follow its bind pose at this scale.
                        var rb = r.bounds;
                        if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
                    }
                    float root = go.transform.localScale.x;
                    float h = any ? b.max.y : 0f, w = any ? Mathf.Max(b.size.x, b.size.z) : 0f;
                    rows.Add((so.unitClass, so.id,
                        $"{so.unitClass,-18} {so.id,-28} root {root:F3}  authored {h:F2} m high, {w:F2} wide  -> drawn {h * k:F2} m high  (radius {so.radius:F3}, speed {so.speed:F3})  {AssetDatabase.GetAssetPath(prefab).Substring("Assets/GameData/TechTree/".Length)}", h));
                }
                finally { Object.DestroyImmediate(go); }
            }
            var sb = new StringBuilder($"UNIT SIZES (presentation factor {k})\n");
            foreach (var r in rows.OrderBy(r => r.cls).ThenBy(r => r.id)) sb.AppendLine(r.line);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, sb.ToString());
            Debug.Log("[UnitSizeProbe] " + rows.Count + " units -> " + Report);
        }
    }
}
