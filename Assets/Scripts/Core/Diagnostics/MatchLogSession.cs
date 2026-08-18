// MatchLogSession.cs
// One folder of logs per match, plus console/exception capture, for alpha
// testers to send back after a play session.
// Location: Assets/Scripts/Core/Diagnostics/MatchLogSession.cs

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    /// <summary>
    /// Gives every match its own folder under <see cref="LogPaths.Directory"/>
    /// and captures the Unity console into it.
    ///
    /// Why this exists: the AI and player logs used to be written flat into
    /// <c>logs/</c> and <b>deleted at the start of every match</b>
    /// (AILogger.Initialize). For a developer running one match at a time that
    /// is fine; for an alpha tester it means only the LAST match survives, and
    /// a crash-then-relaunch destroys the very logs that would explain the
    /// crash. Nothing is deleted now — each match gets a timestamped folder.
    ///
    /// Console capture is the other half. Unity's own Player.log lives in
    /// %USERPROFILE%\AppData\LocalLow\... which testers will not find, so
    /// warnings, errors and stack traces are mirrored into the match folder
    /// alongside everything else.
    /// </summary>
    public static class MatchLogSession
    {
        /// <summary>How many match folders to keep before pruning the oldest.
        /// A play session is a handful of matches; this only stops an
        /// unattended build from filling a disk.</summary>
        private const int KeepMatchFolders = 30;

        private const string ConsoleFileName = "Console.log";
        private const string SummaryFileName = "Summary.txt";

        private static string _matchFolder;
        private static StreamWriter _console;
        private static readonly object _consoleLock = new object();
        private static bool _hooked;
        private static float _matchStartTime;
        private static int _errors, _warnings, _exceptions;

        /// <summary>
        /// Folder the current match's logs go in. Before any match begins this
        /// is the logs root, so boot-time output still lands somewhere useful.
        /// </summary>
        public static string CurrentFolder =>
            string.IsNullOrEmpty(_matchFolder) ? LogPaths.Directory : _matchFolder;

        /// <summary>Full path for a file in the current match's folder.</summary>
        public static string File(string fileName) => Path.Combine(CurrentFolder, fileName);

        // ── Console capture ─────────────────────────────────────────────

        /// <summary>
        /// Start mirroring the Unity console to disk. Installed at load so it
        /// catches menu and bootstrap failures, not just in-match ones.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallConsoleCapture()
        {
            if (_hooked) return;
            _hooked = true;

            // Threaded variant: jobs and background threads log too, and those
            // are exactly the messages a Burst/DOTS bug produces.
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;

            OpenConsole();
            WriteConsoleLine($"=== Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            WriteConsoleLine($"Build: {Application.productName} {Application.version} "
                           + $"({Application.platform}, Unity {Application.unityVersion})");
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:     _errors++; break;
                case LogType.Assert:    _errors++; break;
                case LogType.Exception: _exceptions++; break;
                case LogType.Warning:   _warnings++; break;
                default: break;
            }

            // Keep warnings/errors/exceptions, plus TAGGED plain logs — the
            // "[Something] ..." diagnostics the systems deliberately emit
            // ([BootTrace], [NavGrid], [TerrainCostBake], [PlayerSpawnSystem],
            // …). Those are the lines that say what the game decided, and
            // filtering all LogType.Log threw them away: the first tester logs
            // came back unable to answer "was the nav grid rebuilt for match 2"
            // because the answer had been dropped.
            //
            // Safe on volume: TWBLog (the verbose channel) compiles out of
            // player builds entirely, so the tagged Debug.Log calls that remain
            // are few and deliberate. Untagged logs are still skipped.
            if (type == LogType.Log
                && (condition == null || condition.Length == 0 || condition[0] != '[')) return;

            var sb = new StringBuilder();
            sb.Append('[').Append(Time.realtimeSinceStartup.ToString("0.0")).Append("s] ")
              .Append(type.ToString().ToUpperInvariant()).Append(": ")
              .Append(condition);
            if (!string.IsNullOrEmpty(stackTrace))
                sb.Append('\n').Append(stackTrace.TrimEnd());

            WriteConsoleLine(sb.ToString());
        }

        private static void WriteConsoleLine(string line)
        {
            lock (_consoleLock)
            {
                if (_console == null) return;
                try
                {
                    _console.WriteLine(line);
                    // Flushed per line on purpose: the messages that matter
                    // most are the ones immediately before a hard crash, and a
                    // buffered writer loses exactly those.
                    _console.Flush();
                }
                catch { /* diagnostics must never throw into the game */ }
            }
        }

        private static void OpenConsole()
        {
            lock (_consoleLock)
            {
                CloseConsoleUnlocked();
                try
                {
                    // Instance-discriminated: two copies of the game sharing one
                    // logs folder (Unity's Multiplayer Play Mode virtual players,
                    // or two exes in the same directory) would otherwise both
                    // open Console.log — the second fails and that instance logs
                    // nothing for the rest of the session.
                    _console = new StreamWriter(
                        Path.Combine(CurrentFolder, LogPaths.InstanceFileName(ConsoleFileName)),
                        append: true);
                }
                catch { _console = null; }
            }
        }

        private static void CloseConsoleUnlocked()
        {
            if (_console == null) return;
            try { _console.Flush(); _console.Dispose(); } catch { }
            _console = null;
        }

        // ── Match lifecycle ─────────────────────────────────────────────

        /// <summary>
        /// Open a fresh folder for a match. Safe to call twice; the second
        /// call closes the first match out.
        /// </summary>
        public static void Begin(string mapName, string extraHeader = null)
        {
            End(null);   // close any match left open by an abnormal exit

            string safeMap = Sanitise(string.IsNullOrEmpty(mapName) ? "Map" : mapName);

            // In multiplayer the folder name says WHICH PEER wrote it. Two
            // instances start a match a second or two apart, so timestamps
            // alone leave you guessing which folder is the host's — and the
            // whole point of these logs is comparing the two side by side.
            string role = "";
            if (GameSettings.IsMultiplayer)
            {
                role = GameSettings.IsHost()
                    ? "_host"
                    : $"_client{LogPaths.InstanceSlot}";
            }
            else if (LogPaths.InstanceSlot > 0)
            {
                role = LogPaths.InstanceSuffix;
            }

            string folder = Path.Combine(LogPaths.Directory,
                                         $"{LogPaths.TimestampNow()}_{safeMap}{role}");
            try
            {
                Directory.CreateDirectory(folder);
                _matchFolder = folder;
            }
            catch
            {
                _matchFolder = null;   // fall back to the logs root
            }

            _matchStartTime = Time.realtimeSinceStartup;
            _errors = _warnings = _exceptions = 0;

            // Perf.log caches its resolved path for the whole session; make it
            // re-resolve against this match's folder.
            PerfSpikeLog.Reset();

            // Re-point console capture at the new match folder.
            OpenConsole();
            WriteConsoleLine($"=== Match started {DateTime.Now:yyyy-MM-dd HH:mm:ss} "
                           + $"on {mapName} ===");
            if (!string.IsNullOrEmpty(extraHeader)) WriteConsoleLine(extraHeader);

            Prune();
        }

        /// <summary>
        /// Close the current match's folder and write its summary. Safe to call
        /// when no match is open.
        /// </summary>
        public static void End(string outcome)
        {
            if (string.IsNullOrEmpty(_matchFolder)) return;

            float duration = Time.realtimeSinceStartup - _matchStartTime;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Match summary ===");
                sb.AppendLine($"Ended       : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Duration    : {(int)(duration / 60f)}m {duration % 60f:00.0}s");
                sb.AppendLine($"Outcome     : {(string.IsNullOrEmpty(outcome) ? "unfinished / quit" : outcome)}");
                sb.AppendLine($"Exceptions  : {_exceptions}");
                sb.AppendLine($"Errors      : {_errors}");
                sb.AppendLine($"Warnings    : {_warnings}");
                sb.AppendLine($"Build       : {Application.productName} {Application.version}");

                // The version is hand-typed in Player Settings, so two different
                // builds can carry the same one. The fingerprint is what makes a
                // report point at exactly one build, and it is machine-readable
                // for the log uploader.
                sb.AppendLine($"Fingerprint : {BuildFingerprint.Short}");
                System.IO.File.WriteAllText(Path.Combine(_matchFolder, SummaryFileName), sb.ToString());
            }
            catch { }

            WriteConsoleLine($"=== Match ended ({outcome ?? "unfinished"}) ===");

            // Fire-and-forget, after Summary.txt exists so the upload can
            // describe the match. Only reaches the network if this install came
            // from the launcher; the launcher re-sweeps anything that fails or
            // that a crash prevented from ever running.
            MatchLogUploader.Send(_matchFolder);

            _matchFolder = null;
            OpenConsole();   // back to the logs root for menu-time output
        }

        private static void OnQuitting()
        {
            End("quit");
            lock (_consoleLock)
            {
                WriteConsoleLineUnlocked($"=== Session ended {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                CloseConsoleUnlocked();
            }
        }

        private static void WriteConsoleLineUnlocked(string line)
        {
            if (_console == null) return;
            try { _console.WriteLine(line); _console.Flush(); } catch { }
        }

        // ── Housekeeping ────────────────────────────────────────────────

        /// <summary>
        /// Delete the oldest MATCH folders beyond the keep limit.
        ///
        /// Only folders this class created are ever considered — matched by the
        /// exact timestamp prefix it names them with. An unfiltered
        /// GetDirectories() here would be genuinely destructive: in the editor
        /// the logs root is the project's own <c>logs</c> folder, which on
        /// Windows is the same directory as Unity's case-insensitive
        /// <c>Logs</c>, so pruning would delete Unity's editor logs. In a
        /// shipped build it would delete whatever else a player put there.
        /// </summary>
        private static void Prune()
        {
            try
            {
                var root = new DirectoryInfo(LogPaths.Directory);
                if (!root.Exists) return;

                var all = root.GetDirectories();
                var mine = new System.Collections.Generic.List<DirectoryInfo>(all.Length);
                foreach (var d in all)
                    if (LooksLikeMatchFolder(d.Name)) mine.Add(d);

                if (mine.Count <= KeepMatchFolders) return;

                mine.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); // name == timestamp
                for (int i = 0; i < mine.Count - KeepMatchFolders; i++)
                {
                    try { mine[i].Delete(recursive: true); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// True only for names this class produces: "yyyy-MM-dd_HH-mm-ss_Map".
        /// Deliberately strict — the cost of a false positive is deleting
        /// someone else's folder.
        /// </summary>
        private static bool LooksLikeMatchFolder(string name)
        {
            // Shortest valid name is the 19-char stamp plus '_' and one char.
            if (string.IsNullOrEmpty(name) || name.Length < 21) return false;
            if (name[4] != '-' || name[7] != '-' || name[10] != '_') return false;
            if (name[13] != '-' || name[16] != '-' || name[19] != '_') return false;

            for (int i = 0; i < 19; i++)
            {
                if (i == 4 || i == 7 || i == 10 || i == 13 || i == 16) continue;
                if (!char.IsDigit(name[i])) return false;
            }
            return true;
        }

        private static string Sanitise(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '-' : c);
            return sb.ToString();
        }
    }
}
