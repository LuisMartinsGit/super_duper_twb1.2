// MainMenuBootstrap.cs
// Location: Assets/Scripts/Bootstrap/MainMenuBootstrap.cs
//
// The main menu is now built and wired ENTIRELY in the Unity editor using the
// Synty "Interface Fantasy Menus" assets (native uGUI Buttons, Animators, and
// onClick UnityEvents). No MonoBehaviour is injected at runtime — the old
// auto-injected controllers crashed the editor and clobbered editor wiring, so
// they were removed.
//
// This class now only exposes the menu scene's name, which the rest of the
// codebase uses to return to the menu (InGameMenuPanel, PostGameStatsUI).

namespace TheWaningBorder.Bootstrap
{
    public static class MainMenuBootstrap
    {
        /// <summary>Name of the scene the game returns to as its main menu.
        /// Loaded by SceneManager.LoadScene for return-to-menu throughout the
        /// codebase (InGameMenuPanel, PostGameStatsUI). Make sure this scene is
        /// added to Build Settings.</summary>
        public const string MenuSceneName = "MainMenu";

        /// <summary>
        /// Skirmish setup screen. Its own scene since 2026-08-18 — it used to
        /// be a panel inside MainMenu.unity that the blue menu's Skirmish entry
        /// switched on with SetActive. The blue menu reaches it through a
        /// MenuSceneLink; its CANCEL button comes back to
        /// <see cref="MenuSceneName"/>. Also needs to be in Build Settings.
        /// </summary>
        public const string SkirmishSceneName = "SkirmishMenu";

        /// <summary>
        /// LAN multiplayer screen. Its own scene since 2026-08-19, built from a
        /// copy of the skirmish one so the two lobbies read identically — the
        /// map plate, the roster plate and the footer are the same objects, and
        /// only the slot row differs (flat AI / difficulty / team controls
        /// instead of the skirmish dropdowns). The host / join / browse cards
        /// came across from MainMenu's Panel_Multiplayer and sit on top of the
        /// lobby as centred overlays, which is how that panel already worked.
        ///
        /// MainMenu.unity still carries its own Panel_Multiplayer. Nothing
        /// routes here yet — the blue menu's Multiplayer entry still switches
        /// that in-scene panel on. Also needs to be in Build Settings.
        /// </summary>
        public const string MultiplayerSceneName = "MultiplayerMenu";
    }
}
