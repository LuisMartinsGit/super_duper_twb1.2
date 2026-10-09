// SpawnDelayHelper.cs
// Waits for terrain before spawning players

using System.Collections;
using TheWaningBorder.Core;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.CameraRig;

namespace TheWaningBorder.Bootstrap
{
    public class SpawnDelayHelper : MonoBehaviour
    {
        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_HallTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagLocalTransform;

        #endregion

        public IEnumerator WaitForTerrainAndSpawn()
        {
            // Wait until terrain exists and has valid data. The LoadingScreen
            // is at ~55 % when we get called; while we wait the bar holds
            // and the status reads "Waiting for terrain…".
            LoadingScreen.SetStatus("Waiting for terrain…");
            LoadingScreen.SetProgress(0.55f);

            // 120 s, aligned with the lockstep world-gate bail-out — this used
            // to be a 5 s timeout that PROCEEDED on expiry, so a slow peer
            // spawned everything onto an unfinished heightmap: every position
            // and passability cell differed from the peer that had waited.
            // Spawning onto wrong terrain is never better than spawning late.
            float timeout = 120f;
            float elapsed = 0f;

            while (elapsed < timeout && !TerrainUtility.IsReady())
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!TerrainUtility.IsReady())
                Debug.LogError("[SpawnDelayHelper] Terrain still not ready after 120s — " +
                               "spawning anyway; positions may be wrong for the whole match.");

            // Bootstrap phases — each one yields a frame so the progress
            // bar can repaint between heavy synchronous calls, and the
            // status text gives the player a sense of what's happening.

            // Scan the active scene for design-time spawn markers (player
            // starts, iron / veilstone patches, border nodes). Each spawn
            // bootstrap below checks the registry and uses the marker list
            // when present, otherwise falls back to its procedural path.
            MapMarkerRegistry.Refresh();
            TheWaningBorder.World.Regions.RegionMap.BuildFromMarkers();

            LoadingScreen.SetStatus("Spawning factions…");
            LoadingScreen.SetProgress(0.60f);
            yield return null;
            PlayerSpawnSystem.SpawnAllFactions();

            // Apply the lobby's Start Age selection: pre-promote every
            // faction to Alanthor at the chosen Hall/Temple level + one
            // random choice building + scaled resource stockpile. No-op
            // when StartAge == Age0. Runs after the Halls exist but
            // before AIBootstrap creates brains, so AI factions see the
            // promoted state on their first tick.
            StartAgePromoter.PromoteAllFactions();

            // THE START CLEARING (docs/Design/Territory_Claims.md §11): the
            // ground around every start loses its NoWalk paint and trees, the
            // passability cells under them reopen, and from here on no
            // resource node (nor the curse node on one) may stand there.
            // BEFORE the nature regions, reachability and every node spawn,
            // all of which read it; same sorted starts on every peer.
            TheWaningBorder.World.Terrain.StartClearing.Apply(StartPositions());

            // Nature regions (forests / rock fields) become impassable BEFORE
            // reachability is computed, or the reachability pass would route
            // players through stands that are about to be walls.
            LoadingScreen.SetStatus("Raising nature regions…");
            LoadingScreen.SetProgress(0.64f);
            yield return null;
            TheWaningBorder.World.MapMarkers.NatureRegionBootstrap.BlockNatureRegions();

            // Each player starts holding their own region (Regions.md §2), so
            // each player starts having SEEN it. Runs here because it needs the
            // partition (built above) and the fog manager (built in
            // GameBootstrap), and it must land before the player looks at the
            // map rather than after the first frame.
            TheWaningBorder.World.Regions.RegionStartReveal.RevealHomeRegions(
                PlayerSpawnSystem.SpawnPositions);

            LoadingScreen.SetStatus("Computing reachability…");
            LoadingScreen.SetProgress(0.66f);
            yield return null;
            ComputePlayerReachability();

            // TERRITORY TYPES DECIDE THE NODES (docs/Design/Territory_Claims.md
            // §9, 2026-10-01): every territory is Start / Normal / Normal+iron /
            // Normal+veilstone / Empty / Veilstone rich / Iron rich / Sanctum,
            // and its nodes are generated from that — the scene's node markers
            // are ignored. A map with no territories keeps the marker path.
            bool typedTerritories = TheWaningBorder.World.Regions.RegionMap.Ready
                                    && TheWaningBorder.World.Regions.RegionMap.Count > 0;
            if (typedTerritories)
            {
                LoadingScreen.SetStatus("Laying out territory resources…");
                LoadingScreen.SetProgress(0.78f);
                yield return null;
                var tw = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                var starts = StartPositions();
                TheWaningBorder.World.Regions.TerritoryResources.Resolve(
                    starts, (uint)(GameSettings.SpawnSeed ^ 0x7E4417u));
                if (tw != null && tw.IsCreated)
                    TheWaningBorder.World.Regions.TerritoryResources.Spawn(tw.EntityManager, starts,
                        (uint)(GameSettings.SpawnSeed ^ 0x40DE5u));
            }
            else
            {
            LoadingScreen.SetStatus("Placing iron deposits…");
            LoadingScreen.SetProgress(0.78f);
            yield return null;
            IronDepositBootstrap.SpawnIronDeposits();

            LoadingScreen.SetStatus("Placing veilstone patches…");
            LoadingScreen.SetProgress(0.82f);
            yield return null;
            VeilstoneOutcroppingBootstrap.SpawnVeilstoneOutcroppings();
            // NO VEILSTEEL DEPOSITS (docs/Design/Veilstone_Economy.md,
            // 2026-10-01): veilsteel is made — at an Alanthor Trading Outpost,
            // a Runai Sanctuary — never mined. Authored VeilsteelDepositMarkers
            // still in five map scenes are inert; strip them on the next
            // re-bake.

            // Veilstone coverage (Regions.md §3, 2026-08-31): every starter
            // territory carries veilstone, and half of ALL territories do —
            // veilstone is the army economy AND the ground the curse can
            // conquer. BEFORE the iron pass, so a region that just gained
            // veilstone no longer needs the iron fallback.
            ResourceNodeCoverage.GuaranteeVeilstoneCoverage();

            // Node-quota guarantee (Regions.md §4): every territory gets at
            // least one ore node. AFTER all three ore bootstraps, so authored
            // markers, fallbacks and the veilsteel coverage are on the ground
            // and only a genuine shortfall is filled.
            ResourceNodeCoverage.GuaranteeTerritoryOre();

            // Supply nodes last of the resources: their per-territory top-up
            // (2 per territory, 4 per home) is laid out per TERRITORY, so it
            // needs the partition (built above) rather than the marker list
            // alone. docs/Design/Regions.md §4.
            SupplyNodeBootstrap.SpawnSupplyNodes();
            }

            if (GameSettings.BorderEnabled)
            {
                LoadingScreen.SetStatus("Seeding veilstone border…");
                LoadingScreen.SetProgress(0.85f);
                yield return null;
                // NO WELLS (docs/Design/Territory_Claims.md §6.4, 2026-09-29):
                // the curse starts from nodes raised on random resource nodes,
                // fair by distance to every player, never in a start
                // territory. Blight pockets are retired with the wells — they
                // put curse nodes beside a player's spawn, which the new model
                // forbids.
                BorderNodeBootstrap.PrepareCurseFaction();
                SeedCurseNodes();
            }

            // Last sim-entity spawn is above this line. Prewarm below creates
            // only presentation GameObjects (no NetworkIds), so the lockstep
            // clock may start while shaders warm.
            TheWaningBorder.Core.MatchLifecycle.MapPopulated = true;

            FocusCameraOnHall();

            LoadingScreen.SetStatus("Warming shader variants…");
            LoadingScreen.SetProgress(0.88f);
            yield return null;
            // BuildingPrefabPrewarm covers 88 → 99 % via its own SetProgress
            // calls (it knows its own prefab-list length).
            yield return StartCoroutine(BuildingPrefabPrewarm.PrewarmAll());

            LoadingScreen.SetProgress(1f);
            LoadingScreen.SetStatus("Ready");
            yield return null;
            LoadingScreen.NotifyReady();
            Destroy(gameObject);
        }

        /// <summary>
        /// Collect every Hall's world position and hand it to PassabilityGrid
        /// so it can precompute the connected region every player shares.
        /// Resource bootstraps then place deposits only inside that region.
        /// </summary>
        /// <summary>
        /// Raise the initial curse nodes (Territory_Claims.md §6.4): one per
        /// player unless BorderSettings.initialNodes says otherwise, fair by
        /// distance from every start, seeded from the match seed so every
        /// lockstep peer raises the same nodes.
        /// </summary>
        private static void SeedCurseNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var starts = new System.Collections.Generic.List<Unity.Mathematics.float3>();
            var factions = new System.Collections.Generic.List<Faction>(PlayerSpawnSystem.SpawnPositions.Keys);
            factions.Sort();
            foreach (var f in factions)
            {
                var p = PlayerSpawnSystem.SpawnPositions[f];
                starts.Add(new Unity.Mathematics.float3(p.x, p.y, p.z));
            }

            // VEILSTONE-RICH TERRITORIES START CURSED (Territory_Claims.md §9):
            // the curse rises on every outcrop there. Only a map with none
            // falls back to the fair random draw.
            if (TheWaningBorder.Systems.Border.CurseNodeSeeding.CurseVeilstoneRich(world.EntityManager) > 0)
                return;

            var settings = TheWaningBorder.Data.Border.BorderSettings.Get();
            int count = settings != null && settings.initialNodes > 0
                ? settings.initialNodes
                : Mathf.Max(1, starts.Count);
            TheWaningBorder.Systems.Border.CurseNodeSeeding.SeedInitialNodes(
                world.EntityManager, starts, count, (uint)(GameSettings.SpawnSeed ^ 0x5EEDC0DE));
        }

        // ═══════════════════════════════════════════════════════════════
        // SAVED GAME: restore the snapshot instead of spawning the match
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// The load half of a saved game (docs/Design/Replays_And_Saves.md §3).
        /// Runs where <see cref="WaitForTerrainAndSpawn"/> would: the map is
        /// booted, nothing is spawned, and the snapshot's entities, statics and
        /// system fields take the place of the starting match. Only map-derived
        /// state is rebuilt here — the start clearing, nature blocks,
        /// reachability, territory types and the resource nodes' passability.
        /// </summary>
        public IEnumerator RestoreSnapshot(TheWaningBorder.Core.Save.SaveGameFile save)
        {
            LoadingScreen.SetStatus("Waiting for terrain…");
            LoadingScreen.SetProgress(0.55f);
            float waited = 0f;
            while (waited < 120f && !TerrainUtility.IsReady())
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            MapMarkerRegistry.Refresh();
            TheWaningBorder.World.Regions.RegionMap.BuildFromMarkers();

            LoadingScreen.SetStatus("Restoring the saved match…");
            LoadingScreen.SetProgress(0.62f);
            yield return null;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            int moved = TheWaningBorder.Core.Save.WorldSnapshot.Restore(em, save.World);

            // Entity handles cached from before the move point at nothing now.
            FactionEconomy.ClearCache();
            FactionResourcesHelper.ClearCache();
            PopulationHelper.ClearCache();

            TheWaningBorder.Core.Save.SnapshotState.Restore(world, save.State, out string stateReport);

            // The world clock every absolute timestamp in the components is
            // measured against, and the orders already in flight at the save.
            if (TheWaningBorder.Multiplayer.LockstepFixedStep.RateManager != null)
                TheWaningBorder.Multiplayer.LockstepFixedStep.RateManager.Elapsed = save.WorldElapsed;
            TheWaningBorder.Multiplayer.LockstepManager.Instance?.InjectPendingCommands(save.Pending);

            // ShardrootState carries its own epoch stamp IN the component: a
            // stale one makes ShardrootSystem reset the artefact on tick one.
            var shardQ = em.CreateEntityQuery(typeof(ShardrootState));
            using (var shards = shardQ.ToEntityArray(Allocator.Temp))
                foreach (var e in shards)
                {
                    var st = em.GetComponentData<ShardrootState>(e);
                    if (st.MatchEpoch != 0) { st.MatchEpoch = SimCadence.Epoch; em.SetComponentData(e, st); }
                }
            shardQ.Dispose();

            LoadingScreen.SetProgress(0.70f);
            yield return null;

            // Map-derived state, from the SAVED start positions (restored by the
            // "spawn" snapshot section) — the starts, not today's Fortresses.
            var starts = StartPositions();
            TheWaningBorder.World.Terrain.StartClearing.Apply(starts);
            TheWaningBorder.World.MapMarkers.NatureRegionBootstrap.BlockNatureRegions();
            BlockRestoredObstacles(em);
            ComputePlayerReachability(starts);
            if (TheWaningBorder.World.Regions.RegionMap.Ready && TheWaningBorder.World.Regions.RegionMap.Count > 0)
                TheWaningBorder.World.Regions.TerritoryResources.Resolve(
                    starts, (uint)(GameSettings.SpawnSeed ^ 0x7E4417u));

            TheWaningBorder.Core.MatchLifecycle.MapPopulated = true;
            Debug.Log($"[SaveLoad] Restored tick {save.Tick} ({save.Seconds:0}s): {moved} entities, " +
                      $"{stateReport}, {save.Pending.Count} pending command(s), {clock.ElapsedMilliseconds} ms.");

            FocusCameraOnHall();
            LoadingScreen.SetStatus("Warming shader variants…");
            LoadingScreen.SetProgress(0.88f);
            yield return null;
            yield return StartCoroutine(BuildingPrefabPrewarm.PrewarmAll());

            TheWaningBorder.Core.Replay.ReplaySession.SnapshotRestored();
            LoadingScreen.SetProgress(1f);
            LoadingScreen.SetStatus("Ready");
            yield return null;
            LoadingScreen.NotifyReady();
            Destroy(gameObject);
        }

        /// <summary>
        /// Resource nodes block passability from their factories at creation —
        /// which a restored node never went through. Re-block every one.
        /// </summary>
        private static void BlockRestoredObstacles(EntityManager em)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;
            var q = em.CreateEntityQuery(typeof(ObstacleTag), typeof(LocalTransform));
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();
            foreach (var xf in xfs) grid.BlockObstacle(xf.Position, BuildGrid.ResourceNodeBlockRadius);
        }

        /// <summary>The players' start positions, sorted by faction.</summary>
        private static System.Collections.Generic.List<Unity.Mathematics.float3> StartPositions()
        {
            var starts = new System.Collections.Generic.List<Unity.Mathematics.float3>();
            var factions = new System.Collections.Generic.List<Faction>(PlayerSpawnSystem.SpawnPositions.Keys);
            factions.Sort();
            foreach (var f in factions)
            {
                var p = PlayerSpawnSystem.SpawnPositions[f];
                starts.Add(new Unity.Mathematics.float3(p.x, p.y, p.z));
            }
            return starts;
        }

        /// <summary>Reachability from explicit positions — a restored game
        /// uses the saved starts, not whatever Halls stand today.</summary>
        private static void ComputePlayerReachability(System.Collections.Generic.List<Unity.Mathematics.float3> starts)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null || starts == null || starts.Count == 0) return;
            grid.ComputePlayerReachability(starts.ToArray());
        }

        private static void ComputePlayerReachability()
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var query = QC_HallTagLocalTransform.Get(em, QT_HallTagLocalTransform);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            if (transforms.Length == 0) return;

            var positions = new Unity.Mathematics.float3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
                positions[i] = transforms[i].Position;

            grid.ComputePlayerReachability(positions);
        }

        /// <summary>
        /// Find the local player's Hall and center the camera on it.
        /// </summary>
        private static void FocusCameraOnHall()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            var query = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction)
                {
                    var pos = transforms[i].Position;
                    CameraController.FocusOn(new Vector3(pos.x, pos.y, pos.z), instant: true);
                    return;
                }
            }

        }
    }
}