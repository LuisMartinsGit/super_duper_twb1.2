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
            // Wait until terrain exists and has valid data
            float timeout = 5f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                if (TerrainUtility.IsReady())
                {
                    // Pathfinding test mode: spawn test scenario instead of normal factions
                    if (GameSettings.Mode == GameMode.PathfindingTest)
                    {
                        PathfindingTestSetup.Bootstrap();
                        GameCamera.FocusOn(Vector3.zero, instant: true);
                        LoadingScreen.NotifyReady();
                        Destroy(gameObject);
                        yield break;
                    }

                    PlayerSpawnSystem.SpawnAllFactions();
                    // Flat test map skips world clutter — see GameSettings.FlatTestMap.
                    if (!GameSettings.FlatTestMap)
                        ObstacleBootstrap.SpawnObstacles();
                    IronDepositBootstrap.SpawnIronDeposits();
                    // Mineable crystal patches always spawn (1 near each player
                    // + scattered) so AI/players can age up without hunting
                    // Crystallings first. Curse main nodes respect only their
                    // own toggle now — FlatTestMap previously double-gated and
                    // silently disabled the curse in dev builds.
                    CrystalPatchBootstrap.SpawnCrystalPatches();
                    if (GameSettings.CrystalCurseEnabled)
                        CrystalNodeBootstrap.SpawnCrystalNodes();
                    FocusCameraOnHall();
                    LoadingScreen.NotifyReady();
                    Destroy(gameObject);
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Same gating in the timeout fallback path.
            PlayerSpawnSystem.SpawnAllFactions();
            if (!GameSettings.FlatTestMap)
                ObstacleBootstrap.SpawnObstacles();
            IronDepositBootstrap.SpawnIronDeposits();
            CrystalPatchBootstrap.SpawnCrystalPatches();
            if (GameSettings.CrystalCurseEnabled)
                CrystalNodeBootstrap.SpawnCrystalNodes();
            FocusCameraOnHall();
            LoadingScreen.NotifyReady();
            Destroy(gameObject);
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