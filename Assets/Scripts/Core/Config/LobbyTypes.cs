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
        public string PlayerName;
        /// <summary>Index into FactionColors.ColorPool (0-11)</summary>
        public int ColorIndex;

        public PlayerSlot(int index, Faction faction)
        {
            SlotIndex = index;
            Faction = faction;
            Type = SlotType.Empty;
            AIDifficulty = LobbyAIDifficulty.Normal;
            PlayerName = "";
            ColorIndex = index; // Default: slot 0 = color 0, slot 1 = color 1, etc.
        }

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
    /// Network-specific lobby slot with client tracking.
    /// Used by Multiplayer lobby for networked games.
    /// </summary>
    public class LobbySlot
    {
        public LobbySlotType Type = LobbySlotType.Empty;
        public string PlayerName = "";
        public LobbyAIDifficulty AIDifficulty = LobbyAIDifficulty.Normal;
        public string ClientKey = "";
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
        }
    }
}
