// ReplayRecorder.cs
// Writes the replay of the match being played: the header once, then every
// tick's commands in execution order as LockstepManager.ProcessTick runs them.
// docs/Design/Replays_And_Saves.md
//
// Write-through: each tick's commands are appended as they execute and the file
// is flushed at every recorded hash, so a crash keeps everything up to the
// last second. A saved game is this file copied, plus a "save" marker.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Core.Replay
{
    public static class ReplayRecorder
    {
        static StreamWriter _w;
        static string _path;
        static string _map;
        static int _lastTick = -1;

        /// <summary>Set by a run that mutates the sim outside the command
        /// stream (the headless RICH bank top-up): its replay could never
        /// reproduce the match, so none is written.</summary>
        public static bool Suppressed;

        /// <summary>True while a replay is being written.</summary>
        public static bool IsRecording => _w != null;

        /// <summary>The file being written now, or the last one closed.</summary>
        public static string LastPath { get; private set; }

        /// <summary>Last tick written (the newest the file knows of).</summary>
        public static int LastTick => _lastTick;

        static ReplayRecorderConfig Cfg => ReplayRecorderConfig.I;

        static bool _quitHooked;

        /// <summary>Open a new replay for the match that is starting.</summary>
        public static void Begin(ReplayHeader header)
        {
            End(-1, null);
            if (Suppressed || header == null) return;

            // Application.Quit skips the match teardown (headless runs, Quit
            // to Desktop): close the file with its end line all the same.
            if (!_quitHooked)
            {
                _quitHooked = true;
                Application.quitting += () =>
                {
                    if (_w == null) return;
                    bool decided = TheWaningBorder.Core.MatchLifecycle.MatchDecided
                                   && !string.IsNullOrEmpty(TheWaningBorder.Core.MatchLifecycle.MatchWinner);
                    End(_lastTick, decided ? TheWaningBorder.Core.MatchLifecycle.MatchWinner + " wins" : "quit");
                };
            }

            int keep = Cfg != null ? Cfg.replaysKept : 30;
            try
            {
                _path = SavedGames.NewReplayPath(header.Map, keep);
                _w = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false));
                header.Write(_w);
                _w.Flush();
                _map = header.Map;
                _lastTick = -1;
                LastPath = _path;
                Debug.Log($"[Replay] Recording to {_path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Replay] Could not open a replay file: {e.Message}");
                _w = null;
                _path = null;
            }
        }

        /// <summary>One tick's commands, in execution order. Empty ticks write nothing.</summary>
        public static void Tick(int tick, List<LockstepCommand> commands)
        {
            if (_w == null) return;
            if (tick > _lastTick) _lastTick = tick;
            if (commands == null || commands.Count == 0) return;
            try
            {
                string t = tick.ToString(CultureInfo.InvariantCulture);
                for (int i = 0; i < commands.Count; i++)
                {
                    var cmd = commands[i];
                    _w.Write("c\t"); _w.Write(t); _w.Write('\t');
                    _w.Write(cmd.PlayerIndex.ToString(CultureInfo.InvariantCulture)); _w.Write('\t');
                    _w.WriteLine(cmd.Serialize());
                }
            }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>The state hash at a sync tick — what makes a replay self-verifying.</summary>
        public static void Hash(int tick, uint total)
        {
            if (_w == null) return;
            if (tick > _lastTick) _lastTick = tick;
            try
            {
                _w.Write("h\t");
                _w.Write(tick.ToString(CultureInfo.InvariantCulture));
                _w.Write('\t');
                _w.WriteLine(total.ToString(CultureInfo.InvariantCulture));
                _w.Flush();
            }
            catch (Exception e) { Fail(e); }
        }

        /// <summary>
        /// Close the file. <paramref name="lastTick"/> is the last tick the
        /// match processed (-1 = just close, no end line).
        /// </summary>
        public static void End(int lastTick, string outcome)
        {
            if (_w == null) return;
            try
            {
                if (lastTick >= 0)
                {
                    _w.Write("end\t");
                    _w.Write(lastTick.ToString(CultureInfo.InvariantCulture));
                    _w.Write('\t');
                    _w.WriteLine(ReplayHeader.Clean(outcome ?? ""));
                }
                _w.Flush();
                _w.Dispose();
            }
            catch { }
            _w = null;
            Debug.Log($"[Replay] Closed {_path} at tick {lastTick}");
            _path = null;
        }

        /// <summary>
        /// The replay written so far, as text — what a saved game carries so the
        /// resumed match keeps one whole-match replay. Null when not recording.
        /// </summary>
        public static string ReadSoFar()
        {
            if (_w == null) return null;
            try
            {
                _w.Flush();
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new StreamReader(fs, Encoding.UTF8);
                return r.ReadToEnd();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Replay] Could not read the replay so far: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Open a new replay that CONTINUES a loaded game: the saved replay text
        /// (its header and every tick before the save) is written first, then the
        /// match carries on appending. End and save markers are dropped.
        /// </summary>
        public static void BeginContinuation(string priorText, string map, int snapTick, byte[] saveBytes)
        {
            End(-1, null);
            if (Suppressed || string.IsNullOrEmpty(priorText)) return;
            int keep = Cfg != null ? Cfg.replaysKept : 30;
            try
            {
                _path = SavedGames.NewReplayPath(map, keep);
                _w = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false));
                _lastTick = -1;
                foreach (var raw in priorText.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line.StartsWith("end\t") || line.StartsWith("save\t")) continue;
                    _w.WriteLine(line);
                    if (line.StartsWith("c\t") || line.StartsWith("h\t"))
                    {
                        int t = ReplayHeader.Int(line.Split('\t'), 1);
                        if (t > _lastTick) _lastTick = t;
                    }
                }
                // The snapshot this match was loaded from: watching the replay
                // re-simulates to here, then restores it exactly as the load did.
                if (saveBytes != null && saveBytes.Length > 0)
                {
                    _w.Write("snap\t");
                    _w.Write(snapTick.ToString(CultureInfo.InvariantCulture));
                    _w.Write('\t');
                    _w.WriteLine(Convert.ToBase64String(saveBytes));
                }
                _w.Flush();
                _map = map;
                LastPath = _path;
                Debug.Log($"[Replay] Continuing the loaded game's replay in {_path}"); 
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Replay] Could not open a replay file: {e.Message}");
                _w = null;
                _path = null;
            }
        }

        static void Fail(Exception e)
        {
            Debug.LogWarning($"[Replay] Recording stopped: {e.Message}");
            try { _w?.Dispose(); } catch { }
            _w = null;
        }
    }
}
