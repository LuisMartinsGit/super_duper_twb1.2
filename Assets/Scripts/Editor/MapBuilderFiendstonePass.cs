// MapBuilderFiendstonePass.cs
// EDITOR-ONLY: build "Fiendstone Pass" — the 1024 m, 6-player cliff-shelf map.
//   Waning Border > Maps > Build Fiendstone Pass (1024m, 6 players)
//
// THE DESIGN (docs/Design/Maps/Fiendstone_Pass.md, 2026-10-16): a river
// across the middle with three fords; on each bank three low territories,
// two plateaus joined by a mountain pass, and three starts on cliff shelves
// above them. Every step between levels is a CLIFF (a 4.5 m drop, steeper
// than the 45-degree walk limit) except at the ramps — 24 m chokes where
// archers on the shelf above shoot down on whoever climbs. Fourteen small
// rich pockets, each one ramp in: eight neutral, one beside each start.
// The south bank is the north bank turned 180 degrees, exactly.
//
// THE LAYOUT IS DATA. tools/maps/fiendstone_pass/layout.py authors it and
// writes heights.r16, the masks and layout.json beside itself;
// MapBuilderFromLayout turns those files into a scene. This file only says
// how this map paints and plants:
//
//   * NoWalk paint: nowalk.png — mountains and forest. Cliffs need no paint;
//     their slope blocks them (PassabilityGrid.MaxWalkableSlope).
//   * the river: the WaterPlane; nothing deeper than its wadeDepth walks. The
//     fords sit just UNDER the surface (2026-10-17) — waded, and unclaimable
//     because no territory outline covers the river.
//   * forest: trees planted solid inside forest.png, inside or across
//     territories — NEVER a territory of its own (Regions.md § Region kinds).
//   * the second ground layer is MOSS (meadow.png) in the hollows; bare.png
//     adds worn earth to the dirt layer.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderFiendstonePass
    {
        private static readonly LayoutMapSpec Spec = new LayoutMapSpec
        {
            MapName = "Fiendstone Pass",
            SceneName = "FiendstonePass",
            Folder = "Assets/GameData/Scenes/Maps/Fiendstone Pass",
            LayoutFolder = "tools/maps/fiendstone_pass",
            BoolMasks = new[] { "nowalk.png", "forest.png", "dirt.png", "fringe.png" },
            GrayMasks = new[] { "meadow.png", "bare.png" },
            Paint = map => new MapGenKit.PaintSpec
            {
                MapFolder = "Assets/GameData/Scenes/Maps/Fiendstone Pass",
                Size = map.Size,
                NoWalk = (x, z) => map.Mask("nowalk.png", x, z) ? 1f : 0f,
                Dirt = (x, z) => map.Mask("dirt.png", x, z) ? 0.85f : map.Gray("bare.png", x, z),
                Meadow = (x, z) => map.Gray("meadow.png", x, z),
                MeadowSubstance = "MossSubstance001_COMPILED",
                // The top shelf sits at 23 m and must read as ground, not rock:
                // rock is the cliff faces (steepness) and the mountains above it.
                RockSteepFrom = 30f, RockSteepTo = 45f,
                RockHeightFrom = 26f, RockHeightTo = 30f,
            },
            Flora = map => new MapGenKit.FloraSpec
            {
                MapFolder = "Assets/GameData/Scenes/Maps/Fiendstone Pass",
                Size = map.Size,
                Seed = 0xF1E2,
                // Attempts, not instances: acceptance is the forest mask (planted
                // nearly solid) plus a thin scatter of rocks along the mountain
                // feet. ~4% of the map is forest, so this lands ~5-6k props.
                TreeCount = 160000,
                TreeScale = 0.5f,
                GrassScale = 2.0f,
                DetailDensity = 2,
                CanPlant = (x, z) =>
                {
                    if (map.Mask("forest.png", x, z)) return true;
                    if (!map.Mask("fringe.png", x, z)) return false;
                    // a thin scatter of rocks along the mountain feet
                    uint hsh = (uint)(Mathf.FloorToInt(x) * 73856093) ^ (uint)(Mathf.FloorToInt(z) * 19349663);
                    return (hsh % 100) < 18;
                },
                // Forest grows on every level up to the top shelf (23 m); only
                // the mountains get rocks.
                RockAboveHeight = 27f,
                RockAboveSteep = 50f,
            },
            // River-scale starting values (the tool is tuned for a 70 m deep
            // ocean; this river is 2.4 m deep, so the waves must stay low).
            WaterMaterialName = "River Water",
            InitWaterMaterial = mat =>
            {
                mat.SetFloat("_Depth", 3f);
                mat.SetFloat("_Foam_depth", 0.6f);
                mat.SetFloat("_Waves_height", 0.12f);
                mat.SetColor("_Wave_Direction", new Color(1f, 0.1f, 0f, 0f));
            },
        };

        [MenuItem("Waning Border/Maps/Build Fiendstone Pass (1024m, 6 players)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(Spec.MapName,
                    $"Generate {Spec.MapName}?\n\n" +
                    "  1024 x 1024 m, 6 players, cliff shelves over a river\n" +
                    $"  Layout from {Spec.LayoutFolder}/ (run layout.py first)\n\n" +
                    $"Overwrites {Spec.SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            MapBuilderFromLayout.Build(Spec);
        }

        [MenuItem("Waning Border/Maps/Build + Bake Fiendstone Pass")]
        public static void BuildAndBake() => MapBuilderFromLayout.BuildAndBake(Spec);

        [MenuItem("Waning Border/Maps/Apply River Water To Fiendstone Pass")]
        public static void ApplyRiverWaterToScene() => MapBuilderFromLayout.ApplyWaterToScene(Spec);
    }
}
