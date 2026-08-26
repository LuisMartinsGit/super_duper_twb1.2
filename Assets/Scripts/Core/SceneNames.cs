// SceneNames.cs
// The scene names the rest of the game navigates by.
//
// These were consts on MainMenuBootstrap, which meant fourteen files -- menus,
// panels, the lockstep teardown, the loading screen -- all named the bootstrap
// layer just to know what a scene is called. A scene name is not a bootstrap
// concept; it is a fact about the project, and the layer that boots the game
// should not be the one everybody has to depend on to learn it.
//
// Each of these must also be in Build Settings, which MapSceneSync enforces.

namespace TheWaningBorder.Core
{
    /// <summary>Scene names, by role.</summary>
    public static class SceneNames
    {
        /// <summary>The main menu; every "back to menu" path loads this.</summary>
        public const string Menu = "MainMenu";

        /// <summary>Skirmish setup.</summary>
        public const string Skirmish = "SkirmishMenu";

        /// <summary>Multiplayer lobby.</summary>
        public const string Multiplayer = "MultiplayerMenu";
    }
}
