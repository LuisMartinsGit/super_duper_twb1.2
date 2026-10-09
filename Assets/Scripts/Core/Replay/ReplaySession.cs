// ReplaySession.cs
// The replay or saved game being played, carried across the scene load from
// the menu that picked it to the bootstrap and lockstep manager that use it.
// docs/Design/Replays_And_Saves.md
//
//   Watch   play a replay: the recorded command stream is fed back into
//           LockstepManager.ProcessTick. Local orders are refused, AI brains
//           stay off, the view is a spectator's. Ends at the last recorded tick.
//           With a SaveFile it starts from that save's snapshot instead of
//           tick 0 (the headless snapshot oracle).
//   Resume  load a saved game: the map boots, the SNAPSHOT is restored in
//           place of the starting spawn, and the match continues live from the
//           save's tick. Nothing is re-simulated.

using System;
using TheWaningBorder.Core.Save;

namespace TheWaningBorder.Core.Replay
{
    public enum ReplayMode { None = 0, Watch = 1, Resume = 2 }

    public static class ReplaySession
    {
        public static ReplayMode Mode { get; private set; }

        /// <summary>The replay whose commands are fed (Watch) or whose header
        /// set the match up (Resume).</summary>
        public static ReplayFile File { get; private set; }
        public static string Path { get; private set; }

        /// <summary>The snapshot to restore instead of spawning the match; null
        /// once the bootstrap has consumed it.</summary>
        public static SaveGameFile SaveFile { get; private set; }

        public static bool Active => Mode != ReplayMode.None && File != null;
        public static bool Watching => Mode == ReplayMode.Watch && File != null;
        public static bool Resuming => Mode == ReplayMode.Resume && SaveFile != null;

        /// <summary>The tick the lockstep clock starts at: the save's tick when a
        /// snapshot is being loaded, otherwise 0.</summary>
        public static int StartTick { get; private set; }

        // ── playback state, reset at every (re)start of the match ──────────

        /// <summary>Watch mode: playback speed multiplier, 0 = paused.</summary>
        public static float Speed = 1f;

        /// <summary>Watch mode: fast-forward to this tick (-1 = none).</summary>
        public static int SeekTick = -1;

        /// <summary>Watch mode: a seek BACKWARDS is a restart of the match
        /// plus a seek forward; this carries the target across the reload.</summary>
        public static int SeekAfterRestart = -1;

        /// <summary>The oracle's starting save (PrepareWatchFromSave).</summary>
        static SaveGameFile _baseSave;

        /// <summary>Restart at this embedded snapshot (-1 = none).</summary>
        static int _jumpTo = -1;

        /// <summary>Reloads the map for a jump to an embedded snapshot. Set by the
        /// presentation layer to go through the loading screen; falls back to a
        /// plain scene load (headless).</summary>
        public static Action<string> SceneReloader;

        /// <summary>Run as fast as the budget allows (the headless verifier).</summary>
        public static bool Unthrottled;

        /// <summary>The tick the simulation will run next, published by the manager.</summary>
        public static int CurrentTick;

        /// <summary>The last recorded tick has been played.</summary>
        public static bool Ended;

        /// <summary>A recorded hash disagreed with the re-simulated world.</summary>
        public static bool Diverged;
        public static int DivergedTick = -1;

        /// <summary>Recorded hashes that matched so far.</summary>
        public static int HashesVerified;

        /// <summary>Raised once when a saved game's snapshot has been restored
        /// and the players have the match.</summary>
        public static event Action HandedOver;

        /// <summary>True while the simulation is driven flat out with no real-
        /// time pacing: a seek in a replay, or the headless verifier.</summary>
        public static bool FastForwarding
            => Watching && (Unthrottled || (SeekTick >= 0 && CurrentTick < SeekTick));

        /// <summary>The tick playback runs up to (exclusive).</summary>
        public static int StopTick => File != null ? File.LastTick + 1 : 0;

        /// <summary>0..1 through the recording.</summary>
        public static float Progress
            => File == null || StopTick <= 0 ? 0f : Math.Min(1f, CurrentTick / (float)StopTick);

        /// <summary>
        /// Arm a replay (Watch, <paramref name="path"/> a .twbr) or a saved game
        /// (Resume, a .twbsave), and put its world back into GameSettings. The
        /// caller then loads <c>GameSettings.SelectedMapScene</c>.
        /// </summary>
        public static bool Prepare(string path, ReplayMode mode, out string error)
        {
            Clear();
            if (mode == ReplayMode.Resume)
            {
                var save = SaveGameFile.Load(path, out error);
                if (save == null) return false;
                if (!save.Replay.Header.Apply(out error)) return false;
                SaveFile = save;
                File = save.Replay;
                StartTick = save.Tick;
                GameSettings.WatchingReplay = false;
            }
            else
            {
                var file = ReplayFile.Load(path, out error);
                if (file == null) return false;
                if (!file.Header.Apply(out error)) return false;
                File = file;
                StartTick = 0;
                GameSettings.WatchingReplay = true;
            }
            Path = path;
            Mode = mode;
            Speed = 1f;
            ResetPlayback();
            return true;
        }

        /// <summary>
        /// The snapshot oracle: restore <paramref name="savePath"/>'s snapshot,
        /// then feed <paramref name="replayPath"/>'s commands from the save's tick
        /// on and check its recorded hashes. If the snapshot captured the whole
        /// simulation, every later hash matches the uninterrupted match.
        /// </summary>
        public static bool PrepareWatchFromSave(string savePath, string replayPath, out string error)
        {
            Clear();
            var save = SaveGameFile.Load(savePath, out error);
            if (save == null) return false;
            var file = ReplayFile.Load(replayPath, out error);
            if (file == null) return false;
            if (!file.Header.Apply(out error)) return false;
            SaveFile = save;
            _baseSave = save;
            File = file;
            StartTick = save.Tick;
            Path = replayPath;
            Mode = ReplayMode.Watch;
            GameSettings.WatchingReplay = true;
            Speed = 1f;
            ResetPlayback();
            return true;
        }

        /// <summary>Back to the start of the session (a fresh boot of it).</summary>
        public static void ResetPlayback()
        {
            int target = SeekAfterRestart;
            bool jumping = _jumpTo >= 0;   // mid-replay snapshot jump: keep the tallies
            SeekTick = SeekAfterRestart;
            SeekAfterRestart = -1;

            // A watched replay of a resumed game starts from the embedded
            // snapshot the target lies after (or the one being jumped to).
            if (Watching)
            {
                int at = _jumpTo >= 0 ? _jumpTo : (target >= 0 ? File.SnapshotAtOrBefore(target) : -1);
                _jumpTo = -1;
                SaveGameFile snap = null;
                if (at >= 0)
                {
                    snap = SaveGameFile.Load(Convert.FromBase64String(File.SnapshotData(at)), out string err);
                    if (snap == null) UnityEngine.Debug.LogError($"[Replay] Embedded snapshot at tick {at} unreadable: {err}");
                }
                if (snap != null) { SaveFile = snap; StartTick = at; }
                else if (_baseSave != null) { SaveFile = _baseSave; StartTick = _baseSave.Tick; }
                else { SaveFile = null; StartTick = 0; }
            }

            CurrentTick = StartTick;
            Ended = false;
            if (!jumping)
            {
                Diverged = false;
                DivergedTick = -1;
                HashesVerified = 0;
            }
            if (Watching) GameSettings.WatchingReplay = true;
        }

        /// <summary>
        /// The bootstrap has restored the snapshot. A saved game becomes an
        /// ordinary live match (a restart from the pause menu starts the map
        /// fresh); a watched replay keeps feeding from here.
        /// </summary>
        public static void SnapshotRestored()
        {
            SaveFile = null;
            if (Mode == ReplayMode.Resume)
            {
                Mode = ReplayMode.None;
                File = null;
                StartTick = 0;
                GameSettings.WatchingReplay = false;
                try { HandedOver?.Invoke(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        /// <summary>Watch mode: the recording restores an embedded snapshot at
        /// <paramref name="tick"/> (and this playback did not start from it).</summary>
        public static bool SnapshotDue(int tick)
            => Watching && tick != StartTick && File.SnapshotData(tick) != null;

        /// <summary>Reload the map and continue from the snapshot at <paramref name="tick"/>.</summary>
        public static void JumpToSnapshot(int tick)
        {
            _jumpTo = tick;
            SeekAfterRestart = SeekTick > tick ? SeekTick : -1;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            UnityEngine.Debug.Log($"[Replay] The recorded match was loaded from a save at tick {tick} — restoring it.");
            if (SceneReloader != null) SceneReloader(scene);
            else UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        }

        /// <summary>Forget everything (leaving for the menu, or a fresh match).</summary>
        public static void Clear()
        {
            Mode = ReplayMode.None;
            File = null;
            SaveFile = null;
            _baseSave = null;
            _jumpTo = -1;
            Path = null;
            StartTick = 0;
            Unthrottled = false;
            GameSettings.WatchingReplay = false;
            GameSettings.ReplayHumanMask = -1;
            Speed = 1f;
            SeekTick = -1;
            SeekAfterRestart = -1;
            CurrentTick = 0;
            Ended = false;
            Diverged = false;
            DivergedTick = -1;
            HashesVerified = 0;
        }
    }
}
