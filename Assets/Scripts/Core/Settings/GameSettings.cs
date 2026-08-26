// GameSettings.cs
// Global game configuration and state
// Place in: Assets/Scripts/Core/Settings/GameSettings.cs

using System.Collections.Generic;

// ==================== Enums ====================

public enum GameMode
{
    FreeForAll,
    SoloVsBorder,
    Sandbox,
    Scenario,
    PathfindingTest
}

public enum ScenarioType
{
    LargeMelee = 0,
    LargeRanged = 1,
    LargeMixed = 2,
    HealerTest = 3,
    FourWayCultures = 4,
    FullArmy = 5,
    WallSiege = 6,
    SectShowcase = 7,
    BuildingShowcase = 8,
    BorderCombatTest = 9,
    PatrolDefense = 10,
    AlanthorVsBorder = 11,

    // 12..18 were the task-112 nav-stack milestone scenarios (Phase1..Phase7
    // Test). The milestones are finished and the scenarios were deleted; the
    // indices stay retired so a future scenario cannot silently inherit a
    // saved lobby value that meant something else.
    // Wall-climb / rampart garrison test: a sealed wall enclosure with
    // climb-hub stairs; units ordered inside must use the stairs + rampart.
    WallClimbTest = 19,

    // Longbowman animation showcase: one idle, one patrolling, two shooting an
    // invincible target from different ranges, and a 5 s spawner feeding an
    // immortal attacking enemy (each spawn walks in and dies). Lets the
    // idle/run/shoot/death clips be reviewed live the way gameplay drives them.
    LongbowmanShowcase = 20,

    // Longbowman line battle: two teams of 30 Longbowmen (each split into two
    // 3x5 blocks) facing off across the player-1 start, trading volleys.
    LongbowmanBattle = 21,

    // Building-damage shader test: a row of Alanthor buildings that lose 5% HP
    // per second, so the progressive soot/cracks/missing-pieces damage shader
    // and the death collapse can be reviewed live.
    BuildingDamageTest = 22,

    // Building-damage showcase: a grid of buildings across all cultures
    // (generic / Runai / Feraldis / Alanthor) that all lose 5% HP per second,
    // exercising the damage shader + collapse across many building meshes.
    BuildingDamageShowcase = 23,

    // Guild defense test: a fully-upgraded Alanthor "Guild" (Gatherer's Hut at
    // L3 with the full Survey + reinforcement research) pre-damaged below full
    // HP, swarmed by a Red group. Exercises the Guild's low-HP Stop cast
    // (Veilsteel Pylons), auto-repair, and the Slow/Stop cast VFX.
    GuildDefenseTest = 24,

    // Spell VFX showcase: a flat, textureless plane on which every authored
    // spell (12 sect god powers + Guild Slow/Stop + hero abilities) is laid out
    // in a labelled grid and repeat-cast, so the effect + ground-circle VFX and
    // their colours can be reviewed live. Driven by the Spell prefabs under
    // Resources/Spells.
    SpellShowcase = 25,

    // Hut evolution showcase: one Gatherer's Hut self-constructs with NO
    // workers over 5 s (numbered Lv0 rise), then every 3 s plays the upgrade
    // transition to the next Alanthor level: Lv1 -> Lv2 -> Lv3. Reviews the
    // multi-variant prefab pipeline (BuildingVariantVisual) end to end.
    HutEvolution = 26,
}

/// <summary>
/// task-112 M7 -- determinism replay mode controlling
/// <c>DeterminismReplaySystem</c>. <c>Off</c> = no recording, no
/// comparison. <c>Record</c> = append every sim tick's unit positions
/// to the replay log. <c>Replay</c> = compare each tick's positions
/// against the previously recorded snapshot; assert byte-identical OR
/// log the first divergence and stop the sim (editor only).
/// </summary>
public enum NavReplayMode : byte
{
    Off = 0,
    Record = 1,
    Replay = 2,
}

/// <summary>
/// task-112 M7 (S11) -- debug visualization toggle. Selects which nav
/// data layer <c>NavDebugDrawSystem</c> renders in the Editor scene
/// view. Editor-only; the system is <c>#if UNITY_EDITOR</c>-guarded so
/// the toggle is also a no-op in player builds.
/// </summary>
public enum NavDebugVisualization : byte
{
    None = 0,
    CostField = 1,
    PortalGraph = 2,
    FlowVectors = 3,
    AbstractAStarPath = 4,
    All = 5,
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

/// <summary>
/// Skirmish lobby starting-age option. Pre-promotes every faction to the
/// chosen age before play begins. All variants past Age0 force the
/// Alanthor culture so the player has a clean demo loadout. See
/// <see cref="GameSettings.StartAge"/> and <c>StartAgePromoter</c>.
/// </summary>
/// <summary>
/// Lobby start-age selector. The value IS the starting Temple level, and the
/// era ladder runs one ahead of it (Temple L1 = Era 2), so:
///   Age0 → no promotion   Age2 → Temple L2, Era 3
///   Age1 → Temple L1, Era 2   Age3 → Temple L3, Era 4
///   Age4 → Temple L4, Era 5 — the top of the ladder
/// (TempleLevelConfig.MaxLevel is 4, so Age4 is fully teched.)
/// </summary>
public enum SkirmishStartAge : byte
{
    Age0 = 0,
    Age1 = 1,
    Age2 = 2,
    Age3 = 3,
    Age4 = 4,
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

    /// <summary>
    /// Observer perspective: the faction whose HUD and fog the observer is
    /// currently viewing. Written every frame by SelectionSystem from the
    /// selected asset's owner; null = nothing selected = see everything.
    /// Meaningless outside observer mode — read ViewFaction instead.
    /// </summary>
    public static Faction? ObserverViewFaction = null;

    /// <summary>
    /// The faction whose perspective the local VIEW renders (fog overlay,
    /// entity culling, minimap, resource bar). Normal play: always the
    /// local player. Observer: the selected asset's owner, or null for
    /// full reveal.
    /// </summary>
    public static Faction? ViewFaction => IsObserver ? ObserverViewFaction : LocalPlayerFaction;

    /// <summary>ViewFaction with the local player as fallback, for readers
    /// that need a concrete faction.</summary>
    public static Faction ViewFactionOrLocal => ViewFaction ?? LocalPlayerFaction;

    /// <summary>
    /// Tutorial run: an ordinary match on the shipped map with the coach
    /// overlay (TutorialDirector) walking the player through Age 0.
    ///
    /// Every other launch path clears this — it is static state that would
    /// otherwise survive "quit to menu, start a skirmish" and leave the coach
    /// running over a normal game.
    /// </summary>
    public static bool TutorialActive = false;

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

    /// <summary>
    /// Skirmish start-age dropdown. Lets the lobby pre-promote every faction
    /// to a chosen age before play starts, so the player can demo mid-game
    /// strategy without grinding through the early build order each time.
    ///
    ///   Age0  — current default: bare Hall + builders, no age-up applied.
    ///   Age1  — Alanthor L1: Hall L1, Temple of Ridan L1, one random choice
    ///           building (Shrine of Ahridan / Vault of Almiérra / Fiendstone
    ///           Keep) placed nearby. +200 supplies +50 iron pre-stocked.
    ///   Age2  — Alanthor L2: Hall L2, Temple L2, one random choice building.
    ///           +500 supplies +150 iron +50 veilstone.
    ///   Age3  — L3: Hall L3, Temple L3, one random choice building.
    ///           +1000 supplies +300 iron +100 veilstone +30 veilsteel.
    ///   Age4  — L4 (top of the ladder): Hall L4, Temple L4, Era 5. Every
    ///           culture unit and both ritualist gates are open, which is the
    ///           point — the verb objectives sit behind Temple L3/L4 and are
    ///           otherwise ~15 minutes of build-up away in every test.
    ///
    /// All slots (human + AI) get the same age — clean demo setup. The AI's
    /// SimpleAISystem build-order step pointer is advanced past the end so
    /// every AI starts in the post-build maintenance loop immediately.
    /// </summary>
    public static SkirmishStartAge StartAge = SkirmishStartAge.Age0;

    /// <summary>
    /// Culture every faction is promoted into when <see cref="StartAge"/> is
    /// above Age0. Defaults to Alanthor, which is what the start-age feature
    /// hardcoded before this was configurable — so existing presets behave
    /// exactly as they did.
    ///
    /// Cultures.None is treated as Alanthor by the promoter: a promoted
    /// faction must have SOME culture, or it sits at a raised era with none of
    /// the buildings or units that era implies.
    /// </summary>
    public static byte StartCulture = Cultures.Alanthor;

    // ==================== HUD ====================
    // The CEF-backed web HUD (HudWebController + HudBridge + UseWebHud flag)
    // was REMOVED entirely 2026-07-16 (user request). The UI Toolkit stack
    // (GameplayUIController + regions + modals) is the in-game HUD.

    // ==================== Selection Settings ====================

    /// <summary>
    /// Smart military drag-select: when ON, a click-and-drag rectangle that
    /// contains both military and economic units selects only the military
    /// units (workers / scouts are excluded). When OFF, the rectangle selects
    /// every selectable entity it covers.
    /// </summary>
    public static bool SmartMilitaryDrag = true;

    // ==================== Map Settings ====================

    /// <summary>
    /// Scene name of the map to load when the lobby starts a match. Must
    /// match an entry in <see cref="TheWaningBorder.Core.Maps.MapRegistry"/>
    /// AND be present in File → Build Settings → Scenes in Build. Defaults to
    /// the registry's default (only) hand-authored map.
    /// </summary>
    public static string SelectedMapScene =
        TheWaningBorder.Core.Maps.MapRegistry.Default.SceneName;

    /// <summary>Half the map size (total map = 2 * MapHalfSize).</summary>
    public static int MapHalfSize = 125;

    /// <summary>
    /// Whether fog of war is enabled. ON by default (directive 2026-07-05) —
    /// the skirmish/multiplayer lobbies expose a toggle initialised from
    /// this; test scenarios and the pathfinding test explicitly turn it off.
    /// NOTE: the AI's entire intel layer (IntelSystem sightings, target
    /// scoring, FindClosestEnemyOf) keys its honesty off the
    /// FogOfWarManager — with fog disabled the manager doesn't exist and
    /// the AI legitimately sees everything, same as the human does.
    /// </summary>
    public static bool FogOfWarEnabled = true;

    /// <summary>Whether the The Border faction spawns on this map.</summary>
    public static bool BorderEnabled = true;

    /// <summary>
    /// Flat test map for AI/pathfinding work: skips noise heightmap, terrain
    /// trees, ObstacleBootstrap (forests + rocks), and BorderNodeBootstrap.
    /// Result: flat ground at <c>spawnTargetHeight</c>, only Halls and iron
    /// deposits, water plane hidden below the terrain. Toggle off when you
    /// want production-style maps with hills and forests back.
    /// </summary>
    // Default OFF — production maps need hills, forests, iron-deposit slope
    // checks, and the rest of the world-clutter pipeline. Toggle on only
    // when isolating AI / pathfinding tests from terrain noise.
    public static bool FlatTestMap = false;

    // ==================== Pathfinding Settings ====================

    /// <summary>Cell size for the passability grid (world units per cell). 1 = 1m resolution.</summary>
    public static float PathfindingCellSize = 1f;

    // Flow-field / A* toggles removed in PR3 — navmesh is the only path
    // source. PassabilityGrid stays for non-pathing queries (territorial
    // enclosures, spawn placement, building placement validation).

    // ==================== Navigation Stack (task-112 M7) ====================

    /// <summary>
    /// task-112 M7 -- replay mode for <c>DeterminismReplaySystem</c>.
    /// Defaults to <see cref="NavReplayMode.Off"/>; the Phase7Test
    /// scenario flips it to <see cref="NavReplayMode.Record"/> /
    /// <see cref="NavReplayMode.Replay"/> as appropriate.
    /// </summary>
    public static NavReplayMode NavReplayMode = NavReplayMode.Off;

    /// <summary>
    /// task-112 M7 -- debug visualization mode for
    /// <c>NavDebugDrawSystem</c>. Editor-only; ignored in player builds.
    /// </summary>
    // Temporarily set to CostField so the tester can see which cells the
    // CostFieldStampSystem is actually marking impassable. Flip back to
    // .None once buildings/obstacles are verified to stamp correctly.
    public static NavDebugVisualization NavDebugVisualization = NavDebugVisualization.CostField;

    // ==================== Multiplayer Settings ====================

    /// <summary>Whether the current game is a multiplayer session.</summary>
    public static bool IsMultiplayer = false;

    /// <summary>
    /// TRUE deterministic lockstep: the ECS SimulationSystemGroup is driven at a
    /// FIXED timestep, exactly once per lockstep tick, instead of free-running on
    /// the render frame rate.
    ///
    /// This is not a tuning option — it is the difference between two players
    /// sharing a match and two players running separate games that happen to
    /// share a lobby. With it off, only COMMANDS are synchronised; every outcome
    /// (combat resolution, mining, construction, training) advances on each
    /// peer's own frame clock and the worlds fork within a minute or two.
    ///
    /// Default ON. It is only consulted in multiplayer — single-player keeps its
    /// per-frame simulation either way — and every multiplayer entry point sets
    /// it explicitly so a stale value from a previous session can never decide
    /// it. docs/Multiplayer_LAN_Readiness.md
    /// </summary>
    public static bool DeterministicLockstep = true;

    /// <summary>
    /// True when THIS peer should run AI brains.
    ///
    /// In multiplayer the AI is simulated on the HOST ONLY and its orders reach
    /// everyone else through the lockstep command stream. A client that also ran
    /// its own brains would apply every AI decision twice — once from its local
    /// brain (CommandSource.AI does not queue on a client, so it executes
    /// directly) and once from the host's replicated command — while the two
    /// brains' RNG streams forked on the first differing call.
    ///
    /// Every AI system's OnUpdate must open with this test.
    /// docs/Multiplayer_LAN_Readiness.md
    /// </summary>
    public static bool ShouldRunAIBrains() => !IsMultiplayer || IsHost();

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
        ObserverViewFaction = null;
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