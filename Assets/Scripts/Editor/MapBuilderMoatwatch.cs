// MapBuilderMoatwatch.cs
// EDITOR-ONLY: build "Moatwatch" — four moated corner keeps, two waded fords each, an open rich middle.
//   Waning Border > Maps > Build Moatwatch (512x512m, 4 players)
//
// THE DESIGN (docs/Design/Maps/Moatwatch.md, 2026-10-18): each player starts
// in a corner KEEP inside a moat (deep water) crossed by two waded fords that
// face the middle. Inside: the home and two small garths, poor but safe.
// Outside: one big open expanse cut into many small territories, poor by the
// moats and the edges, rich toward a rock tor in the centre, with a raised
// barrow (the Sanctum) in each quadrant. Mirrored on both axes.
//
// THE LAYOUT IS DATA: tools/maps/moatwatch/layout.py (over tools/maps/layoutkit.py)
// writes heights.r16, the masks and layout.json with --export;
// MapBuilderFromLayout builds the scene with the settings in
// LayoutMapStandardSpec. Cliffs block by slope; mountains and forest carry
// NoWalk paint; the WaterPlane's wadeDepth lets armies wade the fords.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderMoatwatch
    {
        private static readonly LayoutMapSpec Spec =
            LayoutMapStandardSpec.Make("Moatwatch", "Moatwatch", "tools/maps/moatwatch", 0x4A01, 60000);

        [MenuItem("Waning Border/Maps/Build Moatwatch (512x512m, 4 players)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(Spec.MapName,
                    $"Generate {Spec.MapName}?\n\n  512x512m, 4 players\n" +
                    $"  Layout from {Spec.LayoutFolder}/ (run layout.py --export first)\n\n" +
                    $"Overwrites {Spec.SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            MapBuilderFromLayout.Build(Spec);
        }

        [MenuItem("Waning Border/Maps/Build + Bake Moatwatch")]
        public static void BuildAndBake() => MapBuilderFromLayout.BuildAndBake(Spec);
    }
}
