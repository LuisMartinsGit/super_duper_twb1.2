using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.EditorTools
{
    /// <summary>
    /// Rebuilds Resources/ComponentConfigCatalog.asset from every
    /// <see cref="IComponentConfig"/> asset in the project, and enforces the
    /// half of the rule a compiler cannot: that a config asset is named after
    /// the component it configures and sits in the same folder as its .cs.
    /// </summary>
    public static class ComponentConfigTools
    {
        const string CatalogPath = "Assets/Resources/ComponentConfigCatalog.asset";
        const string ConfigSuffix = "Config";

        [MenuItem("Waning Border/Component Config/Rebuild Catalog")]
        public static void Rebuild()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ComponentConfigCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[ComponentConfig] {CatalogPath} is missing — create it first.");
                return;
            }

            var found = new List<ScriptableObject>();
            var problems = new List<string>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<IComponentConfig>())
            {
                if (type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                var paths = AssetDatabase.FindAssets($"t:{type.Name}")
                                         .Select(AssetDatabase.GUIDToAssetPath)
                                         .Distinct()
                                         .ToList();

                if (paths.Count == 0)
                {
                    problems.Add($"{type.Name}: no asset anywhere in the project.");
                    continue;
                }
                if (paths.Count > 1)
                    problems.Add($"{type.Name}: {paths.Count} assets — "
                               + $"a component config must be a single asset ({string.Join(", ", paths)}).");

                foreach (var path in paths)
                {
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (so != null) found.Add(so);
                    problems.AddRange(Violations(type.Name, path));
                }
            }

            catalog.configs = found;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            ComponentConfig.Invalidate();

            var summary = $"[ComponentConfig] catalog rebuilt — {found.Count} configs.";
            if (problems.Count == 0) Debug.Log(summary);
            else Debug.LogWarning(summary + "\n  " + string.Join("\n  ", problems));
        }

        /// <summary>The naming and co-location rule, checked for one asset.</summary>
        static IEnumerable<string> Violations(string configTypeName, string assetPath)
        {
            if (!configTypeName.EndsWith(ConfigSuffix))
            {
                yield return $"{configTypeName}: a config type must be named <Component>{ConfigSuffix}.";
                yield break;
            }

            var component = configTypeName.Substring(0, configTypeName.Length - ConfigSuffix.Length);
            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            var folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');

            if (assetName != component)
                yield return $"{configTypeName}: asset is '{assetName}.asset', expected '{component}.asset' ({assetPath}).";

            if (folder != null && !File.Exists(Path.Combine(folder, component + ".cs")))
                yield return $"{configTypeName}: {component}.cs is not in {folder} — "
                           + "a config asset must sit beside the class it configures.";
        }
    }
}
