// GameUICatalog.cs
// The single bridge between the ARTIST-AUTHORED final UI prefabs (living in
// Assets/GameData/Scenes/Menus/GameUI — deliberately NOT a Resources folder,
// that location is the author's working set) and the runtime. GameUIManager
// loads this catalog from Resources/GameUICatalog and instantiates whatever
// panels are assigned; add one field per panel as it is finished and assign
// the prefab on the asset.

using UnityEngine;

namespace TheWaningBorder.UI.Ingame
{
    [CreateAssetMenu(fileName = "GameUICatalog", menuName = "TWB/Game UI Catalog")]
    public sealed class GameUICatalog : ScriptableObject
    {
        public GameObject resourcePanel;

        public GameObject selectionHeader;

        public GameObject unitDetailsPanel;

        public GameObject unitStatsPanel;

        public GameObject unitRosterPanel;

        public GameObject minimapPanel;

        public GameObject actionsPanel;

        public GameObject specialBuildingMenu;

        public GameObject cultureSelectionMenu;

        public GameObject cultureSelectionButton;

        public GameObject objectivesPanel;

        public GameObject religionPanel;

        /// <summary>The drag-select frame. Replaces the old IMGUI rectangle.</summary>
        public GameObject selectionBox;

        /// <summary>Top-centre notification pills. Replaces the IMGUI stack.</summary>
        public GameObject notificationStack;

        /// <summary>Planning-mode banner + waypoint markers.</summary>
        public GameObject planningModeHud;

        public SymbolEntry[] entitySymbols;

        [System.Serializable]
        public sealed class SymbolEntry
        {
            public string key;
            public Sprite sprite;
        }
    }
}
