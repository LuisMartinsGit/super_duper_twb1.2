// File: Assets/Scripts/UI/Common/ResourceIcons.cs
// Loads and draws resource type icons (Supplies, Iron, Crystal, Veilsteel, Glow).
// Icons are loaded from Resources/UI/icons/Resoueces/ and cached statically.

using UnityEngine;

namespace TheWaningBorder.UI
{
    /// <summary>
    /// Central cache for resource type icons.
    /// Call <see cref="EnsureLoaded"/> once per frame (or before first use) to lazy-load textures.
    /// Draw methods render an icon scaled to match the current font size.
    /// </summary>
    public static class ResourceIcons
    {
        private static Texture2D _supplies;
        private static Texture2D _iron;
        private static Texture2D _crystal;
        private static Texture2D _veilsteel;
        private static Texture2D _glow;
        private static bool _loaded;

        /// <summary>Lazy-load all icons from Resources.</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            // Folder has a typo ("Resoueces") — match what's on disk
            _supplies  = Resources.Load<Texture2D>("UI/icons/Resoueces/Supplies");
            _iron      = Resources.Load<Texture2D>("UI/icons/Resoueces/Iron");
            _crystal   = Resources.Load<Texture2D>("UI/icons/Resoueces/Crystal");
            _veilsteel = Resources.Load<Texture2D>("UI/icons/Resoueces/Veilsteel");
            _glow      = Resources.Load<Texture2D>("UI/icons/Resoueces/Glow");
        }

        private static GUIStyle _tooltipStyle;

        /// <summary>Get the icon texture for a resource by name.</summary>
        public static Texture2D Get(string resourceName)
        {
            EnsureLoaded();
            return resourceName switch
            {
                "Supplies" or "S"  => _supplies,
                "Iron" or "Fe"     => _iron,
                "Crystal" or "Cr"  => _crystal,
                "Veilsteel" or "Vs" => _veilsteel,
                "Glow" or "Gl"     => _glow,
                _ => null
            };
        }

        /// <summary>Get the full display name for a resource (used in tooltips).</summary>
        public static string GetDisplayName(string resourceName)
        {
            return resourceName switch
            {
                "S"  => "Supplies",
                "Fe" => "Iron",
                "Cr" => "Crystal",
                "Vs" => "Veilsteel",
                "Gl" => "Glow",
                _ => resourceName
            };
        }

        /// <summary>
        /// Draw the current IMGUI tooltip near the mouse cursor.
        /// Call this at the END of OnGUI in any panel that uses resource icons.
        /// </summary>
        public static void DrawTooltip()
        {
            if (string.IsNullOrEmpty(GUI.tooltip)) return;

            if (_tooltipStyle == null)
            {
                _tooltipStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(6, 6, 4, 4),
                    normal = { textColor = Color.white }
                };
            }

            var content = new GUIContent(GUI.tooltip);
            var size = _tooltipStyle.CalcSize(content);
            float mx = Event.current.mousePosition.x + 16f;
            float my = Event.current.mousePosition.y + 8f;

            // Keep on screen
            if (mx + size.x > Screen.width) mx = Screen.width - size.x - 4f;
            if (my + size.y > Screen.height) my = Screen.height - size.y - 4f;

            GUI.Box(new Rect(mx, my, size.x, size.y), GUI.tooltip, _tooltipStyle);
        }

        // ═══════════════════════════════════════════════════════════════
        // IMMEDIATE-MODE (GUI.DrawTexture) — for absolute-positioned UI
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw a resource icon at an absolute position, scaled to <paramref name="size"/>.
        /// Returns the width consumed (icon width) so the caller can advance its X cursor.
        /// </summary>
        public static float DrawIcon(float x, float y, string resourceName, float size)
        {
            var tex = Get(resourceName);
            if (tex == null) return 0f;

            var rect = new Rect(x, y, size, size);
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            GUI.color = prev;

            // Invisible label for tooltip
            GUI.Label(rect, new GUIContent("", GetDisplayName(resourceName)));
            return size;
        }

        /// <summary>
        /// Draw icon + value text at an absolute position.
        /// Icon is drawn at font-height, value text follows immediately.
        /// Returns total width consumed.
        /// </summary>
        public static float DrawIconValue(float x, float y, string resourceName,
            string value, float iconSize, GUIStyle valueStyle)
        {
            float w = DrawIcon(x, y + 1f, resourceName, iconSize);
            if (w == 0f) return 0f;

            float gap = 2f;
            var textSize = valueStyle.CalcSize(new GUIContent(value));
            GUI.Label(new Rect(x + w + gap, y, textSize.x + 4f, iconSize), value, valueStyle);
            return w + gap + textSize.x + 4f;
        }

        // ═══════════════════════════════════════════════════════════════
        // LAYOUT-MODE (GUILayout) — for auto-layout UI
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw a resource icon inline using GUILayout, sized to match the given font size.
        /// </summary>
        public static void DrawLayoutIcon(string resourceName, float fontSize)
        {
            var tex = Get(resourceName);
            if (tex == null) return;

            float size = fontSize + 2f;
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            GUI.color = prev;

            // Tooltip on hover
            GUI.Label(rect, new GUIContent("", GetDisplayName(resourceName)));
        }

        /// <summary>
        /// Draw icon + value as a horizontal pair in GUILayout.
        /// </summary>
        public static void DrawLayoutIconValue(string resourceName, string value,
            float fontSize, GUIStyle valueStyle, float totalWidth = 0f)
        {
            var tex = Get(resourceName);
            if (tex == null) return;

            if (totalWidth > 0f)
                GUILayout.BeginHorizontal(GUILayout.Width(totalWidth));
            else
                GUILayout.BeginHorizontal();

            float size = fontSize + 2f;
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            GUI.color = prev;

            // Tooltip on hover
            GUI.Label(rect, new GUIContent("", GetDisplayName(resourceName)));

            GUILayout.Space(2f);
            GUILayout.Label(value, valueStyle);
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════
        // COST DRAWING — replaces FormatCost text with icon+value pairs
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Draw a cost as a horizontal row of icon+value pairs using GUILayout.
        /// Only non-zero resource amounts are shown.
        /// </summary>
        public static void DrawCostLayout(TheWaningBorder.Core.Cost cost,
            float fontSize, GUIStyle valueStyle)
        {
            if (cost.IsZero)
            {
                GUILayout.Label("Free", valueStyle);
                return;
            }

            GUILayout.BeginHorizontal();
            if (cost.Supplies > 0)
            { DrawLayoutIcon("Supplies", fontSize); GUILayout.Label(cost.Supplies.ToString(), valueStyle); GUILayout.Space(6f); }
            if (cost.Iron > 0)
            { DrawLayoutIcon("Iron", fontSize); GUILayout.Label(cost.Iron.ToString(), valueStyle); GUILayout.Space(6f); }
            if (cost.Crystal > 0)
            { DrawLayoutIcon("Crystal", fontSize); GUILayout.Label(cost.Crystal.ToString(), valueStyle); GUILayout.Space(6f); }
            if (cost.Veilsteel > 0)
            { DrawLayoutIcon("Veilsteel", fontSize); GUILayout.Label(cost.Veilsteel.ToString(), valueStyle); GUILayout.Space(6f); }
            if (cost.Glow > 0)
            { DrawLayoutIcon("Glow", fontSize); GUILayout.Label(cost.Glow.ToString(), valueStyle); }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draw a cost using absolute positioning (GUI.DrawTexture + GUI.Label).
        /// Returns total width consumed.
        /// </summary>
        public static float DrawCostAbsolute(float x, float y, TheWaningBorder.Core.Cost cost,
            float iconSize, GUIStyle valueStyle)
        {
            if (cost.IsZero) return 0f;

            float startX = x;
            float spacing = 6f;

            void Add(string res, int amount)
            {
                if (amount <= 0) return;
                x += DrawIconValue(x, y, res, amount.ToString(), iconSize, valueStyle);
                x += spacing;
            }

            Add("Supplies", cost.Supplies);
            Add("Iron", cost.Iron);
            Add("Crystal", cost.Crystal);
            Add("Veilsteel", cost.Veilsteel);
            Add("Glow", cost.Glow);

            return x - startX;
        }
    }
}
