// GameBootstrap.cs
// Main game initialization - coordinates all bootstrap systems
// NOTE: This file should be in Assets/Scripts/Bootstrap/, NOT in Core/Bootstrap/

using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Entities;
using TheWaningBorder.Input;  // Contains GameCamera
using TheWaningBorder.Core.Config;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.FogOfWar;
// TheWaningBorder.World.Minimap using removed with MinimapRenderer (2026-07-17 UI removal)
using TheWaningBorder.Economy;
using TheWaningBorder.Presentation;
using TheWaningBorder.AI;
using TheWaningBorder.UI;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Systems.Research;
using TheWaningBorder.Multiplayer;

using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Core;
namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Main game bootstrap - initializes all game systems when the Game scene loads.
    /// Uses [RuntimeInitializeOnLoadMethod] to auto-run without scene dependencies.
    /// </summary>
    public static class GameBootstrap
    {
        /// <summary>True between a gameplay scene's bootstrap and its
        /// teardown — i.e. "a match's managers and entities are live".</summary>
        private static bool _matchLive;

        /// <summary>Frame the current match was bootstrapped on. sceneLoaded
        /// can reach this handler twice for a single load (the subscription
        /// plus Init's manual call); both land on the same frame, which is
        /// what distinguishes a duplicate callback from a genuine reload of
        /// the same scene (Restart Match).</summary>
        private static int _setupFrame = -1;

        // ═══════════════════════════════════════════════════════════════
        // INITIALIZATION
        // ═══════════════════════════════════════════════════════════════

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            // Reset static state — required when domain reload is disabled
            _matchLive = false;
            _setupFrame = -1;

            SceneManager.sceneLoaded -= OnSceneLoadedHandler;
            SceneManager.sceneLoaded += OnSceneLoadedHandler;
            OnSceneLoadedHandler(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoadedHandler(Scene scene, LoadSceneMode mode)
        {
            // Only bootstrap registered gameplay scenes (the procedural
            // "Game" scene + any hand-authored maps in MapRegistry).
            if (!TheWaningBorder.Core.Maps.MapRegistry.IsGameplayScene(scene.name))
            {
                // Left a match. RuntimeManagers is DontDestroyOnLoad, so
                // without this the whole in-game HUD survives into the main
                // menu — and the "already bootstrapped" latch would still be
                // set, so the NEXT match never bootstraps and inherits the
                // dead one's panels, selection and entities.
                TeardownAfterMatch();
                return;
            }
            if (_setupFrame == Time.frameCount) return;   // duplicate callback, one load

            // Entering gameplay FROM gameplay (Restart Match, or a map change
            // that skips the menu) still has the previous match live.
            TeardownAfterMatch();
            _setupFrame = Time.frameCount;
            _matchLive = true;

            // Open this match's log folder BEFORE any bootstrap phase runs, so
            // AILogger, PerfSpikeLog and the console capture all land in it —
            // including anything that fails during loading.
            // The SEED is the field that makes a tester's report reproducible —
            // it belongs in the header and the summary, not just in memory.
            TheWaningBorder.Core.Diagnostics.MatchLogSession.Begin(
                GameSettings.SelectedMapScene,
                $"Map: {GameSettings.SelectedMapScene}  Mode: {GameSettings.Mode}  "
                + $"Seed: {GameSettings.SpawnSeed}\n"
                + $"Players: {GameSettings.TotalPlayers}  Local: {GameSettings.LocalPlayerFaction}  "
                + $"Culture: {GameSettings.StartCulture}  Age: {GameSettings.StartAge}  "
                + $"Fog: {GameSettings.FogOfWarEnabled}  Curse: {GameSettings.BorderEnabled}"
                // Multiplayer identity in the header, so a log sent back on its
                // own still says which peer wrote it and whether the two agreed
                // about the match before a single tick ran. The fingerprint is
                // the first thing to compare when two logs disagree.
                + (GameSettings.IsMultiplayer
                    ? $"\nRole: {GameSettings.NetworkRole}  Instance: {LogPaths.InstanceSlot}  "
                      + $"Deterministic: {GameSettings.DeterministicLockstep}  "
                      + $"Tick: {TheWaningBorder.Core.Multiplayer.LockstepTiming.TicksPerSecond} Hz  "
                      + $"Cell: {GameSettings.PathfindingCellSize}\n"
                      + $"Build fingerprint: {TheWaningBorder.Core.Multiplayer.MatchSettingsSync.Fingerprint} "
                      + $"({TheWaningBorder.Core.Multiplayer.MatchSettingsSync.BuildLabel})"
                    : ""));

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

        private static void TeardownAfterMatch()
        {
            if (!_matchLive) return;   // nothing to clean up
            _matchLive = false;

            // The pause menu freezes the clock; leaving while paused must not
            // strand the menu at timeScale 0.
            // The player's game-speed preference, not a hardcoded 1.
            GameSpeedControl.Apply();

            TheWaningBorder.Input.SelectionSystem.ClearSelection();
            TheWaningBorder.UI.HUD.GroundTargeting.Cancel();
            GameCamera.Cleanup();

            DestroyPersistent<RuntimeManagers>();   // HUD + presentation stack
            DestroyPersistent<BootstrapDriver>();

            // Entities outlive the scene too. Without this the next match
            // spawns its bases on top of the previous one's survivors.
            //
            // MUST be UniversalQuery, never DestroyAndResetAllEntities():
            // that API destroys UniversalQueryWithSystems, i.e. it also kills
            // every SYSTEM entity. Systems keep updating afterwards but the
            // components they parked on their own SystemHandle in OnCreate are
            // gone for good (OnCreate never runs again), so the world is
            // permanently broken from the first teardown onward:
            //   - Unity.Physics BuildPhysicsWorldData / SimulationSingleton →
            //     "The entity does not exist" + "GetSingleton<SimulationSingleton>()
            //     ... but there are none" thrown from Burst every frame, forever
            //   - every EntityCommandBufferSystem.Singleton (Begin/EndSimulation,
            //     BeginPresentation, …) — the ECBs the whole gameplay stack writes
            //     through (DeathSystem, ProjectileSystem, mining, …)
            // UniversalQuery is the same set MINUS system and meta-chunk entities:
            // gameplay is wiped, engine plumbing survives. The world's WorldTime
            // singleton is re-created lazily by World.TimeSingleton, so losing it
            // here is harmless.
            // ...BUT UniversalQuery is not enough on its own. Unity.Physics
            // does NOT park its singletons on a SystemHandle: BuildPhysicsWorld
            // .OnCreate calls EntityManager.CreateSingleton(PhysicsWorldSingleton)
            // and UnityPhysicsSimulationSystems.OnCreate does CreateEntity() +
            // AddComponentData(SimulationSingleton). Those are ORDINARY
            // entities, so UniversalQuery destroys them too — and since the
            // systems survive, their OnCreate never runs again and physics is
            // dead for the rest of the play session, exactly like the
            // DestroyAndResetAllEntities case above:
            //   "GetSingleton<PhysicsWorldSingleton>() ... but there are none"
            //   from BuildPhysicsWorld / Broadphase / Narrowphase /
            //   CreateJacobians / SolveAndIntegrate, every frame, forever.
            // Spare them explicitly. Our OWN singletons self-heal instead (see
            // NavRequestSchedulerSystem / SpatialHashRebuildSystem), because we
            // control those systems and third-party ones we do not.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                em.CompleteAllTrackedJobs();

                // The portal graph blob is owned by the SINGLETON, not by any
                // one system — PortalGraphBuildSystem and
                // IncrementalPortalRebuildSystem both publish into it and
                // dispose the blob they replaced. Neither can safely cache a
                // handle to free later (that stale handle threw "The
                // BlobAssetReference is not valid"), so ownership transfers
                // here: dispose it at the exact moment the entity holding it
                // is about to be destroyed.
                DisposePortalGraphBlob(em);

                // Same set as UniversalQuery (all entities incl. disabled and
                // prefabs, minus system / meta-chunk entities) minus the
                // engine singletons that can never be rebuilt.
                var wipe = new Unity.Entities.EntityQueryBuilder(Unity.Collections.Allocator.Temp)
                    .WithOptions(Unity.Entities.EntityQueryOptions.IncludePrefab
                               | Unity.Entities.EntityQueryOptions.IncludeDisabledEntities)
                    .WithNone<Unity.Physics.PhysicsWorldSingleton, Unity.Physics.SimulationSingleton>()
                    .Build(em);
                em.DestroyEntity(wipe);
            }

            // Close the per-faction AI / player log writers. They used to stay
            // open until the NEXT match re-initialised them, which left file
            // handles on the previous match's folder for as long as the player
            // sat in the menu — exactly when a tester is trying to zip it.
            TheWaningBorder.AI.AILogger.Cleanup();

            // Close the match's log folder and write its summary. Outcome is
            // whatever VictoryConditionSystem recorded; a quit mid-match
            // reports "unfinished", which is itself useful to see.
            TheWaningBorder.Core.Diagnostics.MatchLogSession.End();

            TWBLog.Log("[GameBootstrap] Match torn down — managers destroyed, "
                + "entities reset, ready to bootstrap again.");
        }

        /// <summary>
        /// Free the portal-graph blob before the wipe destroys the singleton
        /// that owns it. Queried live rather than through any cached handle,
        /// because the blob has two publishers and only the singleton knows
        /// which one is current. Safe when no graph has been built yet.
        /// </summary>
        private static void DisposePortalGraphBlob(Unity.Entities.EntityManager em)
        {
            var q = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadWrite<PortalGraphSingleton>());
            if (q.IsEmptyIgnoreFilter) return;

            var s = q.GetSingleton<PortalGraphSingleton>();
            if (s.Graph.IsCreated) s.Graph.Dispose();

            // Clear the reference so nothing can dispose it a second time in
            // the window before the entity is destroyed.
            s.Graph = default;
            s.Built = 0;
            q.SetSingleton(s);
        }

        /// <summary>Destroy every GameObject hosting a <typeparamref name="T"/>
        /// — by component, not by name, so a scene object that happens to
        /// share the name is never caught.</summary>
        private static void DestroyPersistent<T>() where T : MonoBehaviour
        {
            foreach (var found in Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (found != null) Object.Destroy(found.gameObject);
            }
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

            // TEMP DIAGNOSTIC: trace each bootstrap phase so a stalled MP launch
            // shows its exact stall point as the last "[BootTrace]" line.
            Trace("coroutine start");

            // Statics survive scene loads: a second match would otherwise
            // inherit the previous match's "populated" latch and let the
            // lockstep clock start on an empty world. The epoch bump tells
            // every stateful sim system to reset its per-match fields.
            TheWaningBorder.Core.MatchLifecycle.MapPopulated = false;
            TheWaningBorder.Core.MatchLifecycle.MatchEpoch++;

            // Same idea for periodic-system phase. In multiplayer this is only
            // the first of two bumps: the sim keeps running per-frame after
            // this point, so LockstepFixedStep.Install re-phases again at the
            // moment the deterministic clock actually starts. Here it covers
            // single-player and non-deterministic matches.
            SimCadence.BeginMatch();

            EnsureECSWorld();
            Trace("after EnsureECSWorld");

            bool isScenario = GameSettings.Mode == GameMode.Scenario;
            if (isScenario)
            {
                ScenarioSetup.PreInit();
                yield return null;
            }

            if (GameSettings.IsMultiplayer && LockstepBootstrap.Instance != null)
            {
                Trace("before InitializeLockstepNow");
                LockstepBootstrap.Instance.InitializeLockstepNow();
                Trace("after InitializeLockstepNow");
                yield return null;
            }
            else if (GameSettings.IsMultiplayer)
            {
                // A multiplayer match with no lockstep bootstrap must never
                // boot QUIETLY: nothing replicates, and every remote human's
                // faction silently falls to the AI — both players report "the
                // game ran but nothing propagated" (2026-08-17, the
                // second-match self-destructing bootstrap). If this fires,
                // the lobby start flow failed to create or keep the
                // bootstrap alive.
                Debug.LogError("[GameBootstrap] MULTIPLAYER match booting WITHOUT " +
                    "a LockstepBootstrap instance — no networking will run and " +
                    "remote players' factions fall to the AI. This boot is broken.");
            }

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Loading tech tree…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.38f);
            yield return null;
            InitializeDataSystems();
            Trace("after InitializeDataSystems");

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Setting up camera…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.40f);
            yield return null;
            GameCamera.Ensure();
            Trace("after GameCamera.Ensure");

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Spawning managers…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.42f);
            yield return null;
            CreateManagersObject();
            Trace("after CreateManagersObject");

            yield return WaitForSceneTerrain();

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Building world…");
            TheWaningBorder.UI.Menus.LoadingScreen.SetProgress(0.44f);
            yield return null;
            InitializeWorld();
            Trace("after InitializeWorld");

            // ProceduralTerrain.Start now drives 45 → 55 % via its own
            // SetStatus / SetProgress calls while its generation coroutine
            // runs. We block here until generation is complete so the
            // spawn step below has real heightmap data to read.
            Trace("waiting for terrain generation…");
            while (!TheWaningBorder.World.Terrain.ProceduralTerrain.IsGenerationComplete)
                yield return null;
            Trace("terrain generation complete");

            if (isScenario)
            {
                ScenarioSetup.SpawnScenarioEntities();
                // Scenario spawning is synchronous — the invariant "flag set
                // means the world is fully populated" holds here too.
                TheWaningBorder.Core.MatchLifecycle.MapPopulated = true;
            }
            else
            {
                InitializeFactions();
                InitializeAI();
            }
            Trace("after factions/AI");

            yield return null;
            PostInitializationSync();
            Trace("done — bootstrap complete");

            UnityEngine.Object.Destroy(driver);
        }

        // MapMagic maps ship a serialized tile whose TerrainData is generated
        // at runtime, never saved as an asset: on a fresh scene load the
        // tile's Terrain exists with terrainData == null until
        // MapMagicObject.OnEnable re-generates it over the next frames.
        // InitializeWorld sizes everything (MapHalfSize, FoW bounds,
        // PassabilityGrid) from that terrain, so wait for the data — and for
        // MapMagic to finish applying heights — before building the world.
        // MapMagic is probed via reflection so the runtime assembly never
        // references the third-party asmdef. The timeout keeps a scene with
        // a genuinely missing terrain booting into the existing "no active
        // Unity Terrain" error path instead of hanging the loading screen.
        private static System.Collections.IEnumerator WaitForSceneTerrain()
        {
            var mmType = System.Type.GetType("MapMagic.Core.MapMagicObject, MapMagic");
            var mapMagic = mmType != null
                ? UnityEngine.Object.FindFirstObjectByType(mmType)
                : null;

            bool MMGenerating()
            {
                if (mapMagic == null) return false;
                try { return (bool)mmType.GetMethod("IsGenerating").Invoke(mapMagic, null); }
                catch (System.Exception) { return false; }
            }

            // A scene saved mid-MapMagic-session ships tile Terrains whose
            // TerrainData was runtime-generated and died with the editor
            // session. MapMagic's apply stage silently skips data-less
            // terrains ("chunk removed during apply"), so heights would
            // never land on them — re-create the data the way
            // TerrainTile.CreateTerrain does, before generation starts.
            // Apply keeps the data's X/Z size ("no resize algorithm"), so
            // it must come from mapMagic.tileSize; Y is written by the
            // height output on apply.
            if (mapMagic != null)
            {
                float sx = 1000f, sz = 1000f;
                try
                {
                    object tileSize = mmType.GetField("tileSize").GetValue(mapMagic);
                    var tsType = tileSize.GetType();
                    sx = (float)tsType.GetField("x").GetValue(tileSize);
                    sz = (float)tsType.GetField("z").GetValue(tileSize);
                }
                catch (System.Exception) { }

                foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.terrainData != null) continue;
                    var td = new TerrainData { size = new Vector3(sx, 0f, sz) };
                    t.terrainData = td;
                    var col = t.GetComponent<TerrainCollider>();
                    if (col != null) col.terrainData = td;
                    Trace($"re-created TerrainData for '{t.name}' ({sx}x{sz})");
                }
            }

            bool TerrainReady()
            {
                var t = UnityEngine.Terrain.activeTerrain;
                if (t == null || t.terrainData == null) return false;
                // A freshly created TerrainData sits at the 33-vert default
                // until MapMagic's height apply lands — ready means real
                // heights are in AND MapMagic has gone idle again.
                if (mapMagic != null)
                    return t.terrainData.heightmapResolution > 33 && !MMGenerating();
                return true;
            }

            if (TerrainReady()) yield break;
            if (UnityEngine.Terrain.activeTerrain == null && mapMagic == null) yield break;

            TheWaningBorder.UI.Menus.LoadingScreen.SetStatus("Generating terrain…");
            Trace("waiting for scene terrain (MapMagic)…");

            // Give MapMagic's own OnEnable/Update a moment to start; if it
            // still thinks the serialized tile is Ready (the ready flags
            // are saved with the scene) kick generation explicitly.
            yield return null;
            yield return null;
            if (mapMagic != null && !TerrainReady() && !MMGenerating())
            {
                Trace("forcing MapMagic StartGenerate (serialized tile reported ready)");
                try
                {
                    mmType.GetMethod("StartGenerate", new[] { typeof(bool), typeof(bool) })
                          .Invoke(mapMagic, new object[] { true, true });
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[GameBootstrap] MapMagic StartGenerate failed: " + e.Message);
                }
            }

            // Wait while generation actually progresses; bail once nothing
            // has generated for a while so a scene with a genuinely broken
            // terrain still reaches the regular "no active Unity Terrain"
            // error path instead of hanging the loading screen.
            float idleSince = Time.realtimeSinceStartup;
            float hardDeadline = Time.realtimeSinceStartup + 180f;
            while (!TerrainReady() && Time.realtimeSinceStartup < hardDeadline)
            {
                if (MMGenerating()) idleSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - idleSince > 10f) break;
                yield return null;
            }
            Trace(TerrainReady() ? "scene terrain ready" : "scene terrain wait TIMED OUT");
        }

        // Tiny MonoBehaviour host for the static bootstrap coroutine. Used
        // only to give us access to StartCoroutine from a static context.
        // TEMP DIAGNOSTIC: current bootstrap phase, shown on-screen (IMGUI
        // renders even with no camera) + logged. The last value before a stall
        // pinpoints where launch dies.
        private static void Trace(string phase)
        {
            TheWaningBorder.Core.MatchLifecycle.BootPhase = phase;
            UnityEngine.Debug.Log("[BootTrace] " + phase);
        }

        private class BootstrapDriver : MonoBehaviour
        {
            void OnGUI()
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    normal = { textColor = Color.yellow },
                };
                GUI.Label(new Rect(20f, 20f, 1400f, 40f), "BOOT PHASE: " + TheWaningBorder.Core.MatchLifecycle.BootPhase, style);
            }
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
            // TechCatalog is a static service (replaces the former TechTreeDB MonoBehaviour).
            // Touching it here loads the catalog from Resources up-front; otherwise it would
            // lazy-load on first lookup. No GameObject required.
            //
            // Guarded: this runs inside the bootstrap coroutine, so a data-load failure must
            // NOT abort the rest of bootstrap (camera, managers/UI, world). If the catalog
            // fails to load, log it and continue — stats fall back and the game still starts.
            try
            {
                _ = TechCatalog.IsReady;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GameBootstrap] TechCatalog load failed (continuing bootstrap): {e}");
            }
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
            managersGO.AddComponent<TheWaningBorder.Presentation.NodeRubbleVisualSystem>(); // node rubble / rebuild visual
            managersGO.AddComponent<TheWaningBorder.Presentation.LedgerAutomationVfx>();   // Ledger ability: building aura + cast burst
            managersGO.AddComponent<SelectionSystem>();          // Click + box select
            managersGO.AddComponent<RTSInputManager>();          // Right-click command routing

            // ── FINAL GAME UI (2026-07-17) ─────────────────────────────────
            // The authored uGUI panels (Assets/GameData/Scenes/Menus/GameUI,
            // wired through Resources/GameUICatalog) are THE in-game UI. The
            // old stacks — UnifiedUIManager panel spawner, UI Toolkit jade
            // HUD, IMGUI ResourceHUD/ReligionHUD/VictoryProgressHUD/minimap/
            // ESC menu/post-game — were removed at the user's request.
            // Non-painting runtime pieces stay: BuilderCommandPanel (drives
            // building placement), FloatingHealthBars (worldspace HP bars),
            // PlayerNotificationSystem (transient feedback toasts until the
            // final UI covers messaging).
            managersGO.AddComponent<TheWaningBorder.UI.GameUI.GameUIManager>();
            managersGO.AddComponent<BuilderCommandPanel>();      // Building placement runtime (no painting)
            managersGO.AddComponent<FloatingHealthBars>();       // Worldspace HP bars
            // Floating damage/heal numbers. Lost its mount in the old-UI
            // teardown (nothing referenced it) — restored 2026-08-03; same
            // pool-on-private-canvas pattern as FloatingHealthBars.
            managersGO.AddComponent<TheWaningBorder.UI.HUD.DamageNumbersUI>();
            managersGO.AddComponent<TheWaningBorder.UI.HUD.GameClockHUD>(); // match-time readout (sim time)
            managersGO.AddComponent<TheWaningBorder.UI.HUD.StatsBoardHUD>(); // live AoE-style charts on Display 2
            managersGO.AddComponent<TheWaningBorder.Presentation.CurseBeaconVfx>(); // curse-node beacons + emergence pulses
            managersGO.AddComponent<PlayerNotificationSystem>(); // Feedback toasts
            managersGO.AddComponent<TheWaningBorder.Presentation.ChapelSiteDecals>();

            managersGO.AddComponent<FloatingIncomeDisplay>();   // BFME2-style floating income text
            managersGO.AddComponent<ProjectileVisualSystem>();   // Arrow projectile visuals
            managersGO.AddComponent<BuildingEffectSystem>();    // Construction dust + collapse effects
            managersGO.AddComponent<RitualBeamSystem>();         // Ritual broadcast beams (spec §5.1)
            managersGO.AddComponent<CaravanVisualSystem>();       // Procedural desert-traveler visual (spec refinement #3)
            managersGO.AddComponent<UnitVisualOverlaySystem>();   // Rank pips + Glow halo (spec refinement #7)
            // GathererHutGrassPainter (yellow farm-field quads around huts)
            // REMOVED entirely (2026-07-15, user request).
            managersGO.AddComponent<RallyPointDisplay>();        // Rally point marker display
            managersGO.AddComponent<MovementLineDisplay>();      // Unit movement destination lines
            managersGO.AddComponent<FormationDebugOverlay>();   // F2: leader gimbal + member spots
            managersGO.AddComponent<UnitIndicatorSystem>();     // Direction arrows + state circles
            managersGO.AddComponent<PlanningModeOverlay>();     // Planning mode overlay (Z key)
            managersGO.AddComponent<GameStatsTracker>();          // Resource/population timeline tracker (data only)
            // InGameMenuPanel / EndGameButton / PostGameStatsUI removed with
            // the old UI (2026-07-17). VictoryConditionSystem null-guards
            // their statics; the final UI will own menus and post-game.
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

            // Chromium (CEF) web HUD spawn block REMOVED entirely (2026-07-16,
            // user request) — the IMGUI / UI Toolkit HUDs added above are the
            // only in-game UI stacks now.

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
                // NEVER clear OUR OWN fixed-step driver. InitializeLockstepNow
                // installs it a few bootstrap phases BEFORE this method runs,
                // and this NetCode-defense sweep was silently wiping it —
                // every deterministic MP match then ran frame-driven from this
                // line onward while LockstepFixedStep.Active still read true
                // (the 2026-08-16 desync #5; upstream of #3/#4's veil-timing
                // symptoms). Only a FOREIGN rate manager (NetCode's) is a
                // hijack; ours is the match.
                var simGroup = world.GetExistingSystemManaged<Unity.Entities.SimulationSystemGroup>();
                if (simGroup != null
                    && !(simGroup.RateManager is TheWaningBorder.Multiplayer.LockstepFixedRateManager))
                    simGroup.RateManager = null;

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

            // Create passability grid for non-pathing queries (territorial
            // enclosure scans, spawn placement, building placement validation).
            // task-112 M4: pathing has been moved to the new flow-field
            // stack (NavGridBootstrapSystem / NavCostField / NavGridQuery);
            // PassabilityGrid stays alive for the queries that aren't
            // pathing per the architecture's section 4.5.
            // M8-followup: PassabilityGrid still owns terrain-reachability
            // BFS, geometric Minkowski-sum, and line-of-sight sampling that
            // NavGridQuery doesn't yet replicate. Full migration deferred
            // to a follow-up task; see docs/Technical_Reference_Navigation.md
            // "Future cleanup".
            var existingGrid = Object.FindFirstObjectByType<PassabilityGrid>();
            if (existingGrid == null)
            {
                var gridGO = new GameObject("PassabilityGrid");
                gridGO.AddComponent<PassabilityGrid>();
            }

            // task-112 M4: NavMeshManager GameObject spawn deleted with the
            // rest of the NavMesh stack. The new flow-field stack
            // (NavGridBootstrapSystem) is an ECS system -- no GameObject
            // bootstrap needed; it auto-instantiates with the default world.

            // Initialize fog of war if enabled. Observer matches ALSO create
            // the manager: without it every AI fog check degrades to
            // "everything visible" and the AIs play with map-wide intel from
            // second zero (no scouting needed — a silent map hack). The
            // observer's VIEW follows GameSettings.ViewFaction: the manager
            // toggles its own overlay renderer per frame (full reveal while
            // nothing is selected, the selected asset's owner otherwise),
            // and FogVisibilitySyncSystem / the minimap read the same value.
            if (GameSettings.FogOfWarEnabled)
            {
                FogOfWarManager.SetupFogOfWar();
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
        /// Reset bootstrap state. The scene-load hook now does this by itself
        /// on every exit from a gameplay scene (TeardownAfterMatch); this stays
        /// as the manual entry point for callers that tear a match down without
        /// a scene change.
        /// </summary>
        public static void Reset() => TeardownAfterMatch();
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