// ScenariosPanel.cs
// uGUI controller for the Scenarios browser panel (scene GameObjects under
// UI_Canvas, scaffolded once by MenuPanelsBuilder and then hand-editable).
// Layout: scenario list on the left, thumbnail + description preview on the
// right, BACK / START SCENARIO in the footer.
//
// Entries: ScenarioLibrary definitions first (thumbnail + description), then
// the ScenarioCatalog code-driven test scenarios (generic preview).

using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class ScenariosPanel : MonoBehaviour
    {
        private static readonly Color RowNormal   = new Color(0f, 0f, 0f, 0f);
        private static readonly Color RowSelected = new Color(0.25f, 0.41f, 0.53f, 0.45f);

        [Header("List")]
        public RectTransform ListContent;
        public GameObject ListRowTemplate; // inactive Button + TMP label

        [Header("Preview")]
        public Image ThumbImage;
        public GameObject ThumbPlaceholder;
        public TMP_Text NameText;
        public TMP_Text DescriptionText;

        [Header("Footer")]
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
            if (BackButton != null) BackButton.onClick.AddListener(() => gameObject.SetActive(false));
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
                    if (label != null) label.text = _entries[i].Name;
                    _rowBackgrounds.Add(row.GetComponent<Image>());
                    var btn = row.GetComponent<Button>();
                    if (btn != null) btn.onClick.AddListener(() => Select(idx));
                }
            }

            _selected = -1;
            if (_entries.Count > 0) Select(0);
            else
            {
                if (NameText != null) NameText.text = "NO SCENARIOS";
                if (DescriptionText != null)
                    DescriptionText.text = "No scenario definitions found. Rebuild the scenario " +
                                           "library via Tools ▸ TWB ▸ Scenarios.";
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
            if (NameText != null) NameText.text = e.Name.ToUpperInvariant();
            if (DescriptionText != null) DescriptionText.text = e.Description;

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
                    Launch = () => ScenarioCatalog.Launch(t),
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

            LoadingScreen.Show(def.SceneName);
        }
    }
}
