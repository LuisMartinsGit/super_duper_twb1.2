// SaveGameWriter.cs
// "Save Game": snapshot the match as it stands between two ticks and write it,
// with the replay so far, to Saves/. docs/Design/Replays_And_Saves.md §3

using System.Diagnostics;
using TheWaningBorder.Core.Replay;
using TheWaningBorder.Multiplayer;

namespace TheWaningBorder.Core.Save
{
    public static class SaveGameWriter
    {
        /// <summary>
        /// A single-player lockstep match that is being played (not watched,
        /// not decided) and is recording its replay.
        /// </summary>
        public static bool CanSave
        {
            get
            {
                var lm = LockstepManager.Instance;
                return lm != null && lm.IsSimulationRunning
                       && ReplayRecorder.IsRecording
                       && !GameSettings.IsMultiplayer
                       && !GameSettings.WatchingReplay
                       && MatchLifecycle.MapPopulated
                       && !MatchLifecycle.MatchDecided;
            }
        }

        /// <summary>
        /// Write the save. Called from UI between frames, so the world is between
        /// two ticks and every command buffer has played back.
        /// </summary>
        public static bool Save(string label, out string path, out string error)
        {
            path = null;
            error = null;
            if (!CanSave)
            {
                error = "This match cannot be saved right now.";
                return false;
            }

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "There is no match to save.";
                return false;
            }

            var clock = Stopwatch.StartNew();
            try
            {
                var lm = LockstepManager.Instance;
                var em = world.EntityManager;
                em.CompleteAllTrackedJobs();

                var save = new SaveGameFile
                {
                    Tick = lm.CurrentTick,
                    Seconds = lm.CurrentTick * LockstepManager.TICK_DURATION,
                    Label = label ?? "",
                    WorldElapsed = LockstepFixedStep.RateManager != null ? LockstepFixedStep.RateManager.Elapsed : 0d,
                    ReplayText = ReplayRecorder.ReadSoFar() ?? "",
                };
                save.World = WorldSnapshot.Capture(em, out int entities, out string excluded);
                save.State = SnapshotState.Capture(world, out string stateReport);
                save.Pending.AddRange(lm.CapturePendingCommands());

                path = SavedGames.NewSavePath(GameSettings.SelectedMapScene);
                save.Write(path);

                UnityEngine.Debug.Log($"[Save] Tick {save.Tick} ({save.Seconds:0}s) → {path}: {entities} entities " +
                    $"({save.World.Length / 1024} KB), {stateReport} ({save.State.Length / 1024} KB), " +
                    $"{save.Pending.Count} pending command(s), {clock.ElapsedMilliseconds} ms. " +
                    $"Rebuilt on load, not saved: {excluded}.");
                return true;
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogException(e);
                error = "The save could not be written: " + e.Message;
                return false;
            }
        }
    }
}
