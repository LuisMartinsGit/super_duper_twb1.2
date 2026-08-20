// NetworkStatusOverlay.cs
// The multiplayer match's only window onto its own network state.
//
// WHY IT EXISTS
//   Nothing in-game reported anything. LockstepManager has carried public
//   DesyncDetected / DesyncTick properties for a long time and NO UI read them:
//   on a desync the simulation stopped and the player watched everything freeze
//   with no explanation. A peer that quit froze the other one forever, and in
//   the old frame-driven mode the world kept animating while every order was
//   silently ignored — a game that looks alive but is deaf.
//
//   Three things a player genuinely needs to see, and nothing else:
//     * we are waiting for someone (and who)
//     * the connection is gone
//     * the match has desynced and why the screen stopped moving
//   Ping and input delay ride along in a corner line because they cost nothing
//   and answer "is it me or the network".
//
// IMGUI on purpose. The shipped in-game UI is authored uGUI prefabs, but this is
// a diagnostic surface that has to work when the simulation has stopped and
// that must never depend on a prefab someone forgot to wire — the same reasoning
// that keeps DebugLogOverlay on IMGUI. Multiplayer only; it does not exist in a
// single-player match.
//
// docs/Multiplayer_LAN_Readiness.md
// Location: Assets/Scripts/Multiplayer/Lockstep/NetworkStatusOverlay.cs

using UnityEngine;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Multiplayer
{
    public sealed class NetworkStatusOverlay : MonoBehaviour
    {
        private GUIStyle _banner;
        private GUIStyle _corner;
        private Texture2D _bannerBg;

        /// <summary>Create the overlay if this is a multiplayer match.</summary>
        public static void EnsureExists()
        {
            if (!GameSettings.IsMultiplayer) return;
            if (FindFirstObjectByType<NetworkStatusOverlay>() != null) return;
            var go = new GameObject("NetworkStatusOverlay");
            go.AddComponent<NetworkStatusOverlay>();
            DontDestroyOnLoad(go);
        }

        private void OnDestroy()
        {
            if (_bannerBg != null) Destroy(_bannerBg);
        }

        private void EnsureStyles()
        {
            if (_banner != null) return;

            _bannerBg = new Texture2D(1, 1);
            _bannerBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _bannerBg.Apply();

            _banner = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _banner.normal.textColor = Color.white;
            _banner.normal.background = _bannerBg;
            _banner.padding = new RectOffset(16, 16, 12, 12);

            _corner = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperRight,
            };
            _corner.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        }

        private void OnGUI()
        {
            var ls = LockstepManager.Instance;
            if (ls == null) return;

            EnsureStyles();

            // ── Corner: the numbers, dim and out of the way ─────────────
            float ping = ls.WorstLatencyMs;
            string tickInfo = string.Format(Loc.T("{0} Hz  delay {1:0} ms  "),
                                  LockstepTiming.TicksPerSecond, LockstepTiming.InputDelayMs) +
                              (ping > 0f ? $"ping {ping:0} ms" : "ping —");
            GUI.Label(new Rect(Screen.width - 320f, 6f, 314f, 20f), tickInfo, _corner);

            // ── Banner: only when something is actually wrong ───────────
            string message = null;
            Color tint = Color.white;

            if (ls.DesyncDetected && GameSettings.DeterministicLockstep)
            {
                message = string.Format(
                    Loc.T("The two games have gone out of sync (tick {0}).\n" +
                          "The match cannot continue. A report was written to the logs folder " +
                          "beside the game — please send it along with the other player's copy."),
                    ls.DesyncTick);
                tint = new Color(1f, 0.55f, 0.5f);
            }
            else if (ls.PeerLost)
            {
                message = Loc.T("Lost contact with the other player.\n" +
                                "They may have quit or lost their connection.");
                tint = new Color(1f, 0.75f, 0.45f);
            }
            else if (ls.BlockedOnPlayer >= 0 && ls.BlockedSeconds > LockstepManager.StallWarnSeconds)
            {
                message = string.Format(Loc.T("Waiting for player {0}…  ({1:0}s)"),
                    ls.BlockedOnPlayer + 1, ls.BlockedSeconds);
                tint = new Color(1f, 0.92f, 0.7f);
            }

            if (message == null) return;

            var prev = GUI.color;
            GUI.color = tint;
            float w = Mathf.Min(760f, Screen.width - 80f);
            GUI.Label(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.12f, w, 110f),
                      message, _banner);
            GUI.color = prev;
        }
    }
}
