// SpawnDelayHelper.cs
// Waits for terrain before spawning players
// Location: Assets/Scripts/Bootstrap/SpawnDelayHelper.cs

using System.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.Bootstrap
{
    public class SpawnDelayHelper : MonoBehaviour
    {

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

            LoadingScreen.SetStatus("Computing reachability…");
            LoadingScreen.SetProgress(0.66f);
            yield return null;
            ComputePlayerReachability();

            // Forests / rocks on hand-authored maps come from the scene's own
            // Unity Terrain vegetation, not a procedural scatter pass.

            LoadingScreen.SetStatus("Placing iron deposits…");
            LoadingScreen.SetProgress(0.78f);
            yield return null;
            IronDepositBootstrap.SpawnIronDeposits();

            LoadingScreen.SetStatus("Placing veilstone patches…");
            LoadingScreen.SetProgress(0.82f);
            yield return null;
            VeilstoneOutcroppingBootstrap.SpawnVeilstoneOutcroppings();
            VeilsteelDepositBootstrap.SpawnVeilsteelDeposits();

            if (GameSettings.BorderEnabled)
            {
                LoadingScreen.SetStatus("Seeding veilstone border…");
                LoadingScreen.SetProgress(0.85f);
                yield return null;
                BorderNodeBootstrap.SpawnBorderNodes();
                // §2.5b Age 0 blight pockets — near-spawn haze patches with
                // SmallNode anchors. Needs the Halls (spawned above) and only
                // makes sense with the curse on; the VeilField itself
                // initialises later and BlightPocketSystem seeds the discs
                // as soon as it exists.
                BlightPocketBootstrap.SpawnBlightPockets();
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
        private static void ComputePlayerReachability()
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
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

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction)
                {
                    var pos = transforms[i].Position;
                    GameCamera.FocusOn(new Vector3(pos.x, pos.y, pos.z), instant: true);
                    return;
                }
            }

        }
    }
}