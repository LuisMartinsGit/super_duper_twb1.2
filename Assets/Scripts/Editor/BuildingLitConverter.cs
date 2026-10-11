// BuildingLitConverter.cs
// Puts every material a building can render with on TheWaningBorder/BuildingLit
// (Assets/Shaders/BuildingLit/), the URP Lit copy that does the player colour
// per pixel (BuildingFactionColorMarker, 2026-10-10).
//
// Why on disk and not only at runtime: a build compiles the shader variants
// its MATERIALS use. A material switched to BuildingLit at runtime has no
// compiled variant for its keyword set, so the conversion is an asset edit.
// BuildingLit has every Lit property and keyword, so a converted material
// renders exactly as before until BuildingFactionColorMarker sets its mode.
//
// What "a building can render with": the dependencies of every building SO's
// and building-level SO's prefab (TechTreeCatalog) and the legacy
// Resources/Prefabs/Buildings art. Materials EMBEDDED in a model (not
// extracted to a .mat) cannot be edited and are listed instead; the runtime
// switch covers them, with a warning.

using System.Collections.Generic;
using System.Text;
using TheWaningBorder.Data;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class BuildingLitConverter
    {
        private const string UrpLit = "Universal Render Pipeline/Lit";
        private const string BuildingLit = "TheWaningBorder/BuildingLit";
        private const string LegacyBuildingRoot = "Assets/Resources/Prefabs/Buildings";

        [MenuItem("Waning Border/Buildings/Convert Materials To BuildingLit")]
        public static void Convert()
        {
            var target = Shader.Find(BuildingLit);
            if (target == null)
            {
                Debug.LogError($"[BuildingLitConverter] Shader '{BuildingLit}' not found — has it compiled?");
                return;
            }

            var roots = BuildingPrefabPaths();
            var deps = AssetDatabase.GetDependencies(roots.ToArray(), recursive: true);

            int converted = 0, already = 0;
            var other = new SortedDictionary<string, List<string>>();
            var embedded = new List<string>();
            var log = new StringBuilder();

            foreach (var path in deps)
            {
                // Package assets are not ours to edit (the URP default Lit.mat
                // is a dependency of the primitive fallback).
                if (!path.StartsWith("Assets/")) continue;

                if (path.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase))
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null || mat.shader == null) continue;
                    if (mat.shader == target) { already++; continue; }
                    if (mat.shader.name == UrpLit)
                    {
                        // Assigning the shader keeps every property and keyword
                        // the two shaders share — which is all of Lit's. The
                        // disabled passes are not kept (URP re-derives them for
                        // its own shaders only), so carry them across.
                        bool motionVectorsOff = !mat.GetShaderPassEnabled("MotionVectors");
                        mat.shader = target;
                        if (motionVectorsOff) mat.SetShaderPassEnabled("MotionVectors", false);
                        EditorUtility.SetDirty(mat);
                        converted++;
                        log.Append("  ").Append(path).Append('\n');
                    }
                    else Add(other, mat.shader.name, path);
                    continue;
                }

                // A model's own materials, not remapped to a .mat asset.
                if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (sub is Material m && m.shader != null && m.shader.name == UrpLit)
                            embedded.Add(path + " : " + m.name);
                }
            }

            AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            report.Append($"[BuildingLitConverter] {roots.Count} building prefabs, {converted} material(s) converted, " +
                          $"{already} already BuildingLit.\n");
            if (converted > 0) report.Append("Converted:\n").Append(log);
            foreach (var kv in other)
                report.Append($"Not Lit — left alone ({kv.Key}): {kv.Value.Count}\n  ")
                      .Append(string.Join("\n  ", kv.Value)).Append('\n');
            if (embedded.Count > 0)
                report.Append($"Embedded in a model (extract them to convert; the runtime switch covers them): " +
                              $"{embedded.Count}\n  ").Append(string.Join("\n  ", embedded)).Append('\n');
            Debug.Log(report.ToString());
        }

        private static void Add(SortedDictionary<string, List<string>> d, string key, string value)
        {
            if (!d.TryGetValue(key, out var l)) d[key] = l = new List<string>();
            l.Add(value);
        }

        /// <summary>Every prefab a building can show, as asset paths.</summary>
        private static List<string> BuildingPrefabPaths()
        {
            var paths = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:TechTreeCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<TechTreeCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog == null) continue;
                foreach (var b in catalog.buildings)
                    if (b != null && b.prefab != null) paths.Add(AssetDatabase.GetAssetPath(b.prefab));
                foreach (var l in catalog.buildingLevels)
                    if (l != null && l.prefab != null) paths.Add(AssetDatabase.GetAssetPath(l.prefab));
            }
            if (AssetDatabase.IsValidFolder(LegacyBuildingRoot))
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { LegacyBuildingRoot }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Remove("");
            return new List<string>(paths);
        }
    }
}
