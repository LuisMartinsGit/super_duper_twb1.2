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

            Debug.Log("[GameBootstrap] Initializing game systems...");

            // 0. Ensure ECS world exists (may have been disposed on previous game exit)
            EnsureECSWorld();

            // 1. Initialize core data systems (TechTreeDB)
            InitializeDataSystems();

            // 2. Initialize camera
            GameCamera.Ensure();

            // 3. Create runtime managers GameObject
            CreateManagersObject();

            // 4. Initialize game world (terrain, fog of war)
            InitializeWorld();

            // 5. Initialize factions and economy
            InitializeFactions();

            // 6. Initialize AI players
            InitializeAI();

            // 7. Sync systems after all initialization
            PostInitializationSync();

            Debug.Log($"[GameBootstrap] Game initialized - IsMultiplayer: {GameSettings.IsMultiplayer}, " +
                      $"LocalFaction: {GameSettings.LocalPlayerFaction}, Players: {GameSettings.TotalPlayers}");
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
                Debug.Log("[GameBootstrap] ECS world already exists");
                return;
            }

            Debug.Log("[GameBootstrap] Recreating ECS world...");
            Unity.Entities.DefaultWorldInitialization.Initialize("Default World");
            Debug.Log("[GameBootstrap] ECS world recreated successfully");
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
                Debug.Log("[GameBootstrap] TechTreeDB already initialized");
                return;
            }

            // TechTreeDB is a MonoBehaviour - create it if it doesn't exist
            var existing = Object.FindFirstObjectByType<TechTreeDB>();
            if (existing != null)
            {
                Debug.Log("[GameBootstrap] Found existing TechTreeDB");
                return;
            }

            // Create TechTreeDB GameObject - it will auto-load from Resources in Start()
            var techTreeGO = new GameObject("TechTreeDB");
            techTreeGO.AddComponent<TechTreeDB>();
            Object.DontDestroyOnLoad(techTreeGO);
            Debug.Log("[GameBootstrap] Created TechTreeDB (will auto-load from Resources)");
        }

        // ═══════════════════════════════════════════════════════════════
        // MANAGERS
        // ═══════════════════════════════════════════════════════════════

        private static void CreateManagersObject()
        {
            var existing = Object.FindFirstObjectByType<RuntimeManagers>();
            if (existing != null)
            {
                Debug.Log("[GameBootstrap] RuntimeManagers already exists");
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
            managersGO.AddComponent<SelectionRings>();           // Selection visual indicators
            managersGO.AddComponent<MinimapRenderer>();          // Minimap display
            managersGO.AddComponent<FloatingIncomeDisplay>();   // BFME2-style floating income text
            managersGO.AddComponent<ProjectileVisualSystem>();   // Arrow projectile visuals
            managersGO.AddComponent<GathererHutAreaDisplay>();   // GathererHut radius circle display
            managersGO.AddComponent<RallyPointDisplay>();        // Rally point marker display
            managersGO.AddComponent<MovementLineDisplay>();      // Unit movement destination lines
            managersGO.AddComponent<PlacementGridOverlay>();     // Grid overlay during building placement
            managersGO.AddComponent<GameStatsTracker>();          // Resource/population timeline tracker
            managersGO.AddComponent<EndGameButton>();              // End Game button
            managersGO.AddComponent<PostGameStatsUI>();            // Post-game statistics graphs
            managersGO.AddComponent<VictoryConditionSystem>();      // Win/loss condition checker
            managersGO.AddComponent<FactionResearchState>();       // Research tracking per faction
            managersGO.AddComponent<TechEffectSystem>();            // Tech effect application on research completion
            managersGO.AddComponent<FactionSectState>();            // Sect adoption tracking per faction
            managersGO.AddComponent<SectEffectSystem>();            // Sect passive effect application
            managersGO.AddComponent<InGameMenuPanel>();              // In-game menu (ESC key)
            managersGO.AddComponent<AStarPathStore>();               // A* per-unit path storage
            managersGO.AddComponent<PathfindingToggleHUD>();         // FF/A* toggle button (F5)
            Object.DontDestroyOnLoad(managersGO);
            Debug.Log("[GameBootstrap] Created RuntimeManagers");
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
                Debug.Log("[GameBootstrap] Created ProceduralTerrain");
            }

            // Create passability grid for flow-field pathfinding (needs terrain)
            var existingGrid = Object.FindFirstObjectByType<PassabilityGrid>();
            if (existingGrid == null)
            {
                var gridGO = new GameObject("PassabilityGrid");
                gridGO.AddComponent<PassabilityGrid>();
                Debug.Log("[GameBootstrap] Created PassabilityGrid");
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
                Debug.Log("[GameBootstrap] Created FlowFieldManager");
            }

            // Initialize fog of war if enabled (disabled for Observer - they see everything)
            if (GameSettings.FogOfWarEnabled && !GameSettings.IsObserver)
            {
                FogOfWarManager.SetupFogOfWar();
                Debug.Log("[GameBootstrap] Fog of war initialized");
            }
            else if (GameSettings.IsObserver)
            {
                Debug.Log("[GameBootstrap] Fog of war disabled (Observer mode)");
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
                Debug.Log("[GameBootstrap] Skipping AI initialization (Sandbox/PathfindingTest mode)");
                return;
            }

            AIBootstrap.InitializeAIPlayers(GameSettings.TotalPlayers, GameSettings.LocalPlayerFaction);

            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot != null && slot.Type == SlotType.AI)
                {
                    Debug.Log($"[GameBootstrap] AI initialized for faction {slot.Faction} " +
                              $"(Difficulty: {slot.AIDifficulty})");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // POST INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        private static void PostInitializationSync()
        {
            // Any final synchronization needed after all systems are up
            Debug.Log("[GameBootstrap] Post-initialization sync complete");
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
            Debug.Log("[GameBootstrap] Reset for new game");
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
            Debug.Log("[RuntimeManagers] Awake");
        }
    }
}