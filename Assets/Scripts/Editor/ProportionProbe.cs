// ProportionProbe.cs  (temporary measurement tool, 2026-11-11)
// Measures every Age 0 / Alanthor building as the game draws it — the prefab
// fitted to its SO footprint the way PresentationSpawnSystem does — and the
// main unit models, and writes a CSV to Temp/proportions.csv.
// Run: Unity -batchmode -quit -executeMethod TheWaningBorder.EditorTools.ProportionProbe.Run

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools
{
    public static class ProportionProbe
    {
        private const float Cell = 2f;

        [MenuItem("Waning Border/Debug/Proportion Probe (CSV)")]
        public static void Run()
        {
            var cat = AssetDatabase.LoadAssetAtPath<TechTreeCatalog>("Assets/Resources/TechTreeCatalog.asset");
            var sb = new StringBuilder();
            sb.AppendLine("kind,id,level,culture,footW,footD,detailScale,width,depth,height,height_over_width,door_h,prefab");

            var byId = new Dictionary<string, BuildingDefSO>();
            foreach (var b in cat.buildings) if (b != null && !string.IsNullOrEmpty(b.id)) byId[b.id] = b;

            // Ladders: fit reference = highest level with a FootprintBounds marker, else highest level.
            var ladders = cat.buildingLevels.Where(l => l != null && l.prefab != null)
                .GroupBy(l => (l.buildingId, l.culture));
            var ladderRef = new Dictionary<(string, string), GameObject>();
            foreach (var g in ladders)
            {
                var ordered = g.OrderByDescending(l => l.level).ToList();
                var marked = ordered.FirstOrDefault(l => l.prefab.GetComponentInChildren<TheWaningBorder.Rendering.FootprintBounds>(true) != null);
                ladderRef[g.Key] = (marked ?? ordered[0]).prefab;
            }

            foreach (var b in cat.buildings)
            {
                if (b == null || b.prefab == null) continue;
                if (b.id.StartsWith("Feraldis") || b.id.StartsWith("Runai")) continue;
                Measure(sb, "building", b.id, 0, "", b.footprintCells.x * Cell, b.footprintCells.y * Cell, b.prefab, b.prefab);
            }
            foreach (var l in cat.buildingLevels)
            {
                if (l == null || l.prefab == null || l.culture != "Alanthor") continue;
                if (!byId.TryGetValue(l.buildingId, out var def)) continue;
                var reference = ladderRef.TryGetValue((l.buildingId, l.culture), out var r) ? r : l.prefab;
                Measure(sb, "level", l.buildingId, l.level, l.culture, def.footprintCells.x * Cell, def.footprintCells.y * Cell, l.prefab, reference);
            }
            foreach (var u in cat.units)
            {
                if (u == null || u.prefab == null) continue;
                if (u.id.StartsWith("Feraldis") || u.id.StartsWith("Runai")) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(u.prefab);
                inst.transform.position = new Vector3(0, -2000, 0);
                inst.transform.localScale = u.prefab.transform.localScale;
                var bb = Bounds(inst);
                sb.AppendLine($"unit,{u.id},0,,{u.radius * 2:F2},{u.radius * 2:F2},{inst.transform.localScale.x:F2},{bb.size.x:F2},{bb.size.z:F2},{bb.size.y:F2},{bb.size.y / Mathf.Max(0.01f, bb.size.x):F2},,{AssetDatabase.GetAssetPath(u.prefab)}");
                Object.DestroyImmediate(inst);
            }
            var outPath = System.Environment.GetEnvironmentVariable("TWB_PROBE_OUT") ?? "Temp/proportions.csv";
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[ProportionProbe] wrote Temp/proportions.csv");
        }

        private static void Measure(StringBuilder sb, string kind, string id, int level, string culture, float w, float d, GameObject shown, GameObject reference)
        {
            if (w <= 0 || d <= 0) return;
            var refInst = (GameObject)PrefabUtility.InstantiatePrefab(reference);
            refInst.transform.position = new Vector3(0, -2000, 0);
            refInst.transform.localScale = Vector3.one;
            float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(refInst, w, d);
            float authored = (reference.transform.localScale.x + reference.transform.localScale.y + reference.transform.localScale.z) / 3f;
            Object.DestroyImmediate(refInst);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(shown);
            inst.transform.position = new Vector3(0, -2000, 0);
            float scale = authored * fit;
            inst.transform.localScale = Vector3.one * scale;
            var bb = Bounds(inst);
            float door = 0f;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                if (r.name.ToLowerInvariant().Contains("door")) door = Mathf.Max(door, r.bounds.size.y);
            sb.AppendLine($"{kind},{id},{level},{culture},{w:F1},{d:F1},{scale:F2},{bb.size.x:F2},{bb.size.z:F2},{bb.size.y:F2},{bb.size.y / Mathf.Max(0.01f, Mathf.Max(bb.size.x, bb.size.z)):F2},{door:F2},{AssetDatabase.GetAssetPath(shown)}");
            Object.DestroyImmediate(inst);
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
