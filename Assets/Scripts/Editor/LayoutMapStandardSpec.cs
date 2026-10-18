// LayoutMapStandardSpec.cs
// EDITOR-ONLY: the paint / flora / water settings shared by the maps drawn
// with tools/maps/layoutkit.py (Moatwatch, Highfold, Broken Oath, Trine
// Hollow — 2026-10-18). Their layout.py exports the same mask set Fiendstone
// Pass uses (nowalk / forest / dirt / fringe on/off, meadow / bare 0..1) and
// the same level heights (layoutkit.LEVEL_H: 5 m + 4.5 m a level, mountains
// from 34 m), so one spec shape fits all four; each map's builder only names
// itself and picks a flora seed.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class LayoutMapStandardSpec
    {
        public static LayoutMapSpec Make(string mapName, string sceneName, string layoutFolder, int floraSeed,
            int treeAttempts, string displayName = null)
        {
            string folder = $"Assets/GameData/Scenes/Maps/{mapName}";
            return new LayoutMapSpec
            {
                MapName = mapName,
                DisplayName = displayName,
                SceneName = sceneName,
                Folder = folder,
                LayoutFolder = layoutFolder,
                BoolMasks = new[] { "nowalk.png", "forest.png", "dirt.png", "fringe.png" },
                GrayMasks = new[] { "meadow.png", "bare.png" },
                Paint = map => new MapGenKit.PaintSpec
                {
                    MapFolder = folder,
                    Size = map.Size,
                    NoWalk = (x, z) => map.Mask("nowalk.png", x, z) ? 1f : 0f,
                    Dirt = (x, z) => map.Mask("dirt.png", x, z) ? 0.85f : map.Gray("bare.png", x, z),
                    Meadow = (x, z) => map.Gray("meadow.png", x, z),
                    MeadowSubstance = "MossSubstance001_COMPILED",
                    // The highest ground (level 4 pockets) tops out near 25 m and
                    // must read as ground; rock is the cliff faces and the
                    // mountains, which start at 34 m.
                    RockSteepFrom = 30f, RockSteepTo = 45f,
                    RockHeightFrom = 28f, RockHeightTo = 33f,
                },
                Flora = map => new MapGenKit.FloraSpec
                {
                    MapFolder = folder,
                    Size = map.Size,
                    Seed = floraSeed,
                    // Attempts, not instances: acceptance is the forest mask
                    // (planted nearly solid) plus a thin scatter of rocks along
                    // the mountain feet.
                    TreeCount = treeAttempts,
                    TreeScale = 0.5f,
                    GrassScale = 2.0f,
                    DetailDensity = 2,
                    CanPlant = (x, z) =>
                    {
                        if (map.Mask("forest.png", x, z)) return true;
                        if (!map.Mask("fringe.png", x, z)) return false;
                        uint hsh = (uint)(Mathf.FloorToInt(x) * 73856093) ^ (uint)(Mathf.FloorToInt(z) * 19349663);
                        return (hsh % 100) < 18;
                    },
                    RockAboveHeight = 30f,
                    RockAboveSteep = 50f,
                },
                // Fiendstone Pass's hand-tuned river look (deep blue-teal, no
                // waves, fine caustics) suits a moat or a pond as well.
                WaterMaterialName = "Water",
                InitWaterMaterial = mat =>
                {
                    var river = AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/GameData/Scenes/Maps/Fiendstone Pass/River Water.mat");
                    if (river == null) { Debug.LogError($"[{mapName}] Fiendstone Pass/River Water.mat not found"); return; }
                    mat.CopyPropertiesFromMaterial(river);
                    mat.shaderKeywords = river.shaderKeywords;
                },
            };
        }
    }
}
