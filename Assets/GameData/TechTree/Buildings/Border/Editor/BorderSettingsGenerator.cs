// BorderSettingsGenerator.cs
// Editor tool that seeds Assets/Resources/BorderSettings.asset with the shipped
// defaults (9 army tiers + economy/AI tuning). Mirrors TechTreeSOGenerator.
//
// Idempotent: if the asset already exists it is NOT overwritten (your hand-tuned
// values are authoritative) — use "Reset Border Settings to Defaults" to force it.
//
// Location: Assets/GameData/TechTree/Buildings/Border/Editor/BorderSettingsGenerator.cs

#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Data.Border.Editor
{
    public static class BorderSettingsGenerator
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string AssetPath = "Assets/Resources/BorderSettings.asset";

        [MenuItem("Waning Border/Border/Generate Border Settings")]
        public static void Generate()
        {
            EnsureResourcesFolder();

            var existing = AssetDatabase.LoadAssetAtPath<BorderSettingsSO>(AssetPath);
            if (existing != null)
            {
                EditorUtility.DisplayDialog(
                    "Border Settings",
                    "Assets/Resources/BorderSettings.asset already exists — leaving your values " +
                    "untouched.\n\nUse 'Waning Border ▸ Border ▸ Reset Border Settings to Defaults' " +
                    "to overwrite it with the shipped 9-tier table.",
                    "OK");
                Selection.activeObject = existing;
                return;
            }

            var so = ScriptableObject.CreateInstance<BorderSettingsSO>();
            so.ResetToDefaults();
            AssetDatabase.CreateAsset(so, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BorderSettings.Reload();

            Selection.activeObject = so;
            Debug.Log($"[BorderSettings] Created {AssetPath} with {so.TierCount} default army tiers.");
        }

        [MenuItem("Waning Border/Border/Reset Border Settings to Defaults")]
        public static void ResetToDefaults()
        {
            var so = AssetDatabase.LoadAssetAtPath<BorderSettingsSO>(AssetPath);
            if (so == null)
            {
                Generate();
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Reset Border Settings",
                    "Overwrite Assets/Resources/BorderSettings.asset with the shipped defaults? " +
                    "This discards your current tuning.",
                    "Reset", "Cancel"))
                return;

            so.ResetToDefaults();
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            BorderSettings.Reload();
            Debug.Log("[BorderSettings] Reset to shipped defaults.");
        }

        private static void EnsureResourcesFolder()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            {
                if (!Directory.Exists(ResourcesFolder))
                    AssetDatabase.CreateFolder("Assets", "Resources");
            }
        }
    }
}

#endif // UNITY_EDITOR
