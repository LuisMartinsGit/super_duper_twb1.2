using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.Inspector
{
    /// <summary>
    /// Finds every <see cref="InspectorMetadataSO"/> in the project and answers
    /// "what chrome does this field get?" for the inspector drawers.
    ///
    /// Lookups walk the base chain, because Unity serializes a base class's
    /// fields into the derived component: a field declared on MapMarker is
    /// listed once, under MapMarker, and still resolves when the selected
    /// object is a NatureRegionMarker.
    /// </summary>
    public static class InspectorMetadataRegistry
    {
        // typeFullName -> fieldName -> entry
        static Dictionary<string, Dictionary<string, InspectorMetadataSO.FieldEntry>> _byType;

        static Dictionary<string, Dictionary<string, InspectorMetadataSO.FieldEntry>> Map
        {
            get
            {
                if (_byType == null) Rebuild();
                return _byType;
            }
        }

        /// <summary>Drop the cache; it rebuilds on the next lookup.</summary>
        public static void Invalidate() => _byType = null;

        [MenuItem("Waning Border/Inspector/Reload Metadata")]
        public static void Reload()
        {
            Rebuild();
            Debug.Log($"[InspectorMetadata] reloaded — {_byType.Count} types.");
        }

        /// <summary>
        /// Report entries that no longer match any code — the one failure this
        /// scheme has that the old [Header]/[Tooltip] attributes did not.
        ///
        /// An attribute moved with its field automatically; an SO row does not,
        /// so renaming or deleting a field silently orphans its chrome with no
        /// compile error. Run this after a rename, and before a release.
        /// </summary>
        [MenuItem("Waning Border/Inspector/Validate Metadata")]
        public static void Validate()
        {
            Rebuild();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var report = new System.Text.StringBuilder();
            int missingTypes = 0, missingFields = 0, matched = 0;

            foreach (var pair in _byType)
            {
                var type = ResolveType(pair.Key, assemblies);
                if (type == null)
                {
                    missingTypes++;
                    report.AppendLine($"  MISSING TYPE   {pair.Key}");
                    continue;
                }

                foreach (var field in pair.Value.Keys)
                {
                    if (FindField(type, field) == null)
                    {
                        missingFields++;
                        report.AppendLine($"  MISSING FIELD  {pair.Key}.{field}");
                    }
                    else matched++;
                }
            }

            var summary = $"[InspectorMetadata] {matched} entries matched, "
                        + $"{missingTypes} orphaned types, {missingFields} orphaned fields.";

            if (missingTypes + missingFields == 0) Debug.Log(summary);
            else Debug.LogWarning(summary + "\n" + report);
        }

        /// <summary>Find a type by FullName across every loaded assembly.</summary>
        static Type ResolveType(string fullName, Assembly[] assemblies)
        {
            foreach (var asm in assemblies)
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        static void Rebuild()
        {
            _byType = new Dictionary<string, Dictionary<string, InspectorMetadataSO.FieldEntry>>();

            foreach (var guid in AssetDatabase.FindAssets("t:InspectorMetadataSO"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<InspectorMetadataSO>(path);
                if (so == null || so.types == null) continue;

                foreach (var t in so.types)
                {
                    if (t == null || string.IsNullOrEmpty(t.typeName) || t.fields == null) continue;

                    if (!_byType.TryGetValue(t.typeName, out var fields))
                        _byType[t.typeName] = fields = new Dictionary<string, InspectorMetadataSO.FieldEntry>();

                    foreach (var f in t.fields)
                        if (f != null && !string.IsNullOrEmpty(f.field))
                            fields[f.field] = f;
                }
            }
        }

        /// <summary>True if this type or any of its bases has chrome authored.</summary>
        public static bool HasAnyFor(Type type)
        {
            for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
                if (Map.ContainsKey(cur.FullName ?? string.Empty)) return true;
            return false;
        }

        /// <summary>Chrome for one field, searching the type's base chain.</summary>
        public static bool TryGet(Type type, string field, out InspectorMetadataSO.FieldEntry entry)
        {
            for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
                if (Map.TryGetValue(cur.FullName ?? string.Empty, out var fields)
                    && fields.TryGetValue(field, out entry))
                    return true;

            entry = null;
            return false;
        }

        /// <summary>Tooltip for one field, or "" — the shape GUIContent wants.</summary>
        public static string Tooltip(Type type, string field)
            => TryGet(type, field, out var e) && !string.IsNullOrEmpty(e.tooltip) ? e.tooltip : string.Empty;

        /// <summary>Header above one field, or null when it starts no section.</summary>
        public static string Header(Type type, string field)
            => TryGet(type, field, out var e) && !string.IsNullOrEmpty(e.header) ? e.header : null;

        /// <summary>
        /// The declared type of a serialized field, searching the base chain.
        /// Used to decide whether to recurse into a nested serializable type.
        /// </summary>
        public static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public
                                     | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                var f = cur.GetField(name, Flags);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Rebuild whenever one of the metadata assets is saved.</summary>
        sealed class Watcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                               string[] movedTo, string[] movedFrom)
            {
                foreach (var p in imported)
                    if (p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                        && AssetDatabase.LoadAssetAtPath<InspectorMetadataSO>(p) != null)
                    {
                        Invalidate();
                        return;
                    }

                foreach (var p in deleted)
                    if (p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        Invalidate();
                        return;
                    }
            }
        }
    }
}
