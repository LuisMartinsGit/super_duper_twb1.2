// LauncherSelfUpdate.cs
// Upgrades the tester's launcher from a copy carried inside the build.
// Location: Assets/Scripts/Bootstrap/LauncherSelfUpdate.cs

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Copies the launcher shipped in this build over the one in the install
    /// root, when they differ.
    ///
    /// WHY THE GAME DOES THIS. The install looks like:
    ///
    ///     The Waning Border\
    ///       TWBLauncher.exe     never touched by an update
    ///       game\               everything an update replaces
    ///
    /// The launcher sits outside <c>game\</c> deliberately — Windows locks a
    /// running executable, so an updater beside the files it replaces could
    /// never overwrite itself. The consequence is that a launcher fix could
    /// only reach a tester by asking them to download one by hand. The game
    /// can do it instead: by the time this runs the launcher has already
    /// exited (MainForm.Launch calls Close() straight after starting us), so
    /// its exe is not locked.
    ///
    /// Deliberately conservative:
    ///   - only ever REPLACES an existing launcher, never creates one, so a
    ///     build run directly out of a folder does not sprout an exe;
    ///   - compares by hash, so it is a no-op on every launch but the first
    ///     after a launcher change;
    ///   - writes beside the target and swaps, so a failure cannot leave a
    ///     half-written launcher;
    ///   - swallows everything. A tester whose launcher could not be replaced
    ///     still has a working one, and the next launch tries again.
    /// </summary>
    public static class LauncherSelfUpdate
    {
        private const string LauncherName = "TWBLauncher.exe";

        /// <summary>
        /// Seconds to wait before touching the file. The launcher closes its
        /// window as it starts the game, but process teardown is not
        /// instantaneous and the exe stays locked until it finishes. Failing
        /// is harmless — this runs again next launch — but succeeding first
        /// time means the tester is on the new launcher immediately.
        /// </summary>
        private const int SettleSeconds = 5;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
#if UNITY_EDITOR
            // dataPath is <project>/Assets in the editor, so "one level up" is
            // the project root. Nothing to upgrade, and plenty to damage.
            return;
#else
            // Off the main thread: this sleeps, hashes two files and copies a
            // few hundred KB, none of which the first frame should wait for.
            var thread = new Thread(Run) { IsBackground = true, Name = "LauncherSelfUpdate" };
            thread.Start();
#endif
        }

        private static void Run()
        {
            try
            {
                string gameDir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(gameDir)) return;

                string shipped = Path.Combine(gameDir, LauncherName);
                if (!File.Exists(shipped)) return;   // build predates the carry

                string root = Path.GetDirectoryName(gameDir);
                if (string.IsNullOrEmpty(root)) return;

                string installed = Path.Combine(root, LauncherName);

                // Only ever an UPGRADE. If there is no launcher up there this
                // is not a launcher install, and dropping an exe next to the
                // game folder would be a surprise at best.
                if (!File.Exists(installed)) return;

                if (SameContents(shipped, installed)) return;

                Thread.Sleep(SettleSeconds * 1000);

                // Write beside the target, then swap. File.Copy straight over
                // a locked or in-use exe can truncate it, and a truncated
                // launcher is a tester who cannot start the game at all.
                string staged = installed + ".new";
                File.Copy(shipped, staged, overwrite: true);
                File.Replace(staged, installed, null, ignoreMetadataErrors: true);

                Debug.Log("[LauncherSelfUpdate] Updated the launcher in the install root. " +
                          "The new one is used from the next launch.");
            }
            catch (Exception e)
            {
                // Includes the launcher still holding its exe. Next launch
                // retries; nothing the player needs to know about.
                Debug.Log("[LauncherSelfUpdate] Left the launcher alone " +
                          $"({e.GetType().Name}: {e.Message}). Will retry next launch.");
                TryCleanup();
            }
        }

        private static bool SameContents(string a, string b)
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length) return false;   // cheap reject first

            return Hash(a) == Hash(b);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream));
        }

        private static void TryCleanup()
        {
            try
            {
                string gameDir = Path.GetDirectoryName(Application.dataPath);
                string root = gameDir == null ? null : Path.GetDirectoryName(gameDir);
                if (root == null) return;

                string staged = Path.Combine(root, LauncherName + ".new");
                if (File.Exists(staged)) File.Delete(staged);
            }
            catch
            {
                // Best effort; a stray .new file is harmless and gets
                // overwritten on the next attempt.
            }
        }
    }
}
