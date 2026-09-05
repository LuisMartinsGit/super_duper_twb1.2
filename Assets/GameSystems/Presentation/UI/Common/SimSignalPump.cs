// SimSignalPump.cs
// Drains SimSignals into the HUD, once per frame.
//
// The presentation half of the seam described in Core/SimSignals.cs. The
// simulation enqueues notices and pings without knowing this exists; this is
// the only place that turns them into something on screen.
//
// It lives HERE, in UI, on purpose: the arrow points one way. Core owns the
// queue and the vocabulary (SimPingKind); UI owns the colours, the toast and
// the minimap. Moving any of that back down would rebuild the dependency the
// seam exists to remove.

using TheWaningBorder.Core;
using TheWaningBorder.UI.GameUI;
using UnityEngine;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Pumps queued simulation signals into the HUD. Self-installing, so no
    /// scene wiring can forget it and quietly swallow every notice.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SimSignalPump : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<SimSignalPump>() != null) return;
            var go = new GameObject("[SimSignalPump]");
            go.AddComponent<SimSignalPump>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            // Bounded per frame. A flood should show up as a slightly delayed
            // toast, never as a frame spent draining a queue.
            const int MaxPerFrame = 16;

            for (int i = 0; i < MaxPerFrame && SimSignals.TryDrainNotice(out var n); i++)
            {
                if (n.IsError) PlayerNotificationSystem.NotifyError(n.Text);
                else           PlayerNotificationSystem.Notify(n.Text);
            }

            for (int i = 0; i < MaxPerFrame && SimSignals.TryDrainPing(out var p); i++)
                MinimapPings.Post(p.Position, ColourOf(p.Kind), p.Seconds, p.Big);

            if (SimSignals.TryDrainMatchEnd(out var end))
                ShowMatchEnd(end);
        }

        /// <summary>
        /// The end-of-match screen, or the toast-and-timer when no HUD is up.
        /// Choosing between them is a presentation decision, which is why it
        /// lives here rather than in VictoryConditionSystem -- that system used
        /// to branch on whether the panel had appeared.
        /// </summary>
        private void ShowMatchEnd(SimMatchEnd end)
        {
            if (VictoryPanel.TryShow(end.Title, end.Subtitle, end.LocalWon)) return;

            PlayerNotificationSystem.Notify(end.Title + " — " + end.Subtitle);
            StartCoroutine(ReturnToMenuAfter(ReturnToMenuDelay));
        }

        /// <summary>Seconds between the outcome banner and the return to the
        /// main menu — long enough to read how it ended.</summary>
        private const float ReturnToMenuDelay = 10f;

        private System.Collections.IEnumerator ReturnToMenuAfter(float seconds)
        {
            // Unscaled: the match may have ended with the sim paused.
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                TheWaningBorder.Core.SceneNames.Menu);
        }

        /// <summary>Meaning -> colour. The simulation says what happened; the
        /// HUD decides how it looks.</summary>
        private static Color32 ColourOf(SimPingKind kind)
        {
            switch (kind)
            {
                case SimPingKind.Curse:     return MinimapPings.Curse;
                case SimPingKind.Combat:    return MinimapPings.Damage;
                case SimPingKind.Discovery: return MinimapPings.Power;
                default:                    return MinimapPings.Power;
            }
        }
    }
}
