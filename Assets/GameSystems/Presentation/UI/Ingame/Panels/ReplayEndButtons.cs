// ReplayEndButtons.cs
// The replay entries on the end-of-match screen, added through
// VictoryPanel.ExtraButtons: "Watch Replay" after a match that was recorded,
// "Watch Again" at the end of a replay. docs/Design/Replays_And_Saves.md

using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Replay;

namespace TheWaningBorder.UI.Ingame
{
    public static class ReplayEndButtons
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            VictoryPanel.ExtraButtons -= AddButtons;
            VictoryPanel.ExtraButtons += AddButtons;
            // A replay of a resumed game restores its embedded snapshot through
            // the loading screen, like any other map load.
            ReplaySession.SceneReloader = scene => TheWaningBorder.UI.Menus.LoadingScreen.Show(scene);
        }

        private static void AddButtons(Transform container)
        {
            if (ReplaySession.Watching)
            {
                VictoryPanel.AddButton(container, Loc.T("Watch Again"), WatchAgain);
                return;
            }
            if (ReplayRecorder.IsRecording && !GameSettings.IsMultiplayer)
                VictoryPanel.AddButton(container, Loc.T("Watch Replay"), WatchThisMatch);
        }

        private static void WatchAgain()
        {
            ReplaySession.SeekAfterRestart = -1;
            TheWaningBorder.UI.Menus.LoadingScreen.Show(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// Close the replay of the match that just ended and play it from the
        /// top. The file is closed here rather than at teardown because it has
        /// to be complete — and readable — before the session can load it.
        /// </summary>
        private static void WatchThisMatch()
        {
            var ls = TheWaningBorder.Core.Multiplayer.LockstepServiceLocator.Instance;
            int last = ls != null ? ls.CurrentTick - 1 : -1;
            string outcome = TheWaningBorder.Core.MatchLifecycle.MatchDecided
                ? TheWaningBorder.Core.MatchLifecycle.MatchWinner + " wins" : "quit";
            string path = ReplayRecorder.LastPath;
            ReplayRecorder.End(last, outcome);

            if (string.IsNullOrEmpty(path)
                || !ReplaySession.Prepare(path, ReplayMode.Watch, out string error))
            {
                TheWaningBorder.Core.SimSignals.NotifyError(Loc.T("The replay could not be opened."));
                return;
            }
            TheWaningBorder.UI.Menus.LoadingScreen.Show(GameSettings.SelectedMapScene);
        }
    }
}
