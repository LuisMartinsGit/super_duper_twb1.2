// PathfindingToggleHUD.cs
// Small IMGUI button to toggle between flow field and A* pathfinding at runtime.
// Location: Assets/Scripts/UI/HUD/PathfindingToggleHUD.cs

using UnityEngine;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI toggle button for switching between flow field and A* pathfinding.
    /// Displays "FF" or "A*" in the top-right corner. Hotkey: F5.
    /// Only affects new move commands; units mid-path continue their current method.
    /// </summary>
    public class PathfindingToggleHUD : MonoBehaviour
    {
        private GUIStyle _buttonStyle;
        private bool _stylesInitialized;

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
                Toggle();
        }

        void OnGUI()
        {
            InitStyles();

            string label = GameSettings.UseFlowFields ? "FF" : "A*";
            string tooltip = GameSettings.UseFlowFields
                ? "Flow Fields (F5 to switch to A*)"
                : "A* Pathfinding (F5 to switch to Flow Fields)";

            float btnW = 40f;
            float btnH = 24f;
            float margin = 8f;

            // Position top-right, below resource bar area
            var rect = new Rect(Screen.width - btnW - margin, 60f + margin, btnW, btnH);

            if (GUI.Button(rect, new GUIContent(label, tooltip), _buttonStyle))
                Toggle();
        }

        private static void Toggle()
        {
            GameSettings.UseFlowFields = !GameSettings.UseFlowFields;
            Debug.Log($"[PathfindingToggle] Switched to {(GameSettings.UseFlowFields ? "Flow Fields" : "A*")}");
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _stylesInitialized = true;
        }
    }
}
