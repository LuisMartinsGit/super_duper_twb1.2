// MapBuilderBrokenOath.cs
// EDITOR-ONLY: build "Broken Oath" — the three on open low ground, the two on a plateau above an escarpment.
//   Waning Border > Maps > Build Broken Oath (768x448m, 5 players, 3v2)
//
// THE DESIGN (docs/Design/Maps/Broken_Oath.md, 2026-10-18): a 3v2 map. The
// three (seats 1-3, west) stand on open low ground, homes close together,
// with one-ramp cliff pockets; the two (seats 4-5, east) hold the Highmarch,
// a plateau with four ramps up the escarpment, more safe ground each but
// poor. The middle is low, open and rich, with the Oathstone (a raised
// Sanctum, four ramps). Mirrored north/south only; rectangular (SizeZ).
//
// THE LAYOUT IS DATA: tools/maps/broken_oath/layout.py (over tools/maps/layoutkit.py)
// writes heights.r16, the masks and layout.json with --export;
// MapBuilderFromLayout builds the scene with the settings in
// LayoutMapStandardSpec. Cliffs block by slope; mountains and forest carry
// NoWalk paint; the WaterPlane's wadeDepth lets armies wade the fords.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderBrokenOath
    {
        private static readonly LayoutMapSpec Spec =
            LayoutMapStandardSpec.Make("Broken Oath", "BrokenOath", "tools/maps/broken_oath", 0xB205, 90000);

        [MenuItem("Waning Border/Maps/Build Broken Oath (768x448m, 5 players, 3v2)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(Spec.MapName,
                    $"Generate {Spec.MapName}?\n\n  768x448m, 5 players, 3v2\n" +
                    $"  Layout from {Spec.LayoutFolder}/ (run layout.py --export first)\n\n" +
                    $"Overwrites {Spec.SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            MapBuilderFromLayout.Build(Spec);
        }

        [MenuItem("Waning Border/Maps/Build + Bake Broken Oath")]
        public static void BuildAndBake() => MapBuilderFromLayout.BuildAndBake(Spec);
    }
}
