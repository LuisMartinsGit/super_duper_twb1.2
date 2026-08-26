// MainMenuBootstrap.cs
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
    }
}
