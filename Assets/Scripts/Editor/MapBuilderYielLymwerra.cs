// MapBuilderYielLymwerra.cs
// EDITOR-ONLY: build "Yiel Lymwérra" — the 384 x 192 m, 2-player crescent-bay map.
//   Waning Border > Maps > Build Yiel Lymwerra (384x192m, 2 players)
//
// THE DESIGN (docs/Design/Maps/Yiel_Lymwerra.md, 2026-10-17): mountains in
// the north, a grass plateau with a start on each side of a round bay, a
// coastal cliff down to the beaches and the sea in the south. The bay is
// SHALLOWS — waded, never built on — ringed by cliffs, with two ramps down
// from the plateau and a mouth to the sea; the cursed island Yiel sits in
// its middle and the curse ALWAYS starts there (RegionSeedMarker.CurseStaysHere).
// The east half is the west half mirrored. 384 x 192 m (redrawn 2026-10-17
// from 1024 x 1024): a rectangular terrain, TerrainSpec.SizeZ.
//
// THE LAYOUT IS DATA: tools/maps/yiel_lymwerra/layout.py writes heights.r16,
// the masks and layout.json; MapBuilderFromLayout builds the scene. This file
// only says how the map paints and plants:
//
//   * NoWalk paint: nowalk.png — mountains and forest. Cliffs block by slope.
//   * the sea and the bay: one WaterPlane. Nothing deeper than its wadeDepth
//     walks, so the shallows are waded and the sea is not. The shallows ARE
//     territories (two, split through the island) so the island borders
//     something — without them it could never be claimed — but water
//     forbids building at any depth.
//   * the second ground layer is SAND here (GroundSubstance003), not meadow:
//     sand.png — beaches, the bay floor, the island's shore. Dirt (dirt.png)
//     is the ramps; bare.png adds worn patches.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderYielLymwerra
    {
        private const string Folder = "Assets/GameData/Scenes/Maps/Yiel Lymwerra";

        private static readonly LayoutMapSpec Spec = new LayoutMapSpec
        {
            MapName = "Yiel Lymwerra",
            DisplayName = "Yiel Lymwérra",
            SceneName = "YielLymwerra",
            Folder = Folder,
            LayoutFolder = "tools/maps/yiel_lymwerra",
            BoolMasks = new[] { "nowalk.png", "forest.png", "dirt.png", "fringe.png", "grass.png" },
            GrayMasks = new[] { "sand.png", "bare.png" },
            Paint = map => new MapGenKit.PaintSpec
            {
                MapFolder = Folder,
                Size = map.Size,
                NoWalk = (x, z) => map.Mask("nowalk.png", x, z) ? 1f : 0f,
                Dirt = (x, z) => map.Mask("dirt.png", x, z) ? 0.85f : map.Gray("bare.png", x, z),
                Meadow = (x, z) => map.Gray("sand.png", x, z),
                MeadowSubstance = "GroundSubstance003_COMPILED",
                // The Sanctum pocket tops out near 14 m and must read as
                // ground; rock is the cliff faces and the mountains (22 m up).
                RockSteepFrom = 30f, RockSteepTo = 45f,
                RockHeightFrom = 18f, RockHeightTo = 24f,
            },
            Flora = map => new MapGenKit.FloraSpec
            {
                MapFolder = Folder,
                Size = map.Size,
                Seed = 0x7E11,
                // Attempts, not instances: acceptance is the forest mask
                // (planted nearly solid) plus rocks along the mountain feet.
                TreeCount = 60000,
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
                // Grass blades on the plateau and the island's crown — never on
                // sand, never under water.
                CanGrass = (x, z) => map.Mask("grass.png", x, z),
                RockAboveHeight = 18f,
                RockAboveSteep = 50f,
            },
            // The sea wears Fiendstone Pass's hand-tuned river look (deep
            // blue-teal, waves and foam off, fine caustics). The ocean
            // material's own values (large caustics, waves on) band into
            // stripes across a sea this size.
            WaterMaterialName = "Sea Water",
            InitWaterMaterial = mat =>
            {
                var river = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/GameData/Scenes/Maps/Fiendstone Pass/River Water.mat");
                if (river == null) { Debug.LogError("[Yiel Lymwerra] Fiendstone Pass/River Water.mat not found"); return; }
                mat.CopyPropertiesFromMaterial(river);
                mat.shaderKeywords = river.shaderKeywords;
            },
        };

        [MenuItem("Waning Border/Maps/Build Yiel Lymwerra (384x192m, 2 players)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(Spec.MapName,
                    $"Generate {Spec.MapName}?\n\n" +
                    "  384 x 192 m, 2 players, a crescent bay with a cursed island\n" +
                    $"  Layout from {Spec.LayoutFolder}/ (run layout.py first)\n\n" +
                    $"Overwrites {Spec.SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            MapBuilderFromLayout.Build(Spec);
        }

        [MenuItem("Waning Border/Maps/Build + Bake Yiel Lymwerra")]
        public static void BuildAndBake() => MapBuilderFromLayout.BuildAndBake(Spec);

        [MenuItem("Waning Border/Maps/Apply Sea Water To Yiel Lymwerra")]
        public static void ApplySeaWaterToScene() => MapBuilderFromLayout.ApplyWaterToScene(Spec);
    }
}
