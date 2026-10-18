// MapBuilderHighfold.cs
// EDITOR-ONLY: build "Highfold" — four mountain-valley seats, four open-field seats, a rich centre.
//   Waning Border > Maps > Build Highfold (768x768m, 8 players)
//
// THE DESIGN (docs/Design/Maps/Highfold.md, 2026-10-18): seats 1-4 sit in a
// valley in each corner massif (home, two high pockets, a ledge, ONE ramp to
// the fields): easy to hold, poor. Seats 5-8 sit in the open fields around
// the rich centre (curse, iron-rich, veilstone, four Sanctum barrows round a
// pond). Edge hollows between the massifs, one ramp each. Mirrored on both
// axes.
//
// THE LAYOUT IS DATA: tools/maps/highfold/layout.py (over tools/maps/layoutkit.py)
// writes heights.r16, the masks and layout.json with --export;
// MapBuilderFromLayout builds the scene with the settings in
// LayoutMapStandardSpec. Cliffs block by slope; mountains and forest carry
// NoWalk paint; the WaterPlane's wadeDepth lets armies wade the fords.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderHighfold
    {
        private static readonly LayoutMapSpec Spec =
            LayoutMapStandardSpec.Make("Highfold", "Highfold", "tools/maps/highfold", 0x4F08, 140000);

        [MenuItem("Waning Border/Maps/Build Highfold (768x768m, 8 players)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(Spec.MapName,
                    $"Generate {Spec.MapName}?\n\n  768x768m, 8 players\n" +
                    $"  Layout from {Spec.LayoutFolder}/ (run layout.py --export first)\n\n" +
                    $"Overwrites {Spec.SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            MapBuilderFromLayout.Build(Spec);
        }

        [MenuItem("Waning Border/Maps/Build + Bake Highfold")]
        public static void BuildAndBake() => MapBuilderFromLayout.BuildAndBake(Spec);
    }
}
