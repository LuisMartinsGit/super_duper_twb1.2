// UnitScalePass.cs
// Sets every unit prefab's ROOT scale to 1 (developer, 2026-11-12: "the
// units should not be scaled"). Nothing here touches a building: a building
// prefab is drawn at root scale 1 and the pieces inside it keep whatever
// scales its author gave them — those ratios are the composition.
// Reports to Temp/unit-scale-pass.txt.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    using TheWaningBorder.EditorTools.PrefabAuthoring;
    public static class UnitScalePass
    {
        const string Report = "Temp/unit-scale-pass.txt";

        [MenuItem("Waning Border/Art/Scale/Units: every unit prefab root to scale 1")]
        public static void UnitsToUnitScale()
        {
            // SUPERSEDED 2026-11-14: unit prefab roots now carry the factor that
            // makes each body as tall as the Spearman's (UnitHeightPass). Setting
            // them back to 1 would undo that, so this menu runs that pass instead.
            if (true) { UnitHeightPass.Apply(); return; }
#pragma warning disable CS0162
            var sb = new StringBuilder("UNITS\n");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/GameData/TechTree" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                // <...>/Units/<Unit>/<Prefab>.prefab, the unit's own folder only
                var parts = path.Split('/');
                if (parts.Length < 3 || parts[parts.Length - 3] != "Units") continue;
                // The Spearman is sized to the Worker on purpose (LegionarySpearmanAuthor).
                if (path.EndsWith("/Barracks/Units/Spearman/Spearman.prefab")) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                var s = asset.transform.localScale;
                if ((s - Vector3.one).sqrMagnitude <= 1e-6f) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                root.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                sb.AppendLine($"{path.Substring("Assets/GameData/TechTree/".Length)}: was {s.ToString("0.###")}");
            }
            Write(sb, true);
        }

        static void Write(StringBuilder sb, bool append)
        {
            Directory.CreateDirectory("Temp");
            if (append) File.AppendAllText(Report, sb.ToString()); else File.WriteAllText(Report, sb.ToString());
            Debug.Log("[UnitScalePass] " + sb);
        }
    }
}
