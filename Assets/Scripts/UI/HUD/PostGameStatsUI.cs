// PostGameStatsUI.cs
// Post-game statistics window with timeline graphs
// Location: Assets/Scripts/UI/HUD/PostGameStatsUI.cs

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.UI;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Displays post-game timeline graphs for resources and population.
    /// Shown after the player clicks "End Game".
    /// </summary>
    public class PostGameStatsUI : MonoBehaviour
    {
        public static PostGameStatsUI Instance { get; private set; }
        public static bool IsVisible { get; private set; }

        /// <summary>Result string shown as banner (VICTORY or DEFEAT). Empty if game ended manually.</summary>
        public string GameResult { get; private set; } = "";

        /// <summary>The faction that won the game.</summary>
        public Faction WinnerFaction { get; private set; }

        // Graph settings
        private const float GRAPH_HEIGHT = 200f;
        private const float GRAPH_PADDING = 20f;
        private const float LABEL_WIDTH = 60f;

        // State
        private bool _visible;
        private int _selectedFactionIndex;
        private int _selectedGraphIndex;
        private string[] _factionNames;
        private Faction[] _factionValues;
        private readonly string[] _graphNames = { "Supplies", "Iron", "Crystal", "Veilsteel", "Glow", "Population" };

        // Result banner styles — cached once, picked via ternary at draw time (Fix #221)
        private GUIStyle _resultStyleVictory;
        private GUIStyle _resultStyleDefeat;
        private bool _resultStylesBuilt;

        // Specialty styles with no Styles.cs counterpart
        private GUIStyle _graphLabelStyle;
        private Texture2D _graphBgTex;
        private bool _stylesInit;

        // Scroll
        private Vector2 _scrollPos;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Show the post-game stats window.
        /// </summary>
        public void Show()
        {
            _visible = true;
            IsVisible = true;
            Time.timeScale = 0f; // Pause the game

            // Build faction list
            var factions = new List<Faction>();
            var names = new List<string>();

            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                Faction f = (Faction)i;
                factions.Add(f);
                string label = f == GameSettings.LocalPlayerFaction
                    ? $"{f} (You)"
                    : $"{f} (AI)";
                names.Add(label);
            }

            _factionValues = factions.ToArray();
            _factionNames = names.ToArray();
            _selectedFactionIndex = 0; // Default to player's faction
            _selectedGraphIndex = 0;
        }

        /// <summary>
        /// Show the post-game stats window with a victory/defeat result banner.
        /// </summary>
        public void ShowWithResult(string result, Faction winner)
        {
            GameResult = result;
            WinnerFaction = winner;
            Show();
        }

        /// <summary>
        /// Hide the window and resume.
        /// </summary>
        public void Hide()
        {
            _visible = false;
            IsVisible = false;
            Time.timeScale = 1f;
        }

        void OnGUI()
        {
            if (!_visible) return;
            Styles.Initialize();
            InitStyles();
            BuildResultStyles();

            // Full-screen darkened overlay (canonical case — matches DimOverlayColor exactly)
            Styles.DrawDimOverlay(new Rect(0, 0, Screen.width, Screen.height));

            // Main window
            float windowWidth = Mathf.Min(Screen.width - 80f, 1100f);
            float windowHeight = Mathf.Min(Screen.height - 60f, 750f);
            float windowX = (Screen.width - windowWidth) / 2f;
            float windowY = (Screen.height - windowHeight) / 2f;

            var windowRect = new Rect(windowX, windowY, windowWidth, windowHeight);
            GUI.Box(windowRect, "", Styles.PanelBox);

            GUILayout.BeginArea(new Rect(windowRect.x + 20f, windowRect.y + 15f,
                windowRect.width - 40f, windowRect.height - 30f));

            // Title row
            GUILayout.BeginHorizontal();
            GUILayout.Label("Game Statistics", Styles.Header);
            GUILayout.FlexibleSpace();

            // Game duration
            if (GameStatsTracker.Instance != null)
            {
                float duration = GameStatsTracker.Instance.GameEndTime - GameStatsTracker.Instance.GameStartTime;
                int minutes = (int)(duration / 60f);
                int seconds = (int)(duration % 60f);
                GUILayout.Label($"Duration: {minutes}:{seconds:D2}", Styles.Label);
            }
            GUILayout.EndHorizontal();

            // Result banner (VICTORY / DEFEAT) — uses pre-cached styles, no per-frame alloc
            if (!string.IsNullOrEmpty(GameResult))
            {
                GUILayout.Space(5);
                var s = GameResult == "VICTORY" ? _resultStyleVictory : _resultStyleDefeat;
                GUILayout.Label(GameResult, s);
                GUILayout.Space(5);
            }

            GUILayout.Space(10);

            // Player selector row
            GUILayout.BeginHorizontal();
            GUILayout.Label("Player:", Styles.Label, GUILayout.Width(55));

            for (int i = 0; i < _factionNames.Length; i++)
            {
                Color factionColor = GetFactionColor(_factionValues[i]);
                bool isSelected = (i == _selectedFactionIndex);

                var btnStyle = new GUIStyle(Styles.Button);
                if (isSelected)
                {
                    btnStyle.normal.textColor = Color.white;
                    btnStyle.fontStyle = FontStyle.Bold;
                }
                else
                {
                    btnStyle.normal.textColor = factionColor;
                }

                if (GUILayout.Button(_factionNames[i], btnStyle, GUILayout.Width(100), GUILayout.Height(28)))
                {
                    _selectedFactionIndex = i;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Graph type selector
            GUILayout.BeginHorizontal();
            GUILayout.Label("Show:", Styles.Label, GUILayout.Width(55));
            for (int i = 0; i < _graphNames.Length; i++)
            {
                var btnStyle = new GUIStyle(Styles.Button);
                if (i == _selectedGraphIndex)
                {
                    btnStyle.normal.textColor = Color.white;
                    btnStyle.fontStyle = FontStyle.Bold;
                }
                else
                {
                    btnStyle.normal.textColor = GetGraphColor(i);
                }

                var icon = ResourceIcons.Get(_graphNames[i]);
                var content = icon != null
                    ? new GUIContent(icon, _graphNames[i])
                    : new GUIContent(_graphNames[i]);
                if (GUILayout.Button(content, btnStyle, GUILayout.Width(icon != null ? 36 : 85), GUILayout.Height(26)))
                {
                    _selectedGraphIndex = i;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Scrollable graph area
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            // Draw selected graph for selected faction
            if (GameStatsTracker.Instance != null && _factionValues.Length > _selectedFactionIndex)
            {
                Faction selectedFaction = _factionValues[_selectedFactionIndex];
                DrawGraph(_graphNames[_selectedGraphIndex], selectedFaction, _selectedGraphIndex);

                GUILayout.Space(20);

                // Draw a combined overview of ALL factions for the selected metric
                GUILayout.BeginHorizontal();
                GUILayout.Label("All Players — ", Styles.SubHeader);
                ResourceIcons.DrawLayoutIcon(_graphNames[_selectedGraphIndex], 14f);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
                DrawCombinedGraph(_selectedGraphIndex);
            }
            else
            {
                GUILayout.Label("No data available", Styles.Label);
            }

            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            // Bottom buttons
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Return to Main Menu", Styles.Button, GUILayout.Width(200), GUILayout.Height(40)))
            {
                ReturnToMainMenu();
            }

            GUILayout.Space(20);

            if (GUILayout.Button("Close", Styles.Button, GUILayout.Width(120), GUILayout.Height(40)))
            {
                Hide();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            ResourceIcons.DrawTooltip();
        }

        // ═══════════════════════════════════════════════════════════════
        // GRAPH DRAWING
        // ═══════════════════════════════════════════════════════════════

        private void DrawGraph(string title, Faction faction, int graphIndex)
        {
            var tracker = GameStatsTracker.Instance;
            if (tracker == null || !tracker.FactionTimelines.ContainsKey(faction)) return;

            var data = tracker.FactionTimelines[faction];
            if (data.Count < 2) return;

            Color graphColor = GetGraphColor(graphIndex);
            GUILayout.Label($"{faction} — {title}", Styles.SubHeader);
            GUILayout.Space(4);

            // Reserve rect for graph
            var graphRect = GUILayoutUtility.GetRect(0, GRAPH_HEIGHT + 30f, GUILayout.ExpandWidth(true));
            graphRect.x += LABEL_WIDTH;
            graphRect.width -= LABEL_WIDTH;

            // Background
            GUI.DrawTexture(graphRect, _graphBgTex);

            // Extract values
            float[] values = ExtractValues(data, graphIndex);
            float[] times = new float[data.Count];
            for (int i = 0; i < data.Count; i++) times[i] = data[i].Time;

            float maxVal = 1f;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > maxVal) maxVal = values[i];

            maxVal = RoundUpNice(maxVal);
            float maxTime = times[times.Length - 1];
            if (maxTime <= 0) maxTime = 1f;

            // Grid lines (5 horizontal)
            DrawGridLines(graphRect, maxVal, maxTime);

            // Draw the line
            DrawLine(graphRect, times, values, maxTime, maxVal, graphColor);
        }

        private void DrawCombinedGraph(int graphIndex)
        {
            var tracker = GameStatsTracker.Instance;
            if (tracker == null) return;

            var graphRect = GUILayoutUtility.GetRect(0, GRAPH_HEIGHT + 30f, GUILayout.ExpandWidth(true));
            graphRect.x += LABEL_WIDTH;
            graphRect.width -= LABEL_WIDTH;

            // Background
            GUI.DrawTexture(graphRect, _graphBgTex);

            // Find global max across all factions
            float globalMax = 1f;
            float globalMaxTime = 1f;

            foreach (var kvp in tracker.FactionTimelines)
            {
                if (kvp.Value.Count < 2) continue;
                float[] vals = ExtractValues(kvp.Value, graphIndex);
                for (int i = 0; i < vals.Length; i++)
                    if (vals[i] > globalMax) globalMax = vals[i];
                float lastTime = kvp.Value[kvp.Value.Count - 1].Time;
                if (lastTime > globalMaxTime) globalMaxTime = lastTime;
            }

            globalMax = RoundUpNice(globalMax);

            // Grid
            DrawGridLines(graphRect, globalMax, globalMaxTime);

            // Draw each faction's line
            foreach (var kvp in tracker.FactionTimelines)
            {
                if (kvp.Value.Count < 2) continue;

                float[] values = ExtractValues(kvp.Value, graphIndex);
                float[] times = new float[kvp.Value.Count];
                for (int i = 0; i < kvp.Value.Count; i++) times[i] = kvp.Value[i].Time;

                Color factionColor = GetFactionColor(kvp.Key);
                DrawLine(graphRect, times, values, globalMaxTime, globalMax, factionColor);
            }

            // Legend
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Space(LABEL_WIDTH);
            foreach (var kvp in tracker.FactionTimelines)
            {
                var style = new GUIStyle(_graphLabelStyle) { normal = { textColor = GetFactionColor(kvp.Key) } };
                GUILayout.Label($"■ {kvp.Key}", style, GUILayout.Width(80));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawLine(Rect graphRect, float[] times, float[] values,
            float maxTime, float maxVal, Color color)
        {
            if (values.Length < 2) return;

            // We draw using GL lines for smooth results
            // But since OnGUI doesn't easily support GL, use GUI.DrawTexture with thin rects
            Color prevColor = GUI.color;
            GUI.color = color;

            for (int i = 1; i < values.Length; i++)
            {
                float x0 = graphRect.x + (times[i - 1] / maxTime) * graphRect.width;
                float y0 = graphRect.y + graphRect.height - (values[i - 1] / maxVal) * graphRect.height;
                float x1 = graphRect.x + (times[i] / maxTime) * graphRect.width;
                float y1 = graphRect.y + graphRect.height - (values[i] / maxVal) * graphRect.height;

                DrawThickLine(x0, y0, x1, y1, 2f);
            }

            GUI.color = prevColor;
        }

        private static void DrawThickLine(float x0, float y0, float x1, float y1, float thickness)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length < 0.5f) return;

            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            var pivot = new Vector2(x0, y0);
            var rect = new Rect(x0, y0 - thickness / 2f, length, thickness);

            var matrixBak = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, pivot);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.matrix = matrixBak;
        }

        private void DrawGridLines(Rect graphRect, float maxVal, float maxTime)
        {
            Color prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);

            int gridLines = 5;
            for (int g = 0; g <= gridLines; g++)
            {
                float frac = (float)g / gridLines;
                float y = graphRect.y + graphRect.height - frac * graphRect.height;
                GUI.DrawTexture(new Rect(graphRect.x, y, graphRect.width, 1), Texture2D.whiteTexture);

                // Y-axis label
                int val = (int)(frac * maxVal);
                GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.9f);
                GUI.Label(new Rect(graphRect.x - LABEL_WIDTH - 2f, y - 8f, LABEL_WIDTH - 4f, 16f),
                    FormatNumber(val), _graphLabelStyle);
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
            }

            // X-axis time labels
            int timeLabels = 6;
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.9f);
            for (int t = 0; t <= timeLabels; t++)
            {
                float frac = (float)t / timeLabels;
                float x = graphRect.x + frac * graphRect.width;
                int timeSec = (int)(frac * maxTime);
                int min = timeSec / 60;
                int sec = timeSec % 60;
                GUI.Label(new Rect(x - 15f, graphRect.y + graphRect.height + 2f, 40f, 16f),
                    $"{min}:{sec:D2}", _graphLabelStyle);
            }

            GUI.color = prevColor;
        }

        // ═══════════════════════════════════════════════════════════════
        // DATA EXTRACTION
        // ═══════════════════════════════════════════════════════════════

        private static float[] ExtractValues(List<FactionSnapshot> data, int graphIndex)
        {
            float[] values = new float[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                values[i] = graphIndex switch
                {
                    0 => data[i].Supplies,
                    1 => data[i].Iron,
                    2 => data[i].Crystal,
                    3 => data[i].Veilsteel,
                    4 => data[i].Glow,
                    5 => data[i].Population,
                    _ => 0
                };
            }
            return values;
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static string FormatNumber(int val)
        {
            if (val >= 1000) return $"{val / 1000f:F1}k";
            return val.ToString();
        }

        private static float RoundUpNice(float val)
        {
            if (val <= 10) return 10;
            if (val <= 50) return 50;
            if (val <= 100) return 100;
            if (val <= 200) return 200;
            if (val <= 500) return 500;
            if (val <= 1000) return 1000;
            if (val <= 2000) return 2000;
            if (val <= 5000) return 5000;
            return Mathf.Ceil(val / 1000f) * 1000f;
        }

        private static Color GetGraphColor(int index)
        {
            return index switch
            {
                0 => new Color(1f, 0.85f, 0.4f),     // Supplies - gold
                1 => new Color(0.7f, 0.7f, 0.85f),    // Iron - silver
                2 => new Color(0.4f, 0.75f, 1f),      // Crystal - blue
                3 => new Color(0.8f, 0.5f, 1f),       // Veilsteel - purple
                4 => new Color(1f, 1f, 0.5f),         // Glow - yellow
                5 => new Color(0.5f, 1f, 0.5f),       // Population - green
                _ => Color.white
            };
        }

        private static Color GetFactionColor(Faction faction)
        {
            return faction switch
            {
                Faction.Blue => new Color(0.3f, 0.5f, 1f),
                Faction.Red => new Color(1f, 0.3f, 0.3f),
                Faction.Green => new Color(0.3f, 1f, 0.3f),
                Faction.Yellow => new Color(1f, 1f, 0.3f),
                Faction.Purple => new Color(0.8f, 0.3f, 1f),
                Faction.Orange => new Color(1f, 0.6f, 0.2f),
                Faction.Teal => new Color(0.2f, 0.8f, 0.8f),
                Faction.White => new Color(0.9f, 0.9f, 0.9f),
                Faction.Curse => new Color(0.6f, 0.85f, 0.95f), // icy cyan — crystal aesthetic
                _ => Color.gray
            };
        }

        private void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            IsVisible = false;
            _visible = false;

            // Reset bootstrap state so a new game can start fresh
            GameBootstrap.Reset();

            // Destroy all DontDestroyOnLoad managers
            var managers = Object.FindFirstObjectByType<RuntimeManagers>();
            if (managers != null)
                Destroy(managers.gameObject);

            // Clean up ECS world
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                world.Dispose();
            }

            SceneManager.LoadScene("MainMenu");
        }

        // ═══════════════════════════════════════════════════════════════
        // STYLES
        // ═══════════════════════════════════════════════════════════════

        private void InitStyles()
        {
            if (_stylesInit) return;

            // Graph-area background — unique to this panel (no Styles match)
            _graphBgTex = Styles.MakeSolid(new Color(0.03f, 0.04f, 0.10f, 1f));

            // Tiny right-aligned grey label for graph axis labels — unique to this panel
            _graphLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _stylesInit = true;
        }

        /// <summary>
        /// Build the per-result banner styles once. Picked at draw time via ternary
        /// (Fix #221 — no per-frame GUIStyle alloc).
        /// </summary>
        private void BuildResultStyles()
        {
            if (_resultStylesBuilt) return;

            _resultStyleVictory = new GUIStyle(Styles.BannerLarge)
            {
                normal = { textColor = Styles.VictoryColor }
            };
            _resultStyleDefeat = new GUIStyle(Styles.BannerLarge)
            {
                normal = { textColor = Styles.DefeatColor }
            };

            _resultStylesBuilt = true;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
