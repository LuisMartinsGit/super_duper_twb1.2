// ScenarioSceneCloner.cs
// Makes the scene a new ScenarioType needs.
//
// ScenarioCatalog.SceneFor is `"Scenario_" + <enum name>`, and each scenario
// owns a dedicated scene under Assets/GameData/Scenes/Scenarios/ WITH ITS OWN
// TerrainData — sharing one would let a terrain edit in one scenario show up
// in another. Cloning both halves and repointing the copy at its own terrain
// is fiddly enough by hand that it was worth writing down once.
//
// MapSceneSync picks the new scene up on import and adds it to Build Settings
// on its own, so nothing here touches that list.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class ScenarioSceneCloner
    {
        private const string Root = "Assets/GameData/Scenes/Scenarios/";
        private const string TerrainDir = Root + "_TerrainData/";

        /// <summary>The scene every clone starts from — a flat, near-empty
        /// scenario stage (terrain + light + the scenario script hook).</summary>
        private const string Template = "Scenario_LongbowmanShowcase";

        [MenuItem("Waning Border/Scenarios/Create Scene For Arrow Trails")]
        public static void CreateArrowTrails() => Clone("ArrowTrails");

        /// <summary>
        /// Clone the template scene into `Scenario_&lt;name&gt;.unity` and give
        /// it a private copy of the terrain. Existing scenes are left alone —
        /// re-running this must never quietly discard an authored stage.
        /// </summary>
        public static void Clone(string scenarioName)
        {
            string scenePath = $"{Root}Scenario_{scenarioName}.unity";
            if (File.Exists(scenePath))
            {
                Debug.Log($"[ScenarioClone] {scenePath} already exists — nothing to do.");
                return;
            }

            string templatePath = $"{Root}{Template}.unity";
            if (!File.Exists(templatePath))
            {
                Debug.LogError($"[ScenarioClone] template scene missing: {templatePath}");
                return;
            }

            if (!AssetDatabase.CopyAsset(templatePath, scenePath))
            {
                Debug.LogError($"[ScenarioClone] could not copy {templatePath} -> {scenePath}");
                return;
            }

            // The copy still points at the TEMPLATE's terrain. Give it its own.
            string srcTerrain = $"{TerrainDir}{Template}_Terrain.asset";
            string dstTerrain = $"{TerrainDir}Scenario_{scenarioName}_Terrain.asset";
            bool ownTerrain = File.Exists(srcTerrain)
                              && (File.Exists(dstTerrain) || AssetDatabase.CopyAsset(srcTerrain, dstTerrain));
            if (!ownTerrain)
                Debug.LogWarning($"[ScenarioClone] no private terrain for {scenarioName} — it will " +
                                 $"share {Template}'s, so terrain edits in one will show in the other.");

            AssetDatabase.Refresh();

            if (ownTerrain)
            {
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dstTerrain);
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        foreach (var t in go.GetComponentsInChildren<Terrain>(true))
                            t.terrainData = data;
                        foreach (var c in go.GetComponentsInChildren<TerrainCollider>(true))
                            c.terrainData = data;
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[ScenarioClone] created {scenePath}" +
                      (ownTerrain ? $" + {dstTerrain}" : "") +
                      ". MapSceneSync will add it to Build Settings on the next import.");
        }
    }
}
