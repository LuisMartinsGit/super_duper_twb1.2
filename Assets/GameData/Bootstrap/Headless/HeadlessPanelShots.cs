// HeadlessPanelShots.cs
// SCREENSHOTS OF THE BUILDING PANELS, WITHOUT A PLAYER (2026-11-15, developer:
// "take screenshots of the new menu items"). A harness, not a game mode.
//
//   TheWaningBorder.exe -twbPanelShots <folder> -screen-width 1920 \
//       -screen-height 1080 -screen-fullscreen 0
//
// Run WITH graphics (no -batchmode / -nographics: the HUD has to be drawn).
// It starts a two-player Alanthor skirmish with a human in slot 0 exactly as
// the lobby would, waits for the capital, raises a finished Market, Vault of
// Almiérra and Temple of Ridan beside it, gives the bank something to show,
// then selects each in turn and writes
//   01_market.png  02_vault.png  03_religion.png
// into the folder and quits. Exit 0 = all written, 3 = the match never started.
//
// The buildings are created straight from their factories and the bank is
// topped up outside the command stream, so the run records no replay.

using System;
using System.Collections;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Input;

namespace TheWaningBorder.Bootstrap
{
    internal sealed class HeadlessPanelShots : MonoBehaviour
    {
        private const float BootTimeoutSeconds = 120f;
        private const float SettleSeconds = 6f;     // the HUD and the views appear
        private const float PanelSeconds = 1.2f;    // a panel rebuilds for a new selection

        private string _dir;

        /// <summary>2026-11-18 UI pass: "age0" starts in Age 0 and shoots the
        /// Age 0 screens; "ui" starts in Age 1 Alanthor and shoots the Age 1
        /// ones. Neither = the original Market / Vault / religion set.</summary>
        private static bool _age0, _ui;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = CommandLine.Str(args, "-twbPanelShots");
            // In the open editor there is no command line to pass: a one-shot
            // marker file beside the EditorBridge channel names the folder,
            // and is consumed so the next Play is an ordinary one.
            if (string.IsNullOrEmpty(dir) && Application.isEditor)
            {
                string marker = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "agent-bridge", "panelshots.txt");
                if (File.Exists(marker))
                {
                    var lines = File.ReadAllLines(marker);
                    dir = lines.Length > 0 ? lines[0].Trim() : null;
                    if (lines.Length > 1 && lines[1].Trim() == "age0") _age0 = true;
                    if (lines.Length > 1 && lines[1].Trim() == "ui") _ui = true;
                    File.Delete(marker);
                }
            }
            if (string.IsNullOrEmpty(dir)) return;
            if (Array.IndexOf(args, "-twbPanelShotsAge0") >= 0) _age0 = true;
            if (Array.IndexOf(args, "-twbPanelShotsUi") >= 0) _ui = true;

            var go = new GameObject("HeadlessPanelShots");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessPanelShots>().Begin(dir, CommandLine.Str(args, "-twbMap"));
        }

        private void Begin(string dir, string map)
        {
            _dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(_dir);

            // SkirmishPanel.StartGame minus the UI: slot 0 stays the human.
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TutorialActive = false;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            LobbyConfig.ApplyColorSelections();
            GameSettings.SpawnSeed = 20261115;
            GameSettings.IsObserver = false;
            GameSettings.StartAge = _age0 ? SkirmishStartAge.Age0 : SkirmishStartAge.Age1;
            GameSettings.StartCulture = Cultures.Alanthor;
            GameSettings.SelectedMapScene = string.IsNullOrEmpty(map) ? "YielLymwerra" : map;
            TheWaningBorder.Core.Replay.ReplayRecorder.Suppressed = true;

            Debug.Log($"[PanelShots] {GameSettings.SelectedMapScene} -> {_dir}");
            SceneManager.LoadScene(GameSettings.SelectedMapScene);
            StartCoroutine(Run());
        }

        private static readonly ComponentType[] QT_Capitals =
        {
            ComponentType.ReadOnly<FortressTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };

        private static bool TryCapital(EntityManager em, out float3 at)
        {
            at = default;
            using var q = em.CreateEntityQuery(QT_Capitals);   // one-off harness, disposed
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != GameSettings.LocalPlayerFaction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                at = em.GetComponentData<LocalTransform>(ents[i]).Position;
                return true;
            }
            return false;
        }

        private IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + BootTimeoutSeconds;
            float3 home = default;
            while (true)
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w != null && w.IsCreated && TryCapital(w.EntityManager, out home)) break;
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogError("[PanelShots] the match never started (no capital for the local player)");
                    Application.Quit(3);
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(SettleSeconds);

            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            if (_age0 || _ui)
            {
                yield return UiPass(em, faction, home);
                Debug.Log("[PanelShots] done");
                File.WriteAllText(Path.Combine(_dir, "done.txt"), "ok");
                yield return new WaitForSecondsRealtime(1f);
                Application.Quit(0);
                yield break;
            }

            FactionEconomy.Add(em, faction, Cost.Of(supplies: 20000, iron: 8000, veilstone: 4000), IncomeSource.Other);
            var market = Market.Create(em, home + new float3(16f, 0f, 8f), faction);
            var vault = VaultOfAlmierra.Create(em, home + new float3(-16f, 0f, 8f), faction);
            TempleOfRidan.Create(em, home + new float3(0f, 0f, -18f), faction);

            // Something to look at: a few trades, so the shared prices have
            // moved off their start; two bars open, one full, one part filled,
            // iron started.
            Market.Trade(em, market, Market.GoodSupplies, 8, false);
            Market.Trade(em, market, Market.GoodIron, 5, true);
            var research = FactionResearchState.Instance;
            if (research != null)
            {
                research.CompleteResearch(faction, "SecondCoffer");
                research.CompleteResearch(faction, "ThirdCoffer");
            }
            CommandRouter.VaultTransferDirect(em, vault, 1, 2600, true);
            CommandRouter.VaultTransferDirect(em, vault, 2, 1200, true);
            yield return new WaitForSecondsRealtime(1.5f);

            yield return Shot(em, market, "01_market.png");
            yield return Shot(em, vault, "02_vault.png");
            // The religion menu with two sects adopted, so its rows show.
            FactionReligionPointsHelper.AddKillPoints(em, faction, 4000);
            bool a1 = SectAdoption.TryAdopt(em, faction, SectConfig.Prosperity);
            bool a2 = SectAdoption.TryAdopt(em, faction, SectConfig.Antiquity);
            Debug.Log($"[PanelShots] adopted Prosperity={a1} Antiquity={a2}");
            // Points earned after adopting advance both sects (decision 63).
            FactionReligionPointsHelper.Grant(em, faction, 5);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot(em, Entity.Null, "03_religion.png");

            // The three Fortify structures beside a Watch Tower and a House,
            // for scale; the Watch Tower carries a full Ward Discharge.
            var row = home + new float3(-6f, 0f, 30f);
            var house = Hut.Create(em, row + new float3(-20f, 0f, 0f), faction);
            var watch = WatchTower.Create(em, row + new float3(-12f, 0f, 0f), faction);
            RenewalTower.Create(em, row, faction);
            var bastion = RenewalFortification.Create(em, row + new float3(12f, 0f, 0f), faction);
            RenewalFortress.Create(em, row + new float3(24f, 0f, 0f), faction);
            yield return new WaitForSecondsRealtime(1f);
            if (em.Exists(watch))
                TransientState.Set(em, watch, new WardDischargeCharge
                {
                    Damage = 1f, TriggerRadius = 8f, ChargesLeft = 8, Lifetime = 720f, Timer = 0f,
                });
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot(em, bastion, "04_fortify.png");
            yield return Shot(em, watch, "05_ward.png");
            _ = house;

            Debug.Log("[PanelShots] done");
            File.WriteAllText(Path.Combine(_dir, "done.txt"), "ok");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }

        // ── The 2026-11-18 UI pass ─────────────────────────────────────────

        private IEnumerator UiPass(EntityManager em, Faction faction, float3 home)
        {
            string p = _age0 ? "a0_" : "a1_";
            FactionEconomy.Add(em, faction, Cost.Of(supplies: 2000, iron: 800, veilstone: 300), IncomeSource.Other);
            yield return new WaitForSecondsRealtime(1f);

            var worker = FirstOwn<WorkerTag>(em, faction);
            yield return Shot(em, worker, p + "01_worker.png");            // two build panels, resource panel, religion tab

            var scout = FirstOwnClass(em, faction, UnitClass.Scout);
            yield return Shot(em, scout, p + "02_scout.png");              // no ability bar

            var soldiers = AllOwn<SpearmanTag>(em, faction);
            yield return ShotMany(em, soldiers, p + "03_soldiers.png");    // formations top, stances bottom

            var lith = Litharch.Create(em, home + new float3(6f, 0f, -8f), faction);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(em, lith, p + "04_litharch.png");            // Age 0: no abilities

            var temple = TempleOfRidan.Create(em, home + new float3(0f, 0f, -18f), faction);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot(em, temple, p + "05_temple.png");            // Age 0: Litharch only; Age 1: + Warrior Priests

            yield return Shot(em, worker, p + "06_worker_after_temple.png"); // Age 1: the sect columns
            yield return Shot(em, Entity.Null, p + "07_religion_tab.png");
        }

        private static Entity FirstOwn<T>(EntityManager em, Faction f) where T : unmanaged, IComponentData
        {
            var all = AllOwn<T>(em, f);
            return all.Count > 0 ? all[0] : Entity.Null;
        }

        private static System.Collections.Generic.List<Entity> AllOwn<T>(EntityManager em, Faction f) where T : unmanaged, IComponentData
        {
            var list = new System.Collections.Generic.List<Entity>();
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<FactionTag>());   // one-off harness, disposed
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == f) list.Add(ents[i]);
            return list;
        }

        private static Entity FirstOwnClass(EntityManager em, Faction f, UnitClass cls)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>());   // one-off harness, disposed
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == f
                    && em.GetComponentData<UnitTag>(ents[i]).Class == cls) return ents[i];
            return Entity.Null;
        }

        private IEnumerator ShotMany(EntityManager em, System.Collections.Generic.List<Entity> all, string file)
        {
            SelectionSystem.ClearSelection();
            foreach (var e in all) if (em.Exists(e)) SelectionSystem.AddToSelection(e);
            Entity focus = all.Count > 0 ? all[0] : Entity.Null;
            float until = Time.realtimeSinceStartup + PanelSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                if (focus != Entity.Null && em.Exists(focus))
                {
                    var p = em.GetComponentData<LocalTransform>(focus).Position;
                    CameraController.FocusOn(new Vector3(p.x, p.y, p.z), instant: true);
                }
                yield return null;
            }
            string path = Path.Combine(_dir, file);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[PanelShots] " + path);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private IEnumerator Shot(EntityManager em, Entity focus, string file)
        {
            SelectionSystem.ClearSelection();
            if (focus != Entity.Null && em.Exists(focus)) SelectionSystem.AddToSelection(focus);
            float until = Time.realtimeSinceStartup + PanelSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                // Held on the subject: the cursor may sit at a screen edge.
                if (focus != Entity.Null && em.Exists(focus))
                {
                    var p = em.GetComponentData<LocalTransform>(focus).Position;
                    CameraController.FocusOn(new Vector3(p.x, p.y, p.z), instant: true);
                }
                yield return null;
            }
            string path = Path.Combine(_dir, file);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[PanelShots] " + path);
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
