// ScenarioSetup.cs
// Bootstrap for predefined combat scenarios
// Location: Assets/Scripts/Bootstrap/ScenarioSetup.cs

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
    /// Standalone bootstrap for Scenario game mode.
    /// Spawns predefined battalion layouts for testing combat scenarios.
    /// Skips economy, AI, fog-of-war.
    /// </summary>
    public static class ScenarioSetup
    {
        private const float ArmySpacing = 12f;   // space between battalions in a row
        private const float RowSpacing = 10f;     // space between rows
        private const float ArmySeparation = 60f; // distance between the two armies

        public static void Bootstrap()
        {
            Debug.Log($"[ScenarioSetup] Starting scenario: {GameSettings.ActiveScenario}");

            GameSettings.TotalPlayers = 2;
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
            var managersGO = new GameObject("ScenarioManagers");
            managersGO.AddComponent<EntityViewManager>();
            managersGO.AddComponent<PresentationSpawnSystem>();
            managersGO.AddComponent<SelectionSystem>();
            managersGO.AddComponent<RTSInputManager>();
            managersGO.AddComponent<UnifiedUIManager>();
            managersGO.AddComponent<SelectionRings>();
            managersGO.AddComponent<FloatingHealthBars>();
            managersGO.AddComponent<ResourceHUD>();
            managersGO.AddComponent<ProjectileVisualSystem>();
            managersGO.AddComponent<MovementLineDisplay>();
            managersGO.AddComponent<UnitIndicatorSystem>();
            managersGO.AddComponent<PlanningModeOverlay>();
            Object.DontDestroyOnLoad(managersGO);

            // Create terrain
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

            // Spawn scenario
            switch (GameSettings.ActiveScenario)
            {
                case ScenarioType.LargeMelee:
                    SpawnLargeMelee(em);
                    break;
                case ScenarioType.LargeRanged:
                    SpawnLargeRanged(em);
                    break;
                case ScenarioType.LargeMixed:
                    SpawnLargeMixed(em);
                    break;
                case ScenarioType.HealerTest:
                    SpawnHealerTest(em);
                    break;
            }

            // Focus camera on center
            GameCamera.FocusOn(Vector3.zero, instant: true);

            // Dismiss loading screen
            LoadingScreen.NotifyReady();

            Debug.Log($"[ScenarioSetup] Scenario '{GameSettings.ActiveScenario}' ready");
        }

        // ═══════════════════════════════════════════════════════════════
        // SCENARIO SPAWNERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 6v6 Swordsman battalions in two rows of 3.
        /// </summary>
        private static void SpawnLargeMelee(EntityManager em)
        {
            string unitId = "Swordsman";
            SpawnArmyGrid(em, unitId, unitId, Faction.Blue, 3, 2, new float3(0, 0, -ArmySeparation * 0.5f));
            SpawnArmyGrid(em, unitId, unitId, Faction.Red, 3, 2, new float3(0, 0, ArmySeparation * 0.5f));
        }

        /// <summary>
        /// 6v6 Archer battalions in two rows of 3.
        /// </summary>
        private static void SpawnLargeRanged(EntityManager em)
        {
            string unitId = "Archer";
            SpawnArmyGrid(em, unitId, unitId, Faction.Blue, 3, 2, new float3(0, 0, -ArmySeparation * 0.5f));
            SpawnArmyGrid(em, unitId, unitId, Faction.Red, 3, 2, new float3(0, 0, ArmySeparation * 0.5f));
        }

        /// <summary>
        /// 6v6 mixed: front row Swordsman, back row Archer.
        /// </summary>
        private static void SpawnLargeMixed(EntityManager em)
        {
            // Blue army: front row melee, back row ranged
            SpawnArmyRow(em, "Swordsman", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f));
            SpawnArmyRow(em, "Archer", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f - RowSpacing));

            // Red army: front row melee, back row ranged
            SpawnArmyRow(em, "Swordsman", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f));
            SpawnArmyRow(em, "Archer", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f + RowSpacing));
        }

        /// <summary>
        /// 1 Swordsman battalion with all members at 50% HP + 1 Litharch healer.
        /// </summary>
        private static void SpawnHealerTest(EntityManager em)
        {
            // Spawn a Swordsman battalion at center
            float3 battalionPos = new float3(0, 0, 0);
            battalionPos.y = TerrainUtility.GetHeight(battalionPos.x, battalionPos.z);
            Entity leader = BattalionFactory.SpawnBattalion(em, "Swordsman", battalionPos, Faction.Blue);

            // Set all members to 50% HP
            if (em.HasBuffer<BattalionMember>(leader))
            {
                var members = em.GetBuffer<BattalionMember>(leader);
                for (int i = 0; i < members.Length; i++)
                {
                    var member = members[i].Value;
                    if (em.Exists(member) && em.HasComponent<Health>(member))
                    {
                        var hp = em.GetComponentData<Health>(member);
                        hp.Value = hp.Max / 2;
                        em.SetComponentData(member, hp);
                    }
                }
            }

            // Spawn a Litharch healer nearby
            float3 healerPos = new float3(-8f, 0, 0);
            healerPos.y = TerrainUtility.GetHeight(healerPos.x, healerPos.z);
            UnitFactory.Create(em, "Litharch", healerPos, Faction.Blue);

            Debug.Log("[ScenarioSetup] Healer test: 1 battalion at 50% HP + 1 Litharch");
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Spawn a grid of battalions (cols x rows) centered on the given position.
        /// Uses frontUnitId for the first row and backUnitId for subsequent rows.
        /// </summary>
        private static void SpawnArmyGrid(EntityManager em, string frontUnitId, string backUnitId,
            Faction faction, int cols, int rows, float3 center)
        {
            for (int row = 0; row < rows; row++)
            {
                string unitId = (row == 0) ? frontUnitId : backUnitId;
                for (int col = 0; col < cols; col++)
                {
                    float x = (col - (cols - 1) * 0.5f) * ArmySpacing;
                    float z = (faction == Faction.Blue) ? -row * RowSpacing : row * RowSpacing;
                    float3 pos = center + new float3(x, 0, z);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    BattalionFactory.SpawnBattalion(em, unitId, pos, faction);
                }
            }
        }

        /// <summary>
        /// Spawn a single row of battalions centered on the given position.
        /// </summary>
        private static void SpawnArmyRow(EntityManager em, string unitId, Faction faction,
            int count, float3 center)
        {
            for (int col = 0; col < count; col++)
            {
                float x = (col - (count - 1) * 0.5f) * ArmySpacing;
                float3 pos = center + new float3(x, 0, 0);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                BattalionFactory.SpawnBattalion(em, unitId, pos, faction);
            }
        }
    }
}
