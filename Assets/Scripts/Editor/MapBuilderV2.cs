// MapBuilderV2.cs
// EDITOR-ONLY: build the v2 maps — the half-size redesigns of 2026-11-14.
//   Waning Border > Maps > v2 > Build + Bake <map>
//
// WHY v2: units were halved (radius and speed); the 2 m build grid and every
// building footprint were not, on purpose, so the first maps became too large.
// Each v2 keeps its map's CONCEPT on half the ground with the players closer,
// redrawn so every territory still holds its nodes, a Farm with its whole
// field and Houses (tools/maps/layoutkit.py's building-room probe). The
// originals stay as they are, beside them.
//
// THE LAYOUT IS DATA: tools/maps/<map>_v2/layout.py --export writes
// heights.r16, the masks and layout.json; MapBuilderFromLayout builds the
// scene with LayoutMapStandardSpec (Fiendstone Pass v2 included: it is drawn
// on the shared kit, not Fiendstone's own generator). Each scene is
// "<Original>_v2" in "<Original folder>_v2", dressed in its OWN biome
// (TerrainBiomeDress, the v2 biomes: fen, alpine, canyon, mossy woods, autumn).

using UnityEditor;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderV2
    {
        // Tree ATTEMPTS (acceptance is the forest / fringe masks): denser than the
        // originals' per square metre, and 45 % of the mountain fringe planted
        // (18 % elsewhere) — the 2026-11-14 reskin's trees and rocks. Each v2
        // map has its own biome in TerrainBiomeDress (props re-dressed there).
        private static LayoutMapSpec Moatwatch => LayoutMapStandardSpec.Make(
            "Moatwatch_v2", "Moatwatch_v2", "tools/maps/moatwatch_v2", 0x4A02, 30000, "Moatwatch v2", fringePercent: 45);
        private static LayoutMapSpec Highfold => LayoutMapStandardSpec.Make(
            "Highfold_v2", "Highfold_v2", "tools/maps/highfold_v2", 0x4F02, 60000, "Highfold v2", fringePercent: 45);
        private static LayoutMapSpec BrokenOath => LayoutMapStandardSpec.Make(
            "Broken Oath_v2", "BrokenOath_v2", "tools/maps/broken_oath_v2", 0x5B02, 40000, "Broken Oath v2", fringePercent: 45);
        private static LayoutMapSpec TrineHollow => LayoutMapStandardSpec.Make(
            "Trine Hollow_v2", "TrineHollow_v2", "tools/maps/trine_hollow_v2", 0x7302, 40000, "Trine Hollow v2", fringePercent: 45);
        private static LayoutMapSpec FiendstonePass => LayoutMapStandardSpec.Make(
            "Fiendstone Pass_v2", "FiendstonePass_v2", "tools/maps/fiendstone_pass_v2", 0xF502, 60000, "Fiendstone Pass v2", fringePercent: 45);

        [MenuItem("Waning Border/Maps/v2/Build + Bake Moatwatch v2 (256 m, 4P)")]
        public static void BuildMoatwatch() => MapBuilderFromLayout.BuildAndBake(Moatwatch);

        [MenuItem("Waning Border/Maps/v2/Build + Bake Highfold v2 (384 m, 8P)")]
        public static void BuildHighfold() => MapBuilderFromLayout.BuildAndBake(Highfold);

        [MenuItem("Waning Border/Maps/v2/Build + Bake Broken Oath v2 (384 x 224 m, 5P)")]
        public static void BuildBrokenOath() => MapBuilderFromLayout.BuildAndBake(BrokenOath);

        [MenuItem("Waning Border/Maps/v2/Build + Bake Trine Hollow v2 (320 m, 3P)")]
        public static void BuildTrineHollow() => MapBuilderFromLayout.BuildAndBake(TrineHollow);

        [MenuItem("Waning Border/Maps/v2/Build + Bake Fiendstone Pass v2 (512 m, 6P)")]
        public static void BuildFiendstonePass() => MapBuilderFromLayout.BuildAndBake(FiendstonePass);

        /// <summary>Batch entry: every v2 map whose layout has been exported
        /// (-executeMethod TheWaningBorder.Core.Maps.EditorTools.MapBuilderV2.BuildAll).</summary>
        public static void BuildAll()
        {
            foreach (var spec in new[] { Moatwatch, Highfold, BrokenOath, TrineHollow, FiendstonePass })
            {
                string json = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
                                                     spec.LayoutFolder, "layout.json");
                if (!System.IO.File.Exists(json))
                {
                    UnityEngine.Debug.LogWarning($"[MapBuilderV2] {spec.MapName}: no layout.json yet, skipped");
                    continue;
                }
                MapBuilderFromLayout.BuildAndBake(spec);
            }
        }

        /// <summary>Batch entry: open each built v2 scene and photograph it
        /// (BiomeShotProbe -> Temp/biome-shots/), to judge the biomes.</summary>
        public static void ShootAll()
        {
            foreach (var spec in new[] { Moatwatch, Highfold, BrokenOath, TrineHollow, FiendstonePass })
            {
                string scene = $"{spec.Folder}/{spec.SceneName}.unity";
                if (!System.IO.File.Exists(scene)) continue;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene);
                TheWaningBorder.EditorTools.BiomeShotProbe.Shoot();
            }
            // Temp/ is wiped when a batch-mode editor exits: keep the shots.
            System.IO.Directory.CreateDirectory("Build/_shots");
            foreach (var f in System.IO.Directory.GetFiles("Temp/biome-shots", "*_v2_*.png"))
                System.IO.File.Copy(f, System.IO.Path.Combine("Build/_shots", System.IO.Path.GetFileName(f)), true);
        }

        /// <summary>Batch entry for one map by scene name (-twbV2Map Moatwatch_v2).</summary>
        public static void BuildOne()
        {
            string want = null;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-twbV2Map") want = args[i + 1];
            foreach (var spec in new[] { Moatwatch, Highfold, BrokenOath, TrineHollow, FiendstonePass })
                if (spec.SceneName == want) { MapBuilderFromLayout.BuildAndBake(spec); return; }
            UnityEngine.Debug.LogError($"[MapBuilderV2] no v2 map called \"{want}\"");
        }
    }
}
