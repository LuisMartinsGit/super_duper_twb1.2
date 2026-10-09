// SaveGameFile.cs
// A saved game: a snapshot of the match AND the replay that led to it.
// docs/Design/Replays_And_Saves.md §3
//
// One .twbsave file, a zip with:
//   save.txt     format, the tick the match resumes at, match seconds, label,
//                the world clock (the fixed-step ElapsedTime, exact)
//   replay.twbr  the replay up to that tick — the resumed match keeps writing
//                it, so a loaded game still produces a whole-match replay
//   world.bin    WorldSnapshot: every simulation entity and component
//   state.bin    SnapshotState: statics and system fields
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
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Replay;

namespace TheWaningBorder.Core.Save
{
    public sealed class SaveGameFile
    {
        public const int CurrentFormat = 2;

        public int Format = CurrentFormat;
        public int Tick;
        public float Seconds;
        public string Label = "";
        public double WorldElapsed;
        public string ReplayText = "";
        public byte[] World;
        public byte[] State;
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

        static void Bytes(ZipArchive zip, string name, byte[] data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
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
            catch (Exception e) { error = "Could not read the save: " + e.Message; return null; }
            return Load(bytes, out error);
        }

        public static SaveGameFile Load(byte[] bytes, out string error)
        {
            error = null;
            try
            {
                using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                var save = new SaveGameFile { RawBytes = bytes };
                if (!ReadMeta(zip, save, out error)) return null;
                save.ReplayText = ReadText(zip, "replay.twbr");
                save.Replay = ReplayFile.Parse(save.ReplayText.Split('\n'));
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
                    error = "The save has no world snapshot.";
                    return null;
                }
                return save;
            }
            catch (InvalidDataException)
            {
                error = "Not a saved game this version of the game can read.";
                return null;
            }
            catch (Exception e)
            {
                error = "Could not read the save: " + e.Message;
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
                if (!ReadMeta(zip, save, out _)) return null;
                var header = new ReplayHeader();
                var entry = zip.GetEntry("replay.twbr");
                if (entry != null)
                {
                    using var r = new StreamReader(entry.Open(), Encoding.UTF8);
                    string line;
                    while ((line = r.ReadLine()) != null && line != "begin")
                        header.ReadLine(line.Split('\t'));
                }
                return new SavedGameInfo
                {
                    Path = path,
                    IsSave = true,
                    Header = header,
                    Seconds = save.Seconds,
                    Label = save.Label,
                    Written = File.GetLastWriteTime(path),
                };
            }
            catch { return null; }
        }

        static bool ReadMeta(ZipArchive zip, SaveGameFile save, out string error)
        {
            error = null;
            var c = CultureInfo.InvariantCulture;
            foreach (var line in ReadText(zip, "save.txt").Split('\n'))
            {
                var p = line.TrimEnd('\r').Split('\t');
                switch (p[0])
                {
                    case "format": save.Format = ReplayHeader.Int(p, 1); break;
                    case "tick": save.Tick = ReplayHeader.Int(p, 1); break;
                    case "seconds": float.TryParse(ReplayHeader.Str(p, 1), NumberStyles.Float, c, out save.Seconds); break;
                    case "elapsed": double.TryParse(ReplayHeader.Str(p, 1), NumberStyles.Float, c, out save.WorldElapsed); break;
                    case "label": save.Label = ReplayHeader.Str(p, 1); break;
                }
            }
            if (save.Format != CurrentFormat)
            {
                error = "This save was written by an older save format and cannot be loaded.";
                return false;
            }
            return true;
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
