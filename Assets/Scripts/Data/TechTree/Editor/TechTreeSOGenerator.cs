// TechTreeSOGenerator.cs
// EDITOR-ONLY tool: generate UnitDefSO / BuildingDefSO assets + a TechTreeCatalog
// from the current TechTree.json, so unit/building stats become Inspector-editable.
// Part of: Data/TechTree/Editor/
//
// Run via menu:  Waning Border > Tech Tree > Generate Stat SOs from JSON
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime asmdef
// (TheWaningBorder.Runtime) with no separate editor assembly; the Editor/ folder
// name alone does not exclude it from player builds (see NavDebugDrawSystem.cs).
//
// Idempotent: re-running overwrites the per-id asset fields in place (preserving
// each asset's GUID and any manual references) and rebuilds the catalog list.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Data.EditorTools
{
    public static class TechTreeSOGenerator
    {
        const string JsonAssetPath   = "Assets/Resources/TechTree.json";
        const string RootFolder      = "Assets/GameData";
        const string TechTreeFolder  = "Assets/GameData/TechTree";
        const string UnitsFolder     = "Assets/GameData/TechTree/Units";
        const string BuildingsFolder = "Assets/GameData/TechTree/Buildings";
        const string CatalogPath     = "Assets/Resources/TechTreeCatalog.asset";

        [MenuItem("Waning Border/Tech Tree/Generate Stat SOs from JSON")]
        public static void GenerateMenu()
        {
            var catalog = Build(out int unitCount, out int buildingCount, out string error);
            if (error != null)
            {
                EditorUtility.DisplayDialog("Tech Tree SO Generator", error, "OK");
                return;
            }

            EditorUtility.DisplayDialog("Tech Tree SO Generator",
                $"Done.\n\nUnits: {unitCount}\nBuildings: {buildingCount}\n\n" +
                $"Catalog: {CatalogPath}\n\nNext: assign the catalog to the TechTreeDB component " +
                "(or use the 'Generate + Assign' button on the TechTreeDB inspector).", "OK");

            if (catalog != null)
            {
                EditorGUIUtility.PingObject(catalog);
            }
        }

        /// <summary>
        /// Generate/refresh all SO assets + the catalog from TechTree.json and return the
        /// catalog. Shows no dialogs (UI-free) so callers like the TechTreeDB inspector can
        /// drive it. On failure returns null and sets <paramref name="error"/>.
        /// </summary>
        public static TechTreeCatalog Build(out int unitCount, out int buildingCount, out string error)
        {
            unitCount = 0;
            buildingCount = 0;
            error = null;

            // 1. Load JSON text.
            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonAssetPath);
            if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text))
            {
                error = $"Could not find/read JSON at {JsonAssetPath}.";
                return null;
            }

            // 2. Parse via the SAME parser the runtime uses (no value drift).
            var parsed = TechTreeParser.ParseAll(jsonAsset.text);
            if (parsed.Units.Count == 0 && parsed.Buildings.Count == 0)
            {
                error = "Parser returned no units or buildings. Aborting.";
                return null;
            }

            // 3. Ensure target folders exist.
            EnsureFolder(RootFolder);
            EnsureFolder(TechTreeFolder);
            EnsureFolder(UnitsFolder);
            EnsureFolder(BuildingsFolder);

            var unitSOs = new List<UnitDefSO>();
            var buildingSOs = new List<BuildingDefSO>();
            TechTreeCatalog catalog = null;

            AssetDatabase.StartAssetEditing();
            try
            {
                // 4a. Units (one asset each, organized into a culture subfolder).
                foreach (var def in parsed.Units.Values)
                {
                    if (def == null || string.IsNullOrEmpty(def.id)) continue;
                    string folder = $"{UnitsFolder}/{CultureFolder(def.id)}";
                    string path = ResolveTargetPath(UnitsFolder, "UnitDefSO", $"Unit_{Sanitize(def.id)}", folder);
                    var so = AssetDatabase.LoadAssetAtPath<UnitDefSO>(path);
                    if (so == null)
                    {
                        so = ScriptableObject.CreateInstance<UnitDefSO>();
                        so.CopyFrom(def);
                        AssetDatabase.CreateAsset(so, path);
                    }
                    else
                    {
                        // Asset already exists: do NOT overwrite. The SO is now the
                        // authoritative, hand-editable source; JSON is deprecated. The
                        // generator only seeds MISSING assets so re-running it can never
                        // clobber tuned/doc-aligned values.
                    }
                    unitSOs.Add(so);
                    unitCount++;
                }

                // 4b. Buildings (one asset each, organized into a culture subfolder).
                foreach (var def in parsed.Buildings.Values)
                {
                    if (def == null || string.IsNullOrEmpty(def.id)) continue;
                    string folder = $"{BuildingsFolder}/{CultureFolder(def.id)}";
                    string path = ResolveTargetPath(BuildingsFolder, "BuildingDefSO", $"Building_{Sanitize(def.id)}", folder);
                    var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(path);
                    if (so == null)
                    {
                        so = ScriptableObject.CreateInstance<BuildingDefSO>();
                        so.CopyFrom(def);
                        AssetDatabase.CreateAsset(so, path);
                    }
                    else
                    {
                        // Asset already exists: do NOT overwrite. The SO is now the
                        // authoritative, hand-editable source; JSON is deprecated. The
                        // generator only seeds MISSING assets so re-running it can never
                        // clobber tuned/doc-aligned values.
                    }
                    buildingSOs.Add(so);
                    buildingCount++;
                }

                // 5. Catalog (create or refresh).
                catalog = AssetDatabase.LoadAssetAtPath<TechTreeCatalog>(CatalogPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<TechTreeCatalog>();
                    catalog.units = unitSOs;
                    catalog.buildings = buildingSOs;
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
                else
                {
                    catalog.units = unitSOs;
                    catalog.buildings = buildingSOs;
                    EditorUtility.SetDirty(catalog);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[TechTreeSOGenerator] Generated/updated {unitCount} unit + " +
                      $"{buildingCount} building SOs and catalog at {CatalogPath}.");
            return catalog;
        }

        // Most Age-1 culture content is prefixed (Runai_/Alanthor_/Feraldis_/Sect_).
        // A handful of culture buildings are not, so map them explicitly.
        static readonly Dictionary<string, string> BuildingCultureExceptions = new Dictionary<string, string>
        {
            { "FiendstoneKeep",  "Feraldis" },
            { "KingsCourt",      "Alanthor" },
            { "ThessarasBazaar", "Runai"    },
        };

        /// <summary>Culture subfolder name for a unit/building id (Age0 = pre-culture / human core).</summary>
        static string CultureFolder(string id)
        {
            if (id.StartsWith("Runai_"))    return "Runai";
            if (id.StartsWith("Alanthor_")) return "Alanthor";
            if (id.StartsWith("Feraldis_")) return "Feraldis";
            if (id.StartsWith("Sect_"))     return "Sect";
            if (BuildingCultureExceptions.TryGetValue(id, out var culture)) return culture;
            return "Age0";
        }

        /// <summary>
        /// Resolve where an asset should live. Returns the target culture-folder path,
        /// creating the folder if needed. If a same-named asset already exists elsewhere
        /// under <paramref name="searchRoot"/> (e.g. a previous flat-folder run), it is
        /// moved into the culture folder so re-runs reorganize rather than orphan.
        /// </summary>
        static string ResolveTargetPath(string searchRoot, string typeFilter, string fileName, string targetFolder)
        {
            string targetPath = $"{targetFolder}/{fileName}.asset";
            EnsureFolder(targetFolder);

            // Already at the target location?
            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(targetPath) != null) return targetPath;

            // Existing asset of this name somewhere else under the root? Move it.
            var guids = AssetDatabase.FindAssets($"{fileName} t:{typeFilter}", new[] { searchRoot });
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) != fileName) continue; // exact match only
                if (p == targetPath) return targetPath;
                string err = AssetDatabase.MoveAsset(p, targetPath);
                return string.IsNullOrEmpty(err) ? targetPath : p; // fall back to existing path if move fails
            }

            return targetPath;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int lastSlash = path.LastIndexOf('/');
            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string Sanitize(string id)
        {
            // IDs are already file-safe (letters/digits/underscore) but guard anyway.
            var chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
#endif
