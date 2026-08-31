// HeadlessBatch.cs
// Start a skirmish from the command line, run it for a fixed time, dump the
// metrics, quit. One match per process; the batch loop lives in the runner
// script.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY
//
// A match currently starts only from SkirmishPanel — a UI button. That makes
// every experiment cost a human sitting through it in real time, and four
// consecutive 30-minute matches were each thrown away on a different blocker
// in the same chain. Twenty runs of the same configuration answers in one pass
// what one run answers anecdotally.
//
// This replicates exactly what SkirmishPanel.StartGame does — the settings, the
// team preset, the colour selections, the observer conversion — and then loads
// the map scene directly instead of through LoadingScreen, which is a UI object
// that has nothing to show in batch mode.
//
// USAGE
//   TheWaningBorder.exe -batchmode -nographics -twbHeadless \
//     -twbPlayers 4 -twbLimit 1200 -twbSpeed 3 -twbSeed 12345
//
//   -twbPlayers N   AI factions (all slots become AI; nobody is watching)
//   -twbLimit S     match seconds before the run is ended and dumped
//   -twbSpeed X     Time.timeScale; see the clamp note below
//   -twbSeed N      spawn seed, so runs can be repeated or deliberately varied
//   -twbRich        every faction pinned at FactionResources.ResourceCap
//
// RICH MODE ISOLATES BEHAVIOUR FROM ECONOMY. Every blocker found so far has
// been a money path -- the Advancement wallet that never lent, the working
// float that made reservations inert, the Shrine nobody could afford. Those
// mask what the AI would do if it simply had the resources: whether it builds
// a Royal Stable at all, whether it fields siege, whether armies are lost to
// bad fights rather than to an empty bank. Pinning the bank every second (not
// once at spawn) means spending can never re-create the constraint mid-match.
//
// A rich run is a DIAGNOSTIC, not a balance measurement -- nothing about
// costs, income or pacing can be read from it.
//
// ACCELERATION HAS A CEILING. Everything integrates on deltaTime — steering,
// arrival, separation, the formation wheel — so a big timeScale does not
// produce the same game faster, it produces a coarser game. 3-4x holds; past
// that you are measuring the accelerated build.
// ─────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Core.Config;      // LobbyConfig, SlotType
using TheWaningBorder.Core.Multiplayer;  // NetworkRole

namespace TheWaningBorder.Bootstrap
{
    public class HeadlessBatch : MonoBehaviour
    {
        public static bool Active { get; private set; }

        private float _limit = 1200f;
        private float _t;
        private bool _done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-twbHeadless") < 0) return;

            var go = new GameObject("HeadlessBatch");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessBatch>().Begin(args);
        }

        private void Begin(string[] args)
        {
            Active = true;

            int players = ArgInt(args, "-twbPlayers", 4);
            _limit = ArgInt(args, "-twbLimit", 1200);
            float speed = ArgInt(args, "-twbSpeed", 3);
            int seed = ArgInt(args, "-twbSeed", UnityEngine.Random.Range(1, 99999));
            _rich = Array.IndexOf(args, "-twbRich") >= 0;

            // -twbMap <SceneName>: run on a specific map. Without it the
            // batch inherits GameSettings.SelectedMapScene's static default,
            // which is whatever map sits FIRST in Build Settings — so a new
            // map can be batch-tested without touching the lobby default.
            string mapArg = ArgStr(args, "-twbMap");
            if (!string.IsNullOrEmpty(mapArg))
                GameSettings.SelectedMapScene = mapArg;

            // ── Exactly SkirmishPanel.StartGame, minus the UI. ──
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = players;
            LobbyConfig.SetupSinglePlayer(players);

            // Everyone is an AI: there is no human to be slot 0. This is the
            // same conversion the panel's observer toggle performs, and it is
            // what makes the run self-driving.
            for (int i = 0; i < LobbyConfig.ActiveSlotCount; i++)
                if (LobbyConfig.Slots[i].Type == SlotType.Human)
                    LobbyConfig.Slots[i].Type = SlotType.AI;

            LobbyConfig.ApplyColorSelections();

            GameSettings.SpawnSeed = seed;
            GameSettings.IsObserver = true;
            GameSettings.TutorialActive = false;

            MatchMetrics.Enabled = true;
            gameObject.AddComponent<MatchMetrics>();

            // timeScale directly rather than PlayerProfile.GameSpeed: the
            // profile clamps at 2x for the human-facing setting, and a batch
            // run is not that setting.
            _speed = Mathf.Clamp(speed, 0.25f, 8f);
            Time.timeScale = _speed;

            Debug.Log($"[HeadlessBatch] {players} AI, limit {_limit}s, speed {Time.timeScale}x, " +
                      $"seed {seed}, map {GameSettings.SelectedMapScene}" +
                      (_rich ? ", RICH (banks pinned at cap)" : ""));

            SceneManager.LoadScene(GameSettings.SelectedMapScene);
        }

        private float _speed = 1f;
        private bool _rich;
        private float _nextTopUp;

        /// <summary>
        /// Hold every faction at the resource cap. Called on a 1 s cadence:
        /// a single top-up at spawn would be spent back down within a minute
        /// and the run would quietly become an ordinary one.
        /// </summary>
        private void TopUpAll()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var faction = (Faction)i;
                if (!TheWaningBorder.Economy.FactionEconomy.TryGetBank(em, faction, out var bank))
                    continue;
                if (!em.HasComponent<TheWaningBorder.Economy.FactionResources>(bank)) continue;

                var res = em.GetComponentData<TheWaningBorder.Economy.FactionResources>(bank);
                res.Supplies  = TheWaningBorder.Economy.FactionResources.ResourceCap;
                res.Iron      = TheWaningBorder.Economy.FactionResources.ResourceCap;
                res.Veilstone = TheWaningBorder.Economy.FactionResources.ResourceCap;
                res.Veilsteel = TheWaningBorder.Economy.FactionResources.ResourceCap;
                em.SetComponentData(bank, res);
            }
        }

        private void Update()
        {
            if (_done) return;

            // RE-ASSERT EVERY FRAME. Begin() runs BeforeSceneLoad; GameBootstrap
            // then calls GameSpeedControl.Apply() on match start, which sets
            // Time.timeScale from PlayerProfile.GameSpeed — 0.75 by default —
            // and silently undoes the batch speed. Measured: a run asked for 3x
            // delivered 538 simulated seconds in 533 wall seconds, i.e. 1.0x,
            // which would have turned a 20-match batch from three hours into
            // ten and truncated every match at the runner's timeout.
            if (Time.timeScale != _speed) Time.timeScale = _speed;

            // Unscaled: the limit is a budget on the RUN, and scaling it by the
            // very acceleration it is meant to bound would cancel it out.
            _t += Time.unscaledDeltaTime * Time.timeScale;

            if (_rich && _t >= _nextTopUp)
            {
                _nextTopUp = _t + 1f;
                try { TopUpAll(); }
                catch (Exception e) { Debug.LogWarning($"[HeadlessBatch] top-up: {e.Message}"); }
            }

            // A DECIDED match ends the run — one side left standing is the
            // real end of a match, and everything after it is a victor
            // idling. The limit below stays as the guard against the match
            // that never decides (two turtles, a stalemate).
            if (TheWaningBorder.Core.MatchLifecycle.MatchDecided)
            {
                _done = true;
                Debug.Log($"[HeadlessBatch] match decided at {_t:F0}s — " +
                          $"{TheWaningBorder.Core.MatchLifecycle.MatchWinner} wins; dumping metrics");
                try { MatchMetrics.DumpFinal(); }
                catch (Exception e) { Debug.LogError($"[HeadlessBatch] dump failed: {e.Message}"); }
                Application.Quit();
                return;
            }

            if (_t < _limit) return;

            _done = true;
            Debug.Log($"[HeadlessBatch] limit reached at {_t:F0}s — dumping metrics");
            try { MatchMetrics.DumpFinal(); }
            catch (Exception e) { Debug.LogError($"[HeadlessBatch] dump failed: {e.Message}"); }
            Application.Quit();
        }

        private static string ArgStr(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static int ArgInt(string[] args, string key, int fallback)
        {
            int i = Array.IndexOf(args, key);
            return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v))
                ? v : fallback;
        }
    }
}
