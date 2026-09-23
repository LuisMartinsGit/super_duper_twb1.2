// LauncherGate.cs
// A game started from an install without going through the launcher hands
// over to the launcher and exits.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// WHY. The launcher is what keeps a tester on the current build. Starting
    /// "The Waning Border.exe" directly — from a pinned shortcut into game\,
    /// from Explorer, from a file search — skips the update check, and a peer
    /// on a stale build desyncs against a current one the moment they differ.
    /// So a game that was not started by the launcher starts the launcher
    /// instead and quits; the launcher updates if it must and starts the game
    /// properly.
    ///
    /// HOW THE LAUNCHER IS RECOGNISED. It sets the environment variable named
    /// by <see cref="EnvVar"/> on the process it starts. Environment variables
    /// pass to children and nothing else sets this one, so a game that sees it
    /// was started by the launcher (or by something that deliberately claims
    /// to be — this is a guard against accidents, not a lock).
    ///
    /// WHEN IT STAYS OUT OF THE WAY, each on purpose:
    ///   - the Editor: nothing to hand over to;
    ///   - batch mode: the headless harness and the desync hunt start builds
    ///     directly and would be killed on the spot;
    ///   - <c>-twbNoLauncher</c> on the command line: the developer's escape hatch;
    ///   - no launcher above game\: a raw build folder (D:\Builds\TWB_x.y.z) is
    ///     not an install. Only an install has game\ under a root that holds
    ///     TWBLauncher.exe, which is the layout AppPaths in the launcher lays
    ///     down and LauncherSelfUpdate already relies on.
    ///
    /// THE GAME EXITS ONLY ONCE THE LAUNCHER IS SEEN RUNNING. The one thing
    /// this must never do is leave a tester with nothing: if the launcher
    /// cannot be started, or exits within <see cref="ConfirmSeconds"/>, the
    /// game logs it and simply continues as before. And it exits by ending
    /// the process, not by Application.Quit alone: at BeforeSceneLoad a
    /// deferred quit request was observed to be dropped and the game carried
    /// on into the menu with the launcher beside it. Nothing has been loaded
    /// at this point, so there is nothing an abrupt exit can lose.
    /// </summary>
    public static class LauncherGate
    {
        public const string EnvVar = "TWB_LAUNCHER";
        private const string LauncherName = "TWBLauncher.exe";
        private const string NoLauncherFlag = "-twbNoLauncher";
        private const float ConfirmSeconds = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
#if UNITY_EDITOR
            return;
#else
            try
            {
                if (Application.isBatchMode) return;
                if (Array.IndexOf(Environment.GetCommandLineArgs(), NoLauncherFlag) >= 0) return;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar))) return;

                string gameDir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(gameDir)) return;
                if (!string.Equals(Path.GetFileName(gameDir), "game", StringComparison.OrdinalIgnoreCase)) return;

                string root = Path.GetDirectoryName(gameDir);
                if (string.IsNullOrEmpty(root)) return;

                string launcher = Path.Combine(root, LauncherName);
                if (!File.Exists(launcher)) return;

                UnityEngine.Debug.Log("[LauncherGate] Started without the launcher — starting " + launcher);

                // UseShellExecute = false: a direct CreateProcess, no shell
                // association or verb lookup in between, and a real Process
                // object to watch. Nothing is inherited that the launcher
                // needs, and the marker variable is deliberately NOT set on
                // it — the launcher sets it on the game it starts.
                var started = Process.Start(new ProcessStartInfo(launcher)
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                });

                if (started == null)
                {
                    UnityEngine.Debug.Log("[LauncherGate] Process.Start returned nothing; continuing without the launcher.");
                    return;
                }

                // Give it a moment to prove it is up. A launcher that dies at
                // once (missing runtime, blocked exe) must not take the game
                // with it.
                var deadline = DateTime.UtcNow.AddSeconds(ConfirmSeconds);
                while (DateTime.UtcNow < deadline)
                {
                    if (started.HasExited)
                    {
                        UnityEngine.Debug.Log($"[LauncherGate] The launcher exited at once (code {started.ExitCode}); continuing without it.");
                        return;
                    }
                    Thread.Sleep(100);
                }

                UnityEngine.Debug.Log($"[LauncherGate] Launcher running (pid {started.Id}); this instance exits.");
                Application.Quit();
                Environment.Exit(0);
            }
            catch (Exception e)
            {
                // A gate that cannot run must not stop the game. The tester
                // plays what they have; the launcher catches up next time.
                UnityEngine.Debug.Log("[LauncherGate] Could not hand over to the launcher " +
                                      $"({e.GetType().Name}: {e.Message}); continuing.");
            }
#endif
        }
    }
}
