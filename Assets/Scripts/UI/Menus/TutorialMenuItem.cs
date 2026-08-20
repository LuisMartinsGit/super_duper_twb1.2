// TutorialMenuItem.cs
// Adds a TUTORIAL entry to the blue main menu, and owns the launch flow for it.
//
// The main menu is authored and wired entirely in the editor, and past
// auto-mounted menu controllers clobbered that wiring (see MainMenuBootstrap /
// ShipGateMenuTrim). So this follows the ShipGateMenuTrim pattern instead of
// injecting a controller: a static scene hook that CLONES the existing
// "Menu_Item_Skirmish" object — inheriting its Synty styling, hover FX and
// layout slot for free — relabels it, drops the inherited Inspector wiring,
// and points it at the tutorial launch.
//
// The tutorial itself is not a separate scene. It is a normal single-player
// match on the shipped map against one passive AI, with GameSettings.
// TutorialActive telling GameUIManager to mount TutorialDirector — the coach
// overlay that walks the WHOLE game, opening to victory condition, in seven
// chapters. That keeps it working on whatever map actually ships and means it
// exercises the real game rather than a mock-up.
// Location: Assets/Scripts/UI/Menus/TutorialMenuItem.cs

using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Core.Maps;

namespace TheWaningBorder.UI.Menus
{
    public static class TutorialMenuItem
    {
        /// <summary>Blue-menu item cloned for the tutorial entry.</summary>
        private const string TemplateMenuItem = "Menu_Item_Skirmish";
        private const string TutorialMenuItemName = "Menu_Item_Tutorial";
        private const string Label = "TUTORIAL";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MainMenuBootstrap.MenuSceneName) return;
            if (FindInScene(scene, TutorialMenuItemName) != null) return;   // already added

            var template = FindInScene(scene, TemplateMenuItem);
            if (template == null)
            {
                Debug.LogWarning($"[TutorialMenuItem] \"{TemplateMenuItem}\" not found — the "
                    + "tutorial is still reachable from the Scenarios browser.");
                return;
            }

            var clone = Object.Instantiate(template, template.transform.parent);
            clone.name = TutorialMenuItemName;
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            clone.SetActive(true);

            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
                text.text = Loc.T(Label);

            foreach (var button in clone.GetComponentsInChildren<Button>(true))
            {
                // RemoveAllListeners only drops RUNTIME listeners; the cloned
                // Inspector entry that opens Panel_Skirmish is persistent and
                // has to be switched off explicitly or the tutorial button
                // would open the skirmish lobby as well.
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(Launch);
            }
        }

        /// <summary>
        /// Start the tutorial match: shipped map, Age 0 (the tutorial runs the
        /// full arc from the opening), fog off so the coach's landmarks are
        /// visible, curse wells on because the last chapter is won on them,
        /// one Easy AI opponent to make the map feel inhabited without
        /// pressuring a first-time player.
        /// </summary>
        public static void Launch()
        {
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.MaxStartingResources = false;
            GameSettings.StartAge = SkirmishStartAge.Age0;
            GameSettings.StartCulture = Cultures.None;
            GameSettings.TotalPlayers = 2;
            GameSettings.TutorialActive = true;

            FactionColors.ResetToDefaults();
            LobbyConfig.SetupSinglePlayer(2);
            for (int i = 1; i < LobbyConfig.ActiveSlotCount; i++)
            {
                LobbyConfig.Slots[i].AIDifficulty = LobbyAIDifficulty.Easy;
                // EcoBoom, not a rush personality: the coach walks a beginner
                // through seven steps and should not be interrupted by an
                // early attack.
                LobbyConfig.Slots[i].AIStrategy = LobbyAIStrategy.EcoBoom;
            }
            LobbyConfig.ApplyColorSelections();

            var maps = MapRegistry.Maps;
            if (maps.Count > 0) GameSettings.SelectedMapScene = maps[0].SceneName;
            GameSettings.SpawnSeed = 20260813;   // fixed: the coach's advice is positional

            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        /// <summary>Depth-first search for a named object, inactive ones included.</summary>
        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                        return t.gameObject;
            }
            return null;
        }
    }
}
