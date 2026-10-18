// MapBuilderTrineHollow.cs
// EDITOR-ONLY: build "Trine Hollow" — a bowl turned three ways: rim homes, terraces, a rich basin.
//   Waning Border > Maps > Build Trine Hollow (640x640m, 3 players)
//
// THE DESIGN (docs/Design/Maps/Trine_Hollow.md, 2026-10-18): three-fold
// rotation. Each home sits on the rim with two high pockets; a ring of
// terraces below is cut between seats by mountain spurs, each spur carrying a
// raised Sanctum saddle (the back way between neighbours); the basin floor is
// open and rich, with a cursed mesa in the centre. No water.
//
// THE LAYOUT IS DATA: tools/maps/trine_hollow/layout.py (over tools/maps/layoutkit.py)
// writes heights.r16, the masks and layout.json with --export;
// MapBuilderFromLayout builds the scene with the settings in
// LayoutMapStandardSpec. Cliffs block by slope; mountains and forest carry
// NoWalk paint; the WaterPlane's wadeDepth lets armies wade the fords.

using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderTrineHollow
    {
        private static readonly LayoutMapSpec Spec =
            LayoutMapStandardSpec.Make("Trine Hollow", "TrineHollow", "tools/maps/trine_hollow", 0x7303, 90000);

        [MenuItem("Waning Border/Maps/Build Trine Hollow (640x640m, 3 players)")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(Spec.MapName,
                    $"Generate {Spec.MapName}?\n\n  640x640m, 3 players\n" +
                    $"  Layout from {Spec.LayoutFolder}/ (run layout.py --export first)\n\n" +
                    $"Overwrites {Spec.SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            MapBuilderFromLayout.Build(Spec);
        }

        [MenuItem("Waning Border/Maps/Build + Bake Trine Hollow")]
        public static void BuildAndBake() => MapBuilderFromLayout.BuildAndBake(Spec);
    }
}
