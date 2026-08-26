// Options menu panel with graphics, resolution, fullscreen, and volume settings.
// Persists settings through PlayerProfile (settings.json beside the exe).

using UnityEngine;
using System;
using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TheWaningBorder.UI.Common;
using TheWaningBorder.Audio;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Options menu accessible from the main menu.
    /// Provides settings for graphics quality, resolution, fullscreen mode,
    /// and master volume. All settings persist through PlayerProfile.
    /// </summary>
    public class OptionsMenuUI : MonoBehaviour
    {
        // ================================================================
        // EVENTS
        // ================================================================

        public event Action OnBackPressed;

        // ================================================================
        // UI STATE
        // ================================================================

        // Graphics quality - labels derived from project QualitySettings
        private int _qualityLevel;
        private string[] _qualityLabels;

        // Resolution
        private Resolution[] _availableResolutions;
        private string[] _resolutionLabels;
        private int _selectedResolutionIndex;
        private bool _showResolutionDropdown;
        private Vector2 _resolutionScrollPos;

        // Fullscreen
        private bool _fullscreen;

        // Volume (0-100)
        private float _masterVolume;
        private float _musicVolume;

        // Player name — the one setting that is not about the machine.
        private string _playerName = "";

        // Layout
        private Rect _windowRect;
        private const float PanelWidth = 400f;
        private const float PanelHeight = 720f;   // + the player-name row

        // Specialty cached styles (no Styles.cs counterpart — custom hover/active textures,
        // light-blue section headers, green-gold apply button, slider-specific styles).
        private GUIStyle _titleStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _activeButtonStyle;
        private GUIStyle _dropdownButtonStyle;
        private GUIStyle _dropdownItemStyle;
        private GUIStyle _dropdownItemHoverStyle;
        private GUIStyle _applyButtonStyle;
        private GUIStyle _sliderStyle;
        private GUIStyle _sliderThumbStyle;
        private GUIStyle _statusStyle;
        private bool _stylesBuilt;

        // Status message
        private string _statusMessage;
        private float _statusTimer;

        // ================================================================
        // PUBLIC API - Boot-time settings loader
        // ================================================================

        /// <summary>
        /// Apply persisted settings automatically at app start. The old IMGUI
        /// MainMenuUI used to call LoadAndApplySettings from its Awake; that
        /// menu is deleted (2026-07-16) and the live Synty uGUI menu never
        /// wired it — saved video/audio settings silently stopped applying.
        /// A RuntimeInitializeOnLoadMethod is scene-independent and survives
        /// any future menu rework.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedSettingsOnBoot()
        {
            LoadAndApplySettings();
        }

        /// <summary>
        /// Load persisted settings from PlayerProfile and apply them to Unity APIs.
        /// Runs once at startup via ApplySavedSettingsOnBoot so that saved
        /// settings take effect before the player opens the Options panel.
        /// </summary>
        public static void LoadAndApplySettings()
        {
            // Graphics quality. -1 means the player has never chosen, so the
            // project's own default stands.
            if (PlayerProfile.GraphicsQuality >= 0)
            {
                int quality = Mathf.Clamp(PlayerProfile.GraphicsQuality,
                                          0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(quality, true);
            }

            // Resolution & fullscreen
            int w = PlayerProfile.ResolutionWidth;
            int h = PlayerProfile.ResolutionHeight;
            bool fullscreen = PlayerProfile.Fullscreen >= 0
                ? PlayerProfile.Fullscreen == 1
                : Screen.fullScreen;

            if (w > 0 && h > 0) Screen.SetResolution(w, h, fullscreen);
            else Screen.fullScreen = fullscreen;

            // Master volume
            AudioListener.volume = Mathf.Clamp01(PlayerProfile.MasterVolume / 100f);

            // Music volume. MusicManager reads the profile in Awake; this call
            // covers the case where it already exists (domain reload, settings
            // re-applied mid-session) and no-ops before that.
            MusicManager.SetVolume(Mathf.Clamp01(PlayerProfile.MusicVolume / 100f));
        }

        // ================================================================
        // LIFECYCLE
        // ================================================================

        void OnEnable()
        {
            LoadSettingsToUI();
            _showResolutionDropdown = false;
            _statusMessage = null;
            _statusTimer = 0f;
        }

        void Update()
        {
            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.unscaledDeltaTime;
                if (_statusTimer <= 0f)
                    _statusMessage = null;
            }
        }

        void OnGUI()
        {
            Styles.Initialize();
            if (!_stylesBuilt) BuildStyles();

            // Center the window
            float x = (Screen.width - PanelWidth) * 0.5f;
            float y = (Screen.height - PanelHeight) * 0.5f;
            _windowRect = new Rect(x, y, PanelWidth, PanelHeight);

            _windowRect = GUI.Window(10005, _windowRect, DrawOptionsWindow, "", Styles.PanelBox);
        }

        // ================================================================
        // DRAWING
        // ================================================================

        private void DrawOptionsWindow(int windowId)
        {
            float pad = 20f;
            float contentWidth = PanelWidth - pad * 2;

            GUILayout.BeginArea(new Rect(pad, 15f, contentWidth, PanelHeight - 30f));

            // Title
            GUILayout.Label(Loc.T("OPTIONS"), _titleStyle);
            GUILayout.Space(8f);
            DrawSeparator(contentWidth);
            GUILayout.Space(10f);

            // ---- Player Name ----
            // First in the list on purpose: it is the only setting here that
            // is about the player rather than the machine, and it is the one
            // they came looking for after being asked once at first run.
            GUILayout.Label(Loc.T("Player Name"), _sectionHeaderStyle);
            GUILayout.Space(4f);
            _playerName = GUILayout.TextField(_playerName ?? "", 24,
                                              Styles.Label, GUILayout.Height(28f));
            GUILayout.Space(12f);

            // ---- Graphics Quality ----
            GUILayout.Label(Loc.T("Graphics Quality"), _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < _qualityLabels.Length; i++)
            {
                var style = (i == _qualityLevel) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(Loc.T(_qualityLabels[i]), style, GUILayout.Height(30f)))
                {
                    _qualityLevel = i;
                }
                if (i < _qualityLabels.Length - 1)
                    GUILayout.Space(4f);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);

            // ---- Resolution ----
            GUILayout.Label(Loc.T("Resolution"), _sectionHeaderStyle);
            GUILayout.Space(4f);

            string currentResLabel = _selectedResolutionIndex >= 0 && _selectedResolutionIndex < _resolutionLabels.Length
                ? _resolutionLabels[_selectedResolutionIndex]
                : Loc.T("Unknown");

            if (GUILayout.Button(currentResLabel, _dropdownButtonStyle, GUILayout.Height(28f)))
            {
                _showResolutionDropdown = !_showResolutionDropdown;
            }

            if (_showResolutionDropdown)
            {
                float dropHeight = Mathf.Min(_resolutionLabels.Length * 24f, 160f);

                _resolutionScrollPos = GUILayout.BeginScrollView(
                    _resolutionScrollPos, GUILayout.Height(dropHeight));

                for (int i = 0; i < _resolutionLabels.Length; i++)
                {
                    var itemStyle = (i == _selectedResolutionIndex)
                        ? _dropdownItemHoverStyle : _dropdownItemStyle;

                    if (GUILayout.Button(_resolutionLabels[i], itemStyle, GUILayout.Height(22f)))
                    {
                        _selectedResolutionIndex = i;
                        _showResolutionDropdown = false;
                    }
                }

                GUILayout.EndScrollView();
            }

            GUILayout.Space(14f);

            // ---- Fullscreen ----
            GUILayout.Label(Loc.T("Display Mode"), _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            var windowedStyle = _fullscreen ? _buttonStyle : _activeButtonStyle;
            var fullscreenStyle = _fullscreen ? _activeButtonStyle : _buttonStyle;

            if (GUILayout.Button(Loc.T("Windowed"), windowedStyle, GUILayout.Height(30f)))
                _fullscreen = false;
            GUILayout.Space(4f);
            if (GUILayout.Button(Loc.T("Fullscreen"), fullscreenStyle, GUILayout.Height(30f)))
                _fullscreen = true;

            GUILayout.EndHorizontal();

            GUILayout.Space(14f);

            // ---- Master Volume ----
            GUILayout.Label(Loc.T("Master Volume"), _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            float prevVolume = _masterVolume;
            _masterVolume = GUILayout.HorizontalSlider(
                _masterVolume, 0f, 100f, _sliderStyle, _sliderThumbStyle,
                GUILayout.Height(20f));
            // Apply volume immediately so the user can hear what they're setting.
            // Was previously only applied on Apply, leaving the slider feeling
            // disconnected. Persistence still happens at Apply, into settings.json.
            // (task-062 Q-33)
            if (!Mathf.Approximately(prevVolume, _masterVolume))
                AudioListener.volume = Mathf.Clamp01(_masterVolume / 100f);
            GUILayout.Space(8f);
            GUILayout.Label($"{Mathf.RoundToInt(_masterVolume)}%", Styles.Label, GUILayout.Width(40f));
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);

            // ---- Music Volume ----
            GUILayout.Label(Loc.T("Music Volume"), _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            float prevMusic = _musicVolume;
            _musicVolume = GUILayout.HorizontalSlider(
                _musicVolume, 0f, 100f, _sliderStyle, _sliderThumbStyle,
                GUILayout.Height(20f));
            // Same immediate-audition rule as the master slider: the music is
            // playing while the menu is open, so the change is audible as the
            // thumb moves. Persistence happens at Apply.
            if (!Mathf.Approximately(prevMusic, _musicVolume))
                MusicManager.SetVolume(Mathf.Clamp01(_musicVolume / 100f));
            GUILayout.Space(8f);
            GUILayout.Label($"{Mathf.RoundToInt(_musicVolume)}%", Styles.Label, GUILayout.Width(40f));
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);

            // ---- Language ----
            GUILayout.Label(Loc.T("Language"), _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            bool isPt = Loc.IsPortuguese;
            // Language names are shown in THEIR OWN language on purpose — a
            // player stuck in the wrong one must be able to find their way
            // back without reading it.
            if (GUILayout.Button("English", isPt ? _buttonStyle : _activeButtonStyle, GUILayout.Height(30f)))
                Loc.Language = Loc.English;
            GUILayout.Space(4f);
            if (GUILayout.Button("Português", isPt ? _activeButtonStyle : _buttonStyle, GUILayout.Height(30f)))
                Loc.Language = Loc.Portuguese;
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            // ---- Status message ----
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Label(_statusMessage, _statusStyle);
                GUILayout.Space(6f);
            }

            // ---- Action Buttons ----
            DrawSeparator(contentWidth);
            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(Loc.T("Back"), _buttonStyle, GUILayout.Height(36f), GUILayout.Width(100f)))
            {
                OnBackPressed?.Invoke();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(Loc.T("Apply"), _applyButtonStyle, GUILayout.Height(36f), GUILayout.Width(120f)))
            {
                ApplySettings();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.EndArea();

            // Draggable title bar
            GUI.DragWindow(new Rect(0, 0, 10000, 25));
        }

        private void DrawSeparator(float width)
        {
            var rect = GUILayoutUtility.GetRect(width, 2f);
            var oldColor = GUI.color;
            // Golden separator with custom 0.5 alpha (Styles.HighlightColor is alpha=1)
            var c = Styles.HighlightColor; c.a = 0.5f;
            GUI.color = c;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        // ================================================================
        // SETTINGS LOGIC
        // ================================================================

        private void LoadSettingsToUI()
        {
            // Build quality labels from project QualitySettings
            _qualityLabels = QualitySettings.names;

            // Build resolution list
            BuildResolutionList();

            // Player name
            _playerName = PlayerProfile.PlayerName;

            // Graphics quality
            _qualityLevel = PlayerProfile.GraphicsQuality >= 0
                ? PlayerProfile.GraphicsQuality : QualitySettings.GetQualityLevel();
            _qualityLevel = Mathf.Clamp(_qualityLevel, 0, _qualityLabels.Length - 1);

            // Resolution - find current
            int curW = PlayerProfile.ResolutionWidth > 0 ? PlayerProfile.ResolutionWidth : Screen.width;
            int curH = PlayerProfile.ResolutionHeight > 0 ? PlayerProfile.ResolutionHeight : Screen.height;
            _selectedResolutionIndex = FindResolutionIndex(curW, curH);

            // Fullscreen
            _fullscreen = PlayerProfile.Fullscreen >= 0
                ? PlayerProfile.Fullscreen == 1 : Screen.fullScreen;

            // Volume
            _masterVolume = PlayerProfile.MasterVolume;
            _musicVolume = PlayerProfile.MusicVolume;
        }

        private void BuildResolutionList()
        {
            var resolutions = Screen.resolutions;

            // De-duplicate (ignore refresh rate) and sort descending
            var seen = new HashSet<string>();
            var unique = new List<Resolution>();

            // Iterate in reverse so we get highest refresh rate first for each resolution
            for (int i = resolutions.Length - 1; i >= 0; i--)
            {
                string key = $"{resolutions[i].width}x{resolutions[i].height}";
                if (seen.Add(key))
                    unique.Add(resolutions[i]);
            }

            // Sort by width descending, then height descending
            unique.Sort((a, b) =>
            {
                int cmp = b.width.CompareTo(a.width);
                return cmp != 0 ? cmp : b.height.CompareTo(a.height);
            });

            _availableResolutions = unique.ToArray();
            _resolutionLabels = new string[_availableResolutions.Length];

            for (int i = 0; i < _availableResolutions.Length; i++)
            {
                var r = _availableResolutions[i];
                _resolutionLabels[i] = $"{r.width} x {r.height}";
            }
        }

        private int FindResolutionIndex(int width, int height)
        {
            for (int i = 0; i < _availableResolutions.Length; i++)
            {
                if (_availableResolutions[i].width == width &&
                    _availableResolutions[i].height == height)
                    return i;
            }
            // Fallback: first resolution
            return 0;
        }

        private void ApplySettings()
        {
            // Graphics quality (clamp to valid range in case labels changed)
            _qualityLevel = Mathf.Clamp(_qualityLevel, 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(_qualityLevel, true);
            PlayerProfile.GraphicsQuality = _qualityLevel;

            // Resolution & fullscreen
            if (_selectedResolutionIndex >= 0 && _selectedResolutionIndex < _availableResolutions.Length)
            {
                var res = _availableResolutions[_selectedResolutionIndex];
                Screen.SetResolution(res.width, res.height, _fullscreen);
                PlayerProfile.ResolutionWidth = res.width;
                PlayerProfile.ResolutionHeight = res.height;
            }
            PlayerProfile.Fullscreen = _fullscreen ? 1 : 0;

            // Volume
            AudioListener.volume = Mathf.Clamp01(_masterVolume / 100f);
            PlayerProfile.MasterVolume = _masterVolume;

            // Music volume
            MusicManager.SetVolume(Mathf.Clamp01(_musicVolume / 100f));
            PlayerProfile.MusicVolume = _musicVolume;

            // The name is written straight through by its own setter, so this
            // covers everything above it in one file write.
            PlayerProfile.PlayerName = _playerName;

            // Show status
            _statusMessage = Loc.T("Settings applied!");
            _statusTimer = 2f;

        }

        // ================================================================
        // STYLES
        // ================================================================

        private void BuildStyles()
        {
            // Title: 20pt bold gold, centered — derived from Styles.Header (which is 20pt gold).
            _titleStyle = new GUIStyle(Styles.Header)
            {
                alignment = TextAnchor.MiddleCenter
            };

            // Section headers (light blue) — unique to options menu, no Styles match.
            _sectionHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.6f, 0.8f, 1f) }
            };

            // Button textures — specialty hover/active behavior, unique to options menu.
            var texButton = Styles.MakeSolid(new Color(0.10f, 0.12f, 0.28f, 0.9f));
            var texButtonHover = Styles.MakeSolid(new Color(0.15f, 0.18f, 0.38f, 0.95f));
            var texButtonActive = Styles.MakeSolid(new Color(0.20f, 0.24f, 0.50f, 0.95f));

            // Normal button — gold-on-navy with hover lighten, sourced from Styles.HighlightColor.
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Styles.HighlightColor, background = texButton },
                hover = { textColor = new Color(1f, 0.85f, 0.4f), background = texButtonHover },
                active = { textColor = Color.white, background = texButtonHover },
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(8, 8, 6, 6)
            };

            // Active/selected button (highlighted)
            _activeButtonStyle = new GUIStyle(_buttonStyle)
            {
                normal = { textColor = Color.white, background = texButtonActive },
                hover = { textColor = Color.white, background = texButtonActive }
            };

            // Dropdown button
            _dropdownButtonStyle = new GUIStyle(_buttonStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 8, 6, 6)
            };

            // Dropdown items — separate hover textures (no Styles match for this list pattern).
            var texDropdownItem = Styles.MakeSolid(new Color(0.08f, 0.10f, 0.22f, 0.95f));
            var texDropdownItemHover = Styles.MakeSolid(new Color(0.15f, 0.18f, 0.38f, 0.95f));

            _dropdownItemStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                normal = { textColor = new Color(0.9f, 0.88f, 0.82f), background = texDropdownItem },
                hover = { textColor = new Color(1f, 0.85f, 0.4f), background = texDropdownItemHover },
                active = { textColor = Color.white, background = texDropdownItemHover },
                padding = new RectOffset(10, 6, 3, 3),
                margin = new RectOffset(0, 0, 0, 0)
            };

            _dropdownItemHoverStyle = new GUIStyle(_dropdownItemStyle)
            {
                normal = { textColor = Color.white, background = texDropdownItemHover }
            };

            // Apply button — green-gold variant, no Styles match.
            var texApply = Styles.MakeSolid(new Color(0.12f, 0.18f, 0.10f, 0.9f));
            var texApplyHover = Styles.MakeSolid(new Color(0.18f, 0.26f, 0.14f, 0.95f));

            _applyButtonStyle = new GUIStyle(_buttonStyle)
            {
                normal = { textColor = new Color(0.4f, 1f, 0.4f), background = texApply },
                hover = { textColor = new Color(0.5f, 1f, 0.5f), background = texApplyHover },
                active = { textColor = Color.white, background = texApplyHover }
            };

            // Slider styles — Unity defaults with size overrides.
            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 12f
            };
            _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 16f,
                fixedHeight = 16f
            };

            // Status message style (green, centered) — derived from Styles.Label.
            _statusStyle = new GUIStyle(Styles.Label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Styles.SuccessColor }
            };

            _stylesBuilt = true;
        }
    }
}
