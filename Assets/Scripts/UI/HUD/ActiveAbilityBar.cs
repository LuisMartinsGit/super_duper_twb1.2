// ActiveAbilityBar.cs
// Shows active ability buttons at the top of the screen.
// Location: Assets/Scripts/UI/HUD/ActiveAbilityBar.cs

using UnityEngine;
using System.Collections.Generic;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI bar at the top of the screen showing active sect abilities.
    /// Each adopted sect tech that grants an active ability gets a button here.
    /// </summary>
    public class ActiveAbilityBar : MonoBehaviour
    {
        private const float BarHeight = 36f;
        private const float ButtonWidth = 100f;
        private const float ButtonSpacing = 4f;
        private const float BarPadding = 8f;

        private GUIStyle _barStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _tooltipStyle;
        private bool _stylesInit;

        // Cache
        private float _cacheTimer;
        private List<ActiveAbility> _cachedAbilities = new();

        private struct ActiveAbility
        {
            public string Name;
            public string Description;
            public string SectId;
            public float CooldownRemaining;
        }

        void OnGUI()
        {
            Styles.Initialize();
            if (!_stylesInit) BuildLocalStyles();

            _cacheTimer -= Time.deltaTime;
            if (_cacheTimer <= 0f)
            {
                _cacheTimer = 1f;
                RefreshAbilities();
            }

            if (_cachedAbilities.Count == 0) return;

            float totalWidth = _cachedAbilities.Count * (ButtonWidth + ButtonSpacing) - ButtonSpacing + BarPadding * 2;
            float x = (Screen.width - totalWidth) / 2f;
            float y = 4f;

            var barRect = new Rect(x, y, totalWidth, BarHeight);
            GUI.Box(barRect, "", _barStyle);

            string hoveredTooltip = null;

            for (int i = 0; i < _cachedAbilities.Count; i++)
            {
                var ability = _cachedAbilities[i];
                float btnX = x + BarPadding + i * (ButtonWidth + ButtonSpacing);
                float btnY = y + (BarHeight - 24f) / 2f;
                var btnRect = new Rect(btnX, btnY, ButtonWidth, 24f);

                bool onCooldown = ability.CooldownRemaining > 0f;
                bool wasEnabled = GUI.enabled;
                if (onCooldown) GUI.enabled = false;

                string label = onCooldown
                    ? $"{ability.Name} ({ability.CooldownRemaining:F0}s)"
                    : ability.Name;

                if (GUI.Button(btnRect, label, _buttonStyle))
                {
                    // Ability activation — placeholder for future implementation
                }

                GUI.enabled = wasEnabled;

                if (btnRect.Contains(Event.current.mousePosition))
                {
                    hoveredTooltip = $"<b>{ability.Name}</b>\n{ability.Description}";
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

                var tipRect = new Rect(mousePos.x + 10, mousePos.y + 15, size.x + 16, size.y + 12);
                GUI.Box(tipRect, hoveredTooltip, _tooltipStyle);
            }
        }

        private void RefreshAbilities()
        {
            _cachedAbilities.Clear();

            // task-063 phase 1: this bar previously showed one entry per
            // sect-tech the player had researched, via the deleted
            // FactionSectState + SectConfig.GetTechId / GetTechDisplayName /
            // GetTechDescription bridge. Sect tech research is no longer a
            // thing in the redesign (chapels drive adoption, not research).
            //
            // Phase 2 will repopulate this bar with the new "Active Power"
            // lever per adopted sect — read from SectAdoptionState on the
            // faction bank entity.
        }

        // Build the truly-unique cached locals (bar/button/tooltip — all bordered with golden
        // accents but with custom font sizes and dimensions specific to this top bar).
        // Standard panel/header/label/button styles are not used here because the ability bar
        // has its own visual language (top centered, slim, icon-text mixed buttons).
        private void BuildLocalStyles()
        {
            // Bar background: navy panel bg with thin 1px golden border (alpha 0.6).
            // Both colors derived from Styles to keep palette canonical.
            _barStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = UIHelpers.MakeBorderedTexture(64, 64,
                    new Color(Styles.PanelBgColor.r, Styles.PanelBgColor.g, Styles.PanelBgColor.b, 0.9f),
                    new Color(Styles.HighlightColor.r, Styles.HighlightColor.g,
                              Styles.HighlightColor.b, 0.6f), 1) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Styles.HighlightColor }
            };

            // Tooltip box: bordered word-wrap rich-text with custom padding. Background uses a
            // slightly darker navy variant kept inline (no Styles match). Border from HighlightColor.
            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                richText = true,
                fontSize = 12,
                normal = {
                    textColor = new Color(0.9f, 0.85f, 0.7f),
                    background = UIHelpers.MakeBorderedTexture(64, 64,
                        new Color(0.05f, 0.06f, 0.14f, 0.97f),
                        new Color(Styles.HighlightColor.r, Styles.HighlightColor.g,
                                  Styles.HighlightColor.b, 0.6f), 1)
                },
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _stylesInit = true;
        }
    }
}
