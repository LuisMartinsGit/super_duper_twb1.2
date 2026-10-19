// SaveGameFile.cs
// A saved game: a snapshot of the match AND the replay that led to it.
// docs/Design/Replays_And_Saves.md §3
//
// One .twbsave file, a zip with:
//   save.txt     format (SaveFormatVersion), build (the game version that
//                wrote it), the tick the match resumes at, match seconds,
//                label, the world clock (the fixed-step ElapsedTime, exact)
//   replay.twbr  the replay up to that tick — the resumed match keeps writing
//                it, so a loaded game still produces a whole-match replay
//   world.bin    WorldSnapshot: every simulation entity and component
//   state.bin    SnapshotState: statics and system fields
//   preview.png  the minimap as the player saw it (optional; 2026-10-16)
//   pending.txt  commands already issued for ticks after the save (one tick of
//                input delay), so the first tick after a load runs them
//
// Loading reads ONLY the snapshot (world.bin + state.bin). The replay is
// carried, never re-simulated.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Replay;

namespace TheWaningBorder.Core.Save
{
    public sealed class SaveGameFile
    {
        /// <summary>
        /// THE save-format version — the one place it lives. Any change to the
        /// layout of simulation state (a component or buffer added, removed or
        /// reshaped, a static or system field the snapshot carries) bumps it,
        /// and a save of any other version is refused with a message: a
        /// snapshot restored into a different layout is silently wrong, not
        /// merely old. docs/Design/Replays_And_Saves.md §3 (2026-10-19).
        /// Also mixed into MatchSettingsSync.Fingerprint, so replays recorded
        /// before a bump (which would re-simulate into a different world) are
        /// refused the same way.
        /// </summary>
        // 3 (2026-10-15): PerSectState.UnlockedActives became UnlockMask
        //   (the ECS world layout changed), so format-2 saves are refused.
        // 4 (2026-10-19): faction-wide building levels (FactionBuildingLevel
        //   buffer), PlanOwnership, CelestarPassiveState, Sanctum claim state
        //   and the rest of that day's components — a 0.0.47 snapshot has
        //   none of them.
        public const int SaveFormatVersion = 4;

        /// <summary>Same value; kept for existing callers.</summary>
        public const int CurrentFormat = SaveFormatVersion;

        public int Format = SaveFormatVersion;
        /// <summary>Application.version of the build that wrote the save
        /// (save.txt "build"; saves written before 2026-10-19 fall back to
        /// their replay header's build).</summary>
        public string Build = "";
        public int Tick;
        public float Seconds;
        public string Label = "";
        public double WorldElapsed;
        public string ReplayText = "";
        public byte[] World;
        public byte[] State;
        /// <summary>Minimap bake (PNG) for the Load Game list; optional.</summary>
        public byte[] Preview;
        public readonly List<LockstepCommand> Pending = new List<LockstepCommand>();

        /// <summary>The replay header (and, for the oracle test, the commands).</summary>
        public ReplayFile Replay;

        // ── write ────────────────────────────────────────────────────────

        public void Write(string path)
        {
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var c = CultureInfo.InvariantCulture;
                Text(zip, "save.txt", string.Join("\n",
                    "format\t" + Format.ToString(c),
                    "build\t" + ReplayHeader.Clean(string.IsNullOrEmpty(Build) ? UnityEngine.Application.version : Build),
                    "tick\t" + Tick.ToString(c),
                    "seconds\t" + Seconds.ToString("R", c),
                    "elapsed\t" + WorldElapsed.ToString("R", c),
                    "label\t" + ReplayHeader.Clean(Label)) + "\n");
                Text(zip, "replay.twbr", ReplayText);
                Bytes(zip, "world.bin", World);
                Bytes(zip, "state.bin", State);
                var sb = new StringBuilder();
                foreach (var cmd in Pending)
                    sb.Append("c\t").Append(cmd.Tick.ToString(c)).Append('\t')
                      .Append(cmd.PlayerIndex.ToString(c)).Append('\t').Append(cmd.Serialize()).Append('\n');
                Text(zip, "pending.txt", sb.ToString());
                if (Preview != null && Preview.Length > 0)
                    Bytes(zip, "preview.png", Preview, CompressionLevel.NoCompression);   // PNG is already compressed
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);   // atomic: a crash mid-write never leaves a half save in the list
        }

        static void Text(ZipArchive zip, string name, string text)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(text ?? "");
        }

        static void Bytes(ZipArchive zip, string name, byte[] data,
            CompressionLevel level = CompressionLevel.Optimal)
        {
            var e = zip.CreateEntry(name, level);
            using var s = e.Open();
            if (data != null) s.Write(data, 0, data.Length);
        }

        // ── read ─────────────────────────────────────────────────────────

        /// <summary>The save file exactly as stored — a resumed game embeds it in
        /// its replay, so watching that replay restores the same snapshot.</summary>
        public byte[] RawBytes;

        public static SaveGameFile Load(string path, out string error)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) { error = Loc.T("Could not read the save: ") + e.Message; return null; }
            return Load(bytes, out error);
        }

        public static SaveGameFile Load(byte[] bytes, out string error)
        {
            error = null;
            try
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var save = new SaveGameFile { RawBytes = bytes };
                ReadMeta(zip, save);
                save.ReplayText = ReadText(zip, "replay.twbr");
                save.Replay = ReplayFile.Parse(save.ReplayText.Split('\n'));
                if (string.IsNullOrEmpty(save.Build)) save.Build = save.Replay.Header.Build;
                error = FormatError(save.Format, save.Build);
                if (error != null) return null;
                save.World = ReadBytes(zip, "world.bin");
                save.State = ReadBytes(zip, "state.bin");
                foreach (var line in ReadText(zip, "pending.txt").Split('\n'))
                {
                    var p = line.TrimEnd('\r').Split('\t');
                    if (p.Length < 4 || p[0] != "c") continue;
                    var cmd = LockstepCommand.Deserialize(p[3]);
                    if (cmd == null) continue;
                    cmd.Tick = ReplayHeader.Int(p, 1);
                    cmd.PlayerIndex = ReplayHeader.Int(p, 2);
                    save.Pending.Add(cmd);
                }
                if (save.World == null || save.World.Length == 0)
                {
                    error = Loc.T("The save has no world snapshot.");
                    return null;
                }
                return save;
            }
            catch (InvalidDataException)
            {
                error = Loc.T("Not a saved game this version of the game can read.");
                return null;
            }
            catch (Exception e)
            {
                error = Loc.T("Could not read the save: ") + e.Message;
                return null;
            }
        }

        /// <summary>Just what the Load Game list shows: header and meta.</summary>
        public static SavedGameInfo ReadInfo(string path)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var save = new SaveGameFile();
                ReadMeta(zip, save);
                var header = new ReplayHeader();
                var entry = zip.GetEntry("replay.twbr");
                if (entry != null)
                {
                    using var r = new StreamReader(entry.Open(), Encoding.UTF8);
                    string line;
                    while ((line = r.ReadLine()) != null && line != "begin")
                        header.ReadLine(line.Split('\t'));
                }
                if (string.IsNullOrEmpty(save.Build)) save.Build = header.Build;
                // A save of another format is LISTED, greyed with the reason —
                // never hidden: hiding it made a player's saves vanish after an
                // update with nothing on screen to say why.
                return new SavedGameInfo
                {
                    Path = path,
                    IsSave = true,
                    Incompatibility = FormatError(save.Format, save.Build),
                    Header = header,
                    Seconds = save.Seconds,
                    Label = save.Label,
                    Written = File.GetLastWriteTime(path),
                    Preview = ReadBytes(zip, "preview.png"),
                };
            }
            catch (Exception e)
            {
                // Used to be swallowed silently: a corrupt save just vanished
                // from the list with nothing in the log to say why.
                UnityEngine.Debug.LogWarning($"[SaveGameFile] Could not read {Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        static void ReadMeta(ZipArchive zip, SaveGameFile save)
        {
            var c = CultureInfo.InvariantCulture;
            save.Format = 0;   // no "format" line = older than every versioned save
            foreach (var line in ReadText(zip, "save.txt").Split('\n'))
            {
                var p = line.TrimEnd('\r').Split('\t');
                switch (p[0])
                {
                    case "format": save.Format = ReplayHeader.Int(p, 1); break;
                    case "build": save.Build = ReplayHeader.Str(p, 1); break;
                    case "tick": save.Tick = ReplayHeader.Int(p, 1); break;
                    case "seconds": float.TryParse(ReplayHeader.Str(p, 1), NumberStyles.Float, c, out save.Seconds); break;
                    case "elapsed": double.TryParse(ReplayHeader.Str(p, 1), NumberStyles.Float, c, out save.WorldElapsed); break;
                    case "label": save.Label = ReplayHeader.Str(p, 1); break;
                }
            }
        }

        /// <summary>
        /// Null when this build can restore a save of <paramref name="format"/>;
        /// otherwise the player-facing reason (localized), naming the game
        /// version that wrote it when it is known.
        /// </summary>
        public static string FormatError(int format, string build)
        {
            if (format == SaveFormatVersion) return null;
            bool known = !string.IsNullOrEmpty(build);
            if (format > SaveFormatVersion)
                return known
                    ? string.Format(Loc.T("This save was made with a newer version of the game ({0}) and can't be loaded in this version."), build)
                    : Loc.T("This save was made with a newer version of the game and can't be loaded in this version.");
            return known
                ? string.Format(Loc.T("This save was made with an older version of the game ({0}) and can't be loaded in this version."), build)
                : Loc.T("This save was made with an older version of the game and can't be loaded in this version.");
        }

        /// <summary>
        /// The format check alone — reads save.txt (and the replay header for
        /// the build when save.txt has none), never the world. Used on the
        /// snapshots a replay embeds ("snap" lines) before the replay is armed.
        /// Null = loadable.
        /// </summary>
        public static string CheckFormat(byte[] bytes)
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var save = new SaveGameFile();
                ReadMeta(zip, save);
                if (string.IsNullOrEmpty(save.Build))
                {
                    var entry = zip.GetEntry("replay.twbr");
                    if (entry != null)
                    {
                        var header = new ReplayHeader();
                        using var r = new StreamReader(entry.Open(), Encoding.UTF8);
                        string line;
                        while ((line = r.ReadLine()) != null && line != "begin")
                            header.ReadLine(line.Split('\t'));
                        save.Build = header.Build;
                    }
                }
                return FormatError(save.Format, save.Build);
            }
            catch (Exception e)
            {
                return Loc.T("Could not read the save: ") + e.Message;
            }
        }

        static string ReadText(ZipArchive zip, string name)
        {
            var e = zip.GetEntry(name);
            if (e == null) return "";
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }

        static byte[] ReadBytes(ZipArchive zip, string name)
        {
            var e = zip.GetEntry(name);
            if (e == null) return null;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
