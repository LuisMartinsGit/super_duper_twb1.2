// SettingsPanel.cs
// uGUI controller for the Settings screen (scene GameObjects under UI_Canvas,
// built by MenuSceneBuilder from the Skirmish scene's own parts and then
// hand-editable). Layout: profile + display options on the left, audio +
// language on the right, < MAIN MENU / APPLY in the footer.
//
// Lives in its own scene (SettingsMenu.unity) since 2026-09-07. Before that it
// was OptionsPanelBinder driving an OptionsPanel prefab instance parked
// inactive inside MainMenu.unity, drawn in a look of its own — sized for a
// 1080p frame on a 2160p canvas, with a solid slider fill and button art
// overlapping its labels. The screen is now assembled from the same widgets
// the skirmish lobby uses (its option cells, dropdowns, pill and footer
// buttons), so it cannot drift from that look, and the blue menu's Settings
// entry loads it the way it loads the skirmish screen.
//
// Nothing here draws. It reads the authored nodes BY NAME, pushes the saved
// profile into them, and writes it back on APPLY. Node names are the contract
// with MenuSceneBuilder: renaming one there without renaming it here silently
// disables that control (Find logs which node it could not find).

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Systems.Audio;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class SettingsPanel : MonoBehaviour
    {
        /// <summary>Seconds the "settings applied" line stays up.</summary>
        const float StatusSeconds = 2f;

        // Same pill palette as SkirmishPanel: the menu's gold when on, cold
        // steel when off.
        private static readonly Color PillOn  = new Color(0.690f, 0.525f, 0.173f);
        private static readonly Color PillOff = new Color(0.086f, 0.118f, 0.141f);

        TMP_InputField _playerName;
        TMP_Dropdown _quality;
        TMP_Dropdown _resolution;
        TMP_Dropdown _healthBars;
        TMP_Dropdown _dragPriority;
        Button _fullscreenToggle;
        TMP_Text _fullscreenState;
        Slider _master, _music;
        TMP_Text _masterValue, _musicValue, _status;

        bool _fullscreen;
        Resolution[] _resolutions = Array.Empty<Resolution>();
        float _statusTimer;

        #region Boot

        /// <summary>
        /// Apply the saved profile before the player ever opens this screen.
        /// Static and independent of the scene, so it runs at boot whether or
        /// not Settings is ever shown.
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

            // Gameplay preferences are read straight out of GameSettings by
            // the HUD and the input layer, so they have to be pushed there at
            // BOOT — not when the Settings screen happens to be visited.
            GameSettings.HealthBars = ClampHealthBars(PlayerProfile.HealthBars);
            GameSettings.DragPriority = ClampDragPriority(PlayerProfile.DragPriority);

            AudioListener.volume = Mathf.Clamp01(PlayerProfile.MasterVolume / 100f);

            // MusicManager reads the profile in Awake; this covers the case
            // where it already exists and no-ops before that.
            MusicManager.SetVolume(Mathf.Clamp01(PlayerProfile.MusicVolume / 100f));
        }

        /// <summary>Stored as an int, so a hand-edited or older settings.json
        /// cannot put the enum out of range.</summary>
        static HealthBarMode ClampHealthBars(int v)
            => (HealthBarMode)Mathf.Clamp(v, (int)HealthBarMode.Always, (int)HealthBarMode.None);

        static DragSelectionPriority ClampDragPriority(int v)
            => (DragSelectionPriority)Mathf.Clamp(v, (int)DragSelectionPriority.Economy,
                                                  (int)DragSelectionPriority.Off);

        /// <summary>Option labels, in enum order. Order IS the contract — the
        /// dropdown's index is cast straight back to the enum.</summary>
        static readonly string[] HealthBarLabels =
        {
            "Always", "Own", "Friendly", "Smart", "None",
        };

        static readonly string[] DragPriorityLabels =
        {
            "Economy", "Military", "Off",
        };

        #endregion

        #region Lifecycle

        void Awake()
        {
            _playerName       = Find<TMP_InputField>("PlayerNameInput");
            _quality          = Find<TMP_Dropdown>("QualityDropdown");
            _resolution       = Find<TMP_Dropdown>("ResolutionDropdown");
            _healthBars       = Find<TMP_Dropdown>("HealthBarsDropdown");
            _dragPriority     = Find<TMP_Dropdown>("DragPriorityDropdown");
            _fullscreenToggle = Find<Button>("FullscreenToggle");
            _fullscreenState  = Find<TMP_Text>("FullscreenState");
            _master           = Find<Slider>("MasterSlider");
            _music            = Find<Slider>("MusicSlider");
            _masterValue      = Find<TMP_Text>("MasterValue");
            _musicValue       = Find<TMP_Text>("MusicValue");
            _status           = Find<TMP_Text>("Status");

            // The footer keeps the skirmish footer's node names, so the same
            // hover-motion hook (MenuMotion) picks its buttons up unchanged.
            Bind("PrimaryButton", Apply);
            Bind("BackButton", () => SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Menu));
            Bind("EnglishButton", () => SetLanguage(false));
            Bind("PortugueseButton", () => SetLanguage(true));

            // A Button with a state label, not a uGUI Toggle: the skirmish
            // screen's pills work this way (the binder owns the bool, the pill
            // only paints it), and reusing its cell means reusing its click.
            if (_fullscreenToggle != null)
                _fullscreenToggle.onClick.AddListener(() =>
                {
                    _fullscreen = !_fullscreen;
                    SyncPill(_fullscreenToggle, _fullscreenState, _fullscreen);
                });

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

            Debug.LogWarning($"[Settings] scene has no '{node}' — that control will not work.");
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

        /// <summary>Same paint rule as SkirmishPanel.SyncPill, so a pill on
        /// this screen and one on the lobby read identically.</summary>
        static void SyncPill(Button toggle, TMP_Text state, bool on)
        {
            if (state != null) state.text = Loc.T(on ? "ON" : "OFF");
            if (toggle == null) return;

            var sw = toggle.GetComponent<MenuToggleSwitch>();
            if (sw != null) { sw.SetOn(on); return; }

            if (toggle.targetGraphic is Image img)
                img.color = on ? PillOn : PillOff;
        }

        #endregion

        #region Load / apply

        void LoadIntoPanel()
        {
            if (_playerName != null) _playerName.text = PlayerProfile.PlayerName;

            if (_quality != null)
            {
                _quality.ClearOptions();
                var names = new List<string>(QualitySettings.names.Length);
                foreach (var n in QualitySettings.names) names.Add(Loc.T(n));
                _quality.AddOptions(names);
                int level = PlayerProfile.GraphicsQuality >= 0
                    ? PlayerProfile.GraphicsQuality : QualitySettings.GetQualityLevel();
                _quality.SetValueWithoutNotify(
                    Mathf.Clamp(level, 0, QualitySettings.names.Length - 1));
            }

            BuildResolutionList();

            FillEnumDropdown(_healthBars, HealthBarLabels, PlayerProfile.HealthBars);
            FillEnumDropdown(_dragPriority, DragPriorityLabels, PlayerProfile.DragPriority);

            _fullscreen = PlayerProfile.Fullscreen >= 0
                ? PlayerProfile.Fullscreen == 1 : Screen.fullScreen;
            SyncPill(_fullscreenToggle, _fullscreenState, _fullscreen);

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
            foreach (var r in _resolutions) labels.Add($"{r.width} x {r.height}  ({Aspect(r)})");

            _resolution.ClearOptions();
            _resolution.AddOptions(labels);

            int curW = PlayerProfile.ResolutionWidth > 0 ? PlayerProfile.ResolutionWidth : Screen.width;
            int curH = PlayerProfile.ResolutionHeight > 0 ? PlayerProfile.ResolutionHeight : Screen.height;
            _resolution.SetValueWithoutNotify(IndexOf(curW, curH));
        }

        /// <summary>
        /// The marketing name for a mode's shape — "16:9", "32:9" — so a
        /// player on an ultrawide can find the one that fills their monitor
        /// instead of guessing from four-digit numbers.
        ///
        /// Nearest known ratio within a few percent, because the real
        /// fractions are unhelpful: 2560x1080 and 3440x1440 are both sold as
        /// 21:9 but reduce to 64:27 and 43:18. Anything unrecognised falls
        /// back to the reduced fraction, which is at least honest.
        /// </summary>
        static string Aspect(Resolution r)
        {
            if (r.height <= 0) return "?";

            float ratio = (float)r.width / r.height;
            (string name, float value)[] known =
            {
                ("5:4",   1.250f), ("4:3",   1.333f), ("3:2",  1.500f),
                ("16:10", 1.600f), ("16:9",  1.778f), ("21:9", 2.370f),
                ("32:9",  3.556f), ("48:9",  5.333f),
            };

            foreach (var (name, value) in known)
                if (Mathf.Abs(ratio - value) / value < 0.035f) return name;

            int a = r.width, b = r.height;
            while (b != 0) { int t = b; b = a % b; a = t; }
            return a > 0 ? $"{r.width / a}:{r.height / a}" : "?";
        }

        int IndexOf(int width, int height)
        {
            for (int i = 0; i < _resolutions.Length; i++)
                if (_resolutions[i].width == width && _resolutions[i].height == height)
                    return i;
            return 0;
        }

        static void FillEnumDropdown(TMP_Dropdown dd, string[] labels, int current)
        {
            if (dd == null) return;
            dd.ClearOptions();
            var localized = new List<string>(labels.Length);
            foreach (var l in labels) localized.Add(Loc.T(l));
            dd.AddOptions(localized);
            dd.SetValueWithoutNotify(Mathf.Clamp(current, 0, labels.Length - 1));
        }

        void Apply()
        {
            if (_healthBars != null)
            {
                PlayerProfile.HealthBars = _healthBars.value;
                GameSettings.HealthBars = ClampHealthBars(_healthBars.value);
            }
            if (_dragPriority != null)
            {
                PlayerProfile.DragPriority = _dragPriority.value;
                GameSettings.DragPriority = ClampDragPriority(_dragPriority.value);
            }

            if (_quality != null)
            {
                int level = Mathf.Clamp(_quality.value, 0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(level, true);
                PlayerProfile.GraphicsQuality = level;
            }

            bool fullscreen = _fullscreenToggle != null ? _fullscreen : Screen.fullScreen;

            if (_resolution != null && _resolution.value >= 0 && _resolution.value < _resolutions.Length)
            {
                var res = _resolutions[_resolution.value];
                Screen.SetResolution(res.width, res.height, fullscreen);
                PlayerProfile.ResolutionWidth = res.width;
                PlayerProfile.ResolutionHeight = res.height;
            }
            else
            {
                // No resolution picker in this scene: the fullscreen switch
                // still has to take effect NOW, not only on the next launch.
                // Mirrors LoadAndApplySettings, which has always had this
                // branch.
                Screen.fullScreen = fullscreen;
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
            // Language names are shown in THEIR OWN language on the buttons on
            // purpose — a player stuck in the wrong one must be able to find
            // their way back without reading it.
            Loc.Language = portuguese ? Loc.Portuguese : Loc.English;
            if (_status != null) _status.text = Loc.T("Settings applied!");
            _statusTimer = StatusSeconds;
        }

        #endregion
    }
}
