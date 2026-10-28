// TreeSizePass.cs
// One-shot pass over every map's baked terrain trees (2026-10-28: "change
// tree size to 50 % of the current size"). TerrainData is binary-serialised,
// so the instances cannot be edited as text; this scales widthScale /
// heightScale of every TreeInstance in place and saves the asset. The
// authoring knobs were halved in the same change (MapGenKit FloraSpec
// .TreeScale 0.5 -> 0.25, the three map specs, RegionTreePlanter
// .CanopyWidth 5 -> 2.5 m), so a REBUILT map lands at the same size and
// this pass must not be run on it again.
//
// Batch: Unity.exe -batchmode -nographics -quit -projectPath <proj>
//        -executeMethod TheWaningBorder.EditorTools.TreeSizePass.HalveAllBatch

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class TreeSizePass
    {
        const string MapsRoot = "Assets/GameData/Scenes/Maps";

        [MenuItem("Waning Border/Maps/Scale Baked Trees x0.5 (one-shot — every run halves again)")]
        public static void HalveAll() => ScaleAll(0.5f);

        /// <summary>Batch entry (-executeMethod).</summary>
        public static void HalveAllBatch() => ScaleAll(0.5f);

        public static void ScaleAll(float factor)
        {
            var guids = AssetDatabase.FindAssets("t:TerrainData", new[] { MapsRoot });
            int assets = 0, trees = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (data == null) continue;
                var list = data.treeInstances;
                if (list == null || list.Length == 0) { Debug.Log($"[TreeSizePass] {path}: no trees"); continue; }
                for (int i = 0; i < list.Length; i++)
                {
                    list[i].widthScale *= factor;
                    list[i].heightScale *= factor;
                }
                // snapToHeightmap false: positions are unchanged, and a snap
                // would re-sample heights the planter already set.
                data.SetTreeInstances(list, false);
                EditorUtility.SetDirty(data);
                assets++; trees += list.Length;
                Debug.Log($"[TreeSizePass] {path}: {list.Length} trees x{factor:0.##}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[TreeSizePass] done: {trees} trees in {assets} terrain assets scaled x{factor:0.##}");
        }
    }
}
