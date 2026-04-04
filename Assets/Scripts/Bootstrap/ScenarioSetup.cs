// ScenarioSetup.cs
// Bootstrap for predefined combat scenarios
// Location: Assets/Scripts/Bootstrap/ScenarioSetup.cs

using UnityEngine;
using Unity.Collections;
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
using TheWaningBorder.Core.Commands.Types;
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

            GameSettings.TotalPlayers = GameSettings.ActiveScenario == ScenarioType.FourWayCultures ? 4 : 2;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.IsObserver = GameSettings.ActiveScenario == ScenarioType.FourWayCultures;

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
            managersGO.AddComponent<FormationPreview>();
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
                case ScenarioType.FourWayCultures:
                    SpawnFourWayCultures(em);
                    break;
                case ScenarioType.FullArmy:
                    SpawnFullArmy(em);
                    break;
                case ScenarioType.WallSiege:
                    SpawnWallSiege(em);
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

        /// <summary>
        /// Four-way battle: Basic (Blue/south), Alanthor (Red/east), Runai (Green/north), Feraldis (Yellow/west).
        /// All armies attack-move toward center at game start.
        /// </summary>
        private static void SpawnFourWayCultures(EntityManager em)
        {
            float offset = ArmySeparation * 0.7f;
            float3 center = float3.zero;

            // Blue (south) — basic: Swordsman front, Archer back
            var blueCenter = new float3(0, 0, -offset);
            SpawnArmyRow(em, "Swordsman", Faction.Blue, 4, blueCenter);
            SpawnArmyRow(em, "Archer", Faction.Blue, 4, blueCenter + new float3(0, 0, -RowSpacing));
            AttackMoveAllBattalions(em, Faction.Blue, center);

            // Red (east) — Alanthor: Sentinel front, Crossbowman behind, Cataphract flankers
            // Fewer battalions (expensive pop 2 units) but higher quality
            var redCenter = new float3(offset, 0, 0);
            SpawnArmyRow(em, "Alanthor_Sentinel", Faction.Red, 2, redCenter);
            SpawnArmyRow(em, "Alanthor_Crossbowman", Faction.Red, 2, redCenter + new float3(RowSpacing, 0, 0));
            SpawnArmyRow(em, "Alanthor_Cataphract", Faction.Red, 2, redCenter + new float3(RowSpacing * 0.5f, 0, ArmySpacing));
            AttackMoveAllBattalions(em, Faction.Red, center);

            // Green (north) — Runai: Spearman front, Skirmisher mid, Raider (mounted archer) flanks
            var greenCenter = new float3(0, 0, offset);
            SpawnArmyRow(em, "Runai_Spearman", Faction.Green, 3, greenCenter);
            SpawnArmyRow(em, "Runai_Skirmisher", Faction.Green, 3, greenCenter + new float3(0, 0, RowSpacing));
            SpawnArmyRow(em, "Runai_Raider", Faction.Green, 2, greenCenter + new float3(0, 0, RowSpacing * 2));
            AttackMoveAllBattalions(em, Faction.Green, center);

            // Yellow (west) — Feraldis: Berserker horde front, Hunter (axe thrower) mid, WarboarRider rear
            var yellowCenter = new float3(-offset, 0, 0);
            SpawnArmyRow(em, "Berserker", Faction.Yellow, 4, yellowCenter);
            SpawnArmyRow(em, "Feraldis_Hunter", Faction.Yellow, 3, yellowCenter + new float3(-RowSpacing, 0, 0));
            SpawnArmyRow(em, "Feraldis_WarboarRider", Faction.Yellow, 2, yellowCenter + new float3(-RowSpacing * 2, 0, 0));
            AttackMoveAllBattalions(em, Faction.Yellow, center);

            Debug.Log("[ScenarioSetup] Four-way cultures: Blue(basic) vs Red(Alanthor) vs Green(Runai) vs Yellow(Feraldis)");
        }

        /// <summary>
        /// Issue attack-move toward a destination for all battalion leaders of the given faction.
        /// </summary>
        private static void AttackMoveAllBattalions(EntityManager em, Faction faction, float3 destination)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BattalionLeader>(),
                ComponentType.ReadOnly<FactionTag>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                AttackMoveCommandHelper.Execute(em, entities[i], destination);
            }
        }

        /// <summary>
        /// Full army: 3 Archer battalions, 3 Swordsman battalions, 6 Litharchs, 2 Ballistas per side.
        /// Layout: Front row = 3 Swordsman battalions, Back row = 3 Archer battalions,
        /// Litharchs spread behind archers, Ballistas on flanks behind everything.
        /// </summary>
        private static void SpawnFullArmy(EntityManager em)
        {
            foreach (var faction in new[] { Faction.Blue, Faction.Red })
            {
                float sign = (faction == Faction.Blue) ? -1f : 1f;
                float3 armyCenter = new float3(0, 0, sign * ArmySeparation * 0.5f);

                // Row 1 (front): 3 Swordsman battalions
                SpawnArmyRow(em, "Swordsman", faction, 3, armyCenter);

                // Row 2 (behind front): 3 Archer battalions
                SpawnArmyRow(em, "Archer", faction, 3, armyCenter + new float3(0, 0, sign * RowSpacing));

                // Row 3 (behind archers): 6 Litharchs spread across the line
                for (int i = 0; i < 6; i++)
                {
                    float x = (i - 2.5f) * 4f;
                    float3 pos = armyCenter + new float3(x, 0, sign * RowSpacing * 2f);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    UnitFactory.Create(em, "Litharch", pos, faction);
                }

                // Row 4 (flanks, furthest back): 2 Ballistas on left and right
                for (int i = 0; i < 2; i++)
                {
                    float x = (i == 0) ? -ArmySpacing : ArmySpacing;
                    float3 pos = armyCenter + new float3(x, 0, sign * RowSpacing * 2.5f);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    UnitFactory.Create(em, "Alanthor_Ballista", pos, faction);
                }
            }

            Debug.Log("[ScenarioSetup] Full Army: 3 Archer + 3 Swordsman battalions + 6 Litharchs + 2 Ballistas per side");
        }

        /// <summary>
        /// Wall Siege scenario: Blue has walls with gates and towers defending a position.
        /// Blue has swordsmen behind walls and ballistas on towers.
        /// Red has siege rams and swordsmen attacking the walls.
        /// Tests: wall passability, gate auto-open for friendlies, siege destruction of walls.
        /// </summary>
        private static void SpawnWallSiege(EntityManager em)
        {
            // ── Blue (defender) — south side ──
            // Wall line running east-west at z = -10, with hubs at the ends and middle
            float wallZ = -10f;
            float wallExtent = 24f; // total wall width
            int hubCount = 5; // 5 hubs across = 4 segments
            float hubSpacing = wallExtent / (hubCount - 1);

            var hubs = new Entity[hubCount];
            for (int i = 0; i < hubCount; i++)
            {
                float x = -wallExtent * 0.5f + i * hubSpacing;
                float3 pos = new float3(x, 0, wallZ);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                hubs[i] = AlanthorWall.CreateHub(em, pos, Faction.Blue);
            }

            // Connect hubs with segments (which auto-spawn wall instances)
            for (int i = 0; i < hubCount - 1; i++)
            {
                AlanthorWall.CreateSegment(em, hubs[i], hubs[i + 1], Faction.Blue);
            }

            // Upgrade center instances to gates (find instances near the center gap)
            // We'll upgrade 2 instances closest to x=0 to gates, and 2 near flanks to towers
            UpgradeWallInstancesNear(em, Faction.Blue, new float3(0, 0, wallZ), 3f,
                upgradeType: 2); // Gate at center

            UpgradeWallInstancesNear(em, Faction.Blue, new float3(-wallExtent * 0.35f, 0, wallZ), 2f,
                upgradeType: 1); // Tower on left
            UpgradeWallInstancesNear(em, Faction.Blue, new float3(wallExtent * 0.35f, 0, wallZ), 2f,
                upgradeType: 1); // Tower on right

            // Blue defenders behind the wall
            SpawnArmyRow(em, "Swordsman", Faction.Blue, 2, new float3(0, 0, wallZ - 12f));
            SpawnArmyRow(em, "Archer", Faction.Blue, 2, new float3(0, 0, wallZ - 18f));

            // 2 Ballistas behind the wall on the flanks
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0) ? -10f : 10f;
                float3 pos = new float3(x, 0, wallZ - 14f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                UnitFactory.Create(em, "Alanthor_Ballista", pos, Faction.Blue);
            }

            // ── Red (attacker) — north side, with enemy walls to show destruction ──

            // Red has a small wall section (for Blue to tear down)
            float redWallZ = 30f;
            var redHub1Pos = new float3(-8f, 0, redWallZ);
            var redHub2Pos = new float3(8f, 0, redWallZ);
            redHub1Pos.y = TerrainUtility.GetHeight(redHub1Pos.x, redHub1Pos.z);
            redHub2Pos.y = TerrainUtility.GetHeight(redHub2Pos.x, redHub2Pos.z);
            var redHub1 = AlanthorWall.CreateHub(em, redHub1Pos, Faction.Red);
            var redHub2 = AlanthorWall.CreateHub(em, redHub2Pos, Faction.Red);
            AlanthorWall.CreateSegment(em, redHub1, redHub2, Faction.Red);

            // Red attackers — siege rams + swordsmen approaching Blue's wall
            SpawnArmyRow(em, "Swordsman", Faction.Red, 3, new float3(0, 0, 15f));

            // Siege Rams aimed at the wall
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 8f;
                float3 pos = new float3(x, 0, 20f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                UnitFactory.Create(em, "Feraldis_SiegeRam", pos, Faction.Red);
            }

            // Catapults behind the attackers
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0) ? -12f : 12f;
                float3 pos = new float3(x, 0, 25f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                UnitFactory.Create(em, "Runai_Catapult", pos, Faction.Red);
            }

            Debug.Log("[ScenarioSetup] Wall Siege: Blue defended by walls (with gates+towers) vs Red siege attackers + Red walls to tear down");
        }

        /// <summary>
        /// Find wall instances near a position and instantly complete an upgrade on them.
        /// upgradeType: 1 = Tower, 2 = Gate.
        /// </summary>
        private static void UpgradeWallInstancesNear(EntityManager em, Faction faction,
            float3 searchPos, float radius, byte upgradeType)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<WallInstanceTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = query.ToComponentDataArray<Unity.Transforms.LocalTransform>(Allocator.Temp);

            float radiusSq = radius * radius;
            bool upgraded = false;

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                if (em.HasComponent<WallTowerTag>(entities[i]) || em.HasComponent<WallGateTag>(entities[i]))
                    continue;

                float distSq = math.distancesq(
                    new float2(transforms[i].Position.x, transforms[i].Position.z),
                    new float2(searchPos.x, searchPos.z));

                if (distSq > radiusSq) continue;

                // Instantly apply upgrade (skip timer)
                if (upgradeType == 1)
                {
                    em.AddComponentData(entities[i], new WallTowerTag());
                    em.AddComponentData(entities[i], new BuildingRangedAttack
                    {
                        Range = 16f,
                        Damage = 12,
                        Cooldown = 2.5f,
                        Timer = 0f,
                        MaxTargets = 1
                    });
                    em.AddComponentData(entities[i], new DamageTypeData { Value = DamageType.Ranged });
                    var hp = em.GetComponentData<Health>(entities[i]);
                    em.SetComponentData(entities[i], new Health { Value = 500, Max = 500 });
                    em.SetComponentData(entities[i], new PresentationId
                        { Id = AlanthorWall.TowerPresentationID });
                }
                else if (upgradeType == 2)
                {
                    em.AddComponentData(entities[i], new WallGateTag());
                    em.AddComponentData(entities[i], new WallGateState { IsOpen = 0, RecheckTimer = 0f });
                    em.SetComponentData(entities[i], new PresentationId
                        { Id = AlanthorWall.GatePresentationID });
                }

                upgraded = true;
                break; // Upgrade one instance per call
            }

            if (!upgraded)
                Debug.LogWarning($"[ScenarioSetup] No wall instance found near {searchPos} to upgrade");
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
