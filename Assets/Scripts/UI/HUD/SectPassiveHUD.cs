// SectPassiveHUD.cs
// Shows a row of passive sect icons above the resource bar.
// Hover for tooltip with passive details.
// Location: Assets/Scripts/UI/HUD/SectPassiveHUD.cs

using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI HUD row showing adopted sect passive icons above the resource bar.
    /// Each icon represents an adopted sect. Hover to see passive details.
    /// </summary>
    public class SectPassiveHUD : MonoBehaviour
    {
        private const float IconSize = 28f;
        private const float IconSpacing = 4f;
        private const float RowHeight = 36f;
        private const float RowPadding = 4f;

        // Specialty bordered textures + a cached letter overlay (no Styles match) —
        // colors sourced from Styles.HighlightColor / Styles.PanelBgColor so the
        // palette stays canonical.
        private GUIStyle _iconStyle;
        private GUIStyle _tooltipStyle;
        private GUIStyle _bgStyle;
        private GUIStyle _letterStyle;
        private bool _stylesInit;

        // Cache
        private float _cacheTimer;
        private List<string> _cachedSects = new();

        // Culture colors
        private static readonly Dictionary<string, Color> CultureColors = new()
        {
            { "Alanthor", new Color(0.55f, 0.65f, 0.50f) },
            { "Runai", new Color(0.25f, 0.75f, 0.80f) },
            { "Feraldis", new Color(0.70f, 0.18f, 0.15f) }
        };

        void OnGUI()
        {
            Styles.Initialize();
            if (!_stylesInit) InitStyles();

            // Refresh cache every 0.5s
            _cacheTimer -= Time.deltaTime;
            if (_cacheTimer <= 0f)
            {
                _cacheTimer = 0.5f;
                RefreshCache();
            }

            if (_cachedSects.Count == 0) return;

            // Position: above the resource bar
            float totalWidth = _cachedSects.Count * (IconSize + IconSpacing) - IconSpacing + RowPadding * 2;
            float x = ResourceHUD.HudLeftMargin;
            float y = Screen.height - ResourceHUD.HudBarHeight - ResourceHUD.HudBottomMargin - RowHeight - 4f;

            var rowRect = new Rect(x, y, totalWidth, RowHeight);
            GUI.Box(rowRect, "", _bgStyle);

            string hoveredTooltip = null;

            for (int i = 0; i < _cachedSects.Count; i++)
            {
                string sectId = _cachedSects[i];
                string displayName = SectConfig.GetDisplayName(sectId);
                string culture = SectConfig.GetAffinity(sectId);
                Color color = CultureColors.ContainsKey(culture) ? CultureColors[culture] : Color.gray;

                float iconX = x + RowPadding + i * (IconSize + IconSpacing);
                float iconY = y + (RowHeight - IconSize) / 2f;
                var iconRect = new Rect(iconX, iconY, IconSize, IconSize);

                // Draw colored icon box with first letter
                GUI.color = color;
                GUI.Box(iconRect, "", _iconStyle);
                GUI.color = Color.white;

                // Draw letter (cached style — was per-frame alloc before)
                GUI.Label(iconRect, displayName.Substring(0, 1), _letterStyle);

                // Tooltip on hover
                if (iconRect.Contains(Event.current.mousePosition))
                {
                    string passive = SectConfig.GetPassiveDescription(sectId);
                    hoveredTooltip = $"<b>{displayName}</b> ({culture})\n{passive}";
                }
            }

            // Draw tooltip
            if (!string.IsNullOrEmpty(hoveredTooltip))
            {
                var mousePos = Event.current.mousePosition;
                var content = new GUIContent(hoveredTooltip);
                var size = _tooltipStyle.CalcSize(content);
                size.x = Mathf.Min(size.x, 250f);
                size.y = _tooltipStyle.CalcHeight(content, size.x);

                var tipRect = new Rect(mousePos.x + 10, mousePos.y - size.y - 5, size.x + 16, size.y + 12);
                GUI.Box(tipRect, hoveredTooltip, _tooltipStyle);
            }
        }

        private void RefreshCache()
        {
            _cachedSects.Clear();
            var sectState = FactionSectState.Instance;
            if (sectState == null) return;

            var adopted = sectState.GetAdoptedSects(GameSettings.LocalPlayerFaction);
            if (adopted != null)
                _cachedSects.AddRange(adopted);
        }

        private void InitStyles()
        {
            // Bordered row background — navy bg, gold border at 0.5 alpha.
            _bgStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = UIHelpers.MakeBorderedTexture(64, 64,
                    new Color(Styles.PanelBgColor.r, Styles.PanelBgColor.g, Styles.PanelBgColor.b, 0.85f),
                    new Color(Styles.HighlightColor.r, Styles.HighlightColor.g, Styles.HighlightColor.b, 0.5f), 1) }
            };

            // Icon background — dark grey with gold border at 0.6 alpha.
            _iconStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = UIHelpers.MakeBorderedTexture(32, 32,
                    new Color(0.15f, 0.15f, 0.15f, 0.9f),
                    new Color(Styles.HighlightColor.r, Styles.HighlightColor.g, Styles.HighlightColor.b, 0.6f), 1) }
            };

            // Tooltip box — dark bordered, rich text. Bg literal (0.05,0.06,0.14,0.97) is
            // a slightly darker variant of PanelBgColor for tooltip contrast (no Styles match).
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                richText = true,
                fontSize = 12,
                normal = {
                    textColor = new Color(0.9f, 0.85f, 0.7f),
                    background = UIHelpers.MakeBorderedTexture(64, 64,
                        new Color(0.05f, 0.06f, 0.14f, 0.97f),
                        new Color(Styles.HighlightColor.r, Styles.HighlightColor.g, Styles.HighlightColor.b, 0.6f), 1)
                },
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _letterStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            _stylesInit = true;
        }
    }
}
