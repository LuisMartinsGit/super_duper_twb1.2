// Canonical source of truth for the IMGUI navy + gold theme.
// Palette: gold (0.83, 0.66, 0.26, 1), navy (0.06, 0.08, 0.18, 0.95), inner-row (0.08, 0.10, 0.22, 1).
// Callers: invoke Styles.Initialize() as the first line of OnGUI() and reference Styles.<X>.
// Do NOT declare local GUIStyle fields or write inline new Color(...) literals matching the palette.
// See Assets/Scripts/UI/Common/README.md for the contributor rule and regression-test recipe.

using UnityEngine;

namespace TheWaningBorder.UI.Common
{
    /// <summary>
    /// Shared UI style definitions for IMGUI.
    /// Dark navy background with golden contour/inlay theme.
    /// </summary>
    public static class Styles
    {
        private static bool _initialized = false;

        // Panel backgrounds
        public static GUIStyle PanelBox { get; private set; }
        public static GUIStyle DarkBox { get; private set; }
        public static GUIStyle TransparentBox { get; private set; }

        // Text styles
        public static GUIStyle Header { get; private set; }
        public static GUIStyle SubHeader { get; private set; }
        public static GUIStyle Label { get; private set; }
        public static GUIStyle SmallLabel { get; private set; }
        public static GUIStyle TinyLabel { get; private set; }
        public static GUIStyle CenteredLabel { get; private set; }
        public static GUIStyle RichLabel { get; private set; }

        // Button styles
        public static GUIStyle Button { get; private set; }
        public static GUIStyle SmallButton { get; private set; }
        public static GUIStyle IconButton { get; private set; }
        public static GUIStyle ToggleButton { get; private set; }

        // Slot/list styles
        public static GUIStyle SlotBox { get; private set; }
        public static GUIStyle ListItem { get; private set; }

        // Misc styles (additive — see CreateMiscStyles)
        public static GUIStyle CostStyleAffordable { get; private set; }
        public static GUIStyle CostStyleUnaffordable { get; private set; }
        public static GUIStyle BannerLarge { get; private set; }
        public static GUIStyle TabActive { get; private set; }
        public static GUIStyle TabInactive { get; private set; }
        public static GUIStyle InnerRowBox { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        // THEME COLORS — Dark Navy + Golden
        // ═══════════════════════════════════════════════════════════════
        public static Color PanelBgColor      = new Color(0.06f, 0.08f, 0.18f, 0.95f);   // dark navy
        public static Color DarkBgColor       = new Color(0.04f, 0.05f, 0.12f, 0.9f);     // darker navy
        public static Color TransparentBgColor= new Color(0.03f, 0.04f, 0.12f, 0.6f);    // transparent navy
        public static Color HighlightColor    = new Color(0.83f, 0.66f, 0.26f, 1f);       // golden
        public static Color SuccessColor      = new Color(0.3f, 1f, 0.3f, 1f);
        public static Color WarningColor      = new Color(1f, 0.8f, 0.2f, 1f);
        public static Color ErrorColor        = new Color(1f, 0.3f, 0.3f, 1f);
        public static Color DisabledColor     = new Color(0.5f, 0.5f, 0.5f, 1f);

        // Misc theme colors (additive)
        public static Color AffordableColor   = new Color(0.3f, 0.9f, 0.3f, 1f);          // green (cost is affordable)
        public static Color UnaffordableColor = new Color(1f, 0.3f, 0.3f, 1f);            // red (cost not affordable)
        public static Color VictoryColor      = new Color(1f, 0.85f, 0.2f, 1f);           // gold-ish (post-game banner)
        public static Color DefeatColor       = new Color(1f, 0.25f, 0.25f, 1f);          // red (post-game banner)
        public static Color DimOverlayColor   = new Color(0f, 0f, 0f, 0.7f);              // full-screen modal dim
        public static Color InnerRowColor     = new Color(0.08f, 0.10f, 0.22f, 1f);       // inside-an-outer-panel row tone

        // Textures
        private static Texture2D _panelTex;
        private static Texture2D _darkTex;
        private static Texture2D _transparentTex;
        private static Texture2D _slotTex;

        // Misc textures (additive)
        private static Texture2D _tabActiveTex;
        private static Texture2D _tabInactiveTex;
        private static Texture2D _innerRowTex;

        // Per-faction header style cache (R6). Indexed by (int)Faction (0..7).
        // Lazy per-slot fill: zero allocation on repeat calls for the same faction.
        private static GUIStyle[] _factionAccentStyles;

        /// <summary>
        /// Initialize all styles. Call this in OnGUI before using styles.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            CreateTextures();
            CreatePanelStyles();
            CreateTextStyles();
            CreateButtonStyles();
            CreateSlotStyles();
            CreateMiscStyles();

            _initialized = true;
        }

        private static void CreateTextures()
        {
            // Bordered panel textures for golden contour look
            _panelTex = UIHelpers.MakeBorderedTexture(64, 64, PanelBgColor,
                new Color(0.83f, 0.66f, 0.26f, 0.8f), 2);
            _darkTex = UIHelpers.MakeBorderedTexture(64, 64, DarkBgColor,
                new Color(0.6f, 0.48f, 0.18f, 0.6f), 1);
            _transparentTex = UIHelpers.MakeTexture(2, 2, TransparentBgColor);
            _slotTex = UIHelpers.MakeBorderedTexture(32, 32,
                new Color(0.08f, 0.10f, 0.22f, 0.3f),
                new Color(0.83f, 0.66f, 0.26f, 0.2f), 1);

            // Misc textures (R3 tabs + R5 inner row).
            _tabActiveTex   = UIHelpers.MakeTexture(2, 2, new Color(0.83f, 0.66f, 0.26f, 0.35f));
            _tabInactiveTex = UIHelpers.MakeTexture(2, 2, new Color(0.15f, 0.15f, 0.25f, 0.5f));
            _innerRowTex    = UIHelpers.MakeTexture(2, 2, InnerRowColor);
        }

        private static void CreatePanelStyles()
        {
            PanelBox = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelTex },
                padding = new RectOffset(10, 10, 10, 10)
            };

            DarkBox = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _darkTex },
                padding = new RectOffset(8, 8, 8, 8)
            };

            TransparentBox = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _transparentTex },
                padding = new RectOffset(6, 6, 6, 6)
            };
        }

        private static void CreateTextStyles()
        {
            Header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.83f, 0.66f, 0.26f) }  // golden headers
            };

            SubHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            Label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            SmallLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.7f, 0.68f, 0.60f) }
            };

            TinyLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.6f, 0.58f, 0.50f) }
            };

            CenteredLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            RichLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };
        }

        private static void CreateButtonStyles()
        {
            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                padding = new RectOffset(10, 10, 6, 6),
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            SmallButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(6, 6, 4, 4),
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f) }
            };

            IconButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 4, 4)
            };

            ToggleButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(8, 8, 4, 4)
            };
        }

        private static void CreateSlotStyles()
        {
            SlotBox = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _slotTex },
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };

            ListItem = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _slotTex },
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(0, 0, 1, 1)
            };
        }

        /// <summary>
        /// Build the additive misc styles (R1 cost, R2 banner, R3 tabs, R5 inner row).
        /// Kept separate from the original five creators so the additive boundary stays visible (AD-4).
        /// </summary>
        private static void CreateMiscStyles()
        {
            // R1 — Pre-cached cost label styles. Derived from Label so they share fontSize=13
            // and the standard label font. Pre-caching avoids per-frame allocation in OnGUI hot
            // loops (matches Fix #221 rationale in EntityActionPanel.cs:49-52).
            CostStyleAffordable = new GUIStyle(Label)
            {
                normal = { textColor = AffordableColor }
            };
            CostStyleUnaffordable = new GUIStyle(Label)
            {
                normal = { textColor = UnaffordableColor }
            };

            // R2 — Large banner (post-game VICTORY/DEFEAT). Stays color-neutral; callers tint
            // per-result with VictoryColor / DefeatColor (AD-5).
            BannerLarge = new GUIStyle(Header)
            {
                fontSize = 28,
                alignment = TextAnchor.MiddleCenter
            };

            // R3 — Tab styles (sourced from SkirmishLobbyUI.cs:138-157).
            // Inactive hover swaps to the active background — preserve that behavior.
            TabActive = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                normal = { background = _tabActiveTex, textColor = new Color(0.83f, 0.66f, 0.26f) },
                hover  = { background = _tabActiveTex, textColor = new Color(0.93f, 0.76f, 0.36f) },
                active = { background = _tabActiveTex, textColor = new Color(0.93f, 0.76f, 0.36f) },
                padding = new RectOffset(12, 12, 6, 6)
            };

            TabInactive = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                normal = { background = _tabInactiveTex, textColor = new Color(0.7f, 0.7f, 0.7f) },
                hover  = { background = _tabActiveTex,   textColor = new Color(0.9f, 0.9f, 0.9f) },
                active = { background = _tabActiveTex,   textColor = new Color(0.9f, 0.9f, 0.9f) },
                padding = new RectOffset(12, 12, 6, 6)
            };

            // R5 — Inner row tone (matches ListItem's sizing).
            InnerRowBox = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _innerRowTex },
                padding = new RectOffset(6, 6, 4, 4)
            };
        }

        /// <summary>
        /// Get a label style with custom color.
        /// </summary>
        public static GUIStyle GetColoredLabel(Color color, int fontSize = 13, FontStyle fontStyle = FontStyle.Normal)
        {
            Initialize();
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                normal = { textColor = color }
            };
        }

        /// <summary>
        /// Get header style with custom color.
        /// </summary>
        public static GUIStyle GetColoredHeader(Color color)
        {
            Initialize();
            return new GUIStyle(Header)
            {
                normal = { textColor = color }
            };
        }

        /// <summary>
        /// Draw a section header with golden separator line.
        /// </summary>
        public static void DrawSectionHeader(string text, bool drawSeparator = true)
        {
            Initialize();
            GUILayout.Label(text, Header);
            if (drawSeparator)
            {
                var rect = GUILayoutUtility.GetRect(1, 2, GUILayout.ExpandWidth(true));
                GUI.color = new Color(0.83f, 0.66f, 0.26f, 0.6f);  // golden
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            GUILayout.Space(4);
        }

        /// <summary>
        /// Draw a horizontal golden separator line.
        /// </summary>
        public static void DrawSeparator(float alpha = 0.4f)
        {
            GUILayout.Space(4);
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            GUI.color = new Color(0.83f, 0.66f, 0.26f, alpha);  // golden
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.Space(4);
        }

        /// <summary>
        /// Create a solid 2x2 texture in the given color. Thin wrapper over
        /// UIHelpers.MakeTexture so callers don't need to depend on UIHelpers directly (R7).
        /// </summary>
        public static Texture2D MakeSolid(Color c) => UIHelpers.MakeTexture(2, 2, c);

        /// <summary>
        /// Draw a full-screen (or any-rect) dim overlay using DimOverlayColor.
        /// Mirrors the GUI.color save/restore pattern used by DrawSeparator (R4).
        /// </summary>
        public static void DrawDimOverlay(Rect rect)
        {
            Initialize();
            var prev = GUI.color;
            GUI.color = DimOverlayColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>
        /// Get a Header-derived style tinted with the given faction's color.
        /// Cached per-faction in a fixed-length array indexed by (int)Faction (AD-3).
        /// O(1) lookup, zero allocation on repeat calls for the same faction (R6).
        /// </summary>
        public static GUIStyle GetFactionAccentStyle(Faction faction)
        {
            Initialize();
            if (_factionAccentStyles == null)
                _factionAccentStyles = new GUIStyle[8];

            int idx = (int)faction;
            if (idx < 0 || idx >= _factionAccentStyles.Length)
                return Header;

            if (_factionAccentStyles[idx] == null)
            {
                _factionAccentStyles[idx] = new GUIStyle(Header)
                {
                    normal = { textColor = UIHelpers.GetFactionColor(faction) }
                };
            }
            return _factionAccentStyles[idx];
        }
    }
}
