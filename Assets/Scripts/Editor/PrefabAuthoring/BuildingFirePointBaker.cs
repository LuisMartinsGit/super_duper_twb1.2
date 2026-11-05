using System.Collections.Generic;
using System.Linq;
using TheWaningBorder.Data;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    /// <summary>
    /// Writes each shooting building's fire points onto its SOs, measured from the
    /// prefab that SO shows, one point per tower: the top of its fighting gallery (the
    /// Synty Hoarding_Corner pieces), else of the parts named Tower, else the top of
    /// the building when it has no tower. Spires are left out, since nobody shoots from
    /// a spike. Run it again after changing a shooter's art.
    /// </summary>
    public static class BuildingFirePointBaker
    {
        /// <summary>Metres, as shown in the game: pieces closer than this are one tower.
        /// Below the gap between a Fortress's towers, above a Watch Tower's own width.</summary>
        private const float SameTowerDistance = 3.5f;

        [MenuItem("Waning Border/Art/Bake Building Fire Points")]
        public static void Bake()
        {
            var bases = AssetDatabase.FindAssets("t:BuildingDefSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingDefSO>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(so => so != null).ToList();
            var shooterIds = new HashSet<string>(bases.Where(b => b.attack != null && b.attack.enabled).Select(b => b.id));

            foreach (var b in bases)
            {
                if (b.attack == null || !b.attack.enabled || b.prefab == null) continue;
                b.firePoints = Measure(b.prefab, b.id, b.id, Cultures.None);
                Report(b.name, b.firePoints);
                EditorUtility.SetDirty(b);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingLevelDefSO"))
            {
                var lvl = AssetDatabase.LoadAssetAtPath<BuildingLevelDefSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (lvl == null || lvl.prefab == null) continue;
                bool shoots = shooterIds.Contains(lvl.buildingId) || (lvl.attack != null && lvl.attack.enabled);
                if (!shoots) continue;
                byte culture = (byte)System.Array.IndexOf(CultureConfig.Names, lvl.culture);
                lvl.firePoints = Measure(lvl.prefab, lvl.buildingId, lvl.buildingId, culture);
                Report(lvl.name, lvl.firePoints);
                EditorUtility.SetDirty(lvl);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Report(string name, Vector3[] points)
            => Debug.Log($"[FirePoints] {name}: " + string.Join(" ", points.Select(p => $"({p.x:F2},{p.y:F2},{p.z:F2})")));

        private static Vector3[] Measure(GameObject prefab, string buildingId, string ladder, byte culture)
        {
            var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            try
            {
                var size = BuildingSizeConfig.GetSize(buildingId);
                float scale = PresentationSpawnSystem.ComputeBuildingViewScale(go, prefab, ladder, culture,
                    size.x, size.y, out _, out Vector3 offset);
                go.transform.localScale = Vector3.one * scale;
                go.transform.position = -offset * scale;

                var parts = go.GetComponentsInChildren<MeshRenderer>(true)
                    .Select(r => (renderer: r, path: PathBelow(go.transform, r.transform)))
                    .ToList();
                var towers = parts.Where(p => p.path.Contains("Hoarding_Corner")).Select(p => p.renderer.bounds).ToList();
                if (towers.Count == 0)
                    towers = parts.Where(p => p.path.Contains("Tower")).Select(p => p.renderer.bounds)
                        .Where(b => !IsSpire(b)).ToList();
                var points = towers.Count > 0 ? Clusters(towers, SameTowerDistance) : new List<Vector3>();
                if (points.Count == 0)
                {
                    var top = parts.Where(p => !IsDecoration(p.path)).Select(p => p.renderer.bounds)
                        .Where(b => !IsSpire(b))
                        .OrderByDescending(b => b.max.y).First();
                    points.Add(new Vector3(top.center.x, top.max.y, top.center.z));
                }
                // The view stands 180 degrees round from the entity (PresentationSpawnSystem).
                return points.Select(p => new Vector3(-p.x, p.y, -p.z)).ToArray();
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static List<Vector3> Clusters(List<Bounds> parts, float reach)
        {
            var groups = new List<Bounds>();
            foreach (var b in parts.OrderByDescending(p => p.max.y))
            {
                int hit = groups.FindIndex(g => Vector2.Distance(new Vector2(g.center.x, g.center.z),
                                                                 new Vector2(b.center.x, b.center.z)) < reach);
                if (hit < 0) groups.Add(b);
                else { var g = groups[hit]; g.Encapsulate(b); groups[hit] = g; }
            }
            return groups.Select(g => new Vector3(g.center.x, g.max.y, g.center.z)).ToList();
        }

        private static bool IsSpire(Bounds b) => Mathf.Max(b.size.x, b.size.z) < b.size.y * 0.5f;

        private static bool IsDecoration(string path)
            => new[] { "Prop", "Ground", "Flag", "Banner", "Torii", "Lamp", "Statue" }.Any(path.Contains);

        private static string PathBelow(Transform root, Transform t)
        {
            var names = new List<string>();
            for (; t != null && t != root; t = t.parent) names.Add(t.name);
            return string.Join("/", names);
        }
    }
}
