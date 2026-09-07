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

        public static void Build()
        {
            string outDir = ArgValue(PathArg);
            if (string.IsNullOrEmpty(outDir))
            {
                Fail($"No {PathArg} given. Pass {PathArg} \"C:\\path\\to\\output\".");
                return;
            }

            string error = BuildTo(outDir);
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
        public static string BuildTo(string outDir)
        {
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
                EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray());

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
                options = BuildOptions.None,
            };

            Debug.Log($"[PlayerBuild] Building {PlayerSettings.productName} " +
                      $"{PlayerSettings.bundleVersion} -> {exe}");

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                return $"BuildPipeline threw: {e}";
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
