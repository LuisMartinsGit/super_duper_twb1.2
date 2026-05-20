// SpawnDelayHelper.cs
// Waits for terrain before spawning players
// Location: Assets/Scripts/Bootstrap/SpawnDelayHelper.cs

using System.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.World.Terrain;
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

            float timeout = 5f;
            float elapsed = 0f;

            while (elapsed < timeout && !TerrainUtility.IsReady())
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // PathfindingTest mode short-circuits the normal bootstrap.
            if (GameSettings.Mode == GameMode.PathfindingTest)
            {
                PathfindingTestSetup.Bootstrap();
                GameCamera.FocusOn(Vector3.zero, instant: true);
                LoadingScreen.SetProgress(1f);
                LoadingScreen.NotifyReady();
                Destroy(gameObject);
                yield break;
            }

            // Bootstrap phases — each one yields a frame so the progress
            // bar can repaint between heavy synchronous calls, and the
            // status text gives the player a sense of what's happening.

            LoadingScreen.SetStatus("Spawning factions…");
            LoadingScreen.SetProgress(0.60f);
            yield return null;
            PlayerSpawnSystem.SpawnAllFactions();

            LoadingScreen.SetStatus("Computing reachability…");
            LoadingScreen.SetProgress(0.66f);
            yield return null;
            ComputePlayerReachability();

            if (!GameSettings.FlatTestMap)
            {
                LoadingScreen.SetStatus("Placing forests & rocks…");
                LoadingScreen.SetProgress(0.72f);
                yield return null;
                ObstacleBootstrap.SpawnObstacles();
            }

            LoadingScreen.SetStatus("Placing iron deposits…");
            LoadingScreen.SetProgress(0.78f);
            yield return null;
            IronDepositBootstrap.SpawnIronDeposits();

            LoadingScreen.SetStatus("Placing crystal patches…");
            LoadingScreen.SetProgress(0.82f);
            yield return null;
            CrystalPatchBootstrap.SpawnCrystalPatches();

            if (GameSettings.CrystalCurseEnabled)
            {
                LoadingScreen.SetStatus("Seeding crystal curse…");
                LoadingScreen.SetProgress(0.85f);
                yield return null;
                CrystalNodeBootstrap.SpawnCrystalNodes();
            }

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