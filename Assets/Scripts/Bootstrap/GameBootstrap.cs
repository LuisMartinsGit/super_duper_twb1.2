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
using TheWaningBorder.UI;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Systems.Research;
using TheWaningBorder.Systems.Movement;
using TheWaningBorder.Multiplayer;
using TheWaningBorder.UI.Web;

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
            // Only bootstrap registered gameplay scenes (the procedural
            // "Game" scene + any hand-authored maps in MapRegistry).
            if (!TheWaningBorder.Core.Maps.MapRegistry.IsGameplayScene(scene.name)) return;
            if (_didSetupThisScene) return;
            _didSetupThisScene = true;

            // ABSOLUTE MINIMUM synchronous work: just kick a coroutine. The
            // rest of the bootstrap (TechTreeDB, RuntimeManagers, terrain,
            // factions, AI) is split into staged phases that yield between
            // each step so the loading screen renders. Doing it all
            // synchronously here used to freeze the activation frame for
            // 5-10 s — the first heavy `AddComponent` blocked Unity before
            // OnGUI could repaint, and the player saw a frozen lobby.
            var driver = new GameObject("BootstrapDriver");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.AddComponent<BootstrapDriver>().StartCoroutine(BootstrapCoroutine(driver));
        }

        // Staged bootstrap. Each phase ends with a yield so the loading
        // screen has at least one rendered frame between heavy AddComponent
        // calls (each of which triggers a synchronous Awake on the new
        // component). Status text + progress reflect the current phase so
        // the player sees what's happening.
        private static System.Collections.IEnumerator BootstrapCoroutine(GameObject driver)
        {
            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Initialising world…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.36f);
            yield return null;

            EnsureECSWorld();

            // Battalion test mode: minimal bootstrap.
            if (GameSettings.Mode == GameMode.BattalionTest)
            {
                InitializeDataSystems();
                PathfindingTestSetup.Bootstrap();
                yield break;
            }

            bool isScenario = GameSettings.Mode == GameMode.Scenario;
            if (isScenario)
            {
                ScenarioSetup.PreInit();
                yield return null;
            }

            if (GameSettings.IsMultiplayer && LockstepBootstrap.Instance != null)
            {
                LockstepBootstrap.Instance.InitializeLockstepNow();
                yield return null;
            }

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Loading tech tree…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.38f);
            yield return null;
            InitializeDataSystems();

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Setting up camera…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.40f);
            yield return null;
            GameCamera.Ensure();

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Spawning managers…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.42f);
            yield return null;
            CreateManagersObject();

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Building world…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.44f);
            yield return null;
            InitializeWorld();

            // ProceduralTerrain.Start now drives 45 → 55 % via its own
            // SetStatus / SetProgress calls while its generation coroutine
            // runs. We block here until generation is complete so the
            // spawn step below has real heightmap data to read.
            while (!TheWaningBorder.World.Terrain.ProceduralTerrain.IsGenerationComplete)
                yield return null;

            if (isScenario)
            {
                ScenarioSetup.SpawnScenarioEntities();
            }
            else
            {
                InitializeFactions();
                InitializeAI();
            }

            yield return null;
            PostInitializationSync();

            UnityEngine.Object.Destroy(driver);
        }

        // Tiny MonoBehaviour host for the static bootstrap coroutine. Used
        // only to give us access to StartCoroutine from a static context.
        private class BootstrapDriver : MonoBehaviour { }

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
            managersGO.AddComponent<TheWaningBorder.Presentation.BuildingPrefabSwapSystem>();
            managersGO.AddComponent<SelectionSystem>();          // Click + box select
            managersGO.AddComponent<RTSInputManager>();          // Right-click command routing
            var unifiedUI = managersGO.AddComponent<UnifiedUIManager>();         // Spawns EntityInfo/Action panels in its Awake
            var legacyToolkitHud = managersGO.AddComponent<GameplayUIController>();    // UI Toolkit HUD (jade theme)
            var legacyBuildCmd = managersGO.AddComponent<BuilderCommandPanel>();      // Building placement preview
            var legacyResourceHud = managersGO.AddComponent<ResourceHUD>();
            var legacyReligionHud = managersGO.AddComponent<ReligionHUD>();
            managersGO.AddComponent<TheWaningBorder.Presentation.ChapelSiteDecals>();

            // When the web HUD is on, fit the legacy minimap to the diamond
            // frame painted by CEF. A 192×192 square rotated 45° has a
            // half-diagonal of 192/√2 ≈ 135.8 — matching the 272-diamond's
            // half-extent of 136. Pivot-center; offsetBR is the inset from
            // the screen corner to the diamond CENTER (mm-root is 300×300
            // anchored at right:24,bottom:24 in CSS, so center = 174,174).
            // Numbers assume CEF's 1920×1080 reference; ScaleWithScreenSize
            // keeps the two canvases aligned at other resolutions.
            if (GameSettings.UseWebHud)
            {
                // 188×188 → diagonal ≈ 266, just inside the polygon's 265
                // tip-to-tip span so the rotated corners sit a hair inside
                // the bezel rather than crossing it. Tune on RuntimeManagers
                // → MinimapRenderer.sizePixels in the inspector if needed.
                TheWaningBorder.World.Minimap.MinimapRenderer.OverrideSizePixels = 188;
                TheWaningBorder.World.Minimap.MinimapRenderer.OverrideOffsetBR = new Vector2(174, 174);
                TheWaningBorder.World.Minimap.MinimapRenderer.OverrideCanvasSortingOrder = 101;
                TheWaningBorder.World.Minimap.MinimapRenderer.ForceDedicatedCanvas = true;
            }
            var legacyMinimap     = managersGO.AddComponent<MinimapRenderer>();
            managersGO.AddComponent<FloatingIncomeDisplay>();   // BFME2-style floating income text
            managersGO.AddComponent<ProjectileVisualSystem>();   // Arrow projectile visuals
            managersGO.AddComponent<BuildingEffectSystem>();    // Construction dust + collapse effects
            managersGO.AddComponent<RitualBeamSystem>();         // Ritual broadcast beams (spec §5.1)
            managersGO.AddComponent<CaravanVisualSystem>();       // Procedural desert-traveler visual (spec refinement #3)
            managersGO.AddComponent<UnitVisualOverlaySystem>();   // Rank pips + Glow halo (spec refinement #7)
            // GodPowerHUD removed — sect god powers now route through the existing
            // ReligionHUD's per-sect Fire buttons. Glow allocation lives on each
            // chapel slot (TempleChapelSlot.GlowAllocated).
            var legacyVictoryHud = managersGO.AddComponent<VictoryProgressHUD>();         // Per-culture node-victory tracker (spec §11 item 7)
            managersGO.AddComponent<GathererHutAreaDisplay>();   // GathererHut radius circle display
            managersGO.AddComponent<TheWaningBorder.World.Terrain.GathererHutGrassPainter>(); // Yellow-grass detail patch around completed huts
            managersGO.AddComponent<RallyPointDisplay>();        // Rally point marker display
            managersGO.AddComponent<MovementLineDisplay>();      // Unit movement destination lines
            managersGO.AddComponent<UnitIndicatorSystem>();     // Direction arrows + state circles
            managersGO.AddComponent<PlanningModeOverlay>();     // Planning mode overlay (Z key)
            managersGO.AddComponent<FormationPreview>();        // Formation preview arrows at destination
            managersGO.AddComponent<FormationDragPreview>();    // Right-click-hold formation preview (rows + rotation)
            managersGO.AddComponent<GameStatsTracker>();          // Resource/population timeline tracker
            var legacyInGameMenu = managersGO.AddComponent<InGameMenuPanel>();              // In-game menu (ESC) — statics used by HudBridge even when component is disabled
            var legacyEndGame = managersGO.AddComponent<EndGameButton>();              // End Game button
            var legacyPostGame = managersGO.AddComponent<PostGameStatsUI>();            // Post-game statistics graphs
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
            // PR3 — AStarPathStore + PathfindingToggleHUD removed (legacy nav stack gone).

            // CEF-backed HUD overlay. Lives on its own GameObject (separate
            // Canvas) so the legacy IMGUI HUDs can stay attached for fallback
            // / debugging. When the flag is on, we disable the legacy HUDs
            // so they don't paint on top of the web HUD.
            if (GameSettings.UseWebHud)
            {
                // Silence the legacy HUD / panel painters — leave the components
                // attached so their static APIs (InGameMenuPanel.IsOpen, etc.)
                // still work; just suppress their OnGUI / Update rendering.
                if (legacyToolkitHud != null) legacyToolkitHud.enabled = false;
                if (legacyResourceHud != null) legacyResourceHud.enabled = false;
                if (legacyReligionHud != null) legacyReligionHud.enabled = false;
                // legacyMinimap stays enabled — it renders the actual minimap
                // inscribed inside the web HUD's diamond frame.
                if (legacyVictoryHud != null) legacyVictoryHud.enabled = false;
                if (legacyInGameMenu != null) legacyInGameMenu.enabled = false;
                if (legacyEndGame != null) legacyEndGame.enabled = false;
                if (legacyPostGame != null) legacyPostGame.enabled = false;
                // BuilderCommandPanel does NOT render IMGUI — it only drives
                // building-placement runtime (preview follow, raycast validity,
                // confirm/cancel clicks). The web HUD's Action panel calls
                // BuilderCommandPanel.TriggerBuildingPlacement(...) to start
                // placement, so this component must stay enabled to actually
                // move the preview, accept the click, and spawn the building.

                // UI Toolkit UIDocuments keep rendering even when their owning
                // controller script is disabled — they paint through the panel
                // settings asset. Disable every UIDocument in the scene so the
                // old "Jade" UXML HUD (objectives, mixed-detachment, actions
                // panel, white minimap plate) doesn't bleed through. Also
                // walks DontDestroyOnLoad objects via FindObjectsByType.
                var docs = Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var d in docs)
                {
                    if (d != null) d.enabled = false;
                }

                // UnifiedUIManager.Awake spawns EntityInfoPanel / EntityActionPanel /
                // CultureChoicePopup / TechTreePanel / FloatingHealthBars /
                // PlayerNotificationSystem as sibling components. AddComponent ran
                // Awake synchronously, so they're already attached here — disable
                // the screen-space ones, keep FloatingHealthBars (worldspace).
                if (unifiedUI != null)
                {
                    var infoPanel = unifiedUI.GetComponent<TheWaningBorder.UI.Panels.EntityInfoPanel>();
                    if (infoPanel != null) infoPanel.enabled = false;
                    var actionPanel = unifiedUI.GetComponent<TheWaningBorder.UI.Panels.EntityActionPanel>();
                    if (actionPanel != null) actionPanel.enabled = false;
                    var techTree = unifiedUI.GetComponent<TheWaningBorder.UI.Panels.TechTreePanel>();
                    if (techTree != null) techTree.enabled = false;
                    var culturePopup = unifiedUI.GetComponent<TheWaningBorder.UI.Panels.CultureChoicePopup>();
                    if (culturePopup != null) culturePopup.enabled = false;
                    var notifications = unifiedUI.GetComponent<TheWaningBorder.UI.HUD.PlayerNotificationSystem>();
                    if (notifications != null) notifications.enabled = false;
                    // FloatingHealthBars stays enabled — it draws worldspace HP bars
                    // over units, which the web HUD can't replicate.
                }

                var webHudGo = new GameObject("WebHud");
                webHudGo.AddComponent<HudWebController>();
                webHudGo.AddComponent<HudBridge>();
                Object.DontDestroyOnLoad(webHudGo);
            }

            Object.DontDestroyOnLoad(managersGO);
        }

        // ═══════════════════════════════════════════════════════════════
        // WORLD INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        private static void InitializeWorld()
        {
            // ─── Disable Unity.NetCode rate-managers ─────────────────────
            // `com.unity.netcode` is in Packages/manifest.json but the
            // project's multiplayer is actually its own lockstep layer
            // (Assets/Scripts/Multiplayer/Lockstep). When NetCode is
            // present but not driving a real client/server, its
            // NetcodeClientRateManager gates SimulationSystemGroup on a
            // network clock; without a connection that clock goes
            // negative every frame, the log spams
            // "Delta time was negative. To avoid undefined behaviour the
            // frame is skipped", and every ECS system in the simulation
            // group (movement, targeting, combat, projectiles…) silently
            // stops ticking. Clearing the rate manager restores the
            // default behaviour: tick once per Unity Update.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var simGroup = world.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
                if (simGroup != null) simGroup.RateManager = null;

                var initGroup = world.GetExistingSystemManaged<Unity.Entities.InitializationSystemGroup>();
                if (initGroup != null) initGroup.RateManager = null;

                var presGroup = world.GetExistingSystemManaged<Unity.Entities.PresentationSystemGroup>();
                if (presGroup != null) presGroup.RateManager = null;
            }

            // Decide whether this scene needs procedural terrain generation.
            // Authoritative signal: does the scene ALREADY contain a Unity
            // Terrain? If so, the designer baked one in (MapMagic etc.) and
            // we MUST NOT spawn ProceduralTerrain on top — it would create a
            // second TerrainData and stomp the user's splatmap / heightmap.
            // The MapRegistry IsProcedural flag is only a hint / fallback for
            // empty scenes; the actual terrain presence wins because Unicode
            // normalization on accented scene names (yielLymwérra has the
            // composed-vs-decomposed-é problem) can make registry string
            // matches silently fail.
            var activeScene = SceneManager.GetActiveScene();
            var activeMap = TheWaningBorder.Core.Maps.MapRegistry.GetEntry(activeScene.name);
            var bakedTerrain = UnityEngine.Terrain.activeTerrain;
            bool hasBakedTerrain = bakedTerrain != null && bakedTerrain.terrainData != null;

            TWBLog.Log($"[GameBootstrap] active scene: '{activeScene.name}' → " +
                      $"map='{activeMap.DisplayName}' hasBakedTerrain={hasBakedTerrain}");

            // Hand-authored maps only. The scene ships its own baked Unity
            // Terrain (MapMagic etc.). Flip the bootstrap-wide ready gate so
            // SpawnDelayHelper / TerrainUtility.IsReady() fall through, then
            // size the world to the actual terrain bounds.
            TheWaningBorder.World.Terrain.ProceduralTerrain.MarkExternalTerrainReady();

            if (!hasBakedTerrain)
            {
                Debug.LogError("[GameBootstrap] map has NO active Unity Terrain. " +
                               "Add a Unity Terrain (e.g. from MapMagic) to the scene.");
            }
            else
            {
                var sz = bakedTerrain.terrainData.size;
                var tpos = bakedTerrain.transform.position;
                TWBLog.Log($"[GameBootstrap] using baked Unity Terrain '{bakedTerrain.name}' at " +
                          $"{tpos} size {sz}");

                // Hand-authored maps don't go through the lobby slider that
                // sets GameSettings.MapHalfSize, so it stays at its 125 default
                // and FoW, camera limits, and deposit ranges all size for a
                // 250m map at origin. Snap MapHalfSize to the largest
                // half-extent of the actual terrain so the FoW mesh covers the
                // playable area and the camera can pan to it.
                int half = Mathf.CeilToInt(Mathf.Max(
                    Mathf.Max(Mathf.Abs(tpos.x), Mathf.Abs(tpos.x + sz.x)),
                    Mathf.Max(Mathf.Abs(tpos.z), Mathf.Abs(tpos.z + sz.z))));
                if (half > GameSettings.MapHalfSize)
                {
                    TWBLog.Log($"[GameBootstrap] MapHalfSize {GameSettings.MapHalfSize} -> {half} (from terrain bounds)");
                    GameSettings.MapHalfSize = half;
                }
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

            // NavMeshManager: adopts the scene's pre-baked navmesh (hand-crafted
            // maps) and owns RequestPath / SnapToNavMesh.
            var existingNMM = Object.FindFirstObjectByType<NavMeshManager>();
            if (existingNMM == null)
            {
                var nmmGO = new GameObject("NavMeshManager");
                nmmGO.AddComponent<NavMeshManager>();
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