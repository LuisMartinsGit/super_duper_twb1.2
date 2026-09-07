// ScenariosPanel.cs
// uGUI controller for the Scenarios browser (scene GameObjects under
// UI_Canvas, built by MenuSceneBuilder from the Skirmish scene's own parts and
// then hand-editable). Layout: scenario list on the left, thumbnail +
// briefing on the right, < MAIN MENU / START SCENARIO in the footer.
//
// Lives in its own scene (ScenariosMenu.unity) since 2026-09-07 — it used to
// be Panel_Scenarios inside MainMenu.unity, drawn in the older eyebrow-and-
// gradient look, switched on with SetActive. The blue menu's entry now loads
// this scene (ScenariosMenuButton), and BACK loads MainMenu, the same way the
// Skirmish and Multiplayer screens work. OnEnable still does all the setup,
// so the panel behaves the same whether it is switched on or scene-loaded.
//
// Entries: the tutorial first, then ScenarioLibrary definitions (thumbnail +
// description), then the ScenarioCatalog code-driven test scenarios.

using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class ScenariosPanel : MonoBehaviour
    {
        // The list rows are clones of the skirmish roster row, so the resting
        // colour is that row's own dark plate; selection is the menu's gold,
        // dimmed the way MenuToggleSwitch dims a lit pill.
        private static readonly Color RowNormal   = new Color(0.035f, 0.055f, 0.067f, 0.90f);
        private static readonly Color RowSelected = new Color(0.282f, 0.204f, 0.075f, 0.95f);

        public RectTransform ListContent;
        public GameObject ListRowTemplate; // inactive Button + TMP label

        public Image ThumbImage;
        public GameObject ThumbPlaceholder;
        public TMP_Text NameText;
        public TMP_Text DescriptionText;

        public Button BackButton;
        public Button StartButton;

        private sealed class Entry
        {
            public string Name;
            public string Description;
            public Sprite Thumbnail;
            public System.Action Launch;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Image> _rowBackgrounds = new List<Image>();
        private int _selected = -1;
        private bool _wired;

        private void OnEnable()
        {
            Wire();
            Populate();
        }

        private void Wire()
        {
            if (_wired) return;
            _wired = true;

            // BACK returns to the blue menu by loading it. This screen used to
            // be a panel inside MainMenu.unity and closed itself with
            // SetActive(false); it is its own scene now, so there is nothing
            // behind it to uncover.
            if (BackButton != null)
                BackButton.onClick.AddListener(
                    () => SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Menu));
            if (StartButton != null) StartButton.onClick.AddListener(() =>
            {
                if (_selected >= 0 && _selected < _entries.Count)
                    _entries[_selected].Launch();
            });
        }

        private void Populate()
        {
            CollectEntries();

            if (ListContent != null && ListRowTemplate != null)
            {
                for (int i = ListContent.childCount - 1; i >= 0; i--)
                {
                    var child = ListContent.GetChild(i).gameObject;
                    if (child != ListRowTemplate) Destroy(child);
                }
                _rowBackgrounds.Clear();

                for (int i = 0; i < _entries.Count; i++)
                {
                    int idx = i;
                    var row = Instantiate(ListRowTemplate, ListContent);
                    row.SetActive(true);
                    var label = row.GetComponentInChildren<TMP_Text>(true);
                    if (label != null) label.text = Loc.T(_entries[i].Name);
                    _rowBackgrounds.Add(row.GetComponent<Image>());
                    var btn = row.GetComponent<Button>();
                    if (btn != null) btn.onClick.AddListener(() => Select(idx));
                }
            }

            _selected = -1;
            if (_entries.Count > 0) Select(0);
            else
            {
                if (NameText != null) NameText.text = Loc.T("NO SCENARIOS");
                if (DescriptionText != null)
                    DescriptionText.text = Loc.T("No scenario definitions found. Rebuild the scenario " +
                                                 "library via Tools ▸ TWB ▸ Scenarios.");
            }
        }

        private void Select(int index)
        {
            if (index == _selected) return;
            if (_selected >= 0 && _selected < _rowBackgrounds.Count && _rowBackgrounds[_selected] != null)
                _rowBackgrounds[_selected].color = RowNormal;
            _selected = index;
            if (_selected < _rowBackgrounds.Count && _rowBackgrounds[_selected] != null)
                _rowBackgrounds[_selected].color = RowSelected;

            var e = _entries[_selected];
            // Entries keep their English name/description (they are the
            // translation keys); render translated here.
            if (NameText != null) NameText.text = Loc.T(e.Name).ToUpperInvariant();
            if (DescriptionText != null) DescriptionText.text = Loc.T(e.Description);

            bool hasThumb = e.Thumbnail != null;
            if (ThumbImage != null)
            {
                ThumbImage.sprite = hasThumb ? e.Thumbnail : null;
                ThumbImage.enabled = hasThumb; // null sprite draws a white square
            }
            if (ThumbPlaceholder != null) ThumbPlaceholder.SetActive(!hasThumb);
        }

        private void CollectEntries()
        {
            _entries.Clear();

            // The tutorial heads the list: it is also a top-level main-menu
            // entry (TutorialMenuItem), but the Training Grounds is where a
            // new player looks for it, and this path survives even if the
            // main-menu clone cannot find its template.
            _entries.Add(new Entry
            {
                Name = "Tutorial — The Whole Campaign",
                Description =
                    "A guided match on the standard map against one relaxed opponent, from "
                    + "the opening to the victory condition.\n\n"
                    + "1. Camera controls\n"
                    + "2. Territory, the Hall and the Gatherer's Hut\n"
                    + "3. Barracks, Spearmen and taking a fight\n"
                    + "4. The special building, the age-up and the Temple\n"
                    + "5. Religion Points, sects and their powers\n"
                    + "6. The curse — why it wakes, what it costs, how to break it\n"
                    + "7. The wells — the verb, and how the match is won\n\n"
                    + "Each step tops your bank up so the lesson does not wait on the "
                    + "economy, and the Temple upgrade carries straight to level 4. "
                    + "Steps can be done in ANY ORDER and tick themselves off as you play; "
                    + "skip any of them, or dismiss the coach and keep going as a normal "
                    + "skirmish.",
                Thumbnail = null,
                Launch = TutorialMenuItem.Launch,
            });

            // Rich, data-driven scenarios (thumbnail + description).
            var library = Resources.Load<ScenarioLibrary>(ScenarioLibrary.ResourcePath);
            if (library != null)
            {
                foreach (var def in library.Scenarios)
                {
                    if (def == null || string.IsNullOrEmpty(def.SceneName)) continue;
                    var d = def;
                    _entries.Add(new Entry
                    {
                        Name = string.IsNullOrEmpty(d.DisplayName) ? d.SceneName : d.DisplayName,
                        Description = d.Description,
                        Thumbnail = d.Thumbnail,
                        Launch = () => LaunchDefinition(d),
                    });
                }
            }

            // Code-driven test scenarios (shared catalog; no thumbnails).
            foreach (var (label, type) in ScenarioCatalog.All)
            {
                var t = type;
                _entries.Add(new Entry
                {
                    Name = label,
                    Description = "Code-driven test scenario. Spawns its setup on load; " +
                                  "no briefing available.",
                    Thumbnail = null,
                    Launch = () => LoadingScreen.Show(ScenarioCatalog.Prepare(t)),
                });
            }
        }

        // Mirrors ScenarioCatalog.Launch, but loads the definition's own scene
        // and only routes through ScenarioSetup when the definition asks for
        // legacy code-driven spawns.
        private static void LaunchDefinition(ScenarioDefinition def)
        {
            if (def.UseLegacySpawns)
            {
                GameSettings.Mode = GameMode.Scenario;
                GameSettings.ActiveScenario = def.LegacySpawnType;
            }
            else
            {
                GameSettings.Mode = GameMode.FreeForAll; // scene plays as authored
            }
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.TotalPlayers = 2;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.TutorialActive = false;   // sticky static; see TutorialMenuItem

            LoadingScreen.Show(def.SceneName);
        }
    }
}
