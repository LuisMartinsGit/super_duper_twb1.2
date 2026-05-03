// File: Assets/Scripts/UI/Menus/OptionsMenuUI.cs
// Options menu panel with graphics, resolution, fullscreen, and volume settings.
// Persists settings via PlayerPrefs and applies them to Unity APIs.

using UnityEngine;
using System;
using System.Collections.Generic;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Options menu accessible from the main menu.
    /// Provides settings for graphics quality, resolution, fullscreen mode,
    /// and master volume. All settings persist via PlayerPrefs.
    /// </summary>
    public class OptionsMenuUI : MonoBehaviour
    {
        // ================================================================
        // EVENTS
        // ================================================================

        public event Action OnBackPressed;

        // ================================================================
        // PLAYERPREFS KEYS
        // ================================================================

        private const string PrefGraphicsQuality = "graphics_quality";
        private const string PrefResolutionWidth = "resolution_width";
        private const string PrefResolutionHeight = "resolution_height";
        private const string PrefFullscreen = "fullscreen";
        private const string PrefMasterVolume = "master_volume";

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

        // Layout
        private Rect _windowRect;
        private const float PanelWidth = 400f;
        private const float PanelHeight = 500f;

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
        /// Load persisted settings from PlayerPrefs and apply them to Unity APIs.
        /// Call this once at startup (e.g., from MainMenuUI.Awake) so that
        /// saved settings take effect before the player opens the Options panel.
        /// </summary>
        public static void LoadAndApplySettings()
        {
            // Graphics quality
            if (PlayerPrefs.HasKey(PrefGraphicsQuality))
            {
                int quality = PlayerPrefs.GetInt(PrefGraphicsQuality);
                quality = Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(quality, true);
            }

            // Resolution & fullscreen
            bool hasResolution = PlayerPrefs.HasKey(PrefResolutionWidth) &&
                                 PlayerPrefs.HasKey(PrefResolutionHeight);
            bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;

            if (hasResolution)
            {
                int w = PlayerPrefs.GetInt(PrefResolutionWidth);
                int h = PlayerPrefs.GetInt(PrefResolutionHeight);
                if (w > 0 && h > 0)
                {
                    Screen.SetResolution(w, h, fullscreen);
                }
            }
            else
            {
                Screen.fullScreen = fullscreen;
            }

            // Master volume
            if (PlayerPrefs.HasKey(PrefMasterVolume))
            {
                float vol = PlayerPrefs.GetFloat(PrefMasterVolume, 100f);
                AudioListener.volume = Mathf.Clamp01(vol / 100f);
            }
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
            GUILayout.Label("OPTIONS", _titleStyle);
            GUILayout.Space(8f);
            DrawSeparator(contentWidth);
            GUILayout.Space(10f);

            // ---- Graphics Quality ----
            GUILayout.Label("Graphics Quality", _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < _qualityLabels.Length; i++)
            {
                var style = (i == _qualityLevel) ? _activeButtonStyle : _buttonStyle;
                if (GUILayout.Button(_qualityLabels[i], style, GUILayout.Height(30f)))
                {
                    _qualityLevel = i;
                }
                if (i < _qualityLabels.Length - 1)
                    GUILayout.Space(4f);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);

            // ---- Resolution ----
            GUILayout.Label("Resolution", _sectionHeaderStyle);
            GUILayout.Space(4f);

            string currentResLabel = _selectedResolutionIndex >= 0 && _selectedResolutionIndex < _resolutionLabels.Length
                ? _resolutionLabels[_selectedResolutionIndex]
                : "Unknown";

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
            GUILayout.Label("Display Mode", _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            var windowedStyle = _fullscreen ? _buttonStyle : _activeButtonStyle;
            var fullscreenStyle = _fullscreen ? _activeButtonStyle : _buttonStyle;

            if (GUILayout.Button("Windowed", windowedStyle, GUILayout.Height(30f)))
                _fullscreen = false;
            GUILayout.Space(4f);
            if (GUILayout.Button("Fullscreen", fullscreenStyle, GUILayout.Height(30f)))
                _fullscreen = true;

            GUILayout.EndHorizontal();

            GUILayout.Space(14f);

            // ---- Master Volume ----
            GUILayout.Label("Master Volume", _sectionHeaderStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            float prevVolume = _masterVolume;
            _masterVolume = GUILayout.HorizontalSlider(
                _masterVolume, 0f, 100f, _sliderStyle, _sliderThumbStyle,
                GUILayout.Height(20f));
            // Apply volume immediately so the user can hear what they're setting.
            // Was previously only applied on Apply, leaving the slider feeling
            // disconnected. Persistence still happens at Apply via PlayerPrefs.
            // (task-062 Q-33)
            if (!Mathf.Approximately(prevVolume, _masterVolume))
                AudioListener.volume = Mathf.Clamp01(_masterVolume / 100f);
            GUILayout.Space(8f);
            GUILayout.Label($"{Mathf.RoundToInt(_masterVolume)}%", Styles.Label, GUILayout.Width(40f));
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

            if (GUILayout.Button("Back", _buttonStyle, GUILayout.Height(36f), GUILayout.Width(100f)))
            {
                OnBackPressed?.Invoke();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Apply", _applyButtonStyle, GUILayout.Height(36f), GUILayout.Width(120f)))
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

            // Graphics quality
            _qualityLevel = PlayerPrefs.GetInt(PrefGraphicsQuality, QualitySettings.GetQualityLevel());
            _qualityLevel = Mathf.Clamp(_qualityLevel, 0, _qualityLabels.Length - 1);

            // Resolution - find current
            int curW = PlayerPrefs.GetInt(PrefResolutionWidth, Screen.width);
            int curH = PlayerPrefs.GetInt(PrefResolutionHeight, Screen.height);
            _selectedResolutionIndex = FindResolutionIndex(curW, curH);

            // Fullscreen
            _fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;

            // Volume
            _masterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, AudioListener.volume * 100f);
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
            PlayerPrefs.SetInt(PrefGraphicsQuality, _qualityLevel);

            // Resolution & fullscreen
            if (_selectedResolutionIndex >= 0 && _selectedResolutionIndex < _availableResolutions.Length)
            {
                var res = _availableResolutions[_selectedResolutionIndex];
                Screen.SetResolution(res.width, res.height, _fullscreen);
                PlayerPrefs.SetInt(PrefResolutionWidth, res.width);
                PlayerPrefs.SetInt(PrefResolutionHeight, res.height);
            }
            PlayerPrefs.SetInt(PrefFullscreen, _fullscreen ? 1 : 0);

            // Volume
            AudioListener.volume = Mathf.Clamp01(_masterVolume / 100f);
            PlayerPrefs.SetFloat(PrefMasterVolume, _masterVolume);

            PlayerPrefs.Save();

            // Show status
            _statusMessage = "Settings applied!";
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
