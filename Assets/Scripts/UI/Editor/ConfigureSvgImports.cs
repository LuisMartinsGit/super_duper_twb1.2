// Editor-only tool inside the single runtime asmdef: the Editor/ folder
// convention does NOT apply within an .asmdef, so without this guard the
// file is compiled into PLAYER builds and fails them (UnityEditor missing).
#if UNITY_EDITOR
// Phase 1 helper — flips com.unity.vectorgraphics SVG imports under
// Assets/UI/Vectors/ to produce VectorImage output instead of a GameObject
// prefab. USS background-image rejects GameObject; once the import switches,
// the same `url("...")` reference resolves to a VectorImage asset.
//
// Run once after creating new SVGs:
//   Tools > Waning Border > UI > Configure SVG imports for UI Toolkit
//
// Uses reflection so the project doesn't take a hard compile dependency on
// the VectorGraphics editor assembly — the enum name has drifted across
// preview versions (SvgType.UIToolkit / UIVectorImage / VectorImage).

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.UI.EditorTools
{
    public static class ConfigureSvgImports
    {
        private const string VectorsFolder = "Assets/UI/Vectors";

        // SvgType enum values that produce a UI-Toolkit-compatible asset.
        // Listed in preference order — first match wins.
        private static readonly string[] TargetSvgTypeNames =
        {
            "UIToolkit",
            "VectorImage",
            "UIVectorImage",
        };

        [MenuItem("Tools/Waning Border/UI/Configure SVG imports for UI Toolkit")]
        public static void Run()
        {
            var guids = AssetDatabase.FindAssets("t:Object", new[] { VectorsFolder });
            int changed = 0;
            int skipped = 0;
            var failures = new List<string>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    failures.Add(path + " — no importer");
                    continue;
                }
                if (!importer.GetType().Name.Contains("SVGImporter"))
                {
                    failures.Add(path + " — not a SVGImporter (got " + importer.GetType().Name + ")");
                    continue;
                }

                if (TrySetVectorImageType(importer, out var enumName))
                {
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    Debug.Log("[SVG] " + path + " → " + enumName);
                    changed++;
                }
                else
                {
                    failures.Add(path + " — could not set SvgType");
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ConfigureSvgImports] " + changed + " SVGs updated, " + skipped + " skipped.");
            foreach (var f in failures) Debug.LogWarning("[ConfigureSvgImports] " + f);
        }

        private static bool TrySetVectorImageType(AssetImporter importer, out string usedEnumName)
        {
            usedEnumName = null;
            var t = importer.GetType();

            // Look for any property/field named SvgType, OutputType, or SVGType.
            var member = (MemberInfo)t.GetProperty("SvgType",
                              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? (MemberInfo)t.GetProperty("OutputType",
                              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? (MemberInfo)t.GetField("SvgType",
                              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? (MemberInfo)t.GetField("OutputType",
                              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (member == null) return false;

            Type enumType =
                  member is PropertyInfo pi ? pi.PropertyType
                : member is FieldInfo    fi ? fi.FieldType
                : null;
            if (enumType == null || !enumType.IsEnum) return false;

            foreach (var name in TargetSvgTypeNames)
            {
                if (!Enum.IsDefined(enumType, name)) continue;
                var value = Enum.Parse(enumType, name);
                if (member is PropertyInfo p)
                {
                    p.SetValue(importer, value);
                }
                else
                {
                    ((FieldInfo)member).SetValue(importer, value);
                }
                usedEnumName = name;
                return true;
            }

            // Last resort — list the names we saw so the user can adjust manually.
            usedEnumName = "(none matched: " + string.Join(",", Enum.GetNames(enumType)) + ")";
            return false;
        }
    }
}

#endif // UNITY_EDITOR
