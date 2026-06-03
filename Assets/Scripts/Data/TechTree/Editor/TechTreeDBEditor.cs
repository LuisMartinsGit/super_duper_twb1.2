// TechTreeDBEditor.cs
// EDITOR-ONLY custom inspector for the TechTreeDB component.
// Part of: Data/TechTree/Editor/
//
// Adds a one-click "Generate Stat SOs + Assign Catalog" button so the editable
// UnitDefSO/BuildingDefSO assets are created from TechTree.json AND wired to this
// TechTreeDB in a single step — then you can browse Assets/GameData/TechTree to
// tune stats in the Inspector.
//
// Wrapped in #if UNITY_EDITOR (single runtime asmdef, no separate editor assembly).

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Data.EditorTools
{
    [CustomEditor(typeof(TechTreeDB))]
    public class TechTreeDBEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ScriptableObject Stats", EditorStyles.boldLabel);

            var catalogProp = serializedObject.FindProperty("catalog");
            bool hasCatalog = catalogProp != null && catalogProp.objectReferenceValue != null;

            EditorGUILayout.HelpBox(
                hasCatalog
                    ? "Catalog assigned: unit/building stats load from the SO assets under " +
                      "Assets/GameData/TechTree. Edit those assets to tune stats (even in Play mode)."
                    : "No catalog assigned: stats load from TechTree.json. Click below to generate " +
                      "editable SO assets and assign them here.",
                hasCatalog ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Generate Stat SOs from JSON + Assign Catalog", GUILayout.Height(28)))
            {
                var catalog = TechTreeSOGenerator.Build(out int unitCount, out int buildingCount, out string error);
                if (error != null)
                {
                    EditorUtility.DisplayDialog("Tech Tree SO Generator", error, "OK");
                }
                else
                {
                    serializedObject.Update();
                    catalogProp.objectReferenceValue = catalog;
                    serializedObject.ApplyModifiedProperties();

                    EditorUtility.DisplayDialog("Tech Tree SO Generator",
                        $"Generated {unitCount} unit + {buildingCount} building SO assets and assigned the catalog.\n\n" +
                        "Browse Assets/GameData/TechTree/Units and /Buildings to edit stats.", "OK");
                    if (catalog != null) EditorGUIUtility.PingObject(catalog);
                }
            }

            if (hasCatalog && Application.isPlaying && GUILayout.Button("Reload Stats From Catalog (live)"))
            {
                ((TechTreeDB)target).ReloadFromCatalog();
            }
        }
    }
}
#endif
