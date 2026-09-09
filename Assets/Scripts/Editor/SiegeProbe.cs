// SiegeProbe.cs
// Editor-only diagnostics, driven by the agent bridge via `menu` and readable
// from the Console. Three tools:
//
//   Launch WallSiege Scenario / Launch Quick Skirmish — start a match from
//   inside play mode the way the menus do. Opening a scenario or map scene
//   directly in the editor spawns nothing: the match settings are statics the
//   menu sets before the scene loads, and the domain reload on Play resets
//   them, so they have to be set from within the running player.
//
//   Siege Probe — every siege unit's firing state every two seconds: what it
//   is aiming at, how far, whether it is standing, how its aim and reload
//   timers read, and how many shots are in the air. "The catapults are not
//   attacking" has a dozen possible causes; this is cheaper than guessing.
//
//   Match Probe — the match as a whole every ten seconds: units per faction,
//   what every busy production queue is working on, the curse waves (how many
//   members, how many still in formation, how many fighting) and the
//   Crystalling packs.

using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using System.Globalization;
using System.IO;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.EditorTools
{
    public static class SiegeProbe
    {
        // ── Launchers ──────────────────────────────────────────────────────

        [MenuItem("Waning Border/Debug/Launch WallSiege Scenario")]
        public static void LaunchWallSiege()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode (any scene) first");
                return;
            }
            string scene = ScenarioCatalog.Prepare(ScenarioType.WallSiege);
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            Debug.Log("[SiegeProbe] loading " + scene);
        }

        /// <summary>
        /// The arrow-tip ladder, four lanes side by side. Same launch shape as
        /// the WallSiege one above: the scenario's match settings are statics
        /// the menu sets before the scene loads, so opening
        /// Scenario_ArrowTrails.unity directly spawns nothing at all.
        /// </summary>
        [MenuItem("Waning Border/Debug/Launch Arrow Trails Scenario")]
        public static void LaunchArrowTrails()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode (any scene) first");
                return;
            }
            string scene = ScenarioCatalog.Prepare(ScenarioType.ArrowTrails);
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            Debug.Log("[SiegeProbe] loading " + scene +
                      " — Stone / Iron / Veilstone / Shard, left to right");
        }

        /// <summary>
        /// Human (Blue) vs one Normal AI on Hollow Table, curse on, fog off —
        /// SkirmishPanel.StartGame with the roster it would build by default.
        /// </summary>
        [MenuItem("Waning Border/Debug/Launch Quick Skirmish (HollowTable vs 1 AI)")]
        public static void LaunchQuickSkirmish()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode (any scene) first");
                return;
            }

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            GameSettings.SelectedMapScene = "HollowTable";
            GameSettings.SpawnSeed = 20260907;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            LobbyConfig.ApplyColorSelections();

            Debug.Log("[SiegeProbe] launching quick skirmish on HollowTable, seed " + GameSettings.SpawnSeed);
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        /// <summary>
        /// The AI-army test bed: a HARD AI (first attack at 240 s) with the
        /// curse off, so its army is not eaten before it can march. Same map
        /// and seed as the quick skirmish.
        /// </summary>
        [MenuItem("Waning Border/Debug/Launch Quick Skirmish (Hard AI, no curse)")]
        public static void LaunchHardSkirmish()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode (any scene) first");
                return;
            }

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            LobbyConfig.Slots[1].AIDifficulty = LobbyAIDifficulty.Hard;
            GameSettings.SelectedMapScene = "HollowTable";
            GameSettings.SpawnSeed = 20260907;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = false;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            LobbyConfig.ApplyColorSelections();

            Debug.Log("[SiegeProbe] launching HARD skirmish (no curse) on HollowTable, seed " + GameSettings.SpawnSeed);
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        /// <summary>
        /// Four Hard AIs, free-for-all, on the 4-player map. Nobody holds a
        /// seat — every slot is an AI and the local player observes, which is
        /// what SkirmishPanel does for its observer mode.
        ///
        /// FFA needs no team setup: a slot's TeamIndex defaults to
        /// Alliances.NoTeam, which the hostility table reads as "fights alone,
        /// hostile to everyone" (docs/Design/Teams.md).
        /// </summary>
        [MenuItem("Waning Border/Debug/Launch 4-AI FFA (Sundered Crown)")]
        public static void LaunchFfaFourAI()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FFA] enter play mode (any scene) first");
                return;
            }

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 4;
            LobbyConfig.SetupSinglePlayer(4);
            for (int i = 0; i < 4; i++)
            {
                var slot = LobbyConfig.Slots[i];
                slot.Type = SlotType.AI;                 // slot 0 too — nobody plays
                slot.AIDifficulty = LobbyAIDifficulty.Hard;
                slot.TeamIndex = Alliances.NoTeam;       // every faction alone
                slot.StartIndex = PlayerSlot.AutoStart;
            }
            GameSettings.SelectedMapScene = "SunderedCrown";
            GameSettings.SpawnSeed = 20260908;
            GameSettings.FogOfWarEnabled = true;         // the AI's recon path is part of the test
            GameSettings.BorderEnabled = true;           // curse on: the shipped setting
            GameSettings.IsObserver = true;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            LobbyConfig.ApplyColorSelections();

            Debug.Log("[FFA] launching 4-AI free-for-all on SunderedCrown, seed "
                      + GameSettings.SpawnSeed + " (Hard x4, curse on, fog on)");
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        // ── FFA watch ──────────────────────────────────────────────────────
        // Runs until the simulation decides a winner. One table per interval
        // into FFA.log beside the match logs: what each faction holds, what it
        // banks, and — the thing the extractor fixes were about — how many
        // income buildings it actually raised. Eliminations and the verdict
        // are stamped as they happen.

        private const float FfaLogInterval = 15f;
        private static bool _ffaArmed;
        private static float _ffaNext;
        private static StreamWriter _ffa;
        private static readonly HashSet<string> _ffaSeenElim = new HashSet<string>();

        [MenuItem("Waning Border/Debug/FFA Watch (to a sole survivor)")]
        public static void StartFfaWatch()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FFA] enter play mode in a match first");
                return;
            }
            CloseFfa();
            _ffa = new StreamWriter(MatchLogSession.File("FFA.log"), false);
            _ffa.WriteLine("=== 4-AI free-for-all watch — one row per faction every "
                           + (int)FfaLogInterval + "s ===");
            _ffa.WriteLine("time  faction  units  bldgs halls terr  huts hutLv hallLv mines vmines smelt  "
                           + "supplies iron veilstone veilsteel  army(form/fight)");
            _ffa.Flush();
            _ffaSeenElim.Clear();
            _ffaNext = 0f;
            if (!_ffaArmed)
            {
                EditorApplication.update += FfaTick;
                _ffaArmed = true;
            }
            Debug.Log("[FFA] watching until a sole survivor — " + MatchLogSession.File("FFA.log"));
        }

        private static void FfaTick()
        {
            if (!EditorApplication.isPlaying)
            {
                WFfa("-- play mode ended before a verdict --");
                StopFfaWatch();
                return;
            }
            if (!TryWorld(out var em)) return;
            float t = Time.timeSinceLevelLoad;
            if (t < _ffaNext) return;
            _ffaNext = t + FfaLogInterval;

            // The sim-side verdict (EliminationSystem) — deterministic, and the
            // same one the victory banner reads.
            var vq = em.CreateEntityQuery(ComponentType.ReadOnly<MatchVerdictState>());
            bool decided = false;
            Faction winner = default;
            using (var ents = vq.ToEntityArray(Allocator.Temp))
            {
                if (ents.Length > 0)
                {
                    var v = em.GetComponentData<MatchVerdictState>(ents[0]);
                    decided = v.Decided != 0;
                    winner = v.Winner;
                    if (em.HasBuffer<EliminatedFactionRecord>(ents[0]))
                    {
                        var buf = em.GetBuffer<EliminatedFactionRecord>(ents[0]);
                        for (int i = 0; i < buf.Length; i++)
                        {
                            string key = buf[i].Value + "@" + buf[i].AtSimSeconds.ToString("0");
                            if (!_ffaSeenElim.Add(key)) continue;
                            WFfa(Clock(buf[i].AtSimSeconds) + "  *** " + buf[i].Value
                                 + " ELIMINATED (" + _ffaSeenElim.Count + " of 4 down) ***");
                            Debug.Log("[FFA] " + buf[i].Value + " eliminated at "
                                      + Clock(buf[i].AtSimSeconds));
                        }
                    }
                }
            }
            vq.Dispose();

            for (int f = 0; f < GameSettings.TotalPlayers; f++)
                WFfa(FactionRow(em, (Faction)f, t));
            WFfa("");
            _ffa.Flush();

            if (decided)
            {
                WFfa("=== VERDICT: " + winner + " is the sole survivor, at " + Clock(t) + " ===");
                Debug.Log("[FFA] VERDICT — " + winner + " wins at " + Clock(t));
                StopFfaWatch();
            }
        }

        private static string FactionRow(EntityManager em, Faction faction, float t)
        {
            int units = 0, form = 0, fight = 0;
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                    units++;
                    if (em.HasComponent<FormationMemberState>(ents[i])) form++;
                    if (em.HasComponent<Target>(ents[i])
                        && em.GetComponentData<Target>(ents[i]).Value != Entity.Null) fight++;
                }
            }

            // hutLevels / hallLevel are what the 2026-09-08 income model pays on:
            // a slot doubles per hut level and the Hall doubles the territory.
            int bldgs = 0, halls = 0, huts = 0, mines = 0, vmines = 0, smelt = 0;
            int hutLevels = 0, hallLevel = 0;
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                    bldgs++;
                    if (em.HasComponent<HallTag>(e)) halls++;
                    if (em.HasComponent<GathererHutTag>(e) && !em.HasComponent<RaiderCampTag>(e))
                    {
                        huts++;
                        hutLevels += LevelOf(em, e);
                    }
                    if (em.HasComponent<HallTag>(e)) hallLevel = math.max(hallLevel, LevelOf(em, e));
                    if (em.HasComponent<MineTag>(e)) mines++;
                    if (em.HasComponent<VeilstoneMineTag>(e)) vmines++;
                    if (em.HasComponent<SmelterTag>(e)) smelt++;
                }
            }

            int terr = TheWaningBorder.World.Regions.TerritoryOwnership.Ready
                ? TheWaningBorder.World.Regions.TerritoryOwnership.TerritoriesOf(faction).Count : 0;

            int sup = 0, iron = 0, veil = 0, vsteel = 0;
            if (TheWaningBorder.Economy.FactionEconomy.TryGetResources(em, faction, out var res))
            {
                sup = res.Supplies; iron = res.Iron; veil = res.Veilstone; vsteel = res.Veilsteel;
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0}  {1,-7} {2,5} {3,5} {4,5} {5,4}  {6,4} {7,5} {8,6} {9,5} {10,6} {11,5}  "
                + "{12,8} {13,6} {14,9} {15,9}  {16,3}/{17,-3}",
                Clock(t), faction, units, bldgs, halls, terr,
                huts, hutLevels, hallLevel, mines, vmines, smelt, sup, iron, veil, vsteel, form, fight);
        }

        private static void StopFfaWatch()
        {
            EditorApplication.update -= FfaTick;
            _ffaArmed = false;
            CloseFfa();
        }

        private static void CloseFfa()
        {
            if (_ffa == null) return;
            _ffa.Flush();
            _ffa.Dispose();
            _ffa = null;
        }

        private static void WFfa(string line) { _ffa?.WriteLine(line); }

        /// <summary>A built building with no upgrade state is level 1, not 0.</summary>
        private static int LevelOf(EntityManager em, Entity e)
            => em.HasComponent<BuildingUpgradeState>(e)
                ? math.max(1, em.GetComponentData<BuildingUpgradeState>(e).Level) : 1;

        private static string Clock(float s)
        {
            int m = (int)(s / 60f);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, (int)(s - m * 60));
        }

        /// <summary>
        /// Raise Blue's wrath by one, as if Blue had woken a well (design
        /// §2.10). The curse only answers provocation now, so without this
        /// there is no way to exercise the wave path in a probe match short of
        /// teching a whole culture to its ritualist.
        ///
        /// Run it repeatedly to climb the tier ladder and watch the waves grow
        /// and re-aim; then stop, and watch wrath cool back down.
        /// </summary>
        [MenuItem("Waning Border/Debug/Provoke Curse (Blue +1 wrath)")]
        public static void ProvokeCurseBlue()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Wrath] enter play mode in a match first");
                return;
            }
            var settings = TheWaningBorder.Data.Border.BorderSettings.Get();
            int cap = settings != null ? settings.TierCount : 0;
            TheWaningBorder.Systems.Border.CurseWrath.Provoke(
                Faction.Blue, Time.timeSinceLevelLoad, cap, "debug probe");
            Debug.Log("[Wrath] Blue wrath is now "
                      + TheWaningBorder.Systems.Border.CurseWrath.LevelOf(Faction.Blue));
        }

        /// <summary>
        /// The unit sandbox: an empty flat scenario driven entirely by
        /// SandboxPanel — place units and buildings, paint terrain, and cast
        /// every ability in the game from its Abilities tab.
        /// </summary>
        [MenuItem("Waning Border/Debug/Launch Sandbox")]
        public static void LaunchSandbox()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Sandbox] enter play mode (any scene) first");
                return;
            }

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.Scenario;
            GameSettings.ActiveScenario = ScenarioType.Sandbox;
            GameSettings.TotalPlayers = 1;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.IsObserver = false;
            GameSettings.TutorialActive = false;
            GameSettings.FogOfWarEnabled = false;   // a sandbox should hide nothing
            GameSettings.BorderEnabled = false;

            Debug.Log("[Sandbox] launching the unit sandbox");
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        [MenuItem("Waning Border/Debug/Sandbox: Open Abilities Tab")]
        public static void OpenSandboxAbilities()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Sandbox] play mode first"); return; }
            var panel = Object.FindFirstObjectByType<TheWaningBorder.UI.Ingame.SandboxPanel>();
            if (panel == null) { Debug.LogWarning("[Sandbox] no SandboxPanel — launch the sandbox first."); return; }
            panel.OpenAbilitiesTab();
            Debug.Log("[Sandbox] Abilities tab opened.");
        }

        // ── Siege probe ────────────────────────────────────────────────────

        private const double SiegeSeconds = 30.0;
        private const double SiegePeriod = 2.0;
        private static double _siegeUntil, _siegeNext;
        private static bool _siegeArmed;

        [MenuItem("Waning Border/Debug/Siege Probe (30 s)")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SiegeProbe] enter play mode first");
                return;
            }
            _siegeUntil = EditorApplication.timeSinceStartup + SiegeSeconds;
            _siegeNext = 0.0;
            if (!_siegeArmed)
            {
                EditorApplication.update += SiegeTick;
                _siegeArmed = true;
            }
            Debug.Log("[SiegeProbe] armed for 30 s");
        }

        private static void SiegeTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _siegeUntil)
            {
                EditorApplication.update -= SiegeTick;
                _siegeArmed = false;
                Debug.Log("[SiegeProbe] done");
                return;
            }
            if (EditorApplication.timeSinceStartup < _siegeNext) return;
            _siegeNext = EditorApplication.timeSinceStartup + SiegePeriod;

            if (!TryWorld(out var em)) return;

            // One-shot editor queries, disposed: not a repeating sim path.
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SiegeTag>(),
                ComponentType.ReadOnly<ArcherState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();

            var sb = new StringBuilder();
            sb.Append("[SiegeProbe] units=").Append(Count<UnitTag>(em))
              .Append(" buildings=").Append(Count<BuildingTag>(em))
              .Append(" siege=").Append(ents.Length)
              .Append(" projectiles=").Append(Count<Projectile>(em))
              .Append(" stones=").Append(Count<CatapultShotTag>(em));

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var a = em.GetComponentData<ArcherState>(e);
                var p = em.GetComponentData<LocalTransform>(e).Position;

                sb.Append('\n').Append("  ").Append(Name(em, e)).Append('/').Append(FactionOf(em, e))
                  .Append(" at (").Append(p.x.ToString("0")).Append(',').Append(p.z.ToString("0")).Append(')');

                var tgt = em.HasComponent<Target>(e) ? em.GetComponentData<Target>(e).Value : Entity.Null;
                if (tgt != Entity.Null && em.Exists(tgt))
                {
                    float dist = em.HasComponent<LocalTransform>(tgt)
                        ? math.distance(p, em.GetComponentData<LocalTransform>(tgt).Position) : -1f;
                    int hp = em.HasComponent<Health>(tgt) ? em.GetComponentData<Health>(tgt).Value : -1;
                    sb.Append(" target=").Append(Name(em, tgt)).Append(" d=").Append(dist.ToString("0.0"))
                      .Append(" hp=").Append(hp);
                }
                else sb.Append(" target=none");

                bool moving = em.HasComponent<DesiredDestination>(e)
                    && em.GetComponentData<DesiredDestination>(e).Has != 0;
                sb.Append(" moving=").Append(moving ? 1 : 0)
                  .Append(" aim=").Append(a.AimTimer.ToString("0.00")).Append('/').Append(a.AimTimeRequired.ToString("0.00"))
                  .Append(" cd=").Append(a.CooldownTimer.ToString("0.00"))
                  .Append(" firing=").Append(a.IsFiring)
                  .Append(" retreat=").Append(a.IsRetreating)
                  .Append(" range=").Append(a.MinRange.ToString("0")).Append('-').Append(a.MaxRange.ToString("0"))
                  .Append(" traj=").Append(a.Trajectory);
            }

            Debug.Log(sb.ToString());
        }

        // ── Match probe ────────────────────────────────────────────────────

        private const double MatchSeconds = 240.0;
        private const double MatchPeriod = 10.0;
        private static double _matchUntil, _matchNext;
        private static bool _matchArmed;

        [MenuItem("Waning Border/Debug/Match Probe (240 s)")]
        public static void StartMatchProbe()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[MatchProbe] enter play mode first");
                return;
            }
            _matchUntil = EditorApplication.timeSinceStartup + MatchSeconds;
            _matchNext = 0.0;
            if (!_matchArmed)
            {
                EditorApplication.update += MatchTick;
                _matchArmed = true;
            }
            Debug.Log("[MatchProbe] armed for 240 s");
        }

        private static void MatchTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _matchUntil)
            {
                EditorApplication.update -= MatchTick;
                _matchArmed = false;
                Debug.Log("[MatchProbe] done");
                return;
            }
            if (EditorApplication.timeSinceStartup < _matchNext) return;
            _matchNext = EditorApplication.timeSinceStartup + MatchPeriod;

            if (!TryWorld(out var em)) return;

            var sb = new StringBuilder();
            sb.Append("[MatchProbe] t=").Append(Time.timeSinceLevelLoad.ToString("0")).Append("s units:");

            // Units per faction, with cohesion: how many are travelling, how
            // many of those inside a formation, how many fighting.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                q.Dispose();
                var per = new Dictionary<Faction, int[]>();   // total, moving, formed, fighting
                for (int i = 0; i < ents.Length; i++)
                {
                    if (!per.TryGetValue(facs[i].Value, out var c)) per[facs[i].Value] = c = new int[4];
                    var e = ents[i];
                    c[0]++;
                    if (em.HasComponent<DesiredDestination>(e) && em.GetComponentData<DesiredDestination>(e).Has != 0) c[1]++;
                    if (em.HasComponent<FormationMemberState>(e)) c[2]++;
                    if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) c[3]++;
                }
                foreach (var kv in per)
                    sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value[0])
                      .Append("(mv").Append(kv.Value[1]).Append(" fm").Append(kv.Value[2])
                      .Append(" ft").Append(kv.Value[3]).Append(')');
            }

            // Production: every busy queue, what it is on.
            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<ProductionState>(),
                    ComponentType.ReadOnly<ProductionQueueItem>(),
                    ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                int busy = 0, train = 0, research = 0, upgrade = 0, queued = 0;
                var lines = new List<string>();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    var ps = em.GetComponentData<ProductionState>(e);
                    var buf = em.GetBuffer<ProductionQueueItem>(e);
                    queued += buf.Length;
                    if (ps.Busy == 0 || buf.Length == 0) continue;
                    busy++;
                    var head = buf[0];
                    switch (head.Kind)
                    {
                        case ProductionKind.Train: train++; break;
                        case ProductionKind.Research: research++; break;
                        default: upgrade++; break;
                    }
                    if (lines.Count < 8)
                        lines.Add("  " + FactionOf(em, e) + " " + Name(em, e) + ": " + head.Kind + " "
                                  + (head.Kind == ProductionKind.BuildingUpgrade ? "L" + head.Level : head.Id.ToString())
                                  + " " + (ps.Total - ps.Remaining).ToString("0.0") + "/" + ps.Total.ToString("0.0")
                                  + "s, " + buf.Length + " queued");
                }
                sb.Append(" | production busy=").Append(busy)
                  .Append(" (train ").Append(train).Append(", research ").Append(research)
                  .Append(", upgrade ").Append(upgrade).Append(") queued=").Append(queued);
                foreach (var l in lines) sb.Append('\n').Append(l);
            }

            // Curse waves.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<CurseWaveMember>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                int inFormation = 0, fighting = 0, marching = 0;
                var waves = new HashSet<int>();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    waves.Add(em.GetComponentData<CurseWaveMember>(e).WaveId);
                    if (em.HasComponent<FormationMemberState>(e)) inFormation++;
                    if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) fighting++;
                    if (em.HasComponent<DesiredDestination>(e) && em.GetComponentData<DesiredDestination>(e).Has != 0) marching++;
                }
                sb.Append("\n  curse: waves=").Append(waves.Count).Append(" members=").Append(ents.Length)
                  .Append(" inFormation=").Append(inFormation).Append(" marching=").Append(marching)
                  .Append(" fighting=").Append(fighting)
                  .Append(" groups=").Append(Count<FormationGroup>(em));
            }

            // Crystalling packs.
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<CrystallingPack>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                int maxPack = 0, buffed = 0;
                float maxBonus = 0f;
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    maxPack = math.max(maxPack, em.GetComponentData<CrystallingPack>(e).Size);
                    if (em.HasComponent<BorderBuff>(e))
                    {
                        float b = em.GetComponentData<BorderBuff>(e).AttBonus;
                        if (b > 0f) buffed++;
                        maxBonus = math.max(maxBonus, b);
                    }
                }
                sb.Append("\n  packs: crystallings=").Append(ents.Length).Append(" maxPack=").Append(maxPack)
                  .Append(" buffed=").Append(buffed).Append(" maxBonus=+").Append((maxBonus * 100f).ToString("0")).Append('%');
            }

            Debug.Log(sb.ToString());
        }

        // ── Map trace ──────────────────────────────────────────────────────
        // Every half second of match time: each unit's position and state
        // (moving / in formation / fighting), each building on first sight,
        // deaths as they happen, and the map's fixed features once. Written
        // beside the match logs as MapTrace.txt; feeds the replay-on-a-map
        // visualisation. Lines: R region, N node, B building, U unit,
        // P position sample, D death.

        private const double TraceSeconds = 900.0;   // wall seconds; the match runs at ~0.8x wall under editor frame spikes
        private const float TracePeriod = 0.5f;
        private static double _traceUntil;
        private static float _traceNextGame;
        private static bool _traceArmed;
        private static StreamWriter _trace;
        private static readonly HashSet<string> _traceSeen = new HashSet<string>();
        private static HashSet<string> _traceAlive = new HashSet<string>();

        [MenuItem("Waning Border/Debug/Map Trace (900 s)")]
        public static void StartMapTrace()
        {
            if (!EditorApplication.isPlaying || !TryWorld(out var em))
            {
                Debug.LogWarning("[MapTrace] enter play mode in a match first");
                return;
            }
            CloseTrace();
            string path = MatchLogSession.File("MapTrace.txt");
            _trace = new StreamWriter(path, false);
            _traceSeen.Clear();
            _traceAlive.Clear();
            _traceUntil = EditorApplication.timeSinceStartup + TraceSeconds;
            _traceNextGame = 0f;

            if (RegionMap.Ready)
                for (int i = 0; i < RegionMap.Count; i++)
                {
                    var seed = RegionMap.SeedOf(i);
                    W("R", i, Q(RegionMap.NameOf(i)), F(seed.x), F(seed.y));
                }
            WriteNodes<IronDepositState>(em, "iron");
            WriteNodes<VeilstoneOutcroppingTag>(em, "veilstone");
            WriteNodes<SupplyNodeTag>(em, "supply");
            WriteNodes<BorderMainNodeTag>(em, "well");
            _trace.Flush();

            if (!_traceArmed)
            {
                EditorApplication.update += TraceTick;
                _traceArmed = true;
            }
            Debug.Log("[MapTrace] writing " + path);
        }

        private static void WriteNodes<T>(EntityManager em, string kind) where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();
            for (int i = 0; i < ents.Length; i++)
            {
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                W("N", Key(em, ents[i]), kind, F(p.x), F(p.z));
            }
        }

        private static void TraceTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _traceUntil)
            {
                EditorApplication.update -= TraceTick;
                _traceArmed = false;
                CloseTrace();
                Debug.Log("[MapTrace] done");
                return;
            }
            if (!TryWorld(out var em)) return;
            float t = Time.timeSinceLevelLoad;
            if (t < _traceNextGame) return;
            _traceNextGame = t + TracePeriod;

            var alive = new HashSet<string>();
            string ts = F(t);

            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<BuildingTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    string id = Key(em, e);
                    alive.Add(id);
                    if (!_traceSeen.Add(id)) continue;
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    float w = 4f, h = 4f;
                    if (em.HasComponent<BuildingSize>(e))
                    {
                        var bs = em.GetComponentData<BuildingSize>(e);
                        w = bs.Width; h = bs.Height;
                    }
                    W("B", ts, id, FactionOf(em, e), Q(Name(em, e)), F(p.x), F(p.z), F(w), F(h));
                }
            }
            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                q.Dispose();
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    string id = Key(em, e);
                    alive.Add(id);
                    if (_traceSeen.Add(id)) W("U", ts, id, FactionOf(em, e), Q(Name(em, e)));
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    int flags = 0;
                    if (em.HasComponent<DesiredDestination>(e) && em.GetComponentData<DesiredDestination>(e).Has != 0) flags |= 1;
                    if (em.HasComponent<FormationMemberState>(e)) flags |= 2;
                    if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) flags |= 4;
                    W("P", ts, id, F(p.x), F(p.z), flags);
                }
            }
            foreach (var id in _traceAlive)
                if (!alive.Contains(id)) W("D", ts, id);
            _traceAlive = alive;
            _trace.Flush();
        }

        private static void CloseTrace()
        {
            if (_trace == null) return;
            _trace.Flush();
            _trace.Dispose();
            _trace = null;
        }

        private static string Key(EntityManager em, Entity e)
            => em.HasComponent<NetworkedEntity>(e)
                ? "n" + em.GetComponentData<NetworkedEntity>(e).NetworkId.ToString(CultureInfo.InvariantCulture)
                : e.Index.ToString(CultureInfo.InvariantCulture) + "v" + e.Version.ToString(CultureInfo.InvariantCulture);

        private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string Q(string s) => (s ?? "?").Replace(' ', '_');
        private static void W(params object[] parts) => _trace?.WriteLine(string.Join(" ", parts));

        // ── Helpers ────────────────────────────────────────────────────────

        private static bool TryWorld(out EntityManager em)
        {
            em = default;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            em = world.EntityManager;
            return true;
        }

        private static int Count<T>(EntityManager em) where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            int n = q.CalculateEntityCount();
            q.Dispose();
            return n;
        }

        private static string Name(EntityManager em, Entity e)
        {
            if (em.HasComponent<UnitTypeId>(e)) return em.GetComponentData<UnitTypeId>(e).Value.ToString();
            if (em.HasComponent<DisplayName>(e)) return em.GetComponentData<DisplayName>(e).Value.ToString();
            return em.HasComponent<BuildingTag>(e) ? "building" : "?";
        }

        private static string FactionOf(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value.ToString() : "?";
    }
}
