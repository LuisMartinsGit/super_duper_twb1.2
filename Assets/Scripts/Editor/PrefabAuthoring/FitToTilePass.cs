// FitToTilePass.cs
// Scales the three Archery Range
// levels AS A WHOLE — the prefab ROOT scale, never a piece — so each stands
// on the Royal Stable's tile (4 x 4 cells of 2 m). Developer, 2026-11-12.
//   Garrison_2      NOT here any more - see FootprintScalePass
//   Archery Range   level 1's model extent spans the tile; levels 2 and 3
//                   take the SAME factor, so a level-up does not change size
// Re-runnable: the factor is always measured at root scale 1.
// Reports to Temp/fit-to-tile.txt, with the sizes of the related prefabs it
// does not touch.

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class FitToTilePass
    {
        const string A = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/";
        static readonly string[] Archery =
        {
            A + "ArcheryRange/ArcheryRange_3 2.prefab",   // level 1
            A + "ArcheryRange/ArcheryRange_3.prefab",
            A + "ArcheryRange/ArcheryRange_3 1.prefab",
        };
        static readonly string[] Related =
        {
            "Assets/GameData/TechTree/Age0/Buildings/Barracks/Art/Barracks_Age_0.prefab",
            A + "Garrison/Garrison_2.prefab",
            A + "Garrison/Garrison_3 1.prefab",
            A + "RoyalStable/Art/alanthor_RoyalStable_lv1.prefab",
            A + "SiegeYard/Art/alanthor_SiegeYard_lv1.prefab",
        };
        const float TileMetres = 8f;

        [MenuItem("Waning Border/Art/Scale/Fit Archery Range to the 8 m tile (root scale)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            // The Garrison is no longer scaled here: its root scale comes from
            // FootprintScalePass (tools/art/footprint-scale.txt), which holds the
            // whole model - eaves included - inside the tile with room around it.
            // Fitting its corner markers to the tile, as this pass did, left the
            // eaves over the neighbouring tiles (2026-11-13).
            // Archery Range: level 1's extent, one factor for all three.
            float af = 0f;
            for (int i = 0; i < Archery.Length; i++)
            {
                var root = PrefabUtility.LoadPrefabContents(Archery[i]);
                root.transform.localScale = Vector3.one;
                var b = Measure(root);
                if (i == 0) af = TileMetres / Mathf.Max(b.size.x, b.size.z);
                root.transform.localScale = Vector3.one * af;
                PrefabUtility.SaveAsPrefabAsset(root, Archery[i]);
                PrefabUtility.UnloadPrefabContents(root);
                sb.AppendLine($"{Path.GetFileName(Archery[i])}: {b.size.x:F1} x {b.size.z:F1} x {b.size.y:F1} m at scale 1 -> root scale {af:F4} = {b.size.x * af:F1} x {b.size.z * af:F1} x {b.size.y * af:F1} m");
            }
            sb.AppendLine("NOT CHANGED:");
            foreach (var path in Related)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { sb.AppendLine("  missing " + path); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                var b = Measure(go);
                sb.AppendLine($"  {Path.GetFileName(path)}: {b.size.x:F1} x {b.size.z:F1} x {b.size.y:F1} m, root scale {go.transform.localScale.x:F3}");
                Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/fit-to-tile.txt", sb.ToString());
            Debug.Log("[FitToTile]\n" + sb);
        }

        static Bounds Measure(GameObject go)
        {
            bool any = false; Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
