// MapBuilderTutorialBasic.cs
// EDITOR-ONLY: build the Basic tutorial mission's scene.
//   Waning Border > Maps > Build Tutorial Basic (320x192m, 2 territories)
//
// docs/Design/Tutorial_Missions.md. The layout is DATA:
// tools/maps/tutorial_basic/layout.py --export writes it. The scene lives under
// MapRegistry.TutorialRoot, so it ships and is never a lobby map, which also
// means it has no lobby assets to bake.

using UnityEditor;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderTutorialBasic
    {
        private static readonly LayoutMapSpec Spec = LayoutMapStandardSpec.Make(
            "TutorialBasic", "TutorialBasic", "tools/maps/tutorial_basic", 0x7B01, 90000,
            folderOverride: MapRegistry.TutorialRoot + "TutorialBasic");

        [MenuItem("Waning Border/Maps/Build Tutorial Basic (320x192m, 2 territories)")]
        public static void Build() => MapBuilderFromLayout.Build(Spec);
    }
}
