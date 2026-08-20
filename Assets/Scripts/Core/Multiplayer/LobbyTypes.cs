// LobbyTypes.cs
// Shared lobby configuration types used by both Core and Multiplayer
// Location: Assets/Scripts/Core/Config/LobbyTypes.cs

using UnityEngine;

namespace TheWaningBorder.Core.Config
{
    /// <summary>
    /// AI difficulty levels for lobby configuration.
    /// </summary>
    public enum LobbyAIDifficulty
    {
        Easy,
        Normal,
        Hard,
        Expert
    }

    /// <summary>
    /// AI strategy choice for lobby configuration. Random rolls one of the six
    /// concrete strategies at game start (matches the legacy behaviour);
    /// the rest pin a specific build order from <c>AIBuildOrder.cs</c>.
    /// </summary>
    public enum LobbyAIStrategy
    {
        Random = 0,
        EcoBoom,
        Balanced,
        TechBoom,
        Rush,
        Turtle,
        Defensive,
    }

    /// <summary>
    /// Type of player in a lobby slot.
    /// </summary>
    public enum SlotType
    {
        Empty = 0,
        Human = 1,
        AI = 2,
        Observer = 3
    }

    /// <summary>
    /// Alias for SlotType - used by some Multiplayer code.
    /// Values match SlotType for easy conversion.
    /// </summary>
    public enum LobbySlotType
    {
        Empty = 0,
        Human = 1,
        AI = 2,
        Observer = 3
    }

    /// <summary>
    /// A player slot in the lobby.
    /// </summary>
    public class PlayerSlot
    {
        public int SlotIndex;
        public SlotType Type;
        public Faction Faction;
        public LobbyAIDifficulty AIDifficulty;
        public LobbyAIStrategy AIStrategy;
        public string PlayerName;
        /// <summary>Index into FactionColors.ColorPool (0-11)</summary>
        public int ColorIndex;
        /// <summary>
        /// Team this slot fights for. <c>Alliances.NoTeam</c> (0) means the
        /// slot fights alone and is hostile to everyone — the default, and
        /// identical to the pre-teams free-for-all. docs/Design/Teams.md
        /// </summary>
        public byte TeamIndex;
        /// <summary>
        /// Which of the map's authored player-start positions this slot spawns
        /// at, as an index into <c>MapInfo.PlayerStarts</c> /
        /// <c>MapMarkerRegistry.PlayerStarts</c>.
        /// <c>AutoStart</c> (-1) means "let the spawn layout decide", which is
        /// the default and the pre-picker behaviour.
        /// docs/Design/Lobby_Setup.md
        /// </summary>
        public int StartIndex;

        /// <summary>StartIndex value meaning "unassigned / let the layout pick".</summary>
        public const int AutoStart = -1;

        public PlayerSlot(int index, Faction faction)
        {
            SlotIndex = index;
            Faction = faction;
            Type = SlotType.Empty;
            AIDifficulty = LobbyAIDifficulty.Normal;
            AIStrategy = LobbyAIStrategy.Random;
            PlayerName = "";
            ColorIndex = index; // Default: slot 0 = color 0, slot 1 = color 1, etc.
            TeamIndex = Alliances.NoTeam;
            StartIndex = AutoStart;
        }

        /// <summary>Player-facing team label for the lobby chip.</summary>
        public string GetTeamName()
            => TeamIndex == Alliances.NoTeam ? "No Team" : $"Team {TeamIndex}";

        public string GetFactionName()
        {
            return Faction.ToString();
        }

        /// <summary>
        /// Get the assigned color for this slot from the color pool.
        /// </summary>
        public Color GetFactionColor()
        {
            if (ColorIndex >= 0 && ColorIndex < FactionColors.ColorPool.Length)
                return FactionColors.ColorPool[ColorIndex];
            return Color.gray;
        }

        /// <summary>
        /// Get the display name of this slot's assigned color.
        /// </summary>
        public string GetColorName()
        {
            if (ColorIndex >= 0 && ColorIndex < FactionColors.ColorNames.Length)
                return FactionColors.ColorNames[ColorIndex];
            return "Unknown";
        }
    }

    /// <summary>
    /// Static holder for lobby configuration.
    /// Shared between Core and Multiplayer assemblies.
    /// </summary>
    public static class LobbyConfig
    {
        public static PlayerSlot[] Slots = new PlayerSlot[8];
        public static int ActiveSlotCount = 2;

        static LobbyConfig()
        {
            InitializeSlots();
        }

        public static void InitializeSlots()
        {
            Faction[] factions = {
                Faction.Blue, Faction.Red, Faction.Green, Faction.Yellow,
                Faction.Purple, Faction.Orange, Faction.Teal, Faction.White
            };

            for (int i = 0; i < 8; i++)
            {
                Slots[i] = new PlayerSlot(i, factions[i]);
                // Default color = slot index (0-7)
                Slots[i].ColorIndex = i;
            }
        }

        public static void SetupSinglePlayer(int playerCount)
        {
            // Allow 1 player in Sandbox mode, otherwise min 2
            int minPlayers = GameSettings.IsSandbox ? 1 : 2;
            ActiveSlotCount = Mathf.Clamp(playerCount, minPlayers, 8);

            for (int i = 0; i < 8; i++)
            {
                if (i == 0)
                {
                    Slots[i].Type = SlotType.Human;
                    Slots[i].PlayerName = "Player";
                }
                else if (i < ActiveSlotCount)
                {
                    Slots[i].Type = SlotType.AI;
                    Slots[i].AIDifficulty = LobbyAIDifficulty.Normal;
                }
                else
                {
                    Slots[i].Type = SlotType.Empty;
                }
            }
        }

        public static void SetupMultiplayer(int playerCount)
        {
            ActiveSlotCount = Mathf.Clamp(playerCount, 2, 8);

            for (int i = 0; i < 8; i++)
            {
                if (i < ActiveSlotCount)
                {
                    Slots[i].Type = SlotType.AI;
                    Slots[i].AIDifficulty = LobbyAIDifficulty.Normal;
                }
                else
                {
                    Slots[i].Type = SlotType.Empty;
                }
            }
        }

        /// <summary>
        /// Apply color selections from lobby slots to the FactionColors runtime system.
        /// Call before starting the game.
        /// </summary>
        public static void ApplyColorSelections()
        {
            for (int i = 0; i < 8; i++)
            {
                FactionColors.SetFactionColor(i, Slots[i].ColorIndex);
            }

            // Teams ride along with colours because every match-start path —
            // skirmish, multiplayer host, multiplayer client, tutorial —
            // already funnels through here. Publishing the alliance table
            // anywhere else would leave one of them free-for-all.
            // docs/Design/Teams.md
            Alliances.ApplyFromLobby();
        }
    }
}
