// GameBootstrap.cs
// Main game initialization - coordinates all bootstrap systems
// Location: Assets/Scripts/Bootstrap/GameBootstrap.cs
// NOTE: This file should be in Assets/Scripts/Bootstrap/, NOT in Core/Bootstrap/

using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Entities;
using TheWaningBorder.Input;  // Contains GameCamera
using TheWaningBorder.Core.Config;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Minimap;
using TheWaningBorder.Economy;
using TheWaningBorder.Presentation;
using TheWaningBorder.AI;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Systems.Research;
using TheWaningBorder.Systems.Movement;
using TheWaningBorder.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Main game bootstrap - initializes all game systems when the Game scene loads.
    /// Uses [RuntimeInitializeOnLoadMethod] to auto-run without scene dependencies.
    /// </summary>
    public static class GameBootstrap
    {
        private static bool _didSetupThisScene;

        // ═══════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            // Reset static state — required when domain reload is disabled
            _didSetupThisScene = false;

            SceneManager.sceneLoaded -= OnSceneLoadedHandler;
            SceneManager.sceneLoaded += OnSceneLoadedHandler;
            OnSceneLoadedHandler(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoadedHandler(Scene scene, LoadSceneMode mode)
        {
            // Only bootstrap the Game scene
            if (!string.Equals(scene.name, "Game")) return;
            if (_didSetupThisScene) return;
            _didSetupThisScene = true;

            // 0. Ensure ECS world exists (may have been disposed on previous game exit)
            EnsureECSWorld();

            // Battalion test mode: minimal bootstrap with just battalions
            if (GameSettings.Mode == GameMode.BattalionTest)
            {
                InitializeDataSystems();
                PathfindingTestSetup.Bootstrap();
                return;
            }

            // Scenario mode: same init pipeline as skirmish (so RuntimeManagers
            // gets created — input, formation drag preview, etc. — and terrain,
            // passability grid, flow fields, fog of war flag are all wired up
            // identically). Only the spawn step differs: instead of placing
            // halls + builders for each player, we place the scenario's
            // predefined entities.
            bool isScenario = GameSettings.Mode == GameMode.Scenario;
            if (isScenario)
            {
                ScenarioSetup.PreInit();
            }

            // 0.5. Initialize lockstep BEFORE anything else in multiplayer
            // This ensures LockstepServiceLocator.Instance is available when commands are issued
            if (GameSettings.IsMultiplayer && LockstepBootstrap.Instance != null)
            {
                LockstepBootstrap.Instance.InitializeLockstepNow();
            }

            // 1. Initialize core data systems (TechTreeDB)
            InitializeDataSystems();

            // 2. Initialize camera
            GameCamera.Ensure();

            // 3. Create runtime managers GameObject
            CreateManagersObject();

            // 4. Initialize game world (terrain, fog of war)
            InitializeWorld();

            // 5. Spawn — either scenario entities OR factions/economy/AI
            if (isScenario)
            {
                ScenarioSetup.SpawnScenarioEntities();
            }
            else
            {
                InitializeFactions();
                InitializeAI();
            }

            // 6. Sync systems after all initialization
            PostInitializationSync();
        }

        // ═══════════════════════════════════════════════════════════════
        // ECS WORLD
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Ensure the ECS DefaultGameObjectInjectionWorld exists.
        /// After returning to main menu the previous world is disposed,
        /// so we must recreate it before any ECS operations.
        /// </summary>
        private static void EnsureECSWorld()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                return;
            }

            Unity.Entities.DefaultWorldInitialization.Initialize("Default World");
        }

        // ═══════════════════════════════════════════════════════════════
        // DATA SYSTEMS
        // ═══════════════════════════════════════════════════════════════

        private static void InitializeDataSystems()
        {
            EnsureTechTreeDB();
        }

        private static void EnsureTechTreeDB()
        {
            if (TechTreeDB.Instance != null)
            {
                return;
            }

            // TechTreeDB is a MonoBehaviour - create it if it doesn't exist
            var existing = Object.FindFirstObjectByType<TechTreeDB>();
            if (existing != null)
            {
                return;
            }

            // Create TechTreeDB GameObject - it will auto-load from Resources in Start()
            var techTreeGO = new GameObject("TechTreeDB");
            techTreeGO.AddComponent<TechTreeDB>();
            Object.DontDestroyOnLoad(techTreeGO);
        }

        // ═══════════════════════════════════════════════════════════════
        // MANAGERS
        // ═══════════════════════════════════════════════════════════════

        private static void CreateManagersObject()
        {
            var existing = Object.FindFirstObjectByType<RuntimeManagers>();
            if (existing != null)
            {
                return;
            }

            var managersGO = new GameObject("RuntimeManagers");
            managersGO.AddComponent<RuntimeManagers>();
            managersGO.AddComponent<EntityViewManager>();
            managersGO.AddComponent<PresentationSpawnSystem>();
            managersGO.AddComponent<SelectionSystem>();          // Click + box select
            managersGO.AddComponent<RTSInputManager>();          // Right-click command routing
            managersGO.AddComponent<UnifiedUIManager>();         // Entity info + action panels
            managersGO.AddComponent<BuilderCommandPanel>();      // Building placement preview
            managersGO.AddComponent<ResourceHUD>();              // Resource display
            managersGO.AddComponent<ReligionHUD>();              // Top-center sect-slot HUD (audit fix #3)
            managersGO.AddComponent<MinimapRenderer>();          // Minimap display
            managersGO.AddComponent<FloatingIncomeDisplay>();   // BFME2-style floating income text
            managersGO.AddComponent<ProjectileVisualSystem>();   // Arrow projectile visuals
            managersGO.AddComponent<BuildingEffectSystem>();    // Construction dust + collapse effects
            managersGO.AddComponent<GathererHutAreaDisplay>();   // GathererHut radius circle display
            managersGO.AddComponent<RallyPointDisplay>();        // Rally point marker display
            managersGO.AddComponent<MovementLineDisplay>();      // Unit movement destination lines
            managersGO.AddComponent<UnitIndicatorSystem>();     // Direction arrows + state circles
            managersGO.AddComponent<PlanningModeOverlay>();     // Planning mode overlay (Z key)
            managersGO.AddComponent<FormationPreview>();        // Formation preview arrows at destination
            managersGO.AddComponent<FormationDragPreview>();    // Right-click-hold formation preview (rows + rotation)
            managersGO.AddComponent<GameStatsTracker>();          // Resource/population timeline tracker
            managersGO.AddComponent<InGameMenuPanel>();              // In-game menu (ESC)
            managersGO.AddComponent<EndGameButton>();              // End Game button
            managersGO.AddComponent<PostGameStatsUI>();            // Post-game statistics graphs
            managersGO.AddComponent<VictoryConditionSystem>();      // Win/loss condition checker
            managersGO.AddComponent<FactionResearchState>();       // Research tracking per faction
            managersGO.AddComponent<TechEffectSystem>();            // Tech effect application on research completion
            // Sect system v2 (task-063): adoption state + RP balance live on the
            // faction bank entity (see EconomyBootstrap), not in managed singletons.
            // Phase 2 will add the per-sect, per-lever effect dispatchers.
            // Fix #232: duplicate InGameMenuPanel AddComponent removed.
            // The first AddComponent<InGameMenuPanel>() a few lines above
            // already registers the ESC menu; adding it twice attached two
            // instances of the same MonoBehaviour to the RuntimeManagers
            // GameObject, causing doubled event handling and a second render
            // pass per frame.
            managersGO.AddComponent<AStarPathStore>();               // A* per-unit path storage
            managersGO.AddComponent<PathfindingToggleHUD>();         // FF/A* toggle button (F5)
            Object.DontDestroyOnLoad(managersGO);
        }

        // ═══════════════════════════════════════════════════════════════
        // WORLD INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        private static void InitializeWorld()
        {
            // Create procedural terrain
            var existingTerrain = Object.FindFirstObjectByType<ProceduralTerrain>();
            if (existingTerrain == null)
            {
                var terrainGO = new GameObject("ProceduralTerrain");
                terrainGO.AddComponent<TheWaningBorder.World.Terrain.ProceduralTerrain>();
            }

            // Day-night cycle with directional sun + cloud shadows
            if (Object.FindFirstObjectByType<TheWaningBorder.World.DayNightCycle>() == null)
            {
                var dnGO = new GameObject("DayNightCycle");
                dnGO.AddComponent<TheWaningBorder.World.DayNightCycle>();
            }

            // Create passability grid for flow-field pathfinding (needs terrain)
            var existingGrid = Object.FindFirstObjectByType<PassabilityGrid>();
            if (existingGrid == null)
            {
                var gridGO = new GameObject("PassabilityGrid");
                gridGO.AddComponent<PassabilityGrid>();
            }

            // Create flow field manager for obstacle-aware pathfinding (needs grid)
            var existingFFM = Object.FindFirstObjectByType<FlowFieldManager>();
            if (existingFFM == null)
            {
                var ffmGO = new GameObject("FlowFieldManager");
                ffmGO.AddComponent<FlowFieldManager>();
                #if UNITY_EDITOR
                ffmGO.AddComponent<FlowFieldGizmos>();
                #endif
            }

            // Initialize fog of war if enabled (disabled for Observer - they see everything)
            if (GameSettings.FogOfWarEnabled && !GameSettings.IsObserver)
            {
                FogOfWarManager.SetupFogOfWar();
            }
            else if (GameSettings.IsObserver)
            {
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // FACTIONS & ECONOMY
        // ═══════════════════════════════════════════════════════════════

        private static void InitializeFactions()
        {
            // Initialize economy banks first
            EconomyBootstrap.EnsureFactionBanks(GameSettings.TotalPlayers);

            // Spawn players after terrain is ready (use coroutine)
            var helper = new GameObject("SpawnHelper").AddComponent<SpawnDelayHelper>();
            helper.StartCoroutine(helper.WaitForTerrainAndSpawn());
        }

        // ═══════════════════════════════════════════════════════════════
        // AI INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        private static void InitializeAI()
        {
            // Sandbox / PathfindingTest: no AI opponents
            if (GameSettings.IsSandbox || GameSettings.Mode == GameMode.PathfindingTest)
            {
                return;
            }

            AIBootstrap.InitializeAIPlayers(GameSettings.TotalPlayers, GameSettings.LocalPlayerFaction);

            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot != null && slot.Type == SlotType.AI)
                {
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // POST INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        private static void PostInitializationSync()
        {
            // Any final synchronization needed after all systems are up
        }

        // ═══════════════════════════════════════════════════════════════
        // CLEANUP
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Reset bootstrap state (call when returning to main menu)
        /// </summary>
        public static void Reset()
        {
            _didSetupThisScene = false;
            GameCamera.Cleanup();
        }
    }

    /// <summary>
    /// Placeholder component for runtime managers GameObject.
    /// Add actual manager components here.
    /// </summary>
    public class RuntimeManagers : MonoBehaviour
    {
        void Awake()
        {
        }
    }
}