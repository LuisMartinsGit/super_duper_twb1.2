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
// The tutorial is a set of scripted missions on their own small scenes, never a
// skirmish (docs/Design/Tutorial_Missions.md). The entry loads the Tutorials
// screen (TutorialsMenu.unity, TutorialsPanel), which lists the missions; picking
// one and pressing START MISSION calls Launch here.

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
        // Title case like the authored entries beside it (Skirmish, Multiplayer,
        // Scenarios, Load Game); it was the one item in capitals.
        private const string Label = "Tutorial";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Edit mode: these hooks subscribe in Play and stay subscribed after it
            // (no domain reload on exit), so an editor scene load would edit and
            // dirty the authored scene. Only ever act in Play / a player.
            if (!UnityEngine.Application.isPlaying) return;
            if (scene.name != TheWaningBorder.Core.SceneNames.Menu) return;
            if (FindInScene(scene, TutorialMenuItemName) != null) return;   // already added

            var template = FindInScene(scene, TemplateMenuItem);
            if (template == null)
            {
                Debug.LogWarning($"[TutorialMenuItem] \"{TemplateMenuItem}\" not found — the "
                    + "main menu has no Tutorial entry.");
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
                button.onClick.AddListener(OpenTutorialsScreen);
            }
        }

        /// <summary>Load the Tutorials screen, the list of missions.</summary>
        public static void OpenTutorialsScreen()
            => SceneManager.LoadScene(TheWaningBorder.Core.SceneNames.Tutorials);

        /// <summary>Start a mission.</summary>
        public static void Launch(TutorialMissionId mission)
        {
            LoadingScreen.Show(Prepare(mission));
        }

        /// <summary>
        /// Configure a mission and return its scene: Age 0, fog on, the curse on
        /// because each mission is fought against it (the mission scripts it). Basic
        /// is one faction; Alanthor adds a Beginner AI, which never attacks.
        /// </summary>
        public static string Prepare(TutorialMissionId mission = TutorialMissionId.Basic)
        {
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = true;
            GameSettings.BorderEnabled = true;
            GameSettings.MaxStartingResources = false;
            GameSettings.StartAge = SkirmishStartAge.Age0;
            GameSettings.PerFactionStartCulture = false;
            GameSettings.StartCulture = Cultures.None;
            GameSettings.TutorialActive = true;
            GameSettings.TutorialMission = mission;

            FactionColors.ResetToDefaults();
            LobbyConfig.SetupSinglePlayer(2);   // the lobby's floor; Basic only ever spawns Blue
            if (mission == TutorialMissionId.Advanced)
            {
                LobbyConfig.Slots[1].AIDifficulty = LobbyAIDifficulty.Beginner;
                LobbyConfig.Slots[1].AIStrategy = LobbyAIStrategy.EcoBoom;
            }
            LobbyConfig.ApplyColorSelections();
            GameSettings.TotalPlayers = mission == TutorialMissionId.Advanced ? 2 : 1;

            GameSettings.SelectedMapScene = mission == TutorialMissionId.Advanced
                ? TheWaningBorder.Tutorial.TutorialAdvancedSystemConfig.I.sceneName
                : TheWaningBorder.Tutorial.TutorialMissionSystemConfig.I.sceneName;
            GameSettings.SpawnSeed = 20261107;   // fixed: the mission's node layout never changes
            return GameSettings.SelectedMapScene;
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
