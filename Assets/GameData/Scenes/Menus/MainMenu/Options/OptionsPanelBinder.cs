// OptionsPanelBinder.cs
// The Options screen, bound to an AUTHORED prefab (OptionsPanel.prefab, beside
// this file). Replaces OptionsMenuUI, which drew the whole screen in IMGUI with
// a hand-rolled navy-and-gold GUIStyle set — the last themed immediate-mode
// panel in the game.
//
// Nothing here draws. It reads the authored nodes by name, pushes the saved
// profile into them, and writes it back on Apply.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Systems.Audio;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Drives the authored Options panel. Node names are the contract between
    /// this and the prefab; <see cref="OptionsPanelPrefabBuilder"/> creates them.
    /// </summary>
    public sealed class OptionsPanelBinder : MonoBehaviour
    {
        /// <summary>Raised when the player closes the panel.</summary>
        public event Action OnBackPressed;

        /// <summary>Seconds the "settings applied" line stays up.</summary>
        const float StatusSeconds = 2f;

        TMP_InputField _playerName;
        TMP_Dropdown _quality;
        TMP_Dropdown _resolution;
        Toggle _fullscreen;
        Slider _master, _music;
        TMP_Text _masterValue, _musicValue, _status;

        Resolution[] _resolutions = Array.Empty<Resolution>();
        float _statusTimer;

        #region Boot

        /// <summary>
        /// Apply the saved profile before the player ever opens this panel.
        /// Static and independent of the prefab, so it runs at boot whether or
        /// not the Options screen is ever shown.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySavedSettingsOnBoot() => LoadAndApplySettings();

        /// <summary>Load persisted settings from PlayerProfile onto the Unity APIs.</summary>
        public static void LoadAndApplySettings()
        {
            // -1 means the player never chose, so the project default stands.
            if (PlayerProfile.GraphicsQuality >= 0)
            {
                int quality = Mathf.Clamp(PlayerProfile.GraphicsQuality,
                                          0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(quality, true);
            }

            int w = PlayerProfile.ResolutionWidth;
            int h = PlayerProfile.ResolutionHeight;
            bool fullscreen = PlayerProfile.Fullscreen >= 0
                ? PlayerProfile.Fullscreen == 1
                : Screen.fullScreen;

            if (w > 0 && h > 0) Screen.SetResolution(w, h, fullscreen);
            else Screen.fullScreen = fullscreen;

            AudioListener.volume = Mathf.Clamp01(PlayerProfile.MasterVolume / 100f);

            // MusicManager reads the profile in Awake; this covers the case
            // where it already exists and no-ops before that.
            MusicManager.SetVolume(Mathf.Clamp01(PlayerProfile.MusicVolume / 100f));
        }

        #endregion

        #region Lifecycle

        void Awake()
        {
            _playerName  = Find<TMP_InputField>("PlayerNameInput");
            _quality     = Find<TMP_Dropdown>("QualityDropdown");
            _resolution  = Find<TMP_Dropdown>("ResolutionDropdown");
            _fullscreen  = Find<Toggle>("FullscreenToggle");
            _master      = Find<Slider>("MasterSlider");
            _music       = Find<Slider>("MusicSlider");
            _masterValue = Find<TMP_Text>("MasterValue");
            _musicValue  = Find<TMP_Text>("MusicValue");
            _status      = Find<TMP_Text>("Status");

            Bind("ApplyButton", Apply);
            Bind("BackButton", () => OnBackPressed?.Invoke());
            Bind("EnglishButton", () => SetLanguage(false));
            Bind("PortugueseButton", () => SetLanguage(true));

            if (_master != null) _master.onValueChanged.AddListener(v => Show(_masterValue, v));
            if (_music != null) _music.onValueChanged.AddListener(v => Show(_musicValue, v));

            if (_status != null) _status.text = string.Empty;
        }

        void OnEnable()
        {
            LoadIntoPanel();
            if (_status != null) _status.text = string.Empty;
            _statusTimer = 0f;
        }

        void Update()
        {
            if (_statusTimer <= 0f) return;

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f && _status != null) _status.text = string.Empty;
        }

        #endregion

        #region Binding helpers

        T Find<T>(string node) where T : Component
        {
            foreach (var c in GetComponentsInChildren<T>(true))
                if (c.gameObject.name == node) return c;

            Debug.LogWarning($"[Options] prefab has no '{node}' — that control will not work.");
            return null;
        }

        void Bind(string node, Action onClick)
        {
            var button = Find<Button>(node);
            if (button != null) button.onClick.AddListener(() => onClick());
        }

        static void Show(TMP_Text label, float percent)
        {
            if (label != null) label.text = $"{Mathf.RoundToInt(percent)}%";
        }

        #endregion

        #region Load / apply

        void LoadIntoPanel()
        {
            if (_playerName != null) _playerName.text = PlayerProfile.PlayerName;

            if (_quality != null)
            {
                _quality.ClearOptions();
                _quality.AddOptions(new List<string>(QualitySettings.names));
                int level = PlayerProfile.GraphicsQuality >= 0
                    ? PlayerProfile.GraphicsQuality : QualitySettings.GetQualityLevel();
                _quality.SetValueWithoutNotify(
                    Mathf.Clamp(level, 0, QualitySettings.names.Length - 1));
            }

            BuildResolutionList();

            if (_fullscreen != null)
                _fullscreen.SetIsOnWithoutNotify(PlayerProfile.Fullscreen >= 0
                    ? PlayerProfile.Fullscreen == 1 : Screen.fullScreen);

            if (_master != null) _master.SetValueWithoutNotify(PlayerProfile.MasterVolume);
            if (_music != null) _music.SetValueWithoutNotify(PlayerProfile.MusicVolume);
            Show(_masterValue, PlayerProfile.MasterVolume);
            Show(_musicValue, PlayerProfile.MusicVolume);
        }

        /// <summary>
        /// Screen resolutions, de-duplicated on size (refresh rate ignored) and
        /// sorted largest first.
        /// </summary>
        void BuildResolutionList()
        {
            var seen = new HashSet<string>();
            var unique = new List<Resolution>();
            var all = Screen.resolutions;

            // Reverse, so the highest refresh rate wins for each size.
            for (int i = all.Length - 1; i >= 0; i--)
                if (seen.Add($"{all[i].width}x{all[i].height}"))
                    unique.Add(all[i]);

            unique.Sort((a, b) =>
            {
                int cmp = b.width.CompareTo(a.width);
                return cmp != 0 ? cmp : b.height.CompareTo(a.height);
            });

            _resolutions = unique.ToArray();
            if (_resolution == null) return;

            var labels = new List<string>(_resolutions.Length);
            foreach (var r in _resolutions) labels.Add($"{r.width} x {r.height}");

            _resolution.ClearOptions();
            _resolution.AddOptions(labels);

            int curW = PlayerProfile.ResolutionWidth > 0 ? PlayerProfile.ResolutionWidth : Screen.width;
            int curH = PlayerProfile.ResolutionHeight > 0 ? PlayerProfile.ResolutionHeight : Screen.height;
            _resolution.SetValueWithoutNotify(IndexOf(curW, curH));
        }

        int IndexOf(int width, int height)
        {
            for (int i = 0; i < _resolutions.Length; i++)
                if (_resolutions[i].width == width && _resolutions[i].height == height)
                    return i;
            return 0;
        }

        void Apply()
        {
            if (_quality != null)
            {
                int level = Mathf.Clamp(_quality.value, 0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(level, true);
                PlayerProfile.GraphicsQuality = level;
            }

            bool fullscreen = _fullscreen != null ? _fullscreen.isOn : Screen.fullScreen;

            if (_resolution != null && _resolution.value >= 0 && _resolution.value < _resolutions.Length)
            {
                var res = _resolutions[_resolution.value];
                Screen.SetResolution(res.width, res.height, fullscreen);
                PlayerProfile.ResolutionWidth = res.width;
                PlayerProfile.ResolutionHeight = res.height;
            }
            PlayerProfile.Fullscreen = fullscreen ? 1 : 0;

            if (_master != null)
            {
                AudioListener.volume = Mathf.Clamp01(_master.value / 100f);
                PlayerProfile.MasterVolume = _master.value;
            }
            if (_music != null)
            {
                MusicManager.SetVolume(Mathf.Clamp01(_music.value / 100f));
                PlayerProfile.MusicVolume = _music.value;
            }

            // The name writes through its own setter, so this one call covers
            // everything above it in a single file write.
            if (_playerName != null) PlayerProfile.PlayerName = _playerName.text;

            if (_status != null) _status.text = Loc.T("Settings applied!");
            _statusTimer = StatusSeconds;
        }

        void SetLanguage(bool portuguese)
        {
            // Language names are shown in THEIR OWN language in the prefab on
            // purpose — a player stuck in the wrong one must be able to find
            // their way back without reading it.
            Loc.Language = portuguese ? Loc.Portuguese : Loc.English;
            if (_status != null) _status.text = Loc.T("Settings applied!");
            _statusTimer = StatusSeconds;
        }

        #endregion
    }
}
