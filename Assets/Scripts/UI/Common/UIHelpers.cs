// File: Assets/Scripts/UI/Common/UIHelpers.cs
// Shared UI utility functions and data structures

using UnityEngine;

namespace TheWaningBorder.UI.Common
{
    /// <summary>
    /// Shared utility functions for UI systems.
    /// </summary>
    public static class UIHelpers
    {
        /// <summary>
        /// Create a solid color texture.
        /// </summary>
        public static Texture2D MakeTexture(int width, int height, Color color)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = color;
            var tex = new Texture2D(width, height);
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Create a gradient texture (vertical).
        /// </summary>
        public static Texture2D MakeGradientTexture(int width, int height, Color top, Color bottom)
        {
            var tex = new Texture2D(width, height);
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                Color c = Color.Lerp(bottom, top, t);
                for (int x = 0; x < width; x++)
                {
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Create a texture with a solid fill and a colored border.
        /// Used for golden-contour panel backgrounds.
        /// </summary>
        public static Texture2D MakeBorderedTexture(int width, int height, Color fillColor, Color borderColor, int borderWidth = 2)
        {
            var tex = new Texture2D(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = x < borderWidth || x >= width - borderWidth ||
                                    y < borderWidth || y >= height - borderWidth;
                    tex.SetPixel(x, y, isBorder ? borderColor : fillColor);
                }
            }
            tex.Apply();
            return tex;
        }

        // ═══════════════════════════════════════════════════════════════
        // THEME COLORS — Dark Navy + Golden
        // ═══════════════════════════════════════════════════════════════
        public static readonly Color ThemePanelBg    = new Color(0.06f, 0.08f, 0.18f, 0.95f);
        public static readonly Color ThemeInnerBg    = new Color(0.08f, 0.10f, 0.22f, 0.95f);
        public static readonly Color ThemeGold       = new Color(0.83f, 0.66f, 0.26f, 1f);
        public static readonly Color ThemeGoldDim    = new Color(0.6f, 0.48f, 0.18f, 1f);
        public static readonly Color ThemeGoldBorder = new Color(0.83f, 0.66f, 0.26f, 0.8f);
        public static readonly Color ThemeText       = new Color(0.9f, 0.88f, 0.82f, 1f);
        public static readonly Color ThemeTextDim    = new Color(0.7f, 0.68f, 0.60f, 1f);

        /// <summary>
        /// Check if mouse position is inside a GUI rect (GUI coordinates).
        /// </summary>
        public static bool IsMouseOverRect(Rect guiRect)
        {
            var mousePos = UnityEngine.Input.mousePosition;
            var screenRect = new Rect(
                guiRect.x,
                Screen.height - guiRect.y - guiRect.height,
                guiRect.width,
                guiRect.height
            );
            return screenRect.Contains(mousePos);
        }

        /// <summary>
        /// Convert GUI rect to screen rect (bottom-left origin).
        /// </summary>
        public static Rect GuiToScreenRect(Rect guiRect)
        {
            return new Rect(
                guiRect.x,
                Screen.height - guiRect.y - guiRect.height,
                guiRect.width,
                guiRect.height
            );
        }

        /// <summary>
        /// Format a cost as a compact string.
        /// </summary>
        public static string FormatCost(TheWaningBorder.Core.Cost cost)
        {
            var sb = new System.Text.StringBuilder(64);
            
            void Add(string name, int value)
            {
                if (value > 0)
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(name).Append(' ').Append(value);
                }
            }

            Add("S", cost.Supplies);
            Add("Fe", cost.Iron);
            Add("Cr", cost.Veilstone);
            Add("Vs", cost.Veilsteel);
            Add("Gl", cost.Glow);

            return sb.Length == 0 ? "Free" : sb.ToString();
        }

        /// <summary>
        /// Format a cost with rich text coloring.
        /// Resources the player cannot afford are shown in red; affordable ones in the given color hex.
        /// </summary>
        public static string FormatCostRich(TheWaningBorder.Core.Cost cost, TheWaningBorder.Core.Cost available, string affordHex = "#b8e6b8")
        {
            if (cost.IsZero) return "Free";

            var sb = new System.Text.StringBuilder(128);

            void Add(string name, int needed, int have)
            {
                if (needed <= 0) return;
                if (sb.Length > 0) sb.Append("  ");
                string hex = have >= needed ? affordHex : "#ff5555";
                sb.Append($"<color={hex}>{name} {needed}</color>");
            }

            Add("S", cost.Supplies, available.Supplies);
            Add("Fe", cost.Iron, available.Iron);
            Add("Cr", cost.Veilstone, available.Veilstone);
            Add("Vs", cost.Veilsteel, available.Veilsteel);
            Add("Gl", cost.Glow, available.Glow);

            return sb.ToString();
        }

        /// <summary>
        /// Draw a progress bar.
        /// </summary>
        public static void DrawProgressBar(Rect rect, float progress, Color fillColor, Color bgColor)
        {
            progress = Mathf.Clamp01(progress);

            // Background
            GUI.color = bgColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Fill
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * progress, rect.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a health bar with automatic coloring.
        /// </summary>
        public static void DrawHealthBar(Rect rect, int current, int max, string label = null)
        {
            float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0;
            Color fillColor = ratio > 0.5f ? Color.green : (ratio > 0.25f ? Color.yellow : Color.red);
            
            DrawProgressBar(rect, ratio, fillColor, new Color(0.3f, 0.3f, 0.3f, 1f));

            // Optional label
            if (!string.IsNullOrEmpty(label))
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(rect, label, style);
            }
        }

        /// <summary>
        /// Get faction display color.
        /// </summary>
        public static Color GetFactionColor(Faction faction)
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
                Faction.Border => new Color(0.6f, 0.85f, 0.95f), // icy cyan — veilstone aesthetic
                _ => Color.gray
            };
        }

        /// <summary>
        /// Get faction display name.
        /// </summary>
        public static string GetFactionName(Faction faction, bool includePlayerLabel = false)
        {
            if (includePlayerLabel && faction == GameSettings.LocalPlayerFaction)
                return "PLAYER";

            return faction.ToString();
        }
    }

    // UnifiedUIManager (IMGUI panel spawner + pointer-over aggregator) removed
    // with the old UI (2026-07-17); the final uGUI EventSystem check replaces
    // IsPointerOverAnyPanel and GameUIManager owns panel lifecycle.
}
