// File: Assets/Scripts/UI/Menus/MainMenuUI.cs
// Central manager for the main menu system

using TheWaningBorder.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.UI.Menus
{
    /// <summary>
    /// Central manager for the main menu system.
    /// Handles navigation between: Main Menu, Skirmish Lobby, Multiplayer Lobby.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public enum MenuState
        {
            MainMenu,
            SkirmishLobby,
            MultiplayerLobby,
            Options
        }

        private MenuState _currentState = MenuState.MainMenu;

        // Sub-components
        private SkirmishLobbyUI _skirmishLobby;
        private MultiplayerLobbyUI _multiplayerLobby;
        private OptionsMenuUI _optionsMenu;

        // Window styling
        private Rect _mainMenuRect;
        private GUIStyle _buttonStyle;
        private bool _stylesInitialized = false;

        void Awake()
        {
            // Ensure camera exists
            MenuBootstrap.EnsureMenuCamera();

            // Create sub-components
            _skirmishLobby = gameObject.AddComponent<SkirmishLobbyUI>();
            _skirmishLobby.enabled = false;

            _multiplayerLobby = gameObject.AddComponent<MultiplayerLobbyUI>();
            _multiplayerLobby.enabled = false;

            _optionsMenu = gameObject.AddComponent<OptionsMenuUI>();
            _optionsMenu.enabled = false;

            // Subscribe to back events
            _skirmishLobby.OnBackPressed += () => SetState(MenuState.MainMenu);
            _multiplayerLobby.OnBackPressed += () => SetState(MenuState.MainMenu);
            _optionsMenu.OnBackPressed += () => SetState(MenuState.MainMenu);
        }

        void OnGUI()
        {
            InitStyles();

            if (_currentState == MenuState.MainMenu)
            {
                _mainMenuRect = new Rect(
                    (Screen.width - 240) * 0.5f,
                    (Screen.height - 240) * 0.5f,
                    240, 240);
                _mainMenuRect = GUI.Window(10001, _mainMenuRect, DrawMainMenu, "");
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14
            };

            _stylesInitialized = true;
        }

        private void DrawMainMenu(int windowId)
        {
            GUILayout.Space(8);

            // Skirmish button
            if (GUILayout.Button("Skirmish", GUILayout.Height(36)))
            {
                SetState(MenuState.SkirmishLobby);
            }
            GUILayout.Space(6);

            // Multiplayer button
            if (GUILayout.Button("Multiplayer", GUILayout.Height(36)))
            {
                SetState(MenuState.MultiplayerLobby);
            }
            GUILayout.Space(6);

            // Campaign button (placeholder - disabled)
            GUI.enabled = false;
            if (GUILayout.Button("Campaign (Coming Soon)", GUILayout.Height(36)))
            {
                // Placeholder
            }
            GUI.enabled = true;
            GUILayout.Space(6);

            // Options button
            if (GUILayout.Button("Options", GUILayout.Height(36)))
            {
                SetState(MenuState.Options);
            }
            GUILayout.Space(6);

            // Exit button
            if (GUILayout.Button("Exit", GUILayout.Height(36)))
            {
                ExitGame();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 25));
        }

        private void SetState(MenuState newState)
        {
            _currentState = newState;

            // Enable/disable sub-components
            _skirmishLobby.enabled = (newState == MenuState.SkirmishLobby);
            _multiplayerLobby.enabled = (newState == MenuState.MultiplayerLobby);
            _optionsMenu.enabled = (newState == MenuState.Options);

            // Initialize lobbies
            if (newState == MenuState.SkirmishLobby)
            {
                GameSettings.IsMultiplayer = false;
                GameSettings.NetworkRole = NetworkRole.None;
                LobbyConfig.SetupSinglePlayer(GameSettings.TotalPlayers);
            }
            else if (newState == MenuState.MultiplayerLobby)
            {
                GameSettings.IsMultiplayer = true;
                LobbyConfig.SetupMultiplayer(GameSettings.TotalPlayers);
            }
        }

        private void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>
    /// Helper to ensure menu camera exists.
    /// </summary>
    public static class MenuBootstrap
    {
        public static void EnsureMenuCamera()
        {
            if (Camera.main == null)
            {
                var camGo = new GameObject("MenuCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                camGo.AddComponent<AudioListener>();
            }
        }
    }
}