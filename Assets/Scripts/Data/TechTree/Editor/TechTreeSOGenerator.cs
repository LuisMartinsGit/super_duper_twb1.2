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
        const string CatalogPath     = "Assets/GameData/TechTree/TechTreeCatalog.asset";

        [MenuItem("Waning Border/Tech Tree/Generate Stat SOs from JSON")]
        public static void Generate()
        {
            // 1. Load JSON text.
            var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonAssetPath);
            if (jsonAsset == null || string.IsNullOrWhiteSpace(jsonAsset.text))
            {
                EditorUtility.DisplayDialog("Tech Tree SO Generator",
                    $"Could not find/read JSON at {JsonAssetPath}.", "OK");
                return;
            }

            // 2. Parse via the SAME parser the runtime uses (no value drift).
            var parsed = TechTreeParser.ParseAll(jsonAsset.text);
            if (parsed.Units.Count == 0 && parsed.Buildings.Count == 0)
            {
                EditorUtility.DisplayDialog("Tech Tree SO Generator",
                    "Parser returned no units or buildings. Aborting.", "OK");
                return;
            }

            // 3. Ensure target folders exist.
            EnsureFolder(RootFolder);
            EnsureFolder(TechTreeFolder);
            EnsureFolder(UnitsFolder);
            EnsureFolder(BuildingsFolder);

            int unitCount = 0, buildingCount = 0;
            var unitSOs = new List<UnitDefSO>();
            var buildingSOs = new List<BuildingDefSO>();

            AssetDatabase.StartAssetEditing();
            try
            {
                // 4a. Units.
                foreach (var def in parsed.Units.Values)
                {
                    if (def == null || string.IsNullOrEmpty(def.id)) continue;
                    string path = $"{UnitsFolder}/Unit_{Sanitize(def.id)}.asset";
                    var so = AssetDatabase.LoadAssetAtPath<UnitDefSO>(path);
                    if (so == null)
                    {
                        so = ScriptableObject.CreateInstance<UnitDefSO>();
                        so.CopyFrom(def);
                        AssetDatabase.CreateAsset(so, path);
                    }
                    else
                    {
                        so.CopyFrom(def);
                        EditorUtility.SetDirty(so);
                    }
                    unitSOs.Add(so);
                    unitCount++;
                }

                // 4b. Buildings.
                foreach (var def in parsed.Buildings.Values)
                {
                    if (def == null || string.IsNullOrEmpty(def.id)) continue;
                    string path = $"{BuildingsFolder}/Building_{Sanitize(def.id)}.asset";
                    var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(path);
                    if (so == null)
                    {
                        so = ScriptableObject.CreateInstance<BuildingDefSO>();
                        so.CopyFrom(def);
                        AssetDatabase.CreateAsset(so, path);
                    }
                    else
                    {
                        so.CopyFrom(def);
                        EditorUtility.SetDirty(so);
                    }
                    buildingSOs.Add(so);
                    buildingCount++;
                }

                // 5. Catalog (create or refresh).
                var catalog = AssetDatabase.LoadAssetAtPath<TechTreeCatalog>(CatalogPath);
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
                      $"{buildingCount} building SOs and catalog at {CatalogPath}. " +
                      $"Assign the catalog to the TechTreeDB component to activate SO-driven stats.");
            EditorUtility.DisplayDialog("Tech Tree SO Generator",
                $"Done.\n\nUnits: {unitCount}\nBuildings: {buildingCount}\n\n" +
                $"Catalog: {CatalogPath}\n\nNext: assign the catalog to the TechTreeDB component.", "OK");

            // Ping the catalog so the user sees it.
            var created = AssetDatabase.LoadAssetAtPath<TechTreeCatalog>(CatalogPath);
            if (created != null) EditorGUIUtility.PingObject(created);
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
