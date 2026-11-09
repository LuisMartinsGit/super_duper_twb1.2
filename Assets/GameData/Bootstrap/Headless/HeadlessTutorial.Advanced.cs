// HeadlessTutorial.Advanced.cs
// Plays the Alanthor tutorial mission with the real commands, and tests the
// Tutorials screen (main-menu entry -> mission list).
//
//   "The Waning Border.exe" -twbTutorialAuto -twbMission advanced [-twbWinBy dominion|conquest]
//   "The Waning Border.exe" -twbMenuTest
//
// The last stage's win is reached quickly on purpose: dominion claims both Sanctums
// by hand and conquest kills the rival by hand, so what is under test is that the
// real victory check takes over, and with the mission's short countdown.
// Exit 0 = the player won, 1 = lost, 2 = out of time, 3 = a check failed.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Tutorial;
using TheWaningBorder.UI.Data;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Bootstrap
{
    public sealed partial class HeadlessTutorial
    {
        private bool _advanced, _heldSanctums, _rivalKilled;
        private string _winBy = "dominion";
        private readonly HashSet<string> _placed = new HashSet<string>();
        private float3 _home;
        private bool _homeKnown;

        private static TutorialAdvancedSystemConfig ACfg => TutorialAdvancedSystemConfig.I;

        private void ReadAdvancedArgs(string[] args)
        {
            _advanced = CommandLine.Str(args, "-twbMission") == "advanced";
            _winBy = CommandLine.Str(args, "-twbWinBy") ?? "dominion";
        }

        private static string AStatus() =>
            $"t={Time.time:F0} stage={TutorialMissionState.AdvancedStage} survey={TutorialMissionState.SurveyDone} "
            + $"stopped={TutorialMissionState.Stopped} ranges={TutorialMissionState.Ranges} towers={TutorialMissionState.Towers} "
            + $"claimed={TutorialMissionState.Claimed} rp={TutorialMissionState.ReligionPoints} temples={TutorialMissionState.Temples} "
            + $"sect={TutorialMissionState.SectAdopted} node={TutorialMissionState.NodeDead} monument={TutorialMissionState.MonumentHeld}/{TutorialMissionState.MonumentExplored} "
            + $"waves={TutorialMissionState.WavesSent}";

        private void TickAdvanced(EntityManager em)
        {
            string phase = TutorialMissionState.AdvancedStage.ToString();
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                Log($"PHASE {AStatus()}");
                Shot(em, phase);
                LogAdvancedRules(em);
            }
            if (Time.realtimeSinceStartup - _lastDump > 30f)
            {
                _lastDump = Time.realtimeSinceStartup;
                Log($"DUMP {AStatus()} soldiers={Soldiers(em).Count} workers={Workers(em).Count} curseUnits={CountBorderUnits(em)} "
                    + $"redBuildings={CountBuildings(em, Faction.Red)} bank={Bank(em)}");
            }

            if (TutorialMissionState.Over) { Log("OVER (mission) lost"); Quit(1); return; }
            if (TryVerdict(em, out var winner))
            {
                if (_overAt < 0f)
                {
                    _overAt = Time.realtimeSinceStartup;
                    Log($"VERDICT winner={winner} by {_winBy} {AStatus()}");
                    Shot(em, "verdict");
                }
                else if (Time.realtimeSinceStartup - _overAt > 3f) Quit(winner == Me ? (_failed ? 3 : 0) : 1);
                return;
            }

            if (!_homeKnown)
            {
                var hall = FirstFinished<FortressTag>(em);
                if (hall == Entity.Null) return;
                _home = em.GetComponentData<LocalTransform>(hall).Position;
                _homeKnown = true;
            }

            TrainSoldiers(em);
            switch (TutorialMissionState.AdvancedStage)
            {
                case AdvancedStage.Vault:
                    PlaceOnce(em, "VaultOfAlmierra");
                    break;
                case AdvancedStage.Economy:
                    var guild = FirstFinished<GathererHutTag>(em);
                    if (guild != Entity.Null && !_researched)
                    {
                        CommandRouter.IssueResearch(em, guild, ACfg.surveyTechs[0]);
                        _researched = true;
                        Log("research " + ACfg.surveyTechs[0]);
                    }
                    StopAMine(em);
                    break;
                case AdvancedStage.Military:
                    PlaceOnce(em, ACfg.archeryRangeId);
                    break;
                case AdvancedStage.Defense:
                    PlaceOnce(em, ACfg.towerId);
                    SendArmy(em, new float3(-136f, 0f, 66f));   // WestField
                    break;
                case AdvancedStage.Religion:
                    PlaceOnce(em, ACfg.templeId);
                    var temple = FirstFinished<TempleOfRidanTag>(em);
                    if (temple != Entity.Null && !TutorialMissionState.SectAdopted)
                        foreach (var sect in SectConfig.AllSectIds)
                            CommandRouter.IssueSectAdoption(em, temple, sect);
                    break;
                case AdvancedStage.Sanctum:
                    SendArmy(em, TutorialAdvancedSystemGround(ACfg.nodePosition));
                    break;
                case AdvancedStage.Ruins:
                    GoToMonument(em);
                    break;
                case AdvancedStage.Victory:
                    FinishByTestRoute(em);
                    break;
            }
            SendIdleWorkersToSites(em);
        }

        private static float3 TutorialAdvancedSystemGround(Vector2 p)
            => new float3(p.x, TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.y), p.y);

        private void LogAdvancedRules(EntityManager em)
        {
            void Expect(string what, bool actual, bool expected)
            {
                if (actual != expected) Fail($"{what}: {actual}, expected {expected} at {_lastPhase}");
            }
            var s = TutorialMissionState.AdvancedStage;
            Expect("Temple offered", TutorialMissionState.AllowsBuilding(ACfg.templeId), s >= AdvancedStage.Religion);
            Expect("Fiendstone Keep offered", TutorialMissionState.AllowsBuilding("FiendstoneKeep"), s >= AdvancedStage.Religion);
            Expect("Lexor trainable", TutorialMissionState.AllowsUnit(ACfg.kingUnitId), false);
            Expect("Rival hostile", Alliances.AreHostile(Me, Faction.Red), s == AdvancedStage.Victory);
        }

        private void PlaceOnce(EntityManager em, string id)
        {
            if (_placed.Contains(id) || _pending.Count >= 3) return;
            _spiral++;
            float a = _spiral * 1.1f;
            float r = 28f + _spiral * 3f;
            var pos = TutorialAdvancedSystemGround(new Vector2(_home.x + math.cos(a) * r, _home.z + math.sin(a) * r));
            CommandRouter.IssuePlaceBuilding(em, id, pos, Me, Entity.Null, out var site, CommandSource.LocalPlayer);
            if (site == Entity.Null) return;
            _placed.Add(id);
            Log($"placed {id} at {pos.x:F0},{pos.z:F0}");
            foreach (var w in Workers(em).Take(4))
                CommandRouter.IssueBuild(em, w, site, id, pos, CommandSource.LocalPlayer, append: false);
        }

        /// <summary>Step 2b (2026-11-09): stop extracting at the first finished Mine.</summary>
        private void StopAMine(EntityManager em)
        {
            if (TutorialMissionState.Stopped >= 1) return;
            var mine = FirstFinished<MineTag>(em);
            if (mine == Entity.Null) return;
            CommandRouter.IssueSetExtractorStopped(em, mine, true, CommandSource.LocalPlayer);
            Log("stopped extracting at a Mine");
        }

        private void TrainSoldiers(EntityManager em)
        {
            var barracks = FirstFinished<BarracksTag>(em);
            if (barracks == Entity.Null || QueueLength(em, barracks) >= 3 || Soldiers(em).Count >= 40) return;
            foreach (var a in EntityActionExtractor.GetTrainingActions(barracks, em))
                if (a.Enabled && a.CanAfford) { CommandRouter.IssueTrain(em, barracks, a.Id); return; }
        }

        private static List<Entity> Soldiers(EntityManager em) => Units(em).Where(u =>
            !em.HasComponent<WorkerTag>(u) && u != TutorialAdvancedSystem.Hero && u != TutorialAdvancedSystem.Litharch
            && !IsType(em, u, "Scout")).ToList();

        private static void SendArmy(EntityManager em, float3 to)
        {
            foreach (var u in Soldiers(em))
                if (!TransientState.Active<AttackMoveCommand>(em, u)
                    && math.distance(em.GetComponentData<LocalTransform>(u).Position.xz, to.xz) > 14f)
                    CommandRouter.IssueAttackMove(em, u, to);
        }

        private static void GoToMonument(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<MonumentState>(), ComponentType.ReadOnly<LocalTransform>());
            using var states = q.ToComponentDataArray<MonumentState>(Allocator.Temp);
            using var xf = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            if (states.Length == 0) { Debug.LogError("[TutorialAuto] FAIL: no monument"); return; }
            if (TutorialMissionState.MonumentHeld) { Monument.TryExplore(em, Me, states[0].Territory); return; }
            var hero = TutorialAdvancedSystem.Hero;
            if (hero != Entity.Null && em.Exists(hero) && !TransientState.Active<AttackMoveCommand>(em, hero))
                CommandRouter.IssueMove(em, hero, xf[0].Position);
            foreach (var u in Soldiers(em).Take(3)) CommandRouter.IssueMove(em, u, xf[0].Position);
        }

        private void FinishByTestRoute(EntityManager em)
        {
            if (_winBy == "conquest")
            {
                if (_rivalKilled) return;
                _rivalKilled = true;
                Log("test route: killing the rival");
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<Health>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                    if (em.GetComponentData<FactionTag>(e).Value == Faction.Red)
                    {
                        var hp = em.GetComponentData<Health>(e);
                        hp.Value = 0;
                        em.SetComponentData(e, hp);
                    }
                return;
            }
            if (!_heldSanctums) Log("test route: holding both Sanctums");
            _heldSanctums = true;
            foreach (var t in TerritoryResources.TerritoriesOf(TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType.Sanctum))
                TerritoryOwnership.ForceClaim(t, (int)Me);
        }

        private static bool TryVerdict(EntityManager em, out Faction winner)
        {
            winner = Faction.Border;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<MatchVerdictState>());
            if (q.IsEmptyIgnoreFilter) return false;
            var s = q.GetSingleton<MatchVerdictState>();
            if (s.Decided == 0) return false;
            winner = s.Winner;
            return true;
        }

        private static int CountBuildings(EntityManager em, Faction f)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            return facs.ToArray().Count(x => x.Value == f);
        }

        private static string Bank(EntityManager em)
        {
            FactionEconomy.TryGetBank(em, Me, out var bank);
            return em.Exists(bank) ? $"{em.GetComponentData<FactionResources>(bank).Supplies}s/{em.GetComponentData<FactionResources>(bank).Iron}i" : "?";
        }

        // ── The Tutorials screen ──────────────────────────────────────────

        private IEnumerator RunMenuTest()
        {
            yield return new WaitForSecondsRealtime(3f);
            GameObject Find(string n)
            {
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == n) return t.gameObject;
                return null;
            }
            int bad = 0;
            void Check(string what, bool ok)
            {
                Log($"MENU {(ok ? "PASS" : "FAIL")} {what}");
                if (!ok) bad++;
            }
            var entry = Find("Menu_Item_Tutorial");
            Check("the Tutorial entry exists", entry != null && entry.activeInHierarchy);
            var button = entry != null ? entry.GetComponentInChildren<UnityEngine.UI.Button>(true) : null;
            Check("the Tutorial entry has a button", button != null);
            if (button != null) button.onClick.Invoke();   // what a click does
            yield return new WaitForSecondsRealtime(3f);
            Check("the click opens the Tutorials screen",
                SceneManager.GetActiveScene().name == SceneNames.Tutorials);
            var panel = UnityEngine.Object.FindFirstObjectByType<TheWaningBorder.UI.Menus.Panels.TutorialsPanel>();
            Check("the screen has its panel", panel != null);
            if (panel != null)
            {
                var basic = Find("Row_Basic");
                var alanthor = Find("Row_Advanced");
                Check("the list shows Mission 1", basic != null && basic.activeInHierarchy);
                Check("the list shows Mission 2", alanthor != null && alanthor.activeInHierarchy);
                Check("Mission 1 is selected first", panel.SelectedMission == TutorialMissionId.Basic);
                panel.Select(1);
                Check("selecting the second row picks Mission 2", panel.SelectedMission == TutorialMissionId.Advanced);
                panel.Select(0);
                if (!string.IsNullOrEmpty(_shots)) ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shots, "tutorials_screen.png"));
                yield return new WaitForSecondsRealtime(1f);
            }
            Check("Prepare picks each mission's scene",
                TutorialMenuItem.Prepare(TutorialMissionId.Basic) == TutorialMissionSystemConfig.I.sceneName
                && TutorialMenuItem.Prepare(TutorialMissionId.Advanced) == ACfg.sceneName);
            Log($"MENU DONE failures={bad}");
            Application.Quit(bad == 0 ? 0 : 3);
        }
    }
}
