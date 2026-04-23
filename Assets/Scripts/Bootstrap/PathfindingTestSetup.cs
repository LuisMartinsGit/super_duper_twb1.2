// PathfindingTestSetup.cs
// Minimal bootstrap for BattalionTest game mode: flat terrain, 1-2 battalions, no economy/AI
// Location: Assets/Scripts/Bootstrap/PathfindingTestSetup.cs

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Presentation;
using TheWaningBorder.Input;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.Systems.Movement;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.UI.Menus;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Standalone bootstrap for BattalionTest game mode.
    /// Creates a flat terrain, spawns one player Swordsman battalion (Blue)
    /// and optionally one enemy Swordsman battalion (Red).
    /// Skips economy, AI, fog-of-war.
    /// </summary>
    public static class PathfindingTestSetup
    {
        public static void Bootstrap()
        {

            GameSettings.TotalPlayers = 1;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.IsObserver = false;

            // Ensure ECS world
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                DefaultWorldInitialization.Initialize("Default World");
                world = EntityWorld.DefaultGameObjectInjectionWorld;
            }
            var em = world.EntityManager;

            // Create managers
            var managersGO = new GameObject("BattalionTestManagers");
            managersGO.AddComponent<EntityViewManager>();
            managersGO.AddComponent<PresentationSpawnSystem>();
            managersGO.AddComponent<SelectionSystem>();
            managersGO.AddComponent<RTSInputManager>();
            managersGO.AddComponent<UnifiedUIManager>();
            managersGO.AddComponent<SelectionRings>();
            managersGO.AddComponent<FloatingHealthBars>();
            managersGO.AddComponent<ResourceHUD>();
            managersGO.AddComponent<UnitIndicatorSystem>();
            managersGO.AddComponent<PlanningModeOverlay>();
            managersGO.AddComponent<FormationPreview>();
            Object.DontDestroyOnLoad(managersGO);

            // Create terrain if missing
            if (Object.FindFirstObjectByType<ProceduralTerrain>() == null)
            {
                var terrainGO = new GameObject("ProceduralTerrain");
                terrainGO.AddComponent<ProceduralTerrain>();
            }

            // Create passability grid
            if (Object.FindFirstObjectByType<PassabilityGrid>() == null)
            {
                var gridGO = new GameObject("PassabilityGrid");
                gridGO.AddComponent<PassabilityGrid>();
            }

            // Create flow field manager
            if (Object.FindFirstObjectByType<FlowFieldManager>() == null)
            {
                var ffmGO = new GameObject("FlowFieldManager");
                ffmGO.AddComponent<FlowFieldManager>();
            }

            // Initialize camera
            GameCamera.Ensure();

            // Spawn player battalion (Blue) at center
            float3 playerPos = new float3(0f, 0f, 0f);
            playerPos.y = TerrainUtility.GetHeight(playerPos.x, playerPos.z);
            Entity playerLeader = BattalionFactory.SpawnBattalion(em, "Swordsman", playerPos, Faction.Blue);

            // Spawn enemy battalion (Red) offset
            float3 enemyPos = new float3(30f, 0f, 30f);
            enemyPos.y = TerrainUtility.GetHeight(enemyPos.x, enemyPos.z);
            Entity enemyLeader = BattalionFactory.SpawnBattalion(em, "Swordsman", enemyPos, Faction.Red);

            // Focus camera on player battalion
            GameCamera.FocusOn(new Vector3(playerPos.x, playerPos.y, playerPos.z), instant: true);

            // Dismiss loading screen
            LoadingScreen.NotifyReady();

        }
    }
}
