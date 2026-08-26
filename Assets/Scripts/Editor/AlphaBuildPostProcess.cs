// AlphaBuildPostProcess.cs
// Ships a logs/ folder (and a note for testers) next to the built executable.

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    /// <summary>
    /// The game creates its logs folder at runtime anyway
    /// (<see cref="LogPaths"/>), but shipping it means a tester opening the
    /// game folder sees "logs" immediately and knows what to send back —
    /// rather than having to be told it appears only after they play.
    ///
    /// Also drops a README so the folder explains itself without the tester
    /// having to ask what any of it is.
    /// </summary>
    public sealed class AlphaBuildPostProcess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private const string ReadmeName = "README - please read.txt";

        public void OnPostprocessBuild(BuildReport report)
        {
            string exePath = report.summary.outputPath;
            if (string.IsNullOrEmpty(exePath)) return;

            string buildRoot = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(buildRoot)) return;

            try
            {
                string logs = Path.Combine(buildRoot, "logs");
                Directory.CreateDirectory(logs);
                File.WriteAllText(Path.Combine(logs, ReadmeName), ReadmeText());
                Debug.Log($"[AlphaBuildPostProcess] Prepared logs folder at {logs}");
            }
            catch (System.Exception e)
            {
                // A build must not fail because a convenience folder could not
                // be written — the game recreates it at runtime regardless.
                Debug.LogWarning($"[AlphaBuildPostProcess] Could not prepare the logs "
                                 + $"folder: {e.GetType().Name}: {e.Message}");
            }

            CarryLauncher(buildRoot);
        }

        /// <summary>
        /// Ship the launcher INSIDE the build, so the game can upgrade the
        /// copy sitting in the install root (see LauncherSelfUpdate).
        ///
        /// The launcher lives outside game\ and an update never touches it, so
        /// there was no way to get a new one to a tester short of asking them
        /// to download it by hand. Carrying it in the build costs ~150 KB and
        /// removes that step entirely.
        /// </summary>
        private static void CarryLauncher(string buildRoot)
        {
            const string LauncherName = "TWBLauncher.exe";
            string source = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "tools", "Launcher", "publish", LauncherName));

            if (!File.Exists(source))
            {
                // Not fatal: a build without it simply cannot upgrade the
                // launcher, which is exactly the old behaviour.
                Debug.LogWarning($"[AlphaBuildPostProcess] No launcher at {source} — this build "
                                 + "will not be able to update a tester's launcher. Run "
                                 + "tools/Launcher/publish.ps1 first.");
                return;
            }

            try
            {
                File.Copy(source, Path.Combine(buildRoot, LauncherName), overwrite: true);
                Debug.Log($"[AlphaBuildPostProcess] Carried {LauncherName} into the build.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AlphaBuildPostProcess] Could not carry the launcher: "
                                 + $"{e.GetType().Name}: {e.Message}");
            }
        }

        private static string ReadmeText() =>
@"THE WANING BORDER - alpha test logs
===================================

Thank you for testing!

Every match you play writes a folder in here, named with the date, time and
map, for example:

    2026-08-13_21-04-11_SunderedCrown/

Inside each one:

    Summary.txt   how the match ended, how long it ran, and how many
                  errors/exceptions happened
    Console.log   every warning, error and crash, with stack traces
    Perf.log      moments where the game stuttered
    AI_*.log      what each AI opponent was thinking
    Player_*.log  your own economy over time

Multiplayer matches also write:

    Lockstep.log       the tick-by-tick record both machines should agree on
    Desync_tick*.log   written only if the two games went out of sync

THESE ARE SENT TO ME AUTOMATICALLY
----------------------------------
You do not need to zip anything or email me. When a match ends, that match
folder is sent to me; if the game crashes before it can, the launcher sends
it the next time you start. Only the files described above are sent, and only
from matches - nothing else on your computer is touched.

Your copy stays here either way. Nothing is deleted, so you can always open
these yourself.

If you would rather they were not sent, tell me and I will turn it off for
your key.

WHAT I STILL NEED FROM YOU
--------------------------
Tell me, in your own words, what you were doing when something went wrong.
That plus Console.log is usually all it takes to find a bug, and it is the
one thing the logs cannot tell me.

Multiplayer desyncs used to need the match folder from BOTH machines, which
was awkward to organise. Now that both sides send their own, I get both
halves on my own - just mention that it happened.
";
    }
}
