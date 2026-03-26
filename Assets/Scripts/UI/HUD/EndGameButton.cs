// EndGameButton.cs
// Adds an "End Game" button to the top-right of the HUD
// Location: Assets/Scripts/UI/HUD/EndGameButton.cs

using UnityEngine;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Displays an "End Game" button in the top-right corner.
    /// When clicked, ends the game and shows the post-game statistics window.
    /// </summary>
    public class EndGameButton : MonoBehaviour
    {
        /// <summary>Set to true by VictoryConditionSystem when the game ends automatically.</summary>
        public static bool GameEndedBySystem = false;

        private const float ButtonWidth = 100f;
        private const float ButtonHeight = 28f;
        private const float Margin = 10f;

        private GUIStyle _buttonStyle;
        private bool _stylesInit;
        private bool _showConfirm;

        void Awake()
        {
            GameEndedBySystem = false;
        }

        void OnGUI()
        {
            // Don't show if game ended via victory/defeat system
            if (GameEndedBySystem) return;

            // Don't show if post-game stats are visible
            if (PostGameStatsUI.IsVisible) return;

            // Don't show if in-game menu is open
            if (InGameMenuPanel.IsOpen) return;

            InitStyles();

            float x = Screen.width - ButtonWidth - Margin;
            float y = Margin;

            if (!_showConfirm)
            {
                if (GUI.Button(new Rect(x, y, ButtonWidth, ButtonHeight), "End Game", _buttonStyle))
                {
                    _showConfirm = true;
                }
            }
            else
            {
                // Confirmation buttons
                float confirmWidth = 220f;
                float cx = Screen.width - confirmWidth - Margin;

                // Background
                GUI.Box(new Rect(cx - 5, y - 5, confirmWidth + 10, ButtonHeight + 30),
                    "", GUI.skin.box);

                GUI.Label(new Rect(cx, y, confirmWidth, 20),
                    "Surrender?",
                    new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(1f, 0.85f, 0.4f) }
                    });

                if (GUI.Button(new Rect(cx, y + 22, 100, ButtonHeight), "Surrender"))
                {
                    _showConfirm = false;
                    EndGame();
                }

                if (GUI.Button(new Rect(cx + 110, y + 22, 100, ButtonHeight), "Cancel"))
                {
                    _showConfirm = false;
                }
            }
        }

        private void EndGame()
        {
            // Route through VictoryConditionSystem for proper elimination tracking
            if (VictoryConditionSystem.Instance != null)
            {
                VictoryConditionSystem.Instance.Surrender();
                return;
            }

            // Fallback: original behavior when VictoryConditionSystem is unavailable
            if (GameStatsTracker.Instance != null)
            {
                GameStatsTracker.Instance.EndGame();
            }

            var statsUI = PostGameStatsUI.Instance;
            if (statsUI == null)
            {
                var go = new GameObject("PostGameStatsUI");
                statsUI = go.AddComponent<PostGameStatsUI>();
            }
            statsUI.Show();
        }

        private void InitStyles()
        {
            if (_stylesInit) return;

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.4f) }
            };

            _stylesInit = true;
        }
    }
}
