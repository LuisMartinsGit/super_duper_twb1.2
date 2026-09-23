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

        /// <summary>
        /// The authored frame vocabulary, so the CODE-BUILT panels (special
        /// actions, spells, top choice bar, builder palette, pause, victory)
        /// wear the same Synty frames as the prefab panels instead of the old
        /// flat navy + gold strips. Filled from the same sprites the authored
        /// prefabs use: Frame_Box_Medium_05 (+ mask) for a panel, the roster's
        /// Frame_Box_Medium_03 (+ mask) for a button or slot.
        /// </summary>
        public ChromeSet chrome;

        [System.Serializable]
        public sealed class ChromeSet
        {
            public Sprite panelFrame;
            public Sprite panelFrameMask;
            /// <summary>Tint of the panel mask fill — the authored panels' deep teal.</summary>
            public Color panelFill;
            /// <summary>Image.pixelsPerUnitMultiplier for the panel frame (authored: 4).</summary>
            public float panelSlice;
            public Sprite buttonFrame;
            public Sprite buttonFrameMask;
            public float buttonSlice;
        }

        [System.Serializable]
        public sealed class SymbolEntry
        {
            public string key;
            public Sprite sprite;
        }
    }
}
