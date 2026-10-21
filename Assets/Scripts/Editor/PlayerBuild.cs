// PlayerBuild.cs
// Command-line entry point for producing a Windows player.
//
// Lives in the TheWaningBorder.Editor assembly (Editor platform only), so
// it no longer needs a #if UNITY_EDITOR guard -- it cannot reach a player
// build at all. Before the split it sat inside the runtime asmdef, where
// the Editor/ folder convention does not apply and the guard was the only
// thing keeping UnityEditor out of the shipped assembly.
//
//   Unity.exe -quit -batchmode -nographics ^
//     -projectPath "<project>" ^
//     -executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build ^
//     -buildPath "C:\Users\...\TWB_LATEST" ^
//     -twbEdition A ^
//     -logFile "<project>\logs\build.log"
//
// The editor must be CLOSED: Unity takes an exclusive lock on a project, and
// a second instance refuses to open one that is already open.
//
// -- Why this exists ------------------------------------------------------
// Builds were made by hand from the Build Settings window, which meant the
// release script's -BuildPath pointed at whatever a human last produced.
// tools/release.ps1 reads the version from Player Settings, so a stale folder
// packaged after a version bump ships the OLD binary under the NEW number,
// straight to testers, with nothing to catch it.
//
// -- The scene list is not EditorBuildSettings verbatim -------------------
// MapSceneSync registers a scene filter through
// BuildPlayerWindow.RegisterBuildPlayerHandler, and that only covers builds
// started from the Build Settings WINDOW. A -executeMethod build calls
// BuildPipeline directly and never reaches it. Both routes now go through
// MapSceneSync.ScenesForPlayerBuild, or this one would quietly ship the two
// dozen scenario scenes the ship gate exists to keep out.

using System;
using System.IO;
using System.Linq;
using TheWaningBorder.Core.Maps.EditorTools;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class PlayerBuild
    {
        private const string PathArg = "-buildPath";
        private const string EditionArg = "-twbEdition";

        /// <summary>
        /// The edition of the build in progress, or null when no build was
        /// started through here. EditionBuildGate reads it to refuse a build
        /// from the Build Settings window (which would carry no edition define
        /// and so ship every culture), and AlphaBuildPostProcess reads it to
        /// carry the launcher of the same edition.
        /// </summary>
        public static string ActiveEdition { get; private set; }

        public static void Build()
        {
            string outDir = ArgValue(PathArg);
            if (string.IsNullOrEmpty(outDir))
            {
                Fail($"No {PathArg} given. Pass {PathArg} \"C:\\path\\to\\output\".");
                return;
            }

            string edition = (ArgValue(EditionArg) ?? "A").Trim().ToUpperInvariant();
            string error = BuildTo(outDir, edition);
            if (error != null) { Fail(error); return; }
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// The build itself, callable from a RUNNING editor (the bridge's
        /// `build` command uses it) as well as from batch mode. Returns null
        /// on success, else the reason. Same scene gate, same options, same
        /// output layout as the batch path — it IS the batch path minus the
        /// process exit.
        /// </summary>
        /// <summary>Set by the bridge's build-dev command: the next build is a
        /// development (profiling) player, as -twbDevelopment does in batch mode.</summary>
        public static bool DevelopmentOnce;

        public static string BuildTo(string outDir, string edition = "A")
        {
            // A = the demo (Age 0 + Alanthor), B = + Feraldis, C = + Runai.
            // The define is what GameEdition reads at runtime; the catalog
            // strip (EditionContent) is what keeps the other cultures' assets
            // out of the build. docs/Design/Alpha_Build.md § Editions.
            if (edition != "A" && edition != "B" && edition != "C")
                return $"Unknown edition '{edition}'. Pass {EditionArg} A, B or C.";

            try
            {
                Directory.CreateDirectory(outDir);
            }
            catch (Exception e)
            {
                return $"Cannot create or write {outDir}: {e.Message}";
            }

            // Enabled scenes only, then through the ship gate — see the header.
            var scenes = MapSceneSync.ScenesForPlayerBuild(
                EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                edition);

            if (scenes == null || scenes.Length == 0)
                return "Build Settings has no enabled scenes.";

            // Scene 0 is the boot scene. Saying so in the log has caught a
            // reordered list more than once.
            Debug.Log($"[PlayerBuild] {scenes.Length} scene(s); boot scene is '{scenes[0]}'.");

            string exe = Path.Combine(outDir, PlayerSettings.productName + ".exe");
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                extraScriptingDefines = new[] { "TWB_EDITION_" + edition },
                // -twbDevelopment: a profiling player (ENABLE_PROFILER on, so
                // EcsSystemProfiler can time every system). Never shipped.
                options = DevelopmentOnce || Array.IndexOf(Environment.GetCommandLineArgs(), "-twbDevelopment") >= 0
                    ? BuildOptions.Development : BuildOptions.None,
            };

            // The edition rides in the version string ("0.0.41B"), so the
            // lobby handshake, replays and every match log tell editions apart
            // without a field of their own, and a demo player can never join a
            // developer lobby. Restored afterwards: ProjectSettings keeps the
            // bare version, which is what release.ps1 reads.
            string baseVersion = PlayerSettings.bundleVersion;
            string builtVersion = edition == "A" ? baseVersion : baseVersion + edition;

            BuildReport report;
            using (var strip = EditionContent.Strip(edition))
            {
                if (strip.Error != null) return strip.Error;

                ActiveEdition = edition;
                PlayerSettings.bundleVersion = builtVersion;
                Debug.Log($"[PlayerBuild] Building {PlayerSettings.productName} " +
                          $"{builtVersion} (edition {edition}) -> {exe}");
                try
                {
                    report = BuildPipeline.BuildPlayer(options);
                }
                catch (Exception e)
                {
                    return $"BuildPipeline threw: {e}";
                }
                finally
                {
                    // Restoring in memory is NOT enough: the build saves project
                    // settings to disk while the suffixed version is set (seen
                    // 2026-10-12 — ProjectSettings.asset read "0.0.41B" after a
                    // B build, and the next DEMO release went out tagged
                    // v0.0.41B). Save the restored value back over it.
                    PlayerSettings.bundleVersion = baseVersion;
                    AssetDatabase.SaveAssets();
                    ActiveEdition = null;
                }
            }

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                // Name the first error rather than making someone read the
                // whole editor log to find out why a batch build stopped.
                string first = report.steps
                    .SelectMany(s => s.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content)
                    .FirstOrDefault();

                return $"Build {summary.result}. {summary.totalErrors} error(s)." +
                       (first == null ? "" : $" First: {first}");
            }

            // Proof, not trust: anything from a stripped culture that still
            // reached the build (a new reference path the strip does not know)
            // fails it, and with no edition.txt release.ps1 will not ship it.
            var leaks = EditionContent.FindLeaks(report, edition);
            if (leaks.Count > 0)
            {
                foreach (var leak in leaks) Debug.LogError("[PlayerBuild] Edition leak: " + leak);
                return $"Edition {edition} build carries {leaks.Count} asset(s) from a culture it must not ship " +
                       $"(first: {leaks[0]}). Find what references it and extend EditionContent.";
            }

            // release.ps1 refuses to publish a build whose stamp does not match
            // its -Edition: the last line of defence against shipping B as A.
            File.WriteAllText(Path.Combine(outDir, "edition.txt"), edition);

            Debug.Log($"[PlayerBuild] SUCCESS — {summary.outputPath}, " +
                      $"{summary.totalSize / 1048576} MB, " +
                      $"{summary.totalTime.TotalMinutes:F1} min, " +
                      $"{summary.totalWarnings} warning(s).");
            return null;
        }

        /// <summary>Value of a `-flag value` pair on Unity's command line.</summary>
        private static string ArgValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        /// <summary>
        /// Log and exit NON-ZERO. Batch mode ignores a thrown exception's exit
        /// code in some Unity versions, so a build that failed would report
        /// success to the shell and the release script would happily package
        /// whatever stale files were in the output folder.
        /// </summary>
        private static void Fail(string message)
        {
            Debug.LogError("[PlayerBuild] " + message);
            EditorApplication.Exit(1);
        }
    }
}
