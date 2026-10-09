// SavedGames.cs
// Where replays and saved games live, and the cheap listing the menus show.
// docs/Design/Replays_And_Saves.md

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace TheWaningBorder.Core.Replay
{
    /// <summary>One row of the Saved Games / Replays list.</summary>
    public sealed class SavedGameInfo
    {
        public string Path;
        public bool IsSave;
        public ReplayHeader Header;
        /// <summary>Match time the file reaches: the save marker or the last recorded tick.</summary>
        public float Seconds;
        public string Label = "";
        public string Outcome = "";
        public DateTime Written;
        public bool Compatible => Header != null && Header.Compatible;
    }

    public static class SavedGames
    {
        public const string ReplayExtension = ".twbr";
        public const string SaveExtension = ".twbsave";

        /// <summary>Saved games: beside the executable, like the logs, so a
        /// player can find (and back up) them.</summary>
        public static string SavesDirectory => Resolve("Saves");

        /// <summary>Replays: beside the executable.</summary>
        public static string ReplaysDirectory => Resolve("Replays");

        static readonly Dictionary<string, string> _resolved = new Dictionary<string, string>();

        // Same policy as LogPaths: next to the exe if writable, else the
        // persistent data path (an install under Program Files is read-only).
        static string Resolve(string folder)
        {
            if (_resolved.TryGetValue(folder, out var hit)) return hit;
            string chosen = null;
            try
            {
                string preferred = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", folder));
                Directory.CreateDirectory(preferred);
                chosen = preferred;
            }
            catch { }
            if (chosen == null)
            {
                chosen = System.IO.Path.Combine(Application.persistentDataPath, folder);
                try { Directory.CreateDirectory(chosen); } catch { }
            }
            _resolved[folder] = chosen;
            return chosen;
        }

        /// <summary>
        /// A fresh replay path for a match on <paramref name="map"/>. Headless
        /// batch runs write into the match's log folder (pruned with it, and
        /// picked up with the rest of the run's artefacts); a player's matches
        /// go to Replays/, which keeps the newest <paramref name="keep"/>.
        /// </summary>
        public static string NewReplayPath(string map, int keep)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            if (Application.isBatchMode)
            {
                try
                {
                    string folder = TheWaningBorder.Core.Diagnostics.MatchLogSession.CurrentFolder;
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        return System.IO.Path.Combine(folder, "Replay" + ReplayExtension);
                }
                catch { }
            }

            string dir = ReplaysDirectory;
            Prune(dir, "*" + ReplayExtension, Math.Max(1, keep) - 1);
            return Unique(System.IO.Path.Combine(dir, $"{stamp}_{SafeName(map)}{ReplayExtension}"));
        }

        /// <summary>A fresh path for a saved game.</summary>
        public static string NewSavePath(string map)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            return Unique(System.IO.Path.Combine(SavesDirectory, $"{stamp}_{SafeName(map)}{SaveExtension}"));
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string stem = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),
                System.IO.Path.GetFileNameWithoutExtension(path));
            string ext = System.IO.Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string p = $"{stem}-{i}{ext}";
                if (!File.Exists(p)) return p;
            }
        }

        static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Match";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.ToString();
        }

        static void Prune(string dir, string pattern, int keep)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles(pattern);
                if (files.Length <= keep) return;
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = keep; i < files.Length; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>Saved games, newest first.</summary>
        public static List<SavedGameInfo> ListSaves() => List(SavesDirectory, "*" + SaveExtension);

        /// <summary>Replays, newest first.</summary>
        public static List<SavedGameInfo> ListReplays() => List(ReplaysDirectory, "*" + ReplayExtension);

        static List<SavedGameInfo> List(string dir, string pattern)
        {
            var result = new List<SavedGameInfo>();
            try
            {
                foreach (var f in new DirectoryInfo(dir).GetFiles(pattern))
                {
                    var info = f.Extension == SaveExtension
                        ? TheWaningBorder.Core.Save.SaveGameFile.ReadInfo(f.FullName)
                        : ReadInfo(f.FullName);
                    if (info != null) result.Add(info);
                }
            }
            catch { }
            result.Sort((a, b) => b.Written.CompareTo(a.Written));
            return result;
        }

        /// <summary>
        /// The header plus the closing markers, without parsing the command
        /// body: the header is at the top and every marker the list needs
        /// (last tick, end, save) is in the last few kilobytes.
        /// </summary>
        public static SavedGameInfo ReadInfo(string path)
        {
            try
            {
                var info = new SavedGameInfo
                {
                    Path = path,
                    Header = new ReplayHeader(),
                    Written = File.GetLastWriteTime(path),
                };

                using (var r = new StreamReader(path, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line == "begin") break;
                        info.Header.ReadLine(line.Split('\t'));
                    }
                }
                if (info.Header.Format <= 0) return null;

                int lastTick = -1;
                foreach (var line in TailLines(path, 8192))
                {
                    var p = line.Split('\t');
                    if (p.Length < 2) continue;
                    if (p[0] == "c" || p[0] == "h" || p[0] == "end")
                    {
                        int t = ReplayHeader.Int(p, 1);
                        if (t > lastTick) lastTick = t;
                        if (p[0] == "end") info.Outcome = ReplayHeader.Str(p, 2);
                    }
                    else if (p[0] == "save")
                    {
                        info.IsSave = true;
                        float.TryParse(ReplayHeader.Str(p, 2), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out info.Seconds);
                        info.Label = ReplayHeader.Str(p, 3);
                    }
                }
                if (!info.IsSave && lastTick >= 0 && info.Header.TicksPerSecond > 0)
                    info.Seconds = (lastTick + 1) / (float)info.Header.TicksPerSecond;
                return info;
            }
            catch { return null; }
        }

        static IEnumerable<string> TailLines(string path, int bytes)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, fs.Length - bytes);
            fs.Seek(start, SeekOrigin.Begin);
            using var r = new StreamReader(fs, Encoding.UTF8);
            if (start > 0) r.ReadLine();   // drop the partial first line
            string line;
            var lines = new List<string>();
            while ((line = r.ReadLine()) != null) lines.Add(line);
            return lines;
        }

        public static bool Delete(string path)
        {
            try { File.Delete(path); return true; }
            catch { return false; }
        }
    }
}
