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
                case ScenarioType.CurseCombatTest:
                    SpawnCurseCombatTest(em);
                    break;
                case ScenarioType.PatrolDefense:
                    SpawnPatrolDefense(em);
                    break;
                case ScenarioType.AlanthorVsCrystal:
                    SpawnAlanthorVsCrystal(em);
                    break;
            }

            GameCamera.FocusOn(Vector3.zero, instant: true);
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
                    UnitFactory.Create(em, "Swordsman", pos, Faction.Red);
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
            const float ColSpacing = 16f;
            const float RowZSpacing = 24f;

            // Each row uses a distinct faction. Set the per-faction culture so
            // procedural building generators render the correct culture treatment
            // (FactionColors.GetFactionCulture is what feeds into culture overlays).
            FactionColors.SetFactionCulture(Faction.Blue,   Cultures.None);
            FactionColors.SetFactionCulture(Faction.Teal,   Cultures.None);
            FactionColors.SetFactionCulture(Faction.Green,  Cultures.Runai);
            FactionColors.SetFactionCulture(Faction.Yellow, Cultures.Feraldis);
            FactionColors.SetFactionCulture(Faction.Red,    Cultures.Alanthor);

            // Each culture row leads with the four culture-aware Era 1 buildings
            // (Hall, Hut, GatherersHut, Barracks) so the four cultural variants
            // line up vertically across rows for side-by-side comparison.
            var rows = new (Faction faction, string[] buildings)[]
            {
                // Era 1 generic (no culture)
                (Faction.Blue, new[] { "Hall", "Hut", "GatherersHut", "Barracks" }),
                // Era 2 pre-culture choice buildings (no culture yet)
                (Faction.Teal, new[] { "ShrineOfAhridan", "TempleOfRidan", "VaultOfAlmierra" }),
                // Runai (Runai_TradingPost omitted — reuses Alanthor_PracticeRange presentation)
                (Faction.Green, new[] {
                    "Hall", "Hut", "GatherersHut", "Barracks",
                    "ThessarasBazaar", "Runai_Outpost", "Runai_TradeHub",
                    "Runai_Vault", "Runai_VeilsteelFoundry", "Runai_SiegeWorkshop"
                }),
                // Feraldis
                (Faction.Yellow, new[] {
                    "Hall", "Hut", "GatherersHut", "Barracks",
                    "FiendstoneKeep", "Feraldis_HuntingLodge", "Feraldis_LoggingStation",
                    "Feraldis_Foundry", "Feraldis_Tower", "Feraldis_Longhouse", "Feraldis_SiegeYard"
                }),
                // Alanthor
                (Faction.Red, new[] {
                    "Hall", "Hut", "GatherersHut", "Barracks",
                    "KingsCourt", "Alanthor_Wall", "Alanthor_Tower", "Alanthor_PracticeRange",
                    "Alanthor_SiegeYard", "Alanthor_Smelter", "Alanthor_Crucible"
                }),
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
                    BuildingFactory.Create(em, buildings[c], pos, faction);
                }
            }
        }

        /// <summary>
        /// Crystal Curse Combat Test: five attacker/target pairs, each row a
        /// single Curse unit hitting an "invincible" Hall (HP = 1e9) so the
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
        private static void SpawnCurseCombatTest(EntityManager em)
        {
            // Camera focuses on origin (middle row by default).
            SpawnCurseTestPair(em, "Crystalling",  5f,  -70f);
            SpawnCurseTestPair(em, "Veilstinger", 24f, -35f);
            SpawnCurseTestPair(em, "Veilstinger", 16f,   0f);
            SpawnCurseTestPair(em, "Godsplinter", 22f,  35f);
            SpawnCurseTestPair(em, "Godsplinter", 13f,  70f);

            // Starter crystal patch placed between rows 3 (z=0) and 4 (z=35),
            // off the firing line at x=0 z=17.5 so it sits in clear camera view.
            // Lets us watch CursePendingPileSystem grow the patch past 45 nodes
            // and convert it into a secondary curse location.
            SpawnBattleSiteCrystalPatch(em, new float3(0f, 0f, 17.5f));
        }

        /// <summary>
        /// Spawn one row of the Curse Combat Test: an invincible invisible
        /// dummy on the right and a Red Curse unit on the left. The dummy
        /// has just enough ECS components for TargetingSystem to lock onto
        /// it (LocalTransform + FactionTag + BuildingTag + Health) and no
        /// PresentationId, so the projectile and its impact VFX play out in
        /// full view rather than being clipped by a building mesh.
        /// </summary>
        private static void SpawnCurseTestPair(EntityManager em, string unitId, float distance, float rowZ)
        {
            float halfDist = distance * 0.5f;

            // Invincible invisible target on the right.
            float3 dummyPos = new float3(halfDist, 0f, rowZ);
            dummyPos.y = TerrainUtility.GetHeight(dummyPos.x, dummyPos.z);
            CreateInvincibleDummy(em, dummyPos, Faction.Blue);

            // Attacker: Curse unit on the left, facing the dummy.
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
        /// Seed a starter resource patch one cadaver short of the conversion
        /// threshold (44 nodes — see CursePendingPileSystem.PatchConvertNodeThreshold = 45).
        /// Each node is filled to MaxCrystalPerNode (60) so the very first
        /// curse-unit-death payout has no top-up room and is forced to spawn
        /// a new node, tipping the patch to 45 and triggering its conversion
        /// into a secondary curse location. Lets us watch the whole pipeline
        /// in one short test session.
        /// </summary>
        private static void SpawnBattleSiteCrystalPatch(EntityManager em, float3 center)
        {
            center.y = TerrainUtility.GetHeight(center.x, center.z);

            const int NodeCount = 44;
            const int CrystalPerNode = 60;  // = CursePendingPileSystem.MaxCrystalPerNode

            // Hex grid for an even, packed cluster — same approach as
            // CrystalPatchBootstrap's near patch. 3 rings yield 37 slots;
            // 4 rings yield 61; we walk slot-by-slot until we've placed 44.
            const int Rings = 4;
            const float Spacing = 2.6f;     // hex-cell spacing; outermost ring at radius ~10.4 m, well under PatchClusterRadius = 12 so the whole grid stays one patch
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

            UnityEngine.Debug.Log($"[ScenarioSetup] seeded {placed}-node resource patch at ({center.x:F1}, {center.z:F1}) — one curse-unit death will tip it past {placed + 1}.");

            static void PlaceNode(EntityManager em, float3 center, float ox, float oz)
            {
                float x = center.x + ox;
                float z = center.z + oz;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.Create(em, new float3(x, y, z), CrystalPerNode);
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
            spawner.UnitId = "Swordsman";
            spawner.SoldierFaction = Faction.Blue;

            // Starter 44-node resource patch east of the patrol ring (just past
            // the 35 m outer spawn) so the curse-unit-death payouts have
            // somewhere to deposit. See CursePendingPileSystem.
            SpawnBattleSiteCrystalPatch(em, new float3(50f, 0f, 0f));
        }

        /// <summary>
        /// Alanthor Vs Crystal Horde: 2 battalions of each Alanthor battalion-tier
        /// unit (Sentinel / Crossbowman / Cataphract = 6 battalions total) on the
        /// south side facing a 50-unit Crystal horde — 30 Crystallings, 15
        /// Veilstingers, 5 Godsplinters — on the north side. Both armies
        /// attack-move toward the centre on spawn.
        /// </summary>
        private static void SpawnAlanthorVsCrystal(EntityManager em)
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
            AttackMoveAllBattalions(em, Faction.Red, center);

            // ── Crystal Horde (Blue, north) ──
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

            // Starter 44-node resource patch east of the battle line so the
            // curse-unit-death payouts have somewhere to deposit. The Alanthor
            // army will be killing Crystallings/Veilstingers/Godsplinters as
            // the armies engage, which is exactly what feeds CursePendingPile.
            SpawnBattleSiteCrystalPatch(em, new float3(40f, 0f, 0f));
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
