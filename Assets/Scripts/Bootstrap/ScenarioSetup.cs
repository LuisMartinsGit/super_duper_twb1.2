// ScenarioSetup.cs
// Bootstrap for predefined combat scenarios
// Location: Assets/Scripts/Bootstrap/ScenarioSetup.cs

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Presentation;
using TheWaningBorder.Input;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.Systems.Movement;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;
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

        /// <summary>
        /// Camera focus target for the active scenario. Defaults to world
        /// origin; scenarios that build their layout away from origin (e.g.
        /// anchored to a player start) override it so the post-spawn FocusOn
        /// frames their content instead of empty terrain.
        /// </summary>
        private static float3 _scenarioFocus = float3.zero;

        /// <summary>
        /// Set scenario-specific GameSettings BEFORE the main world init runs
        /// (so fog-of-war state, player count, observer flag are correct when
        /// GameBootstrap.InitializeWorld and InitializeAI run).
        /// </summary>
        public static void PreInit()
        {
            bool fourPlayer =
                GameSettings.ActiveScenario == ScenarioType.FourWayCultures ||
                GameSettings.ActiveScenario == ScenarioType.BuildingShowcase;
            GameSettings.TotalPlayers = fourPlayer ? 4 : 2;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.IsObserver = fourPlayer;
        }

        /// <summary>
        /// Place the scenario's predefined entities. Called by GameBootstrap
        /// AFTER the world / managers / terrain have been set up the same way
        /// they are for skirmish — only the unit/building placement differs.
        /// </summary>
        public static void SpawnScenarioEntities()
        {
            // Default camera target; a spawner may override (see _scenarioFocus).
            _scenarioFocus = float3.zero;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[ScenarioSetup] No ECS world — bootstrap order is wrong");
                return;
            }
            var em = world.EntityManager;

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
                case ScenarioType.SectShowcase:
                    SpawnSectShowcase(em);
                    break;
                case ScenarioType.BuildingShowcase:
                    SpawnBuildingShowcase(em);
                    break;
                case ScenarioType.BorderCombatTest:
                    SpawnBorderCombatTest(em);
                    break;
                case ScenarioType.PatrolDefense:
                    SpawnPatrolDefense(em);
                    break;
                case ScenarioType.AlanthorVsBorder:
                    SpawnAlanthorVsBorder(em);
                    break;
                case ScenarioType.Phase1Test:
                    Phase1TestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.Phase2Test:
                    Phase2TestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.Phase3Test:
                    Phase3TestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.Phase4Test:
                    Phase4TestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.Phase5Test:
                    Phase5TestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.Phase7Test:
                    Phase7TestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.WallClimbTest:
                    WallClimbTestSetup.SpawnScenarioEntities(em);
                    break;
                case ScenarioType.LongbowmanShowcase:
                    SpawnLongbowmanShowcase(em);
                    break;
                case ScenarioType.LongbowmanBattle:
                    SpawnLongbowmanBattle(em);
                    break;
                case ScenarioType.BuildingDamageTest:
                    SpawnBuildingDamageTest(em);
                    break;
                case ScenarioType.BuildingDamageShowcase:
                    SpawnBuildingDamageShowcase(em);
                    break;
                case ScenarioType.GuildDefenseTest:
                    SpawnGuildDefenseTest(em);
                    break;
                case ScenarioType.SpellShowcase:
                    SpawnSpellShowcase(em);
                    break;
                case ScenarioType.HutEvolution:
                    SpawnHutEvolution(em);
                    break;
            }

            // Re-center the entire scenario onto player 1's designed start.
            // Scenarios author their layout around world origin; on hand-authored
            // maps that origin can be unplayable (e.g. underwater), so shift
            // everything the scenario produced — entity transforms, their pre-set
            // move/guard targets, and the runtime spawners — onto the player-1
            // start, then point the camera there.
            float3 origin = GetPlayer1StartPosition();
            if (math.lengthsq(new float2(origin.x, origin.z)) > 0.01f)
            {
                RecenterScenario(em, origin.x, origin.z);
                _scenarioFocus = origin;
            }

            GameCamera.FocusOn(
                new Vector3(_scenarioFocus.x, _scenarioFocus.y, _scenarioFocus.z),
                instant: true);
            LoadingScreen.NotifyReady();
        }

        /// <summary>
        /// Legacy entry point — kept for API compatibility. Delegates to the
        /// new split. GameBootstrap.OnSceneLoadedHandler now uses the split
        /// directly so scenarios go through the same init flow as skirmish.
        /// </summary>
        public static void Bootstrap()
        {
            PreInit();
            SpawnScenarioEntities();
        }

        // ═══════════════════════════════════════════════════════════════
        // SCENARIO SPAWNERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 6v6 Swordsman battalions in two rows of 3.
        /// </summary>
        private static void SpawnLargeMelee(EntityManager em)
        {
            string unitId = "Spearman";
            SpawnArmyGrid(em, unitId, unitId, Faction.Blue, 3, 2, new float3(0, 0, -ArmySeparation * 0.5f));
            SpawnArmyGrid(em, unitId, unitId, Faction.Red, 3, 2, new float3(0, 0, ArmySeparation * 0.5f));

            // Longbowman support line behind each melee block.
            SpawnArmyRow(em, "Longbowman", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f - RowSpacing * 2f));
            SpawnArmyRow(em, "Longbowman", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f + RowSpacing * 2f));
        }

        /// <summary>
        /// 6v6 Archer battalions in two rows of 3.
        /// </summary>
        private static void SpawnLargeRanged(EntityManager em)
        {
            string unitId = "Archer";
            SpawnArmyGrid(em, unitId, unitId, Faction.Blue, 3, 2, new float3(0, 0, -ArmySeparation * 0.5f));
            SpawnArmyGrid(em, unitId, unitId, Faction.Red, 3, 2, new float3(0, 0, ArmySeparation * 0.5f));

            // Longbowman line behind each archer block (longer range than Archer).
            SpawnArmyRow(em, "Longbowman", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f - RowSpacing * 2f));
            SpawnArmyRow(em, "Longbowman", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f + RowSpacing * 2f));
        }

        /// <summary>
        /// 6v6 mixed: front row Swordsman, back row Archer.
        /// </summary>
        private static void SpawnLargeMixed(EntityManager em)
        {
            // Blue army: front row melee, back row ranged
            SpawnArmyRow(em, "Spearman", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f));
            SpawnArmyRow(em, "Archer", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f - RowSpacing));
            SpawnArmyRow(em, "Longbowman", Faction.Blue, 3, new float3(0, 0, -ArmySeparation * 0.5f - RowSpacing * 2f));

            // Red army: front row melee, mid row archers, back row longbowmen
            SpawnArmyRow(em, "Spearman", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f));
            SpawnArmyRow(em, "Archer", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f + RowSpacing));
            SpawnArmyRow(em, "Longbowman", Faction.Red, 3, new float3(0, 0, ArmySeparation * 0.5f + RowSpacing * 2f));
        }

        /// <summary>
        /// 1 Swordsman battalion with all members at 50% HP + 1 Litharch healer.
        /// </summary>
        private static void SpawnHealerTest(EntityManager em)
        {
            // Spawn a cluster of Swordsmen at center, each at 50% HP, for the
            // Litharch to heal.
            for (int i = 0; i < 9; i++)
            {
                float3 pos = new float3((i % 3) * 1.5f - 1.5f, 0, (i / 3) * 1.5f - 1.5f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                Entity unit = UnitFactory.Create(em, "Spearman", pos, Faction.Blue);
                if (em.HasComponent<Health>(unit))
                {
                    var hp = em.GetComponentData<Health>(unit);
                    hp.Value = hp.Max / 2;
                    em.SetComponentData(unit, hp);
                }
            }

            // Spawn a Litharch healer nearby
            float3 healerPos = new float3(-8f, 0, 0);
            healerPos.y = TerrainUtility.GetHeight(healerPos.x, healerPos.z);
            UnitFactory.Create(em, "Litharch", healerPos, Faction.Blue);

        }

        /// <summary>
        /// Guild defense test: a fully-upgraded Alanthor "Guild" (Gatherer's Hut
        /// at L3 with the whole Survey + reinforcement research line) at the
        /// centre, pre-damaged, swarmed by a Red group ordered to attack-move
        /// onto it. Once the swarm chews the Guild below 50% HP it fires its
        /// Veilsteel-Pylons Stop burst (the fully-upgraded tier) on everyone
        /// inside its gather radius, then goes on a 90 s cooldown while
        /// auto-repair (Iron reinforcements) ticks between hits. Lets the
        /// Slow/Stop cast trigger + VFX be reviewed live.
        /// </summary>
        /// <summary>
        /// Hut Evolution showcase (win conditions are already off for every
        /// scenario — VictoryConditionSystem skips GameMode.Scenario):
        /// a Gatherer's Hut self-constructs with NO workers over 10 s
        /// (numbered Lv0 rise), then every 5 s the driver plays the next
        /// step of the full Guild evolution — level dissolves AND each tech
        /// visual (Iron Reinforcements, survey tiers, Veilstone Walls,
        /// Veilsteel Surveying) — while the camera orbits the building at
        /// half the RTS distance. See HutEvolutionDriver for the step list.
        /// The faction culture is deliberately left None so the finished hut
        /// stays on Lv0 until the driver plays each transition (and so no
        /// culture-reactive systems fire); the driver switches the visual
        /// variants directly.
        /// </summary>
        private static void SpawnHutEvolution(EntityManager em)
        {
            var faction = Faction.Blue;

            float3 hutPos = new float3(0f, 0f, 0f);
            hutPos.y = TerrainUtility.GetHeight(hutPos.x, hutPos.z);
            Entity hut = TheWaningBorder.Entities.GatherersHut.Create(em, hutPos, faction);
            if (hut == Entity.Null) return;

            // Worker-less construction: the auto-construct tag makes
            // BuildingConstructionSystem/AutoConstructionSystem advance the
            // site at 1.0 progress/s with zero builders.
            var uc = new UnderConstruction { Progress = 0f, Total = 10f };
            if (em.HasComponent<UnderConstruction>(hut)) em.SetComponentData(hut, uc);
            else em.AddComponentData(hut, uc);
            if (!em.HasComponent<AutoConstructTag>(hut))
                em.AddComponent<AutoConstructTag>(hut);
            if (em.HasComponent<Health>(hut))
            {
                var hp = em.GetComponentData<Health>(hut);
                em.SetComponentData(hut, new Health { Value = 1, Max = hp.Max });
            }

            var driverGo = new GameObject("HutEvolutionDriver");
            var driver = driverGo.AddComponent<TheWaningBorder.Presentation.HutEvolutionDriver>();
            driver.Configure(hut, Cultures.Alanthor, upgradeInterval: 5f);

            _scenarioFocus = hutPos;
        }

        private static void SpawnGuildDefenseTest(EntityManager em)
        {
            var faction = Faction.Blue;

            // Mark Blue as having researched the whole Guild line so the hut's
            // income + reinforcement systems are fully active (read live from
            // FactionResearchState). Deliberately NOT setting the faction
            // culture — that would trip the Alanthor hut age-up / self-destruct
            // path; the Guild powers only need the research flags + level.
            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null)
            {
                // Walls (Slow ward) but NOT Pylons — so this scenario fires the
                // SLOW cast (AuraCircling power-up + AuraSimple aura + per-enemy
                // AuraSlowdown). Add "VeilsteelPylons" back to test the Stop ward.
                string[] guildTechs =
                {
                    "IronSurveying1", "IronSurveying2", "IronSurveying3",
                    "VeilstoneSurvey1", "VeilstoneSurvey2", "VeilsteelSurvey",
                    "IronReinforcements", "VeilstoneWalls",
                };
                foreach (var tech in guildTechs)
                    research.CompleteResearch(faction, tech);
            }

            // The Guild itself, fully built at the origin.
            float3 hutPos = new float3(0f, 0f, 0f);
            hutPos.y = TerrainUtility.GetHeight(hutPos.x, hutPos.z);
            Entity guild = TheWaningBorder.Entities.GatherersHut.Create(em, hutPos, faction);

            if (guild != Entity.Null)
            {
                // Fully upgrade to Guild L3 via the same path the in-game
                // upgrade uses (capture base stats, then apply each level).
                if (!em.HasComponent<BuildingUpgradeState>(guild))
                {
                    int baseHp = em.HasComponent<Health>(guild)
                        ? em.GetComponentData<Health>(guild).Max : 0;
                    em.AddComponentData(guild, new BuildingUpgradeState
                    {
                        Level = 0,
                        BaseHpMax = baseHp,
                        BaseAttackCooldown = 0f,
                        BasePopulationProvider = 0,
                    });
                }
                for (byte lvl = 1; lvl <= 3; lvl++)
                    TheWaningBorder.Systems.Buildings.BuildingUpgradeSystem.ApplyLevel(em, guild, lvl);

                // Start it damaged so the swarm pushes it under 50% quickly and
                // the Stop burst fires early in the fight.
                if (em.HasComponent<Health>(guild))
                {
                    var hp = em.GetComponentData<Health>(guild);
                    hp.Value = (int)(hp.Max * 0.6f);
                    em.SetComponentData(guild, hp);
                }
            }

            // Seven SINGLE Red units (4 Swordsman + 3 Longbowman) — spawned via
            // UnitFactory.Create, not SpawnArmyRow/SpawnBattalion, so each is one
            // individual unit rather than a full battalion. Fanned across an arc
            // just outside the gather radius (19.5) and each ordered to
            // attack-move onto the Guild. Kept small so it wears the Guild below
            // 50% HP gradually — a bigger force deletes it inside a single 0.5 s
            // tick before the defensive cast can fire.
            const float ring = 24f;
            string[] attackers = { "Spearman", "Spearman", "Spearman", "Spearman", "Longbowman", "Longbowman", "Longbowman" };
            for (int i = 0; i < attackers.Length; i++)
            {
                float t = attackers.Length > 1 ? (float)i / (attackers.Length - 1) : 0.5f;
                float ang = math.lerp(-math.PI * 0.5f, math.PI * 0.5f, t); // 180° arc
                float3 pos = new float3(math.sin(ang) * ring, 0f, math.cos(ang) * ring);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                Entity attacker = UnitFactory.Create(em, attackers[i], pos, Faction.Red);
                if (attacker != Entity.Null)
                    AttackMoveCommandHelper.Execute(em, attacker, hutPos);
            }

            _scenarioFocus = hutPos;
        }

        /// <summary>
        /// Spell VFX showcase: a flat, textureless plane laid over the map's
        /// hidden terrain, on which every spell prefab (Resources/Spells) is
        /// placed in a labelled grid and repeat-cast. Spawns no ECS units — the
        /// whole thing is driven by a single SpellShowcaseDriver MonoBehaviour so
        /// its per-frame recast + world labels run without a gameplay sim.
        /// </summary>
        private static void SpawnSpellShowcase(EntityManager em)
        {
            // Build everything around player 1's start so it lines up with the
            // post-spawn camera focus / RecenterScenario below.
            float3 p1 = GetPlayer1StartPosition();

            var go = new UnityEngine.GameObject("SpellShowcase");
            var driver = go.AddComponent<TheWaningBorder.Abilities.Vfx.SpellShowcaseDriver>();
            // Leaves spellPrefabs empty → the driver auto-loads every Spell
            // prefab under Resources/Spells.
            driver.center = new UnityEngine.Vector3(p1.x, p1.y, p1.z);
            driver.hideSceneTerrain = true;   // flat textureless plane is the only ground
            driver.buildGround = true;
            driver.buildCamera = false;       // reuse the RTS GameCamera
            driver.buildLight = false;        // the map scene already has lighting

            _scenarioFocus = p1;
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
            SpawnArmyRow(em, "Spearman", Faction.Blue, 4, blueCenter);
            SpawnArmyRow(em, "Archer", Faction.Blue, 4, blueCenter + new float3(0, 0, -RowSpacing));
            SpawnArmyRow(em, "Longbowman", Faction.Blue, 4, blueCenter + new float3(0, 0, -RowSpacing * 2f));
            AttackMoveAllBattalions(em, Faction.Blue, center);

            // Red (east) — Alanthor: Sentinel front, Crossbowman behind, Cataphract flankers
            // Fewer battalions (expensive pop 2 units) but higher quality
            var redCenter = new float3(offset, 0, 0);
            SpawnArmyRow(em, "Alanthor_Sentinel", Faction.Red, 2, redCenter);
            SpawnArmyRow(em, "Alanthor_Crossbowman", Faction.Red, 2, redCenter + new float3(RowSpacing, 0, 0));
            SpawnArmyRow(em, "Alanthor_Cataphract", Faction.Red, 2, redCenter + new float3(RowSpacing * 0.5f, 0, ArmySpacing));
            SpawnArmyRow(em, "Longbowman", Faction.Red, 2, redCenter + new float3(RowSpacing * 2f, 0, 0));
            AttackMoveAllBattalions(em, Faction.Red, center);

            // Green (north) — Runai: Spearman front, Skirmisher mid, Raider (mounted archer) flanks
            var greenCenter = new float3(0, 0, offset);
            SpawnArmyRow(em, "Runai_Spearman", Faction.Green, 3, greenCenter);
            SpawnArmyRow(em, "Runai_Skirmisher", Faction.Green, 3, greenCenter + new float3(0, 0, RowSpacing));
            SpawnArmyRow(em, "Runai_Raider", Faction.Green, 2, greenCenter + new float3(0, 0, RowSpacing * 2));
            SpawnArmyRow(em, "Longbowman", Faction.Green, 3, greenCenter + new float3(0, 0, RowSpacing * 3f));
            AttackMoveAllBattalions(em, Faction.Green, center);

            // Yellow (west) — Feraldis: Berserker horde front, Hunter (axe thrower) mid, WarboarRider rear
            var yellowCenter = new float3(-offset, 0, 0);
            SpawnArmyRow(em, "Berserker", Faction.Yellow, 4, yellowCenter);
            SpawnArmyRow(em, "Feraldis_Hunter", Faction.Yellow, 3, yellowCenter + new float3(-RowSpacing, 0, 0));
            SpawnArmyRow(em, "Feraldis_WarboarRider", Faction.Yellow, 2, yellowCenter + new float3(-RowSpacing * 2, 0, 0));
            SpawnArmyRow(em, "Longbowman", Faction.Yellow, 3, yellowCenter + new float3(-RowSpacing * 3f, 0, 0));
            AttackMoveAllBattalions(em, Faction.Yellow, center);

        }

        /// <summary>
        /// Issue attack-move toward a destination for all units of the given faction.
        /// </summary>
        private static void AttackMoveAllBattalions(EntityManager em, Faction faction, float3 destination)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
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
                SpawnArmyRow(em, "Spearman", faction, 3, armyCenter);

                // Row 2 (behind front): 3 Archer battalions
                SpawnArmyRow(em, "Archer", faction, 3, armyCenter + new float3(0, 0, sign * RowSpacing));

                // Row 2.5 (just behind the archers): 3 Longbowman battalions
                SpawnArmyRow(em, "Longbowman", faction, 3, armyCenter + new float3(0, 0, sign * RowSpacing * 1.5f));

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
                    UnitFactory.Create(em, "Alanthor_Catapult", pos, faction);
                }
            }

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
            //
            // task-109 Phase 7: this remains the legacy per-instance single-cell gate
            // for backwards-compat verification (the IMGUI EntityActionPanel still
            // drives the per-instance path). The 5-wide gate region — the new
            // BFME2-style path Phase 5 added — is seeded on the Red wall below,
            // where the 16 m hub spacing leaves room for 5 contiguous instances.
            UpgradeWallInstancesNear(em, Faction.Blue, new float3(0, 0, wallZ), 3f,
                upgradeType: 2); // Gate at center (legacy 1-instance gate)

            UpgradeWallInstancesNear(em, Faction.Blue, new float3(-wallExtent * 0.35f, 0, wallZ), 2f,
                upgradeType: 1); // Tower on left
            UpgradeWallInstancesNear(em, Faction.Blue, new float3(wallExtent * 0.35f, 0, wallZ), 2f,
                upgradeType: 1); // Tower on right

            // Blue defenders behind the wall
            SpawnArmyRow(em, "Spearman", Faction.Blue, 2, new float3(0, 0, wallZ - 12f));
            SpawnArmyRow(em, "Archer", Faction.Blue, 2, new float3(0, 0, wallZ - 18f));
            SpawnArmyRow(em, "Longbowman", Faction.Blue, 2, new float3(0, 0, wallZ - 24f));

            // 2 Ballistas behind the wall on the flanks
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0) ? -10f : 10f;
                float3 pos = new float3(x, 0, wallZ - 14f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                UnitFactory.Create(em, "Alanthor_Catapult", pos, Faction.Blue);
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

            // task-109 Phase 7: seed a 5-wide gate region on the Red wall
            // for testing battalion throughput end-to-end. The Red hubs are
            // 16 m apart (x=-8 to x=+8) so the segment spawns 8 wall
            // instances; PickGateRegionInstances selects the centre 5 and
            // tags them as a region. This exercises:
            //   - Phase 5's WallGateRegionTag + WallGateGroup archetype.
            //   - WallGatePassabilitySystem's RegionDetectRadius = 6.0 branch
            //     (so all 5 cells open in unison when a friendly battalion
            //     approaches from either end).
            //   - PassabilityGrid's per-cell Block/Unblock on Phase 5's
            //     WallGateState toggles.
            // Bypasses the 8 s ConvertSegmentToGate timer + the faction-
            // bank spend (scenarios run without a seeded economy) by
            // instant-tagging — same archetype changes WallUpgradeSystem
            // Loop 2 applies on completion.
            SeedFiveWideGateOnSegment(em, redHub1, redHub2, Faction.Red);

            // Red attackers — siege rams + swordsmen approaching Blue's wall
            SpawnArmyRow(em, "Spearman", Faction.Red, 3, new float3(0, 0, 15f));
            SpawnArmyRow(em, "Longbowman", Faction.Red, 3, new float3(0, 0, 28f));

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
            // upgraded tracking removed

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


                break; // Upgrade one instance per call
            }

        }

        /// <summary>
        /// task-109 Phase 7: instantly seed a 5-wide gate region on the
        /// segment between <paramref name="hubA"/> and <paramref name="hubB"/>.
        /// Mirrors the archetype changes <c>WallUpgradeSystem.Loop2</c> applies
        /// on a player-initiated segment→gate conversion, but bypasses the
        /// 8 s timer and the faction-bank spend (scenarios run without a
        /// seeded economy). Behaviour:
        ///   1. Find the segment entity (via the hub's <c>WallHubLink</c> buffer).
        ///   2. Call <c>AlanthorWall.PickGateRegionInstances</c> with a null
        ///      focus to pick the segment-midpoint 5 (cap-at-length).
        ///   3. Tag each picked instance with <c>WallGateTag</c> +
        ///      <c>WallGateRegionTag</c> + <c>WallGateGroup{ Leader = centre }</c>
        ///      + <c>WallGateState</c>, and swap PresentationId to gate visual.
        /// No-op (logs a warning) if the segment can't be found or has zero
        /// live instances.
        /// </summary>
        private static void SeedFiveWideGateOnSegment(EntityManager em,
            Entity hubA, Entity hubB, Faction faction)
        {
            // Resolve the segment via hub A's link buffer.
            if (!em.HasBuffer<WallHubLink>(hubA))
            {
                UnityEngine.Debug.LogWarning(
                    "[ScenarioSetup] SeedFiveWideGateOnSegment: hubA has no WallHubLink buffer");
                return;
            }

            Entity segment = Entity.Null;
            var links = em.GetBuffer<WallHubLink>(hubA);
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].ConnectedHub == hubB)
                {
                    segment = links[i].Segment;
                    break;
                }
            }
            if (segment == Entity.Null || !em.Exists(segment))
            {
                UnityEngine.Debug.LogWarning(
                    "[ScenarioSetup] SeedFiveWideGateOnSegment: no segment between provided hubs");
                return;
            }

            // PickGateRegionInstances allocates a NativeList we own; we Dispose
            // after the loop. Entity.Null focus → centre-anchored window.
            using var members = AlanthorWall.PickGateRegionInstances(
                em, segment, Entity.Null, Allocator.Temp);

            if (members.Length == 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[ScenarioSetup] SeedFiveWideGateOnSegment: segment has no live instances");
                return;
            }

            int leaderIdx = members.Length / 2;
            Entity leader = members[leaderIdx];

            for (int i = 0; i < members.Length; i++)
            {
                Entity inst = members[i];
                if (!em.Exists(inst)) continue;

                if (!em.HasComponent<WallGateTag>(inst))
                    em.AddComponentData(inst, new WallGateTag());
                if (!em.HasComponent<WallGateRegionTag>(inst))
                    em.AddComponentData(inst, new WallGateRegionTag());
                if (em.HasComponent<WallGateGroup>(inst))
                    em.SetComponentData(inst, new WallGateGroup { Leader = leader });
                else
                    em.AddComponentData(inst, new WallGateGroup { Leader = leader });
                if (!em.HasComponent<WallGateState>(inst))
                    em.AddComponentData(inst, new WallGateState { IsOpen = 0, RecheckTimer = 0f });

                em.SetComponentData(inst, new PresentationId
                {
                    Id = AlanthorWall.GatePresentationID
                });
            }
        }

        /// <summary>
        /// Sect Showcase: 12 test areas arranged in a 4x3 grid, one per sect.
        /// Each area has 3 friendly sect units (Blue) facing 5 enemy Swordsmen (Red).
        /// Player can select sect units and test their abilities.
        /// Layout: Alanthor sects (top row), Runai sects (middle row), Feraldis sects (bottom row).
        /// </summary>
        private static void SpawnSectShowcase(EntityManager em)
        {
            GameSettings.TotalPlayers = 2;
            GameSettings.IsObserver = false;

            // 12 sects: 4 columns x 3 rows
            var sects = new (string unitId, string label)[]
            {
                // Row 0 — Alanthor (4 sects)
                ("Sect_ScarGuard",         "Renewal: ScarGuard\nRapidMend (self-heal)"),
                ("Sect_GolemAutark",        "Antiquity: GolemAutark\nArcanePulse (AOE dmg)"),
                ("Sect_StoneWarden",        "LivingStone: StoneWarden\nFortify (armor+root self)"),
                ("Sect_ArchivistAdept",     "VeiledMemory: ArchivistAdept\nDispel (strip buffs)"),
                // Row 1 — Runai (4 sects)
                ("Sect_FlameWarden",        "StillFlame: FlameWarden\nSanction (root enemy)"),
                ("Sect_VaultKeeper",        "QuietVault: VaultKeeper\nSafeguard (AOE armor)"),
                ("Sect_GlassmarkArcanist",  "MirrorRite: GlassmarkArcanist\nMirrorShield (reflect)"),
                ("Sect_Judicator",          "ShardJudgment: Judicator\nCondemn (+25% dmg taken)"),
                // Row 2 — Feraldis (4 sects)
                ("Sect_Ashblade",           "EmberAsh: Ashblade\nIgnite (fire dmg x3)"),
                ("Sect_Brandbreaker",       "HollowBrand: Brandbreaker\nWarCry (AOE slow)"),
                ("Sect_Chaincaster",        "FlamewroughtChains: Chaincaster\nChainBind (root)"),
                ("Sect_Nullblade",          "UnmakersGrasp: Nullblade\nVoidStrike (+40 next hit)"),
            };

            float colSpacing = 30f;  // distance between area centers in X
            float rowSpacing = 30f;  // distance between area centers in Z
            float gridOffsetX = -colSpacing * 1.5f; // center the 4-column grid
            float gridOffsetZ = -rowSpacing * 1f;    // center the 3-row grid

            for (int i = 0; i < sects.Length; i++)
            {
                int col = i % 4;
                int row = i / 4;

                float3 areaCenter = new float3(
                    gridOffsetX + col * colSpacing,
                    0f,
                    gridOffsetZ + row * rowSpacing);
                areaCenter.y = TerrainUtility.GetHeight(areaCenter.x, areaCenter.z);

                // Spawn 3 friendly sect units (Blue) on the south side of the area
                for (int u = 0; u < 3; u++)
                {
                    float x = (u - 1) * 2.5f;
                    float3 pos = areaCenter + new float3(x, 0, -4f);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    UnitFactory.Create(em, sects[i].unitId, pos, Faction.Blue);
                }

                // Spawn 5 enemy Swordsmen (Red) on the north side as targets
                for (int u = 0; u < 5; u++)
                {
                    float x = (u - 2) * 2.5f;
                    float3 pos = areaCenter + new float3(x, 0, 6f);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    UnitFactory.Create(em, "Spearman", pos, Faction.Red);
                }
            }

            // Focus camera on center of grid
            GameCamera.FocusOn(new UnityEngine.Vector3(0, 0, 0), instant: true);

        }

        /// <summary>
        /// Building Showcase: one of each building type, organised by culture.
        /// Five rows centred on origin, each row a different faction colour for
        /// quick visual separation:
        ///   Row 0 (Blue,    south): Era-1 generic buildings.
        ///   Row 1 (Teal):           Era-2 choice buildings (pre-culture).
        ///   Row 2 (Green):          Runai culture buildings.
        ///   Row 3 (Yellow):         Feraldis culture buildings.
        ///   Row 4 (Red,     north): Alanthor culture buildings.
        /// </summary>
        private static void SpawnBuildingShowcase(EntityManager em)
        {
            // Review grid: every building in every upgrade state on a flat,
            // empty map (victory conditions are already off in Scenario mode).
            //
            //   Age 0 column (Blue, no culture): each Age 0 building in its
            //     TRUE Lv0 state — a cultured faction can never show Lv0 on a
            //     completed building (the presentation clamps to culture L1),
            //     which is why this section runs under a culture-less faction.
            //     The Temple of Ridan sits at the bottom with all 6 chapel
            //     statues docked in its heptagon ring, and a Worker stands
            //     beside it for scale.
            //   One section per culture (Alanthor / Runai / Feraldis): rows
            //     are that culture's buildings, columns are L1 | L2 | L3 via
            //     BuildingUpgradeState — the same path a real level-up takes,
            //     so the variant switch and the footprint refit are exercised
            //     exactly as in a match.
            //
            // A bright-green 2 m build-grid overlay (the Gatherer's Hut
            // area-circle green) covers the whole layout so every footprint
            // can be read against real cells.
            const float Col = 22f;   // roomy for the largest footprints (12 m) + chapel ring
            const float Row = 22f;
            const float TopZ = 110f;

            EnsureShowcaseTerrain(360f);

            FactionColors.SetFactionCulture(Faction.Blue,   Cultures.None);
            FactionColors.SetFactionCulture(Faction.Red,    Cultures.Alanthor);
            FactionColors.SetFactionCulture(Faction.Green,  Cultures.Runai);
            FactionColors.SetFactionCulture(Faction.Yellow, Cultures.Feraldis);

            // ── Age 0 section (X = -150): true Lv0 states ────────────────
            string[] age0 = { "Hall", "Hut", "GatherersHut", "Barracks",
                              "ShrineOfRidan", "VaultOfAlmierra" };
            for (int i = 0; i < age0.Length; i++)
                PlaceShowcaseBuilding(em, age0[i], Faction.Blue,
                    new float3(-150f, 0f, TopZ - i * Row), level: 0);

            // Temple of Ridan + the 6 chapel statues docked in its ring.
            // Spawned directly (the slot-driven path routes through the sect
            // adoption economy and would destroy an uncredited chapel). Two
            // sects per culture cluster so the statue variety reads.
            float3 templePos = new float3(-150f, 0f, TopZ - 7.5f * Row);
            templePos.y = TerrainUtility.GetHeight(templePos.x, templePos.z);
            BuildingFactory.Create(em, "TempleOfRidan", templePos, Faction.Blue);

            string[] statueSects = { SectConfig.Antiquity, SectConfig.Renewal,
                                     SectConfig.Silence,   SectConfig.Justice,
                                     SectConfig.War,       SectConfig.Ash };
            for (int i = 0; i < statueSects.Length; i++)
            {
                float3 slotPos = templePos + TempleChapelRing.WorldOffset(i);
                slotPos.y = TerrainUtility.GetHeight(slotPos.x, slotPos.z);
                var chapel = BuildingFactory.Create(
                    em, SectConfig.ChapelIdFor(statueSects[i]), slotPos, Faction.Blue);

                // BFME2 docking, same as TempleChapelBuildSystem: the statue's
                // back sits flush against its temple face, door facing outward.
                if (chapel != Entity.Null && em.HasComponent<LocalTransform>(chapel))
                {
                    float3 outward = slotPos - templePos;
                    outward.y = 0f;
                    var lt = em.GetComponentData<LocalTransform>(chapel);
                    lt.Rotation = quaternion.LookRotationSafe(outward, math.up());
                    em.SetComponentData(chapel, lt);
                }
            }

            // The size-reference Worker, beside the temple ring.
            float3 workerPos = new float3(-134f, 0f, templePos.z + 10f);
            workerPos.y = TerrainUtility.GetHeight(workerPos.x, workerPos.z);
            UnitFactory.Create(em, "Worker", workerPos, Faction.Blue);

            // ── Culture sections: rows x (L1 | L2 | L3) ──────────────────
            // Alanthor_Wall is intentionally absent: it is a multi-entity hub
            // set, not a single upgradeable building (same rule as the
            // damage-test scenarios).
            var sections = new (Faction faction, float startX, string[] buildings)[]
            {
                (Faction.Red, -100f, new[] {
                    "Hall", "Hut", "GatherersHut", "Barracks",
                    "KingsCourt", "Alanthor_Tower", "Alanthor_SiegeYard", "Alanthor_Smelter" }),
                (Faction.Green, -22f, new[] {
                    "Hall", "Hut", "GatherersHut", "Barracks",
                    "ThessarasBazaar", "Runai_Outpost", "Runai_TradeHub",
                    "Runai_Vault", "Runai_VeilsteelFoundry", "Runai_SiegeWorkshop" }),
                (Faction.Yellow, 56f, new[] {
                    "Hall", "Hut", "GatherersHut", "Barracks",
                    "FiendstoneKeep", "Feraldis_HuntingLodge", "Feraldis_LoggingStation",
                    "Feraldis_Foundry", "Feraldis_Tower", "Feraldis_Longhouse",
                    "Feraldis_SiegeYard" }),
            };

            foreach (var (faction, startX, buildings) in sections)
            {
                for (int r = 0; r < buildings.Length; r++)
                {
                    for (byte level = 1; level <= 3; level++)
                    {
                        var pos = new float3(startX + (level - 1) * Col, 0f, TopZ - r * Row);
                        var e = PlaceShowcaseBuilding(em, buildings[r], faction, pos, level);

                        // The completed-culture read
                        // (CultureConfig.GetCompletedCulture) resolves off the
                        // faction's Hall, so the culture must be stamped on
                        // the Hall entities as they appear — every later
                        // visual in the section then renders its culture
                        // branch.
                        if (r == 0 && e != Entity.Null)
                        {
                            var culture = FactionColors.GetFactionCulture(faction);
                            if (em.HasComponent<FactionProgress>(e))
                                em.SetComponentData(e, new FactionProgress { Culture = culture });
                            else
                                em.AddComponentData(e, new FactionProgress { Culture = culture });
                        }
                    }
                }
            }

            // ── The green build-grid over the whole layout ───────────────
            ScenarioGridOverlay.Create(-170f, -140f, 120f, 130f);

            _scenarioFocus = new float3(-30f, 0f, 20f);
        }

        /// <summary>
        /// Spawn one showcase building and stamp its upgrade level. Level 1-3
        /// drives the same BuildingUpgradeState the real upgrade flow uses, so
        /// BuildingPrefabSwapSystem performs the authentic variant switch;
        /// level 0 leaves the state unstamped (the culture-less faction shows
        /// the true Lv0 model).
        /// </summary>
        private static Entity PlaceShowcaseBuilding(EntityManager em, string buildingId,
            Faction faction, float3 pos, byte level)
        {
            pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
            var e = BuildingFactory.Create(em, buildingId, pos, faction);
            if (e == Entity.Null || level == 0) return e;

            var st = em.HasComponent<BuildingUpgradeState>(e)
                ? em.GetComponentData<BuildingUpgradeState>(e)
                : default;
            st.Level = level;
            if (st.BaseHpMax == 0 && em.HasComponent<Health>(e))
                st.BaseHpMax = em.GetComponentData<Health>(e).Max;
            if (em.HasComponent<BuildingUpgradeState>(e)) em.SetComponentData(e, st);
            else em.AddComponentData(e, st);
            return e;
        }

        /// <summary>
        /// Grow the scenario's flat terrain to fit the showcase grid. The
        /// authored Scenario_BuildingShowcase terrain is 100 x 100 m — smaller
        /// than the layout — so widen it in place and re-centre it on the
        /// origin. Heightmap is all zeros, so resizing keeps it flat.
        /// </summary>
        private static void EnsureShowcaseTerrain(float size)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return;

            var td = terrain.terrainData;
            if (td.size.x < size)
                td.size = new Vector3(size, Mathf.Max(1f, td.size.y), size);
            terrain.transform.position = new Vector3(-size * 0.5f, 0f, -size * 0.5f);
            terrain.Flush();
        }

        /// <summary>
        /// Building-damage shader test. Places a row of Alanthor buildings and
        /// tags each with <see cref="DebugBuildingDamageTarget"/> so
        /// DebugBuildingDamageSystem drains 5% of their max HP per second. As the
        /// HP falls, BuildingDamageVisual drives the progressive soot/cracks/
        /// missing-pieces damage shader, culminating in the normal collapse when
        /// each building hits 0 HP (~20 s after spawn).
        /// </summary>
        private static void SpawnBuildingDamageTest(EntityManager em)
        {
            FactionColors.SetFactionCulture(Faction.Red, Cultures.Alanthor);

            // Single-entity Alanthor buildings (Alanthor_Wall is a multi-entity
            // hub, so it's intentionally left out of the damage row).
            var buildings = new[]
            {
                "Hall", "Barracks", "Alanthor_Tower",
                "Alanthor_SiegeYard", "Alanthor_Smelter", "KingsCourt",
            };

            const float ColSpacing = 16f;
            float startX = -((buildings.Length - 1) * 0.5f) * ColSpacing;

            for (int c = 0; c < buildings.Length; c++)
            {
                float3 pos = new float3(startX + c * ColSpacing, 0f, 0f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                var e = BuildingFactory.Create(em, buildings[c], pos, Faction.Red);
                if (e != Entity.Null)
                    em.AddComponentData(e, new DebugBuildingDamageTarget { Accumulator = 0f });
            }

            _scenarioFocus = float3.zero;
        }

        /// <summary>
        /// Building-damage showcase. A grid of buildings — one row per culture
        /// (generic / Runai / Feraldis / Alanthor) — each tagged with
        /// <see cref="DebugBuildingDamageTarget"/> so DebugBuildingDamageSystem
        /// drains 5% of their max HP per second. Exercises the progressive
        /// BuildingDamage shader and the collapse across many building meshes at
        /// once. Multi-entity hubs (e.g. Alanthor_Wall) are left out so every
        /// placed building is a single damageable entity.
        /// </summary>
        private static void SpawnBuildingDamageShowcase(EntityManager em)
        {
            const float ColSpacing = 16f;
            const float RowZSpacing = 24f;

            FactionColors.SetFactionCulture(Faction.Blue,   Cultures.None);
            FactionColors.SetFactionCulture(Faction.Green,  Cultures.Runai);
            FactionColors.SetFactionCulture(Faction.Yellow, Cultures.Feraldis);
            FactionColors.SetFactionCulture(Faction.Red,    Cultures.Alanthor);

            var rows = new (Faction faction, string[] buildings)[]
            {
                (Faction.Blue,   new[] { "Hall", "Hut", "GatherersHut", "Barracks" }),
                (Faction.Green,  new[] { "Runai_Outpost", "Runai_TradeHub", "ThessarasBazaar", "Runai_Vault" }),
                (Faction.Yellow, new[] { "Feraldis_HuntingLodge", "Feraldis_Longhouse", "Feraldis_Tower", "Feraldis_Foundry" }),
                (Faction.Red,    new[] { "Alanthor_Tower", "Alanthor_SiegeYard", "Alanthor_Smelter", "KingsCourt" }),
            };

            float startZ = -((rows.Length - 1) * 0.5f) * RowZSpacing;

            for (int r = 0; r < rows.Length; r++)
            {
                var (faction, buildings) = rows[r];
                float rowZ = startZ + r * RowZSpacing;
                float startX = -((buildings.Length - 1) * 0.5f) * ColSpacing;

                for (int c = 0; c < buildings.Length; c++)
                {
                    float3 pos = new float3(startX + c * ColSpacing, 0f, rowZ);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    var e = BuildingFactory.Create(em, buildings[c], pos, faction);
                    if (e != Entity.Null)
                        em.AddComponentData(e, new DebugBuildingDamageTarget { Accumulator = 0f });
                }
            }

            _scenarioFocus = float3.zero;
        }

        /// <summary>
        /// The Border Combat Test: five attacker/target pairs, each row a
        /// single Border unit hitting an "invincible" Hall (HP = 1e9) so the
        /// attack reads continuously. Row spacing 35 m keeps each
        /// Veilstinger/Godsplinter's secondary-target search inside its own
        /// row (no cross-row leakage with a 24 m max range).
        ///
        ///   Row 0 (z = -70): Crystalling melee  vs Hall   (start 5 m apart)
        ///   Row 1 (z = -35): Veilstinger max    vs Hall   (24 m — laser cap)
        ///   Row 2 (z =   0): Veilstinger middle vs Hall   (16 m — mid-band)
        ///   Row 3 (z = +35): Godsplinter max    vs Hall   (22 m — laser cap)
        ///   Row 4 (z = +70): Godsplinter middle vs Hall   (13 m — mid-band)
        /// </summary>
        private static void SpawnBorderCombatTest(EntityManager em)
        {
            // Camera focuses on origin (middle row by default).
            SpawnBorderTestPair(em, "Crystalling",  5f,  -70f);
            SpawnBorderTestPair(em, "Veilstinger", 24f, -35f);
            SpawnBorderTestPair(em, "Veilstinger", 16f,   0f);
            SpawnBorderTestPair(em, "Godsplinter", 22f,  35f);
            SpawnBorderTestPair(em, "Godsplinter", 13f,  70f);

            // Starter veilstone patch placed between rows 3 (z=0) and 4 (z=35),
            // off the firing line at x=0 z=17.5 so it sits in clear camera view.
            SpawnBattleSiteVeilstoneOutcropping(em, new float3(0f, 0f, 17.5f));
        }

        /// <summary>
        /// Spawn one row of the Border Combat Test: an invincible invisible
        /// dummy on the right and a Red Border unit on the left. The dummy
        /// has just enough ECS components for TargetingSystem to lock onto
        /// it (LocalTransform + FactionTag + BuildingTag + Health) and no
        /// PresentationId, so the projectile and its impact VFX play out in
        /// full view rather than being clipped by a building mesh.
        /// </summary>
        private static void SpawnBorderTestPair(EntityManager em, string unitId, float distance, float rowZ)
        {
            float halfDist = distance * 0.5f;

            // Invincible invisible target on the right.
            float3 dummyPos = new float3(halfDist, 0f, rowZ);
            dummyPos.y = TerrainUtility.GetHeight(dummyPos.x, dummyPos.z);
            CreateInvincibleDummy(em, dummyPos, Faction.Blue);

            // Attacker: Border unit on the left, facing the dummy.
            float3 attackerPos = new float3(-halfDist, 0f, rowZ);
            attackerPos.y = TerrainUtility.GetHeight(attackerPos.x, attackerPos.z);
            UnitFactory.Create(em, unitId, attackerPos, Faction.Red);
        }

        /// <summary>
        /// Spawn a barebones invincible test dummy: just enough ECS state for
        /// the targeting + combat systems to engage it. No PresentationId, so
        /// no visual is ever spawned — the explosion VFX has an unobstructed
        /// stage. HP is 1 × 10⁹ so it can't die in any practical session.
        /// </summary>
        private static Entity CreateInvincibleDummy(EntityManager em, float3 position, Faction faction)
        {
            var entity = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(Health),
                typeof(Radius)
            );
            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            em.SetComponentData(entity, new FactionTag { Value = faction });
            em.SetComponentData(entity, new Health { Value = 1_000_000_000, Max = 1_000_000_000 });
            // Small radius so attackers stop at the right distance and the
            // projectile-hit XZ check (HitRadius = 0.8 m) lands on the point.
            em.SetComponentData(entity, new Radius { Value = 0.5f });
            return entity;
        }

        /// <summary>
        /// Seed a starter resource patch for the battle-site test scenarios.
        /// Purely a fixed mineable field — veilstone does not regrow or
        /// respawn (it behaves exactly like iron).
        /// </summary>
        private static void SpawnBattleSiteVeilstoneOutcropping(EntityManager em, float3 center)
        {
            center.y = TerrainUtility.GetHeight(center.x, center.z);

            const int NodeCount = 44;
            const int VeilstonePerNode = 60;

            // Hex grid for an even, packed cluster — same approach as
            // VeilstoneOutcroppingBootstrap's near patch. 3 rings yield 37 slots;
            // 4 rings yield 61; we walk slot-by-slot until we've placed 44.
            const int Rings = 4;
            const float Spacing = 2.6f;     // hex-cell spacing; 2.6 m node-to-node hops stay under PatchClusterRadius (3 m adjacency) so the whole grid floods as one patch
            const float SQRT3_OVER_2 = 0.8660254f;

            int placed = 0;

            // Centre cell first.
            PlaceNode(em, center, 0f, 0f);
            placed++;

            // Ring walk.
            int[,] hexDirs = new int[,]
            {
                {  1,  0 }, {  1, -1 }, {  0, -1 },
                { -1,  0 }, { -1,  1 }, {  0,  1 }
            };

            for (int ring = 1; ring <= Rings && placed < NodeCount; ring++)
            {
                int q = -ring;
                int r = ring;
                for (int side = 0; side < 6 && placed < NodeCount; side++)
                {
                    for (int step = 0; step < ring && placed < NodeCount; step++)
                    {
                        float ox = Spacing * (q + r * 0.5f);
                        float oz = Spacing * r * SQRT3_OVER_2;
                        PlaceNode(em, center, ox, oz);
                        placed++;
                        q += hexDirs[side, 0];
                        r += hexDirs[side, 1];
                    }
                }
            }

            TWBLog.Log($"[ScenarioSetup] seeded {placed}-node resource patch at ({center.x:F1}, {center.z:F1}) — one border-unit death will tip it past {placed + 1}.");

            static void PlaceNode(EntityManager em, float3 center, float ox, float oz)
            {
                float x = center.x + ox;
                float z = center.z + oz;
                float y = TerrainUtility.GetHeight(x, z);
                VeilstoneOutcropping.Create(em, new float3(x, y, z), VeilstonePerNode);
            }
        }

        /// <summary>
        /// Patrol Defense: six Veilstingers walk a 15 m radius circle clockwise
        /// while a wave of passive soldiers spawns every 3 s from a 35 m outer
        /// ring and walks toward the centre. Veilstingers fire while moving
        /// and split their two guns across the two nearest soldiers when
        /// available (single soldier → both guns on it; two+ → one each).
        /// Soldiers don't shoot back — their Damage and Target components are
        /// stripped right after creation, so combat queries can't match them.
        /// </summary>
        private static void SpawnPatrolDefense(EntityManager em)
        {
            const float CircleRadius = 15f;
            const int   VeilstingerCount = 6;
            const int   WaypointCount = 12;  // 30° spacing
            const float OuterSpawnRadius = 35f;
            const float InnerTargetRadius = 5f;
            const float WaveInterval = 3f;

            Vector3 center = Vector3.zero;

            // Generate the patrol loop — N points spaced evenly on the circle.
            var waypoints = new Vector3[WaypointCount];
            for (int i = 0; i < WaypointCount; i++)
            {
                float ang = (i / (float)WaypointCount) * Mathf.PI * 2f;
                float x = center.x + Mathf.Cos(ang) * CircleRadius;
                float z = center.z + Mathf.Sin(ang) * CircleRadius;
                waypoints[i] = new Vector3(x, TerrainUtility.GetHeight(x, z), z);
            }

            // Patrol controller GameObject — drives DesiredDestination every
            // LateUpdate so the standard movement systems carry the units.
            var patrolGo = new GameObject("ScenarioPatrolController");
            var patrol = patrolGo.AddComponent<ScenarioPatrolController>();

            // 6 Veilstingers, each starting at every other waypoint so they
            // stay roughly evenly spaced as they walk the loop.
            int stride = WaypointCount / VeilstingerCount; // 2
            for (int i = 0; i < VeilstingerCount; i++)
            {
                int startIdx = i * stride;
                Vector3 spawn = waypoints[startIdx];
                var entity = Veilstinger.Create(em,
                    new float3(spawn.x, spawn.y, spawn.z), Faction.Red);
                if (entity == Entity.Null) continue;

                // HoldPositionTag prevents the Veilstinger combat system's
                // chase branch from overwriting the patrol's DesiredDestination
                // when a soldier exits maxRange. The in-range branch no
                // longer touches movement on its own.
                if (!em.HasComponent<HoldPositionTag>(entity))
                    em.AddComponent<HoldPositionTag>(entity);

                // PatrolTag flags the unit as an "active scanner" in
                // TargetingSystem — without it, the auto-acquire skips any
                // unit whose DesiredDestination.Has is set, and the patrol
                // controller writes that every frame. The result without
                // this tag is that the Veilstingers never get a target
                // assigned and never fire.
                if (!em.HasComponent<PatrolTag>(entity))
                    em.AddComponent<PatrolTag>(entity);

                patrol.Units.Add(new ScenarioPatrolController.PatrolUnit
                {
                    Entity = entity,
                    Waypoints = waypoints,
                    // Walk toward the NEXT waypoint so the unit starts moving.
                    CurrentWaypoint = (startIdx + 1) % WaypointCount,
                    // Pre-target slightly outside max-range so the moment a
                    // soldier crosses MaxRange (24 m), the AimTimer is
                    // already counting and the first shot fires fast.
                    EngageRange = 30f
                });
            }

            // Wave spawner GameObject — passive Faction.Blue soldiers spawn
            // on the outer ring and head for a random point near the centre.
            var spawnerGo = new GameObject("ScenarioWaveSpawner");
            var spawner = spawnerGo.AddComponent<ScenarioWaveSpawner>();
            spawner.Center = center;
            spawner.SpawnRadius = OuterSpawnRadius;
            spawner.InnerTargetRadius = InnerTargetRadius;
            spawner.Interval = WaveInterval;
            spawner.UnitId = "Spearman";
            spawner.SoldierFaction = Faction.Blue;

            // Starter 44-node resource patch east of the patrol ring (just past
            // the 35 m outer spawn) — a fixed mineable field for the scenario.
            SpawnBattleSiteVeilstoneOutcropping(em, new float3(50f, 0f, 0f));
        }

        /// <summary>
        /// Alanthor Vs Veilstone Horde: 2 battalions of each Alanthor battalion-tier
        /// unit (Sentinel / Crossbowman / Cataphract = 6 battalions total) on the
        /// south side facing a 50-unit Veilstone horde — 30 Crystallings, 15
        /// Veilstingers, 5 Godsplinters — on the north side. Both armies
        /// attack-move toward the centre on spawn.
        /// </summary>
        private static void SpawnAlanthorVsBorder(EntityManager em)
        {
            // Set the Red player faction to the Alanthor culture so the
            // battalion units render with the right cultural treatment.
            FactionColors.SetFactionCulture(Faction.Red, Cultures.Alanthor);

            float3 center = float3.zero;
            float armyOffset = ArmySeparation * 0.5f;

            // ── Alanthor (Red, south) ──
            // Three rows of 2 battalions: Sentinel front, Crossbowman behind,
            // Cataphract at the back (cavalry held in reserve / flank).
            var redCenter = new float3(0, 0, -armyOffset);
            SpawnArmyRow(em, "Alanthor_Sentinel",    Faction.Red, 2, redCenter);
            SpawnArmyRow(em, "Alanthor_Crossbowman", Faction.Red, 2, redCenter + new float3(0, 0, -RowSpacing));
            SpawnArmyRow(em, "Alanthor_Cataphract",  Faction.Red, 2, redCenter + new float3(0, 0, -RowSpacing * 2f));
            SpawnArmyRow(em, "Longbowman",           Faction.Red, 2, redCenter + new float3(0, 0, -RowSpacing * 3f));
            AttackMoveAllBattalions(em, Faction.Red, center);

            // ── Veilstone Horde (Blue, north) ──
            // 30 Crystallings (melee front), 15 Veilstingers (mid),
            // 5 Godsplinters (siege rear). Spawned as loose units; each
            // gets an AttackMoveCommand toward centre so they march in.
            var blueCenter = new float3(0, 0, armyOffset);
            const float HordeUnitSpacing = 2f;

            // Row depths relative to blueCenter (positive Z = farther from centre).
            float crystallingDepth = 0f;
            float veilstingerDepth = RowSpacing;
            float godsplinterDepth = RowSpacing * 2f;

            // 30 Crystallings — two rows of 15.
            for (int i = 0; i < 30; i++)
            {
                int col = i % 15;
                int row = i / 15;
                float x = (col - 7f) * HordeUnitSpacing;
                float3 pos = blueCenter + new float3(x, 0, crystallingDepth + row * HordeUnitSpacing);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                var e = UnitFactory.Create(em, "Crystalling", pos, Faction.Blue);
                if (e != Entity.Null) AttackMoveCommandHelper.Execute(em, e, center);
            }

            // 15 Veilstingers — one row.
            for (int i = 0; i < 15; i++)
            {
                float x = (i - 7f) * HordeUnitSpacing * 1.5f;
                float3 pos = blueCenter + new float3(x, 0, veilstingerDepth);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                var e = UnitFactory.Create(em, "Veilstinger", pos, Faction.Blue);
                if (e != Entity.Null) AttackMoveCommandHelper.Execute(em, e, center);
            }

            // 5 Godsplinters — one row, spaced wider (siege-class).
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2f) * HordeUnitSpacing * 3f;
                float3 pos = blueCenter + new float3(x, 0, godsplinterDepth);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                var e = UnitFactory.Create(em, "Godsplinter", pos, Faction.Blue);
                if (e != Entity.Null) AttackMoveCommandHelper.Execute(em, e, center);
            }

            // Starter 44-node resource patch east of the battle line — a fixed
            // mineable field for the scenario.
            SpawnBattleSiteVeilstoneOutcropping(em, new float3(40f, 0f, 0f));
        }

        /// <summary>
        /// Longbowman animation showcase. Spawns the Longbowman in each of the
        /// states the game drives so its idle / run / shoot / death clips can be
        /// reviewed live, exactly as gameplay produces them:
        ///   1. one idle Longbowman (Target stripped, so it never engages);
        ///   2. one patrolling back and forth between two waypoints;
        ///   3. two Longbowmen firing on an invincible dummy from ~16 m and ~34 m;
        ///   4. a spawner that sends a Longbowman every 5 s at an immortal
        ///      attacking enemy, where each one walks in and dies — then the
        ///      next one spawns.
        /// All showcase units are Blue (local player); targets/enemy are Red.
        /// </summary>
        private static void SpawnLongbowmanShowcase(EntityManager em)
        {
            // Authored around origin; SpawnScenarioEntities re-centers the whole
            // scenario onto the player-1 start afterwards (o is the local origin).
            float3 o = float3.zero;

            // ── 1) Idle ──
            // Strip Target so the targeting/combat systems skip it (same trick
            // ScenarioWaveSpawner uses for passive walkers) — it just idles.
            {
                float3 pos = o + new float3(-18f, 0f, 0f);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                var idle = UnitFactory.Create(em, "Longbowman", pos, Faction.Blue);
                if (idle != Entity.Null && em.HasComponent<Target>(idle))
                    em.RemoveComponent<Target>(idle);
            }

            // ── 2) Patrol back and forth ──
            // A ScenarioPatrolController walks it between two waypoints via the
            // standard MovementSystem. Target stripped so it never stops to fight.
            {
                Vector3 a = new Vector3(o.x - 8f, 0f, o.z + 12f);
                Vector3 b = new Vector3(o.x - 8f, 0f, o.z - 12f);
                a.y = TerrainUtility.GetHeight(a.x, a.z);
                b.y = TerrainUtility.GetHeight(b.x, b.z);

                var patrol = UnitFactory.Create(em, "Longbowman",
                    new float3(a.x, a.y, a.z), Faction.Blue);
                if (patrol != Entity.Null)
                {
                    if (em.HasComponent<Target>(patrol))
                        em.RemoveComponent<Target>(patrol);

                    // The patrol controller only drives units that already have
                    // DesiredDestination — Longbowman.Create doesn't add one.
                    var dest = new DesiredDestination
                    {
                        Position = new float3(b.x, b.y, b.z),
                        Has = 1
                    };
                    if (em.HasComponent<DesiredDestination>(patrol))
                        em.SetComponentData(patrol, dest);
                    else
                        em.AddComponentData(patrol, dest);

                    var go = new GameObject("ShowcasePatrolController");
                    var ctrl = go.AddComponent<ScenarioPatrolController>();
                    ctrl.Units.Add(new ScenarioPatrolController.PatrolUnit
                    {
                        Entity = patrol,
                        Waypoints = new[] { a, b },
                        CurrentWaypoint = 1, // head for b first
                        EngageRange = 0f
                    });
                }
            }

            // ── 3) Two shooters vs an invincible target, different ranges ──
            {
                float3 dummyPos = o + new float3(10f, 0f, 14f);
                dummyPos.y = TerrainUtility.GetHeight(dummyPos.x, dummyPos.z);
                CreateInvincibleDummy(em, dummyPos, Faction.Red);

                // Near ~16 m, far ~34 m. Both inside Longbowman LOS (35) and
                // attack range (12-40), so they auto-acquire and keep firing.
                float3 near = o + new float3(10f, 0f, -2f);
                float3 far  = o + new float3(10f, 0f, -20f);
                near.y = TerrainUtility.GetHeight(near.x, near.z);
                far.y  = TerrainUtility.GetHeight(far.x, far.z);
                UnitFactory.Create(em, "Longbowman", near, Faction.Blue);
                UnitFactory.Create(em, "Longbowman", far,  Faction.Blue);
            }

            // ── 4) Spawner -> immortal attacking enemy -> death loop ──
            {
                float3 enemyPos = o + new float3(10f, 0f, 48f);
                enemyPos.y = TerrainUtility.GetHeight(enemyPos.x, enemyPos.z);

                // Immortal attacker: a melee unit that one-shots the incoming
                // (passive) Longbowmen so the death clip plays reliably. Huge HP
                // = immortal; HoldPositionTag keeps it planted at the lane end.
                var enemy = UnitFactory.Create(em, "Spearman", enemyPos, Faction.Red);
                if (enemy != Entity.Null)
                {
                    if (em.HasComponent<Health>(enemy))
                        em.SetComponentData(enemy, new Health { Value = 1_000_000_000, Max = 1_000_000_000 });
                    if (em.HasComponent<Damage>(enemy))
                        em.SetComponentData(enemy, new Damage { Value = 100_000 });
                    if (em.HasComponent<AttackCooldown>(enemy))
                    {
                        var cd = em.GetComponentData<AttackCooldown>(enemy);
                        cd.Cooldown = 0.6f;
                        em.SetComponentData(enemy, cd);
                    }
                    if (!em.HasComponent<HoldPositionTag>(enemy))
                        em.AddComponent<HoldPositionTag>(enemy);
                }

                // Wave spawner: one Longbowman every 5 s on a ring around the
                // enemy; each walks in (combat stripped by the spawner) and dies.
                var go = new GameObject("ShowcaseLongbowmanSpawner");
                var spawner = go.AddComponent<ScenarioWaveSpawner>();
                spawner.Center = new Vector3(enemyPos.x, enemyPos.y, enemyPos.z);
                spawner.SpawnRadius = 20f;
                spawner.InnerTargetRadius = 1.5f;
                spawner.Interval = 5f;
                spawner.UnitId = "Longbowman";
                spawner.SoldierFaction = Faction.Blue;
            }
        }

        /// <summary>
        /// Longbowman line battle: two teams of 30 Longbowmen face off across
        /// the player-1 start. Each team is split into two 3x5 blocks (5 wide,
        /// 3 deep = 15 each). The firing lines are placed ~30 m apart so every
        /// archer sits inside Longbowman LOS/range (12–40 m) and trades volleys
        /// without charging; HoldPositionTag keeps the formation intact.
        /// Blue (south) faces north, Red (north) faces south.
        /// </summary>
        private static void SpawnLongbowmanBattle(EntityManager em)
        {
            // Authored around origin; SpawnScenarioEntities re-centers afterwards.
            float3 o = float3.zero;

            const int Cols = 5;               // each block is 5 wide…
            const int Rows = 3;               // …and 3 deep (5x3 = 15; two blocks = 30/team)
            const float UnitSpacing = 3f;     // gap between archers in a block
            const float BlockGap = 8f;        // gap between a team's two blocks (along X)
            const float TeamSeparation = 30f; // distance between the two firing lines (along Z)

            float blockWidth = (Cols - 1) * UnitSpacing;            // 12 m
            float blockOffsetX = (blockWidth + BlockGap) * 0.5f;    // half the two-block span

            float blueZ = o.z - TeamSeparation * 0.5f;  // south team
            float redZ = o.z + TeamSeparation * 0.5f;   // north team

            // Face the opposing line (model forward is +Z, so Blue = identity,
            // Red = 180°). Both teams therefore aim across the player-1 start.
            quaternion blueFacing = quaternion.LookRotationSafe(new float3(0, 0, 1), math.up());
            quaternion redFacing = quaternion.LookRotationSafe(new float3(0, 0, -1), math.up());

            // Two 3x5 blocks per team, side by side along X, centred on the start.
            SpawnLongbowBlock(em, new float3(o.x - blockOffsetX, 0, blueZ), Cols, Rows, UnitSpacing, Faction.Blue, blueFacing);
            SpawnLongbowBlock(em, new float3(o.x + blockOffsetX, 0, blueZ), Cols, Rows, UnitSpacing, Faction.Blue, blueFacing);
            SpawnLongbowBlock(em, new float3(o.x - blockOffsetX, 0, redZ), Cols, Rows, UnitSpacing, Faction.Red, redFacing);
            SpawnLongbowBlock(em, new float3(o.x + blockOffsetX, 0, redZ), Cols, Rows, UnitSpacing, Faction.Red, redFacing);
        }

        /// <summary>
        /// Spawn one cols×rows block of Longbowmen centred on <paramref name="center"/>,
        /// each facing <paramref name="facing"/> and holding position so the
        /// formation stays put while the two lines trade fire (units auto-acquire
        /// enemies in LOS via TargetingSystem).
        /// </summary>
        private static void SpawnLongbowBlock(EntityManager em, float3 center, int cols, int rows,
            float spacing, Faction faction, quaternion facing)
        {
            float halfW = (cols - 1) * spacing * 0.5f;
            float halfD = (rows - 1) * spacing * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float3 pos = center + new float3(c * spacing - halfW, 0, r * spacing - halfD);
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);

                    var e = UnitFactory.Create(em, "Longbowman", pos, faction);
                    if (e == Entity.Null) continue;

                    if (em.HasComponent<LocalTransform>(e))
                    {
                        var xf = em.GetComponentData<LocalTransform>(e);
                        xf.Rotation = facing;
                        em.SetComponentData(e, xf);
                    }

                    // Hold the line — don't chase out-of-range enemies.
                    if (!em.HasComponent<HoldPositionTag>(e))
                        em.AddComponent<HoldPositionTag>(e);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Number of individual soldiers a single Alanthor battalion fans out
        /// into. The army scenarios author Alanthor troops one entity per
        /// battalion ("battalion model"); this expands each into a block of
        /// individuals so the scenarios reflect real squad sizes.
        /// </summary>
        private const int AlanthorBattalionSize = 20;

        /// <summary>
        /// True for Alanthor troop units that stand in for a battalion of
        /// soldiers (infantry / archers / cavalry). The Ballista is a single
        /// siege engine, not a battalion, so it is excluded.
        /// </summary>
        private static bool IsAlanthorBattalionUnit(string unitId)
        {
            return unitId == "Longbowman"
                || (unitId.StartsWith("Alanthor_") && unitId != "Alanthor_Catapult");
        }

        /// <summary>
        /// Spawn one army "slot". For an Alanthor battalion unit this fans out
        /// into a packed block of <see cref="AlanthorBattalionSize"/> individual
        /// soldiers centred on the slot (battalion → individuals); every other
        /// unit spawns as a single entity. Routed through by SpawnArmyRow /
        /// SpawnArmyGrid, so every battalion-model army scenario picks it up.
        /// </summary>
        private static void SpawnBattalion(EntityManager em, string unitId, float3 center, Faction faction)
        {
            if (!IsAlanthorBattalionUnit(unitId))
            {
                float3 single = center;
                single.y = TerrainUtility.GetHeight(single.x, single.z);
                UnitFactory.Create(em, unitId, single, faction);
                return;
            }

            // 5-wide x 4-deep packed block = 20 soldiers, centred on the slot.
            const int cols = 5;
            const int rows = AlanthorBattalionSize / cols; // 4
            const float spacing = 1.6f;
            float halfW = (cols - 1) * spacing * 0.5f;
            float halfD = (rows - 1) * spacing * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float3 p = center + new float3(c * spacing - halfW, 0, r * spacing - halfD);
                    p.y = TerrainUtility.GetHeight(p.x, p.z);
                    UnitFactory.Create(em, unitId, p, faction);
                }
            }
        }

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
                    SpawnBattalion(em, unitId, pos, faction);
                }
            }
        }

        /// <summary>
        /// Spawn a single row of units centered on the given position.
        /// </summary>
        private static void SpawnArmyRow(EntityManager em, string unitId, Faction faction,
            int count, float3 center)
        {
            for (int col = 0; col < count; col++)
            {
                float x = (col - (count - 1) * 0.5f) * ArmySpacing;
                float3 pos = center + new float3(x, 0, 0);
                SpawnBattalion(em, unitId, pos, faction);
            }
        }

        /// <summary>
        /// Resolve player 1's designed start position. Scenarios bypass the
        /// normal skirmish spawn path (SpawnDelayHelper → PlayerSpawnSystem),
        /// which is what scans the scene for PlayerStartMarkers, so a scenario
        /// that wants to sit on the player's start has to look the marker up
        /// itself — otherwise it defaults to world origin, which on water-heavy
        /// maps spawns everything underwater. Player 1 is the local player
        /// (Faction.Blue in scenarios). Falls back to the first available
        /// marker, then to origin. The returned position is snapped to terrain
        /// height.
        /// </summary>
        private static float3 GetPlayer1StartPosition()
        {
            // Scenarios don't run the marker scan; do it here so a hand-authored
            // map's PlayerStartMarkers are honoured.
            MapMarkerRegistry.Refresh();

            PlayerStartMarker marker =
                MapMarkerRegistry.FindPlayerMarker(GameSettings.LocalPlayerFaction);
            if (marker == null)
            {
                foreach (var m in MapMarkerRegistry.PlayerStarts)
                {
                    if (m != null) { marker = m; break; }
                }
            }

            float3 pos = float3.zero;
            if (marker != null)
            {
                var w = marker.WorldPosition;
                pos = new float3(w.x, 0f, w.z);
            }
            else
            {
                Debug.LogWarning(
                    "[ScenarioSetup] No PlayerStartMarker found — scenario stays " +
                    "centered on world origin (may be underwater).");
            }

            pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
            return pos;
        }

        /// <summary>
        /// Shift everything the active scenario just spawned by (ox, oz) so the
        /// layout — authored around world origin — sits on the player-1 start.
        /// Covers entity transforms plus the world positions held by pre-set
        /// move/attack/guard commands and the runtime spawner / patrol
        /// MonoBehaviours. Entities are flat (world-space LocalTransform, no
        /// Parent hierarchy — the presentation layer reads LocalTransform as the
        /// world position), so shifting every transform is safe and complete.
        /// Each shifted position is re-grounded to terrain height at its new XZ.
        /// </summary>
        private static void RecenterScenario(EntityManager em, float ox, float oz)
        {
            // 1. Every entity transform.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadWrite<LocalTransform>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    var xf = em.GetComponentData<LocalTransform>(e);
                    xf.Position = Reground(xf.Position, ox, oz);
                    em.SetComponentData(e, xf);
                }
            }

            // 2. Pre-set move / attack / guard targets, so commanded armies head
            //    to the re-centered battle instead of marching back to origin.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadWrite<DesiredDestination>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    var c = em.GetComponentData<DesiredDestination>(e);
                    if (c.Has == 0) continue;
                    c.Position = Reground(c.Position, ox, oz);
                    em.SetComponentData(e, c);
                }
            }
            {
                var q = em.CreateEntityQuery(ComponentType.ReadWrite<AttackMoveCommand>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    var c = em.GetComponentData<AttackMoveCommand>(e);
                    c.Destination = Reground(c.Destination, ox, oz);
                    em.SetComponentData(e, c);
                }
            }
            {
                var q = em.CreateEntityQuery(ComponentType.ReadWrite<GuardPoint>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    var c = em.GetComponentData<GuardPoint>(e);
                    if (c.Has == 0) continue;
                    c.Position = Reground(c.Position, ox, oz);
                    em.SetComponentData(e, c);
                }
            }

            // 3. Runtime spawners / patrol routes (MonoBehaviours, not entities).
            foreach (var sp in UnityEngine.Object.FindObjectsByType<ScenarioWaveSpawner>(
                         FindObjectsSortMode.None))
            {
                float3 c = Reground(new float3(sp.Center.x, sp.Center.y, sp.Center.z), ox, oz);
                sp.Center = new Vector3(c.x, c.y, c.z);
            }
            foreach (var pc in UnityEngine.Object.FindObjectsByType<ScenarioPatrolController>(
                         FindObjectsSortMode.None))
            {
                foreach (var unit in pc.Units)
                {
                    if (unit.Waypoints == null) continue;
                    for (int i = 0; i < unit.Waypoints.Length; i++)
                    {
                        var wp = unit.Waypoints[i];
                        float3 g = Reground(new float3(wp.x, wp.y, wp.z), ox, oz);
                        unit.Waypoints[i] = new Vector3(g.x, g.y, g.z);
                    }
                }
            }
        }

        /// <summary>Offset an XZ position and re-snap its Y to terrain height.</summary>
        private static float3 Reground(float3 p, float ox, float oz)
        {
            p.x += ox;
            p.z += oz;
            p.y = TerrainUtility.GetHeight(p.x, p.z);
            return p;
        }
    }
}
