// SpellPanel.cs
// IMGUI panel showing available spells with cooldown indicators
// Location: Assets/Scripts/UI/HUD/SpellPanel.cs

using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// IMGUI panel that displays available spells for the local player's adopted sects.
    /// Shows spell buttons with cooldown overlays. Clicking a spell enters targeting mode.
    ///
    /// Positioned at the left side, above the bottom HUD bar.
    /// Only visible when the faction has adopted at least one sect.
    /// </summary>
    public class SpellPanel : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private Faction humanFaction = GameSettings.LocalPlayerFaction;

        // Layout constants
        private const float PanelWidth = 200f;
        private const float PanelHeight = 200f;
        private const float ButtonSize = 22f;
        private const float ButtonSpacing = 3f;
        private const float Margin = 6f;

        // Local cached styles — all unique to spell panel layout (small icon-grid header,
        // bordered button bg, dark cooldown overlay, dim tooltip, bold targeting indicator).
        // Standard styles are sourced from Styles.cs; only specialty styles cached here.
        private GUIStyle _panelBg;
        private GUIStyle _headerStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _cooldownStyle;
        private GUIStyle _tooltipStyle;
        private GUIStyle _targetingStyle;
        private Texture2D _texPanel;
        private Texture2D _texButton;
        private Texture2D _texCooldown;
        private bool _stylesBuilt;

        // Spell-panel-local color constants (no clean Styles.cs counterpart).
        // Gold accents come from Styles.HighlightColor directly. Dim tooltip text uses an
        // inline literal that is close to Styles.SmallLabel's (0.7,0.68,0.6) but darker.
        private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.18f, 0.88f);
        private static readonly Color ButtonColor = new Color(0.12f, 0.14f, 0.25f, 0.95f);
        private static readonly Color CooldownColor = new Color(0.0f, 0.0f, 0.0f, 0.65f);
        private static readonly Color DimText = new Color(0.6f, 0.58f, 0.50f);

        // Cached spell list
        private readonly List<SpellDefinition> _availableSpells = new();
        private float _refreshTimer;

        /// <summary>Returns true if the mouse is over the spell panel.</summary>
        public static bool IsPointerOverPanel { get; private set; }

        void OnGUI()
        {
            Styles.Initialize();
            BuildLocalStyles();

            var sectState = FactionSectState.Instance;
            var spellState = SpellState.Instance;
            var castSystem = SpellCastSystem.Instance;
            if (sectState == null || spellState == null) return;

            // Refresh available spells periodically
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                RefreshAvailableSpells(sectState);
                _refreshTimer = 1.0f;
            }

            if (_availableSpells.Count == 0) return;

            // Panel position: left-aligned, above the bottom HUD bar
            float panelX = ResourceHUD.HudLeftMargin;
            float hudBarTop = Screen.height - ResourceHUD.HudBarHeight - ResourceHUD.HudBottomMargin;
            float panelY = hudBarTop - PanelHeight - ResourceHUD.PanelGap;

            Rect panelRect = new Rect(panelX, panelY, PanelWidth, PanelHeight);
            IsPointerOverPanel = panelRect.Contains(Event.current.mousePosition);

            // Draw panel background
            GUI.Box(panelRect, GUIContent.none, _panelBg);

            GUILayout.BeginArea(new Rect(panelX + Margin, panelY + Margin,
                PanelWidth - Margin * 2, PanelHeight - Margin * 2));

            // Header
            GUILayout.Label("SPELLS", _headerStyle);
            GUILayout.Space(4);

            // Targeting indicator
            if (castSystem != null && castSystem.IsTargeting)
            {
                GUILayout.Label($"Targeting: {castSystem.ActiveSpell.Name}", _targetingStyle);
                GUILayout.Label("Click target location (Right-click to cancel)", _tooltipStyle);
                GUILayout.Space(4);
            }

            // Spell buttons in a grid (4 per row)
            int col = 0;
            GUILayout.BeginHorizontal();

            foreach (var spell in _availableSpells)
            {
                if (col >= 4)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.Space(ButtonSpacing);
                    GUILayout.BeginHorizontal();
                    col = 0;
                }

                DrawSpellButton(spell, spellState, castSystem);
                GUILayout.Space(ButtonSpacing);
                col++;
            }

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawSpellButton(SpellDefinition spell, SpellState spellState, SpellCastSystem castSystem)
        {
            bool onCooldown = spellState.IsOnCooldown(humanFaction, spell.Id);
            float remaining = spellState.GetCooldownRemaining(humanFaction, spell.Id);
            bool isActive = castSystem != null && castSystem.IsTargeting &&
                            castSystem.ActiveSpell?.Id == spell.Id;

            // Button rect
            Rect btnRect = GUILayoutUtility.GetRect(ButtonSize, ButtonSize, GUILayout.Width(ButtonSize));

            // Draw button background
            Color oldColor = GUI.color;
            GUI.color = isActive ? Styles.HighlightColor : Color.white;
            GUI.Box(btnRect, GUIContent.none, _buttonStyle);
            GUI.color = oldColor;

            // Draw spell name abbreviation (first 3 chars)
            string abbrev = spell.Name.Length > 3 ? spell.Name.Substring(0, 3) : spell.Name;
            GUI.Label(new Rect(btnRect.x + 4, btnRect.y + 4, ButtonSize - 8, 20f), abbrev, _headerStyle);

            // Draw cooldown overlay
            if (onCooldown)
            {
                GUI.Box(btnRect, GUIContent.none, _cooldownStyle);
                string cdText = remaining > 1f ? $"{remaining:F0}s" : $"{remaining:F1}s";
                GUI.Label(new Rect(btnRect.x, btnRect.y + ButtonSize * 0.35f, ButtonSize, 20f),
                    cdText, _cooldownStyle);
            }

            // Tooltip on hover
            if (btnRect.Contains(Event.current.mousePosition))
            {
                float tipX = btnRect.x;
                float tipY = btnRect.y - 45f;
                GUI.Label(new Rect(tipX, tipY, 200f, 40f),
                    $"{spell.Name}\n{spell.Description}", _tooltipStyle);
            }

            // Click handler. Earlier missing braces meant BeginTargeting ran
            // unconditionally — clicking an active spell to cancel called
            // CancelTargeting then immediately re-entered targeting. The
            // user's mental model ("click again to cancel") didn't work; they
            // had to right-click in the world to cancel. (task-060 F-3)
            if (!onCooldown && GUI.Button(btnRect, GUIContent.none, GUIStyle.none))
            {
                if (castSystem != null)
                {
                    if (isActive)
                        castSystem.CancelTargeting();
                        castSystem.BeginTargeting(humanFaction, spell);
                }
            }
        }

        private void RefreshAvailableSpells(FactionSectState sectState)
        {
            _availableSpells.Clear();

            var adopted = sectState.GetAdoptedSects(humanFaction);
            foreach (var sectId in adopted)
            {
                var spell = SpellDatabase.GetSpellForSect(sectId);
                if (spell != null)
                    _availableSpells.Add(spell);
            }
        }

        // Build the truly-unique cached locals (panel/button/cooldown/tooltip/header/targeting).
        // All textures use Styles.MakeSolid() instead of a local MakeTex helper.
        private void BuildLocalStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _texPanel = Styles.MakeSolid(PanelColor);
            _texButton = Styles.MakeSolid(ButtonColor);
            _texCooldown = Styles.MakeSolid(CooldownColor);

            _panelBg = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _texPanel }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Styles.HighlightColor }
            };

            _buttonStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _texButton },
                border = new RectOffset(2, 2, 2, 2)
            };

            _cooldownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white, background = _texCooldown }
            };

            _tooltipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = DimText }
            };

            _targetingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.8f, 0.2f) }
            };
        }
    }
}
