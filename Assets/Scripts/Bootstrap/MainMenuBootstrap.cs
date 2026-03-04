// MainMenuBootstrap.cs
// Initializes the main menu when the game starts
// Location: Assets/Scripts/Bootstrap/MainMenuBootstrap.cs

using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Bootstrap for the main menu - runs automatically on game start.
    /// Creates MainMenuUI when appropriate scenes load.
    /// </summary>
    public static class MainMenuBootstrap
    {
        private static bool _menuCreated;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // Handle the initial scene
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Skip if this is the Game scene (GameBootstrap handles that)
            if (string.Equals(scene.name, "Game")) 
            {
                _menuCreated = false; // Reset so menu can be created when returning
                return;
            }

            // Create menu if it doesn't exist
            if (_menuCreated) return;
            if (Object.FindFirstObjectByType<MainMenuUI>() != null) return;

            Debug.Log($"[MainMenuBootstrap] Creating MainMenuUI for scene: {scene.name}");
            
            var menuGO = new GameObject("MainMenuUI");
            menuGO.AddComponent<MainMenuUI>();
            
            _menuCreated = true;
        }
    }
}