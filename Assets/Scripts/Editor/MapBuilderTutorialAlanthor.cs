// MapBuilderTutorialAlanthor.cs
// EDITOR-ONLY: build the Alanthor tutorial mission's scene.
//   Waning Border > Maps > Build Tutorial Alanthor (384x256m, 8 territories)
//
// docs/Design/Tutorial_Missions.md section 4. The layout is DATA:
// tools/maps/tutorial_alanthor/layout.py --export writes it. The scene lives under
// MapRegistry.TutorialRoot, so it ships and is never a lobby map.

using UnityEditor;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderTutorialAlanthor
    {
        private static readonly LayoutMapSpec Spec = LayoutMapStandardSpec.Make(
            "TutorialAlanthor", "TutorialAlanthor", "tools/maps/tutorial_alanthor", 0x7B02, 60000,
            folderOverride: MapRegistry.TutorialRoot + "TutorialAlanthor");

        [MenuItem("Waning Border/Maps/Build Tutorial Alanthor (384x256m, 8 territories)")]
        public static void Build() => MapBuilderFromLayout.Build(Spec);
    }
}
