// GameUICatalog.cs
// The single bridge between the ARTIST-AUTHORED final UI prefabs (living in
// Assets/GameData/Scenes/Menus/GameUI — deliberately NOT a Resources folder,
// that location is the author's working set) and the runtime. GameUIManager
// loads this catalog from Resources/GameUICatalog and instantiates whatever
// panels are assigned; add one field per panel as it is finished and assign
// the prefab on the asset.

using UnityEngine;

namespace TheWaningBorder.UI.GameUI
{
    [CreateAssetMenu(fileName = "GameUICatalog", menuName = "TWB/Game UI Catalog")]
    public sealed class GameUICatalog : ScriptableObject
    {
        [Tooltip("Resource indicator (Supplies / Iron / Veilstone / Veilsteel / Housing).")]
        public GameObject resourcePanel;

        [Tooltip("Selection header: portrait symbol + name label for the current selection.")]
        public GameObject selectionHeader;

        [Tooltip("Unit details: stat chips (Health / Attack / Armor / Speed) for the selection.")]
        public GameObject unitDetailsPanel;

        [Tooltip("Unit stats: HP slider/label + attack cooldown slider/label for the selected unit.")]
        public GameObject unitStatsPanel;

        [Tooltip("Unit roster: one clickable entry per distinct unit type in a mixed selection; " +
                 "clicking an entry focuses the stats panel on that type.")]
        public GameObject unitRosterPanel;

        [Tooltip("Minimap frame; its \"Map\" Image is filled at runtime with an elevation " +
                 "image generated from the loaded map's terrain (MinimapPanelBinder).")]
        public GameObject minimapPanel;

        [Tooltip("Actions panel: 3x5 authored button grid (\"Actions\" node). Builder = build " +
                 "palette; building = top row units, lower rows research (ActionsPanelPrefabBinder). " +
                 "When assigned it replaces the code-built builder palette and train/research grids.")]
        public GameObject actionsPanel;

        [Tooltip("Special-building choice menu (Shrine / Vault / Keep radial buttons). Shown " +
                 "top-center until a choice building is started; driven by TopChoiceBar, which " +
                 "hides its code-built buttons when this is assigned.")]
        public GameObject specialBuildingMenu;

        [Tooltip("Culture selection menu (Panel_Alanthor / Panel_Feraldis / Panel_Runai). Opened " +
                 "by the culture selection button; driven by TopChoiceBar.")]
        public GameObject cultureSelectionMenu;

        [Tooltip("Culture selection opener: the authored \"SELECT CULTURE\" pill " +
                 "(CultureSelection folder). Pinned top-center once a special building is " +
                 "started; opens the culture selection menu (TopChoiceBar).")]
        public GameObject cultureSelectionButton;

        [Tooltip("Objectives panel (top-left): step rows Step_Special / Step_Culture / " +
                 "Step_Temple / Step_Military, driven by ObjectivesPanelBinder.")]
        public GameObject objectivesPanel;

        [Tooltip("Religion panel (mid-right): RP + TempleInfo labels, chapel Slot1-6 " +
                 "buttons and the hidden sect Picker, driven by ReligionPanelBinder. " +
                 "Shown once the faction owns a completed Temple of Ridan.")]
        public GameObject religionPanel;

        [Tooltip("Symbol sprite per entity display name (\"Archer\", \"Hall\", ...). " +
                 "Key \"Mixed\" is used for mixed multi-selections; " +
                 "keys \"Unit\" and \"Building\" are the fallbacks.")]
        public SymbolEntry[] entitySymbols;

        [System.Serializable]
        public sealed class SymbolEntry
        {
            public string key;
            public Sprite sprite;
        }
    }
}
