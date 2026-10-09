// ReplayFile.cs
// The replay / saved-game file: what a match was set up as, and every command
// it executed, tick by tick. docs/Design/Replays_And_Saves.md
//
// FORMAT (UTF-8 text, one record per line, fields separated by TAB)
//
//   TWBR      <format version>
//   build     <Application.version>
//   fp        <MatchSettingsSync.Fingerprint>
//   created   <local time, ISO 8601>
//   map       <scene name>
//   settings  <MatchSettingsSync.Capture() blob — seed, rules, tick rate…>
//   players   <total> <active slots> <observer> <local faction> <human mask> <multiplayer>
//   lockstep  <ticks per second> <input delay ticks>
//   slot      <index> <type> <faction> <difficulty> <strategy> <colour> <team> <start> <name>
//   begin
//   c         <tick> <player> <LockstepCommand.Serialize()>
//   h         <tick> <SimStateHash.Total>
//   end       <tick> <outcome>
//   save      <tick> <match seconds> <label>          (saved games only)
//
// TAB is the separator because the command payload already uses ',' between
// its own fields and the network uses '|' between commands; neither can carry
// a tab (QueueCommand refuses ',' and '|' in BuildingId, and the remaining
// fields are numbers).
//
// The commands are the LOSSLESS wire form ("R" floats) — the same text every
// multiplayer peer parses — never Lockstep.log's rounded millimetres. A
// replay is the match re-simulated from these, so it is only valid for the
// build that wrote it; the fingerprint is checked before anything boots.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Replay
{
    /// <summary>One lobby slot as it was when the match started.</summary>
    public struct ReplaySlot
    {
        public int Index, Type, Faction, Difficulty, Strategy, Color, Team, Start;
        public string Name;
    }

    /// <summary>Everything needed to boot the same world again.</summary>
    public sealed class ReplayHeader
    {
        public const int CurrentFormat = 1;

        public int Format = CurrentFormat;
        public string Build = "";
        public string Fingerprint = "";
        public string Created = "";
        public string Map = "";
        public string Settings = "";
        public int TotalPlayers;
        public int ActiveSlots;
        public bool Observer;
        public int LocalFaction;
        public int HumanMask;
        public bool Multiplayer;
        public int TicksPerSecond = LockstepTiming.DefaultTicksPerSecond;
        public int InputDelayTicks = 1;
        public readonly List<ReplaySlot> Slots = new List<ReplaySlot>(8);

        /// <summary>True when this build can re-simulate the file.</summary>
        public bool Compatible => Fingerprint == MatchSettingsSync.Fingerprint;

        /// <summary>The header of the match being played right now.</summary>
        public static ReplayHeader CaptureCurrent()
        {
            var h = new ReplayHeader
            {
                Build = UnityEngine.Application.version,
                Fingerprint = MatchSettingsSync.Fingerprint,
                Created = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Map = GameSettings.SelectedMapScene ?? "",
                Settings = MatchSettingsSync.Capture(),
                TotalPlayers = GameSettings.TotalPlayers,
                ActiveSlots = LobbyConfig.ActiveSlotCount,
                Observer = GameSettings.IsObserver,
                LocalFaction = (int)GameSettings.LocalPlayerFaction,
                Multiplayer = GameSettings.IsMultiplayer,
                TicksPerSecond = LockstepTiming.TicksPerSecond,
                InputDelayTicks = LockstepTiming.InputDelayTicks,
            };

            int mask = 0;
            for (int f = 0; f < 8; f++)
                if (GameSettings.IsFactionHumanControlled((Faction)f)) mask |= 1 << f;
            h.HumanMask = mask;

            for (int i = 0; i < LobbyConfig.Slots.Length; i++)
            {
                var s = LobbyConfig.Slots[i];
                if (s == null) continue;
                h.Slots.Add(new ReplaySlot
                {
                    Index = i,
                    Type = (int)s.Type,
                    Faction = (int)s.Faction,
                    Difficulty = (int)s.AIDifficulty,
                    Strategy = (int)s.AIStrategy,
                    Color = s.ColorIndex,
                    Team = s.TeamIndex,
                    Start = s.StartIndex,
                    Name = s.PlayerName ?? "",
                });
            }
            return h;
        }

        /// <summary>
        /// Put this header back into GameSettings and the lobby table, so the
        /// next map load builds the recorded world. Refuses a file from another
        /// build — a different simulation would take a different path from the
        /// first command on, and the player would watch nonsense.
        /// </summary>
        public bool Apply(out string error)
        {
            error = null;
            if (!Compatible)
            {
                error = $"Recorded by build {Build}; this is {UnityEngine.Application.version}. " +
                        "A replay or save only plays on the build that made it.";
                return false;
            }
            if (!MatchSettingsSync.Apply(Settings, out error)) return false;

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.FactionToPlayerMapping.Clear();
            GameSettings.TutorialActive = false;
            GameSettings.TotalPlayers = TotalPlayers;
            GameSettings.IsObserver = Observer;
            GameSettings.ObserverViewFaction = null;
            GameSettings.LocalPlayerFaction = (Faction)LocalFaction;
            GameSettings.ReplayHumanMask = HumanMask;
            if (!string.IsNullOrEmpty(Map)) GameSettings.SelectedMapScene = Map;

            LockstepTiming.TicksPerSecond = TicksPerSecond;
            LockstepTiming.InputDelayTicks = InputDelayTicks;

            LobbyConfig.InitializeSlots();
            LobbyConfig.ActiveSlotCount = ActiveSlots;
            foreach (var r in Slots)
            {
                if (r.Index < 0 || r.Index >= LobbyConfig.Slots.Length) continue;
                var s = LobbyConfig.Slots[r.Index];
                s.Type = (SlotType)r.Type;
                s.Faction = (Faction)r.Faction;
                s.AIDifficulty = (LobbyAIDifficulty)r.Difficulty;
                s.AIStrategy = (LobbyAIStrategy)r.Strategy;
                s.ColorIndex = r.Color;
                s.TeamIndex = (byte)r.Team;
                s.StartIndex = r.Start;
                s.PlayerName = r.Name;
            }
            LobbyConfig.ApplyColorSelections();
            return true;
        }

        public void Write(TextWriter w)
        {
            var c = CultureInfo.InvariantCulture;
            w.Write("TWBR\t"); w.WriteLine(Format.ToString(c));
            w.Write("build\t"); w.WriteLine(Clean(Build));
            w.Write("fp\t"); w.WriteLine(Clean(Fingerprint));
            w.Write("created\t"); w.WriteLine(Clean(Created));
            w.Write("map\t"); w.WriteLine(Clean(Map));
            w.Write("settings\t"); w.WriteLine(Clean(Settings));
            w.WriteLine(string.Join("\t", "players",
                TotalPlayers.ToString(c), ActiveSlots.ToString(c), Observer ? "1" : "0",
                LocalFaction.ToString(c), HumanMask.ToString(c), Multiplayer ? "1" : "0"));
            w.WriteLine(string.Join("\t", "lockstep",
                TicksPerSecond.ToString(c), InputDelayTicks.ToString(c)));
            foreach (var s in Slots)
                w.WriteLine(string.Join("\t", "slot",
                    s.Index.ToString(c), s.Type.ToString(c), s.Faction.ToString(c),
                    s.Difficulty.ToString(c), s.Strategy.ToString(c), s.Color.ToString(c),
                    s.Team.ToString(c), s.Start.ToString(c), Clean(s.Name)));
            w.WriteLine("begin");
        }

        /// <summary>Parse one header line. False when the line is not a header line.</summary>
        internal bool ReadLine(string[] p)
        {
            switch (p[0])
            {
                case "TWBR": Format = Int(p, 1); return true;
                case "build": Build = Str(p, 1); return true;
                case "fp": Fingerprint = Str(p, 1); return true;
                case "created": Created = Str(p, 1); return true;
                case "map": Map = Str(p, 1); return true;
                case "settings": Settings = Str(p, 1); return true;
                case "players":
                    TotalPlayers = Int(p, 1); ActiveSlots = Int(p, 2); Observer = Int(p, 3) != 0;
                    LocalFaction = Int(p, 4); HumanMask = Int(p, 5); Multiplayer = Int(p, 6) != 0;
                    return true;
                case "lockstep":
                    TicksPerSecond = Int(p, 1); InputDelayTicks = Math.Max(1, Int(p, 2));
                    return true;
                case "slot":
                    Slots.Add(new ReplaySlot
                    {
                        Index = Int(p, 1), Type = Int(p, 2), Faction = Int(p, 3),
                        Difficulty = Int(p, 4), Strategy = Int(p, 5), Color = Int(p, 6),
                        Team = Int(p, 7), Start = Int(p, 8), Name = Str(p, 9),
                    });
                    return true;
            }
            return false;
        }

        internal static string Clean(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        internal static int Int(string[] p, int i)
            => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        internal static string Str(string[] p, int i) => i < p.Length ? p[i] : "";
    }

    /// <summary>
    /// A whole replay in memory: header, commands by tick, recorded hashes, and
    /// the end / save markers.
    /// </summary>
    public sealed class ReplayFile
    {
        public ReplayHeader Header = new ReplayHeader();

        /// <summary>Commands per tick, in recorded (= execution) order.</summary>
        public readonly Dictionary<int, List<LockstepCommand>> Commands
            = new Dictionary<int, List<LockstepCommand>>();

        /// <summary>The recorded state hash at each sync tick.</summary>
        public readonly Dictionary<int, uint> Hashes = new Dictionary<int, uint>();

        /// <summary>Last tick the recording knows of (any record type).</summary>
        public int LastTick = -1;

        /// <summary>Tick of the <c>end</c> line, -1 if the match never closed cleanly.</summary>
        public int EndTick = -1;
        public string Outcome = "";

        /// <summary>Saved game marker: resume at this tick. -1 = a plain replay.</summary>
        public int SaveTick = -1;

        /// <summary>
        /// "snap" lines: a game resumed from a save carries that save (base64)
        /// at the tick it was loaded. Watching re-simulates up to it, then
        /// restores the snapshot exactly as the player's load did.
        /// </summary>
        public readonly List<(int Tick, string Data)> Snapshots = new List<(int, string)>();

        /// <summary>The latest snapshot at or before <paramref name="tick"/>, or -1.</summary>
        public int SnapshotAtOrBefore(int tick)
        {
            int best = -1;
            foreach (var s in Snapshots) if (s.Tick <= tick && s.Tick > best) best = s.Tick;
            return best;
        }

        /// <summary>The base64 save stored at <paramref name="tick"/>, or null.</summary>
        public string SnapshotData(int tick)
        {
            foreach (var s in Snapshots) if (s.Tick == tick) return s.Data;
            return null;
        }
        public float SaveSeconds;
        public string SaveLabel = "";

        public bool IsSave => SaveTick >= 0;

        /// <summary>Where playback stops: the save marker, or the last recorded tick.</summary>
        public int StopTick => IsSave ? SaveTick : LastTick + 1;

        public static ReplayFile Load(string path, out string error)
        {
            error = null;
            try
            {
                var file = Parse(File.ReadLines(path, Encoding.UTF8));
                if (file.Header.Format <= 0 || file.Header.Format > ReplayHeader.CurrentFormat)
                {
                    error = "Not a replay this version of the game can read.";
                    return null;
                }
                return file;
            }
            catch (Exception e)
            {
                error = "Could not read the file: " + e.Message;
                return null;
            }
        }

        /// <summary>Parse replay text (a file's lines, or the copy inside a save).</summary>
        public static ReplayFile Parse(IEnumerable<string> lines)
        {
            var file = new ReplayFile();
            bool body = false;
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var p = line.Split('\t');
                if (!body)
                {
                    if (p[0] == "begin") { body = true; continue; }
                    file.Header.ReadLine(p);
                    continue;
                }
                file.ReadBodyLine(p);
            }
            return file;
        }

        void ReadBodyLine(string[] p)
        {
            var c = CultureInfo.InvariantCulture;
            switch (p[0])
            {
                case "c":
                {
                    if (p.Length < 4) return;
                    int tick = ReplayHeader.Int(p, 1);
                    var cmd = LockstepCommand.Deserialize(p[3]);
                    if (cmd == null) return;
                    cmd.Tick = tick;
                    cmd.PlayerIndex = ReplayHeader.Int(p, 2);
                    if (!Commands.TryGetValue(tick, out var list))
                        Commands[tick] = list = new List<LockstepCommand>(4);
                    list.Add(cmd);
                    if (tick > LastTick) LastTick = tick;
                    return;
                }
                case "h":
                {
                    int tick = ReplayHeader.Int(p, 1);
                    if (p.Length > 2 && uint.TryParse(p[2], NumberStyles.Integer, c, out uint h))
                        Hashes[tick] = h;
                    if (tick > LastTick) LastTick = tick;
                    return;
                }
                case "end":
                    EndTick = ReplayHeader.Int(p, 1);
                    Outcome = ReplayHeader.Str(p, 2);
                    if (EndTick > LastTick) LastTick = EndTick;
                    return;
                case "snap":
                    if (p.Length > 2) Snapshots.Add((ReplayHeader.Int(p, 1), p[2]));
                    return;
                case "save":
                    SaveTick = ReplayHeader.Int(p, 1);
                    float.TryParse(ReplayHeader.Str(p, 2), NumberStyles.Float, c, out SaveSeconds);
                    SaveLabel = ReplayHeader.Str(p, 3);
                    return;
            }
        }
    }
}
