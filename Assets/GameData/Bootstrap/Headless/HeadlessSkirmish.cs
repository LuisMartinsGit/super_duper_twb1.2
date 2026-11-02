// HeadlessSkirmish.cs
// The all-AI local skirmish both headless harnesses start: HeadlessBatch as
// the match itself, HeadlessMp as the warm-up it plays before its multiplayer
// match. One copy of the lobby-less setup, so the two cannot drift apart.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Config;       // LobbyConfig, SlotType
using TheWaningBorder.Core.Multiplayer;  // NetworkRole

namespace TheWaningBorder.Bootstrap
{
    internal static class HeadlessSkirmish
    {
        /// <summary>
        /// Exactly SkirmishPanel.StartGame minus the UI: a local free-for-all
        /// of <paramref name="players"/> factions on the selected map with
        /// every slot an AI — there is no human to be slot 0, so the human
        /// slot is converted the way the panel's observer toggle converts it
        /// — colours and teams published, the spawn seed set, and nobody
        /// watching (observer, no tutorial).
        /// </summary>
        public static void SetupAllAi(int players, int seed)
        {
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TutorialActive = false;
            GameSettings.TotalPlayers = players;
            LobbyConfig.SetupSinglePlayer(players);
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                if (LobbyConfig.Slots[i].Type == SlotType.Human)
                    LobbyConfig.Slots[i].Type = SlotType.AI;
            LobbyConfig.ApplyColorSelections();
            GameSettings.SpawnSeed = seed;
            GameSettings.IsObserver = true;
        }
    }
}
