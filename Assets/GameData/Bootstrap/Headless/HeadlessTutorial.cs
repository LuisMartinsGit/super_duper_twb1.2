// HeadlessTutorial.cs
// Plays the Basic tutorial mission start to finish with the real commands, as a
// test: every stage, the button filters, the waves and the win.
//
//   "The Waning Border.exe" -twbTutorialAuto [-twbTutorialShots <folder>]
//       [-twbSpeed 3] [-twbLimit <wall seconds>] [-twbTutorialNoFog]
//       [-twbTutorialLose]   destroys the Shelter once the curse opens, to test the loss
//
// Exit 0 = the node fell, 1 = the mission was lost, 2 = out of time, 3 = a button
// filter or a stage rule was wrong. Run it WITHOUT -batchmode to get screenshots.
// docs/Design/Tutorial_Missions.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.AI;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Input;
using TheWaningBorder.Tutorial;
using TheWaningBorder.UI;
using TheWaningBorder.UI.Data;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.Bootstrap
{
    public sealed partial class HeadlessTutorial : MonoBehaviour
    {
        private const float ThinkSeconds = 1f;
        private static readonly string[] SettleOrder =
        {
            "Hut", "Hut", "GatherersHut", "Mine", "VeilstoneMine", "Barracks",
            "Hut", "Hut", "Hut", "Hut", "GatherersHut", "GatherersHut", "Mine",
        };

        private string _shots;
        private float _limit, _thinkTimer, _started;
        private bool _armySent, _failed;
        private int _siteIndex, _scoutTarget, _shotIndex, _placeIndex, _spiral;
        private string _lastPhase = "";
        private bool _shelterPlaced;
        private readonly List<(Entity site, string id, float3 pos)> _pending = new List<(Entity, string, float3)>();
        private float _overAt = -1f;

        private static TutorialMissionSystemConfig Cfg => TutorialMissionSystemConfig.I;
        private static Faction Me => GameSettings.LocalPlayerFaction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            bool menuTest = CommandLine.Has(args, "-twbMenuTest");
            if (!menuTest && !CommandLine.Has(args, "-twbTutorialAuto")) return;
            var go = new GameObject("HeadlessTutorial");
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<HeadlessTutorial>();
            if (menuTest)
            {
                driver._menuTest = true;
                driver._shots = CommandLine.Str(args, "-twbTutorialShots");
                if (!string.IsNullOrEmpty(driver._shots)) Directory.CreateDirectory(driver._shots);
                driver.StartCoroutine(driver.RunMenuTest());
                return;
            }
            driver.Begin(args);
        }

        private void Begin(string[] args)
        {
            _shots = CommandLine.Str(args, "-twbTutorialShots");
            if (!string.IsNullOrEmpty(_shots)) Directory.CreateDirectory(_shots);
            _limit = CommandLine.Int(args, "-twbLimit", 3600);
            _forceLoss = CommandLine.Has(args, "-twbTutorialLose");
            Time.timeScale = CommandLine.Int(args, "-twbSpeed", 3);
            _started = Time.realtimeSinceStartup;
            ReadAdvancedArgs(args);
            string scene = TutorialMenuItem.Prepare(_advanced ? TutorialMissionId.Advanced : TutorialMissionId.Basic);
            if (CommandLine.Has(args, "-twbTutorialNoFog")) GameSettings.FogOfWarEnabled = false;
            SceneManager.LoadScene(scene);
        }

        private static void Log(string line) => Debug.Log("[TutorialAuto] " + line);

        private void Fail(string why)
        {
            Debug.LogError("[TutorialAuto] FAIL: " + why);
            _failed = true;
        }

        private bool _menuTest;

        private void Update()
        {
            if (_menuTest) return;
            if (Time.realtimeSinceStartup - _started > _limit)
            {
                Log("OUT OF TIME " + Status());
                Quit(2);
                return;
            }
            _thinkTimer += Time.unscaledDeltaTime;
            if (_thinkTimer < ThinkSeconds) return;
            _thinkTimer = 0f;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (!MatchLifecycle.MapPopulated) return;
            var em = world.EntityManager;
            if (_advanced) { TickAdvanced(em); return; }
            if (!em.Exists(TutorialMissionSystem.Litharch)) return;

            string phase = TutorialMissionState.Stage + (TutorialMissionState.CampFound ? "+camp" : "")
                         + (TutorialMissionState.CurseOpen ? "+curse" : "");
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                Log($"PHASE {phase} {Status()}");
                CheckOffers(em);
                LogButtons(em);
                Shot(em, phase);
            }
            if (TutorialMissionState.WavesSent > 0 && Time.frameCount % 240 == 0)
                Log($"STATUS {Status()} curseUnits={CountBorderUnits(em)}");

            if (TutorialMissionState.Over)
            {
                if (_overAt < 0f)
                {
                    _overAt = Time.realtimeSinceStartup;
                    Log($"OVER won={TutorialMissionState.Won} {Status()}");
                    Shot(em, "over");
                }
                else if (Time.realtimeSinceStartup - _overAt > 3f)
                    Quit(_failed ? 3 : TutorialMissionState.Won ? 0 : 1);
                return;
            }

            if (Time.realtimeSinceStartup - _lastDump > 30f)
            {
                _lastDump = Time.realtimeSinceStartup;
                Dump(em);
            }

            if (_forceLoss && TutorialMissionState.CurseOpen)
            {
                var shelter = FirstFinished<FortressTag>(em);
                if (shelter != Entity.Null)
                    em.SetComponentData(shelter, new Health { Value = 0, Max = em.GetComponentData<Health>(shelter).Max });
            }

            switch (TutorialMissionState.Stage)
            {
                case TutorialStage.Rescue: Rescue(em); break;
                case TutorialStage.Camp: Camp(em); break;
                case TutorialStage.Settle: Build(em); break;
                case TutorialStage.Muster: Build(em); Muster(em); break;
            }
        }

        private static string Status() =>
            $"t={Time.time:F0} workers={TutorialMissionState.Workers} shelter={TutorialMissionState.Shelters} "
            + $"huts={TutorialMissionState.Huts} gh={TutorialMissionState.GatherersHuts} vm={TutorialMissionState.VeilstoneMines} "
            + $"mines={TutorialMissionState.IronMines} rax={TutorialMissionState.Barracks} scouts={TutorialMissionState.ScoutsTrained} "
            + $"found={TutorialMissionState.SpearmenFound} trained={TutorialMissionState.SpearmenTrained} waves={TutorialMissionState.WavesSent}";

        private void Quit(int code)
        {
            Log("EXIT " + code);
            Application.Quit(code);
        }

        // ── What must and must not be offered ──────────────────────────────

        private void CheckOffers(EntityManager em)
        {
            void Expect(string what, bool actual, bool expected)
            {
                if (actual != expected) Fail($"{what}: {actual}, expected {expected} at {_lastPhase}");
            }
            var s = TutorialMissionState.Stage;
            bool camp = TutorialMissionState.CampFound;
            Expect("Fortress offered", TutorialMissionState.AllowsBuilding("Fortress"), s == TutorialStage.Camp && camp);
            Expect("Hut offered", TutorialMissionState.AllowsBuilding("Hut"), s >= TutorialStage.Settle);
            Expect("Temple offered", TutorialMissionState.AllowsBuilding("TempleOfRidan"), false);
            Expect("Scout offered", TutorialMissionState.AllowsUnit("Scout"), s == TutorialStage.Muster);
            Expect("Worker offered", TutorialMissionState.AllowsUnit("Worker"), false);
            Expect("StoneWeapons offered", TutorialMissionState.AllowsResearch("StoneWeapons"), s == TutorialStage.Muster);
        }

        private void LogButtons(EntityManager em)
        {
            string Line(List<ActionButton> list) => string.Join(", ", list.Select(
                a => $"{a.Id}{(a.Enabled ? "" : "[off]")}{(a.CanAfford ? "" : "[poor]")}"));
            var palette = typeof(EntityActionExtractor).GetMethod("GetBuildingActions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Log("palette: " + Line((List<ActionButton>)palette.Invoke(null, null)));
            foreach (var b in new[] { FirstFinished<FortressTag>(em), FirstFinished<BarracksTag>(em) })
                if (b != Entity.Null)
                    Log($"{UpgradeBuildingCommandHelper.ResolveBuildingId(em, b)}: train [{Line(EntityActionExtractor.GetTrainingActions(b, em))}] "
                        + $"research [{Line(EntityActionExtractor.GetResearchActions(b, em))}]");
        }

        // ── Stage players ─────────────────────────────────────────────────

        private void Rescue(EntityManager em)
        {
            int perSite = Cfg.workersPerSite;
            _siteIndex = Mathf.Clamp(TutorialMissionState.Workers / perSite, 0, Cfg.workerSites.Length - 1);
            CommandRouter.IssueMove(em, TutorialMissionSystem.Litharch,
                TutorialMissionSystem.Ground(Cfg.workerSites[_siteIndex]));
        }

        private void Camp(EntityManager em)
        {
            var camp = TutorialMissionSystem.Ground(Cfg.campSpot);
            var workers = Workers(em);
            if (!TutorialMissionState.CampFound)
            {
                CommandRouter.IssueMove(em, TutorialMissionSystem.Litharch, camp);
                foreach (var w in workers) CommandRouter.IssueMove(em, w, camp);
                return;
            }
            if (!_shelterPlaced)
            {
                // A Shelter is placed by a named worker standing near the site.
                Entity nearest = Entity.Null;
                float best = float.MaxValue;
                foreach (var w in workers)
                {
                    float d = math.distance(em.GetComponentData<LocalTransform>(w).Position.xz, camp.xz);
                    if (d < best) { best = d; nearest = w; }
                }
                foreach (var w in workers) CommandRouter.IssueMove(em, w, camp);
                if (nearest == Entity.Null || best > 25f) return;
                bool ok = CommandRouter.IssuePlaceBuilding(em, "Fortress", camp, Me, nearest, out var site, CommandSource.LocalPlayer);
                Log($"place Shelter: queued={ok} site={site}");
                if (site == Entity.Null) return;
                _shelterPlaced = true;
                foreach (var w in workers)
                    CommandRouter.IssueBuild(em, w, site, "Fortress", camp, CommandSource.LocalPlayer, append: false);
                return;
            }
            SendIdleWorkersToSites(em);
        }

        /// <summary>A plan turns into a new entity when a worker breaks ground, so
        /// sites are looked up, not remembered.</summary>
        private void SendIdleWorkersToSites(EntityManager em)
        {
            var idle = Workers(em).Where(w => !AICommon.IsCommittedWorker(em, w)).ToList();
            if (idle.Count == 0) return;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnderConstruction>(),
                ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                if (idle.Count == 0) return;
                if (em.GetComponentData<FactionTag>(e).Value != Me) continue;
                string id = UpgradeBuildingCommandHelper.ResolveBuildingId(em, e);
                CommandRouter.IssueBuild(em, idle[0], e, id, em.GetComponentData<LocalTransform>(e).Position);
                idle.RemoveAt(0);
            }
        }

        private void Build(EntityManager em)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (!em.Exists(p.site) || (em.HasComponent<BuildingTag>(p.site) && !em.HasComponent<UnderConstruction>(p.site)))
                    _pending.RemoveAt(i);
            }

            // A plan the game cancelled (an overlap, say) is simply placed again.
            if (_placeIndex >= SettleOrder.Length && _pending.Count == 0
                && TutorialMissionState.Barracks < Cfg.barracksNeeded
                && TutorialMissionState.Stage == TutorialStage.Settle)
            {
                _placeIndex = Array.IndexOf(SettleOrder, "Barracks");
                _replacing = true;
            }

            if (_placeIndex < SettleOrder.Length && _pending.Count < 3)
            {
                string id = SettleOrder[_placeIndex];
                if (TryPlaceSpot(em, id, out var pos))
                {
                    // The call returns "queued for lockstep"; a mission has none,
                    // so a placed site is the created entity.
                    // One Mine button raises whichever the node needs, as for a player.
                    string button = id;
                    if (id == "Mine" || id == "VeilstoneMine")
                        TheWaningBorder.World.Regions.TerritoryOwnership.ResolveExtractorAt(em, Me, pos, out button, out pos);
                    CommandRouter.IssuePlaceBuilding(em, button, pos, Me, Entity.Null, out var site, CommandSource.LocalPlayer);
                    if (site == Entity.Null)
                        Log($"refused {id} at {pos.x:F0},{pos.z:F0}");
                    else
                    {
                        _usedNodes.Add(pos.xz);
                        _pending.Add((site, id, pos));
                        // Only the Barracks is ever re-placed: straight to the end.
                        _placeIndex = _replacing ? SettleOrder.Length : _placeIndex + 1;
                        _replacing = false;
                        Log($"placed {id} at {pos.x:F0},{pos.z:F0}");
                    }
                }
            }

            var idle = Workers(em).Where(w => !AICommon.IsCommittedWorker(em, w)).ToList();
            foreach (var p in _pending)
            {
                if (idle.Count == 0) break;
                if (!em.Exists(p.site)) continue;
                CommandRouter.IssueBuild(em, idle[0], p.site, p.id, p.pos);
                idle.RemoveAt(0);
            }
            SendIdleWorkersToSites(em);
        }

        private bool TryPlaceSpot(EntityManager em, string id, out float3 pos)
        {
            pos = default;
            if (id == "GatherersHut") return FreeNode<SupplyNodeTag>(em, out pos);
            if (id == "Mine") return FreeNode<IronMineTag>(em, out pos);
            if (id == "VeilstoneMine") return FreeNode<VeilstoneOutcroppingTag>(em, out pos);
            // A spiral of spots round the camp, away from the Shelter's footprint.
            _spiral++;
            float a = _spiral * 1.2f;
            float r = 22f + _spiral * 2.2f;
            pos = TutorialMissionSystem.Ground(new Vector2(
                Cfg.campSpot.x + math.cos(a) * r, Cfg.campSpot.y + math.sin(a) * r));
            return true;
        }

        private bool FreeNode<T>(EntityManager em, out float3 pos) where T : unmanaged, IComponentData
        {
            pos = default;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<LocalTransform>());
            using var xf = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            foreach (var x in xf)
            {
                if (_pending.Any(p => math.distance(p.pos.xz, x.Position.xz) < 3f)) continue;
                if (_usedNodes.Any(u => math.distance(u, x.Position.xz) < 3f)) continue;
                pos = x.Position;
                return true;
            }
            return false;
        }
        private readonly List<float2> _usedNodes = new List<float2>();

        private void Muster(EntityManager em)
        {
            var shelter = FirstFinished<FortressTag>(em);
            var barracks = FirstFinished<BarracksTag>(em);

            var scouts = Units(em).Where(u => IsType(em, u, "Scout")).ToList();
            if (shelter != Entity.Null && TutorialMissionState.ScoutsTrained + QueueLength(em, shelter) < Cfg.scoutsNeeded)
                CommandRouter.IssueTrain(em, shelter, "Scout");

            if (barracks != Entity.Null)
            {
                if (!_researched) { CommandRouter.IssueResearch(em, barracks, "StoneWeapons"); _researched = true; Log("research StoneWeapons"); }
                int spearmen = Units(em).Count(u => em.HasComponent<SpearmanTag>(u));
                if (QueueLength(em, barracks) < 3 && spearmen < 30)
                    CommandRouter.IssueTrain(em, barracks, "Spearman");
            }

            int lost = Cfg.spearmanSites.Length * Cfg.spearmenPerSite;
            if (TutorialMissionState.SpearmenFound < lost && scouts.Count > 0)
            {
                _scoutTarget = Mathf.Clamp(TutorialMissionState.SpearmenFound / Cfg.spearmenPerSite, 0, Cfg.spearmanSites.Length - 1);
                foreach (var s in scouts)
                    CommandRouter.IssueMove(em, s, TutorialMissionSystem.Ground(Cfg.spearmanSites[_scoutTarget]));
            }

            // Hold the camp through the first full wave, as the lesson asks, then
            // march on the node and keep marching.
            bool ready = TutorialMissionState.SpearmenFound >= lost
                      && TutorialMissionState.SpearmenTrained >= Cfg.spearmenToTrain
                      && TutorialMissionState.WavesSent >= 2
                      && CountBorderUnits(em) == 0;
            if (ready && TutorialMissionState.CurseOpen) _marching = true;
            var soldiers = Units(em).Where(u => em.HasComponent<SpearmanTag>(u)).ToList();
            if (_marching)
            {
                if (!_armySent) { Log("army marches on the node " + Status()); _armySent = true; Shot(em, "march"); }
                var node = TutorialMissionSystem.Ground(Cfg.nodePosition);
                foreach (var u in soldiers)
                    if (!TransientState.Active<AttackMoveCommand>(em, u))
                        CommandRouter.IssueAttackMove(em, u, node);
            }
            else if (TutorialMissionState.CurseOpen)
            {
                var hold = TutorialMissionSystem.Ground(Cfg.campSpot + new Vector2(30f, 0f));
                foreach (var u in soldiers)
                    if (!TransientState.Active<AttackMoveCommand>(em, u)
                        && math.distance(em.GetComponentData<LocalTransform>(u).Position.xz, hold.xz) > 25f)
                        CommandRouter.IssueAttackMove(em, u, hold);
            }
        }
        private bool _researched, _marching;

        private float _lastDump;
        private bool _replacing, _forceLoss;

        /// <summary>Where the run is stuck, when it is.</summary>
        private void Dump(EntityManager em)
        {
            Log($"DUMP {Status()} placeIndex={_placeIndex} pending={_pending.Count} curseUnits={CountBorderUnits(em)} "
                + $"nearestCurseToCamp={NearestCurseToCamp(em):F0}m soldiers={Units(em).Count(u => em.HasComponent<SpearmanTag>(u))} "
                + $"buildingHpLost={BuildingHpLost(em)}");
            foreach (var p in _pending)
                Log($"  pending {p.id} at {p.pos.x:F0},{p.pos.z:F0} exists={em.Exists(p.site)}");
            foreach (var w in Workers(em))
                Log($"  worker {w} committed={AICommon.IsCommittedWorker(em, w)} at {em.GetComponentData<LocalTransform>(w).Position}");
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnderConstruction>(), ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
                Log($"  site {UpgradeBuildingCommandHelper.ResolveBuildingId(em, e)} {e} uc={em.GetComponentData<UnderConstruction>(e).Progress:F1}/{em.GetComponentData<UnderConstruction>(e).Total:F1}");
        }

        // ── Queries (a test harness, so plain per-tick queries are disposed) ──

        private static List<Entity> Units(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var list = new List<Entity>();
            for (int i = 0; i < ents.Length; i++) if (facs[i].Value == Me) list.Add(ents[i]);
            return list;
        }

        private static List<Entity> Workers(EntityManager em) =>
            Units(em).Where(u => em.HasComponent<WorkerTag>(u)).ToList();

        private static bool IsType(EntityManager em, Entity u, string id) =>
            em.HasComponent<UnitTypeId>(u) && em.GetComponentData<UnitTypeId>(u).Value == id;

        private static float NearestCurseToCamp(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float best = float.PositiveInfinity;
            var camp = new float2(Cfg.campSpot.x, Cfg.campSpot.y);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == Faction.Border)
                    best = math.min(best, math.distance(xfs[i].Position.xz, camp));
            return best;
        }

        private static int BuildingHpLost(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>(), ComponentType.Exclude<UnderConstruction>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            int lost = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == Me) lost += hps[i].Max - hps[i].Value;
            return lost;
        }

        private static int CountBorderUnits(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++) if (facs[i].Value == Faction.Border) n++;
            return n;
        }

        private static Entity FirstFinished<T>(EntityManager em) where T : unmanaged, IComponentData
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
                if (em.GetComponentData<FactionTag>(e).Value == Me && !em.HasComponent<UnderConstruction>(e))
                    return e;
            return Entity.Null;
        }

        private static int QueueLength(EntityManager em, Entity building) =>
            em.HasBuffer<ProductionQueueItem>(building) ? em.GetBuffer<ProductionQueueItem>(building, true).Length : 0;

        // ── Screenshots ───────────────────────────────────────────────────

        private void Shot(EntityManager em, string name)
        {
            if (string.IsNullOrEmpty(_shots)) return;

            // Show what the phase is about: a worker's build menu, or the
            // Barracks' training and research.
            SelectionSystem.ClearSelection();
            Entity focus = Entity.Null;
            var stage = TutorialMissionState.Stage;
            if (stage == TutorialStage.Rescue) focus = TutorialMissionSystem.Litharch;
            else if (stage == TutorialStage.Muster) focus = FirstFinished<BarracksTag>(em);
            else focus = Workers(em).FirstOrDefault();
            if (focus != Entity.Null && em.Exists(focus))
            {
                SelectionSystem.AddToSelection(focus);
                var p = em.GetComponentData<LocalTransform>(focus).Position;
                CameraController.FocusOn(new Vector3(p.x, p.y, p.z), instant: true);
                Log($"shot {name}: focus {p.x:F0},{p.z:F0}");
            }
            StartCoroutine(ShotLater($"{_shotIndex++:00}_{name}.png", focus));
        }

        private System.Collections.IEnumerator ShotLater(string file, Entity focus)
        {
            // Let the panels rebuild for the new selection first. The camera is
            // held on the subject: this window's cursor may sit at a screen edge,
            // and edge panning would drag the view away meanwhile.
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            float until = Time.realtimeSinceStartup + 0.7f;
            while (Time.realtimeSinceStartup < until)
            {
                if (focus != Entity.Null && em.Exists(focus))
                {
                    var p = em.GetComponentData<LocalTransform>(focus).Position;
                    CameraController.FocusOn(new Vector3(p.x, p.y, p.z), instant: true);
                }
                yield return null;
            }
            var cam = Camera.main;
            if (cam != null) Log($"camera at {cam.transform.position} looking {cam.transform.forward}");
            ScreenCapture.CaptureScreenshot(Path.Combine(_shots, file));
        }
    }
}
