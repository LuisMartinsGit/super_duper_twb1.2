// GameSettings.cs
// Global game configuration and state
// Place in: Assets/Scripts/Core/Settings/GameSettings.cs

using System.Collections.Generic;

// ==================== Enums ====================

public enum GameMode
{
    FreeForAll,
    SoloVsCurse,
    Sandbox,
    BattalionTest,
    Scenario,
    PathfindingTest
}

public enum ScenarioType
{
    LargeMelee,
    LargeRanged,
    LargeMixed,
    HealerTest,
    FourWayCultures,
    FullArmy,
    WallSiege,
    SectShowcase,
    BuildingShowcase
}

public enum SpawnLayout
{
    Circle,          // Evenly spaced around a ring
    TwoSides,        // Players split across two sides
    TwoEachSide8,     // 2 players per side (up to 8 total)
}

public enum TwoSidesPreset
{
    LeftRight,   // LR
    UpDown,      // UD
    LeftUp,      // LU (adjacent)
    LeftDown,    // LD
    RightUp,     // RU
    RightDown    // RD
}

public enum NetworkRole
{
    None,       // Single-player mode
    Server,     // Hosting multiplayer game
    Client      // Joined multiplayer game
}

// ==================== Game Settings ====================

/// <summary>
/// Static configuration for game sessions.
/// Set before starting a match from lobby/menu systems.
/// </summary>
public static class GameSettings
{
    // ==================== Player Settings ====================

    /// <summary>Total number of players (human + AI) in the match.</summary>
    public static int TotalPlayers = 2;

    /// <summary>Current game mode.</summary>
    public static GameMode Mode = GameMode.FreeForAll;

    /// <summary>Active scenario (only used when Mode == Scenario).</summary>
    public static ScenarioType ActiveScenario = ScenarioType.LargeMelee;

    /// <summary>Whether the local player is observing (no units, no commands, full visibility).</summary>
    public static bool IsObserver = false;

    /// <summary>Convenience: true when current mode is Sandbox.</summary>
    public static bool IsSandbox => Mode == GameMode.Sandbox;

    // ==================== Spawn Settings ====================

    /// <summary>Minimum distance from map edge for spawns.</summary>
    public static int SpawnEdgeBufferMin = 30;

    /// <summary>Maximum distance from map edge for spawns.</summary>
    public static int SpawnEdgeBufferMax = 60;

    /// <summary>Minimum separation between player spawns.</summary>
    public static int SpawnMinSeparation = 100;

    /// <summary>Layout pattern for player spawns.</summary>
    public static SpawnLayout SpawnLayout = SpawnLayout.Circle;

    /// <summary>Preset for TwoSides spawn layout.</summary>
    public static TwoSidesPreset TwoSides = TwoSidesPreset.LeftRight;

    /// <summary>Seed for reproducible spawn randomness.</summary>
    public static int SpawnSeed = 1234567;

    // ==================== Economy Settings ====================

    /// <summary>Start every faction with 100,000 of each resource (debug / sandbox).</summary>
    public static bool MaxStartingResources = false;

    // ==================== Map Settings ====================

    /// <summary>Half the map size (total map = 2 * MapHalfSize).</summary>
    public static int MapHalfSize = 125;

    /// <summary>Whether fog of war is enabled.</summary>
    public static bool FogOfWarEnabled = false;

    /// <summary>Whether the Crystal Curse faction spawns on this map.</summary>
    public static bool CrystalCurseEnabled = true;

    /// <summary>
    /// Flat test map for AI/pathfinding work: skips noise heightmap, terrain
    /// trees, ObstacleBootstrap (forests + rocks), and CrystalNodeBootstrap.
    /// Result: flat ground at <c>spawnTargetHeight</c>, only Halls and iron
    /// deposits, water plane hidden below the terrain. Toggle off when you
    /// want production-style maps with hills and forests back.
    /// </summary>
    public static bool FlatTestMap = true;

    // ==================== Pathfinding Settings ====================

    /// <summary>Cell size for the passability grid (world units per cell). 1 = 1m resolution.</summary>
    public static float PathfindingCellSize = 1f;

    /// <summary>True = flow fields (shared BFS), false = A* (per-unit paths).</summary>
    public static bool UseFlowFields = true;

    // ==================== Multiplayer Settings ====================

    /// <summary>Whether the current game is a multiplayer session.</summary>
    public static bool IsMultiplayer = false;

    /// <summary>The network role of this instance (None for single-player).</summary>
    public static NetworkRole NetworkRole = NetworkRole.None;

    /// <summary>Faction controlled by the local player.</summary>
    public static Faction LocalPlayerFaction = Faction.Blue;

    /// <summary>
    /// Mapping of factions to player client IDs in multiplayer.
    /// Key: Faction, Value: NetworkManager client ID (ulong).
    /// Factions not in this dictionary are AI-controlled.
    /// </summary>
    public static Dictionary<Faction, ulong> FactionToPlayerMapping = new Dictionary<Faction, ulong>();

    // ==================== Methods ====================

    /// <summary>
    /// Reset all settings to single-player defaults.
    /// </summary>
    public static void ResetToSinglePlayer()
    {
        IsMultiplayer = false;
        NetworkRole = NetworkRole.None;
        LocalPlayerFaction = Faction.Blue;
        FactionToPlayerMapping.Clear();
        IsObserver = false;
        Mode = GameMode.FreeForAll;
    }

    /// <summary>
    /// Check if a faction is controlled by a human player (vs AI).
    /// In single-player, only Blue is human-controlled.
    /// In multiplayer, checks the FactionToPlayerMapping.
    /// </summary>
    public static bool IsFactionHumanControlled(Faction faction)
    {
        // Observer mode: no faction is human-controlled — AI plays all sides
        if (IsObserver) return false;

        if (!IsMultiplayer)
        {
            return faction == Faction.Blue; // Single-player: only Blue is human
        }
        return FactionToPlayerMapping.ContainsKey(faction);
    }

    /// <summary>
    /// Check if a faction is controlled by the local player.
    /// In single-player, only Blue is locally controlled.
    /// In multiplayer, compares against LocalPlayerFaction.
    /// </summary>
    public static bool IsFactionLocallyControlled(Faction faction)
    {
        if (!IsMultiplayer)
        {
            return faction == Faction.Blue;
        }
        return faction == LocalPlayerFaction;
    }

    /// <summary>
    /// Get the local player's faction.
    /// </summary>
    public static Faction GetLocalFaction()
    {
        return LocalPlayerFaction;
    }

    /// <summary>
    /// Check if this instance is the host/server.
    /// </summary>
    public static bool IsHost()
    {
        return NetworkRole == NetworkRole.Server;
    }

    /// <summary>
    /// Check if this instance is a client (not host).
    /// </summary>
    public static bool IsClient()
    {
        return NetworkRole == NetworkRole.Client;
    }
    
}