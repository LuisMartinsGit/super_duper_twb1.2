// TutorialsPanel.cs
// uGUI controller for the Tutorials screen (TutorialsMenu.unity), a copy of the
// Scenarios browser: the mission list on the left, the briefing on the right,
// < MAIN MENU / START MISSION in the footer. The main menu's Tutorial entry loads
// this scene (TutorialMenuItem); BACK loads MainMenu.
//
// One row per tutorial mission, in order. Each launches through
// TutorialMenuItem.Launch, which configures the mission and loads its scene.
// docs/Design/Tutorial_Missions.md

using System.Collections.Generic;
using TheWaningBorder.Core.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TheWaningBorder.UI.Menus.Panels
{
    public sealed class TutorialsPanel : MonoBehaviour
    {
        // Same plates as the Scenarios list (ScenariosPanel).
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
            public TutorialMissionId Mission;
        }

        // English text is the translation key (Loc.Pt.Tutorial).
        private static readonly Entry[] Missions =
        {
            new Entry
            {
                Name = "1: The Basics",
                Mission = TutorialMissionId.Basic,
                Description =
                    "Age 0, on a small map with two territories: yours and the curse's.\n\n"
                    + "1. Move the camera and give orders\n"
                    + "2. Rescue the lost workers\n"
                    + "3. Build the Shelter and settle the camp\n"
                    + "4. Huts, Gatherer's Huts, mines and the Barracks\n"
                    + "5. Train Scouts and Spearmen\n"
                    + "6. Survive the curse waves and destroy the node",
            },
            new Entry
            {
                Name = "2: Alanthor",
                Mission = TutorialMissionId.Advanced,
                Description =
                    "Your town is built. Age up to Alanthor and win the match.\n\n"
                    + "1. Build the Vault of Almiérra and age up\n"
                    + "2. The Alanthor economy: Guilds and Mines\n"
                    + "3. The Alanthor army\n"
                    + "4. Walls, towers and claiming territory\n"
                    + "5. Religion Points, the Temple and the sects\n"
                    + "6. The cursed Sanctum, the monument and the Shardroot\n"
                    + "7. Three ways to win: conquest, enshrinement, or every Sanctum",
            },
        };

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

            if (BackButton != null)
                BackButton.onClick.AddListener(
                    () => SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Menu));
            if (StartButton != null) StartButton.onClick.AddListener(() =>
            {
                if (_selected >= 0 && _selected < Missions.Length)
                    TutorialMenuItem.Launch(Missions[_selected].Mission);
            });
        }

        private void Populate()
        {
            if (ListContent != null && ListRowTemplate != null)
            {
                for (int i = ListContent.childCount - 1; i >= 0; i--)
                {
                    var child = ListContent.GetChild(i).gameObject;
                    if (child != ListRowTemplate) Destroy(child);
                }
                _rowBackgrounds.Clear();

                for (int i = 0; i < Missions.Length; i++)
                {
                    int idx = i;
                    var row = Instantiate(ListRowTemplate, ListContent);
                    row.name = "Row_" + Missions[i].Mission;
                    row.SetActive(true);
                    var label = row.GetComponentInChildren<TMP_Text>(true);
                    if (label != null) label.text = Loc.T(Missions[i].Name);
                    _rowBackgrounds.Add(row.GetComponent<Image>());
                    var btn = row.GetComponent<Button>();
                    if (btn != null) btn.onClick.AddListener(() => Select(idx));
                }
            }

            // No mission has a picture yet; the placeholder stands in.
            if (ThumbImage != null) { ThumbImage.sprite = null; ThumbImage.enabled = false; }
            if (ThumbPlaceholder != null) ThumbPlaceholder.SetActive(true);

            _selected = -1;
            Select(0);
        }

        /// <summary>Highlight a row and show its briefing. Public for the headless menu test.</summary>
        public void Select(int index)
        {
            if (index == _selected || index < 0 || index >= Missions.Length) return;
            if (_selected >= 0 && _selected < _rowBackgrounds.Count && _rowBackgrounds[_selected] != null)
                _rowBackgrounds[_selected].color = RowNormal;
            _selected = index;
            if (_selected < _rowBackgrounds.Count && _rowBackgrounds[_selected] != null)
                _rowBackgrounds[_selected].color = RowSelected;

            var e = Missions[_selected];
            if (NameText != null) NameText.text = Loc.T(e.Name).ToUpperInvariant();
            if (DescriptionText != null) DescriptionText.text = Loc.T(e.Description);
        }

        public int MissionCount => Missions.Length;
        public TutorialMissionId SelectedMission => _selected >= 0 ? Missions[_selected].Mission : TutorialMissionId.None;
    }
}
