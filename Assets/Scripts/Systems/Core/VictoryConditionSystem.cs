// VictoryConditionSystem.cs
// Polls ECS world to detect faction elimination and trigger victory/defeat
// Location: Assets/Scripts/Systems/Core/VictoryConditionSystem.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Periodically checks whether each faction can still REBUILD. A faction
    /// is retired the moment it has no Hall, no military building and no
    /// builder unit — the three lifelines back into a match. When only one
    /// faction remains, the game ends with a victory/defeat outcome.
    /// </summary>
    public class VictoryConditionSystem : MonoBehaviour
    {
        public static VictoryConditionSystem Instance { get; private set; }

        private const float CheckInterval = 2f;
        private const float GracePeriod = 10f;

        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _buildingsQuery;
        private EntityQuery _buildersQuery;
        private float _timer;
        private float _gameStartTime;
        private bool _initialized;
        private bool _gameOver;
        private HashSet<Faction> _aliveFactions = new HashSet<Faction>();
        private HashSet<Faction> _eliminatedFactions = new HashSet<Faction>();
        /// <summary>Factions observed with at least one lifeline at some point.
        /// Elimination requires membership here, so a faction can never be
        /// retired before its base has finished spawning.</summary>
        private HashSet<Faction> _everSeenAlive = new HashSet<Faction>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated) return;

            _em = _world.EntityManager;
            _buildingsQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>());
            // Builders — any unit that can raise a structure. CanBuild is the
            // build capability itself, so this covers Workers and anything
            // else that gains it. Conscripted Feraldis Workers are deliberately
            // INCLUDED: they keep CanBuild and can be pulled back off the front
            // to rebuild, so they are a live lifeline even while soldiering.
            _buildersQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());

            _gameStartTime = Time.time;

            // Sandbox / Scenario mode: no victory conditions.
            // Scenarios are sandboxed combat fixtures — the player should never
            // get a victory or defeat banner just because one faction loses all
            // its (often-zero) buildings.
            if (GameSettings.IsSandbox
                || GameSettings.Mode == GameMode.Scenario)
            {
                _initialized = false;
                return;
            }

            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                Faction faction = (Faction)i;

                // Observer matches: every faction is a real AI participant —
                // including the observer's nominal LocalPlayerFaction, whose
                // slot is AI-controlled and spawns a base. Track them all so
                // eliminations and the winner banner work for a full
                // AI-vs-AI match. (The old skip left LocalPlayerFaction
                // untracked, so its elimination never registered.)
                _aliveFactions.Add(faction);
            }

            _initialized = true;
        }

        void Update()
        {
            if (!_initialized || _gameOver) return;
            if (GameStatsTracker.Instance != null && GameStatsTracker.Instance.GameEnded) return;

            // Grace period to avoid false eliminations at game start
            if (Time.time - _gameStartTime < GracePeriod) return;

            _timer += Time.deltaTime;
            if (_timer >= CheckInterval)
            {
                _timer = 0f;
                CheckVictoryConditions();
            }
        }

        private void CheckVictoryConditions()
        {
            if (_world == null || !_world.IsCreated) return;

            // ── SURVIVAL TEST (rewritten 2026-08-07) ────────────────────
            // A faction is alive while it can still REBUILD. Three
            // independent lifelines, ANY of which keeps it in the match:
            //
            //   Hall              → can train Workers → can rebuild anything
            //   Military building → can train an army
            //   A builder unit    → can raise a new Hall or Barracks
            //
            // Lose all three and there is no path back, so the faction is
            // retired immediately.
            //
            // WAS: "owns zero completed buildings". That kept dead factions in
            // the match for the rest of its length — the 2026-08-07 8-player
            // FFA had Purple frozen on 18 supplies / 0 military for 25 minutes
            // and Green likewise, both untouchable because they still owned a
            // scatter of gatherer huts. Six of eight factions were finished by
            // minute 19 and the match ran to 45. Under this rule they retire
            // when they actually die, and the finishing move stops being
            // "hunt sixteen 1-tile huts across a 400x400 map".
            const int MaxFactions = 9;   // Blue..White + Border
            var hasHall = new bool[MaxFactions];
            var hasMilitaryBuilding = new bool[MaxFactions];
            var hasBuilder = new bool[MaxFactions];

            using (var entities = _buildingsQuery.ToEntityArray(Allocator.Temp))
            using (var factionTags = _buildingsQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Faction faction = factionTags[i].Value;
                    int fi = (int)faction;
                    if (fi < 0 || fi >= MaxFactions) continue;
                    if (!_aliveFactions.Contains(faction)) continue;

                    // A foundation is not a lifeline — but the builder raising
                    // it is, and that is counted separately below.
                    if (_em.HasComponent<UnderConstruction>(entities[i])) continue;

                    if (_em.HasComponent<HallTag>(entities[i])) { hasHall[fi] = true; continue; }
                    if (!hasMilitaryBuilding[fi] && IsMilitaryBuilding(entities[i]))
                        hasMilitaryBuilding[fi] = true;
                }
            }

            using (var builders = _buildersQuery.ToEntityArray(Allocator.Temp))
            using (var builderFactions = _buildersQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < builders.Length; i++)
                {
                    int fi = (int)builderFactions[i].Value;
                    if (fi < 0 || fi >= MaxFactions) continue;
                    if (hasBuilder[fi]) continue;
                    // A corpse mid-cleanup is not a builder.
                    if (_em.HasComponent<Health>(builders[i])
                        && _em.GetComponentData<Health>(builders[i]).Value <= 0) continue;
                    hasBuilder[fi] = true;
                }
            }

            // Detect newly eliminated factions
            float gameTime = Time.time - _gameStartTime;
            var newlyEliminated = new List<Faction>();

            foreach (var faction in _aliveFactions)
            {
                int fi = (int)faction;
                if (fi < 0 || fi >= MaxFactions) continue;

                bool canRebuild = hasHall[fi] || hasMilitaryBuilding[fi] || hasBuilder[fi];
                if (canRebuild)
                {
                    _everSeenAlive.Add(faction);
                    continue;
                }

                // NEVER retire a faction we have not yet observed alive. The
                // grace period alone is a timer, and a timer cannot know
                // whether this faction's base has actually finished
                // spawning — a slow bootstrap, a late-joining peer or a
                // deferred ECB playback would all read as "no lifelines" and
                // delete a player before their first frame. Elimination is
                // irreversible; require positive evidence of life first.
                if (!_everSeenAlive.Contains(faction)) continue;

                newlyEliminated.Add(faction);
                // AILogger, not TWBLog: TWBLog is [Conditional("TWB_VERBOSE")]
                // and compiles to nothing in a normal build, so elimination —
                // the single most important event in a match — was invisible
                // in every postmortem. This lands in AI_<Faction>.log.
                TheWaningBorder.AI.AILogger.Log(faction, "VICTORY",
                    $"ELIMINATED at {gameTime:0}s — no Hall, no military building, no builders.");
                TWBLog.Log($"[Victory] {faction} eliminated at {gameTime:0}s — " +
                           "no Hall, no military building, no builders.");
            }

            foreach (var faction in newlyEliminated)
            {
                _aliveFactions.Remove(faction);
                _eliminatedFactions.Add(faction);

                // Defeat is VISIBLE and TOTAL (2026-08-11): announce it and
                // self-destruct every remaining asset of the retired faction
                // — a defeated player's leftover huts and stragglers no
                // longer litter the match.
                PlayerNotificationSystem.Notify($"{faction} has been DEFEATED");
                SelfDestructFactionAssets(faction);

                if (GameStatsTracker.Instance != null)
                {
                    GameStatsTracker.Instance.RecordElimination(faction, gameTime);
                }


                // If local player was eliminated, show defeat immediately.
                // Not in observer mode — the observer has no stake; the
                // match runs until one AI faction remains.
                if (!GameSettings.IsObserver && faction == GameSettings.LocalPlayerFaction)
                {
                    Faction winner = _aliveFactions.Count == 1
                        ? GetSingleFaction(_aliveFactions)
                        : faction; // No clear winner yet
                    TriggerGameEnd(winner, true);
                    return;
                }
            }

            // If only one faction remains, they win
            if (_aliveFactions.Count <= 1)
            {
                Faction winner = _aliveFactions.Count == 1
                    ? GetSingleFaction(_aliveFactions)
                    : GameSettings.LocalPlayerFaction;
                TriggerGameEnd(winner);
            }
        }

        private void TriggerGameEnd(Faction winner, bool localPlayerDefeated = false)
        {
            if (_gameOver) return;
            _gameOver = true;

            if (GameStatsTracker.Instance != null)
            {
                GameStatsTracker.Instance.EndGame();
            }

            // Result string. Missing braces on the original else-if let
            // the VICTORY/DEFEAT line run unconditionally and overwrite
            // the surrender "DEFEAT" path. Fixed by collapsing the two
            // surrender branches into one ternary.
            string result;
            if (GameSettings.IsObserver)
            {
                result = $"{winner} WINS";
            }
            else if (localPlayerDefeated)
            {
                result = "DEFEAT";
            }
            else
            {
                result = winner == GameSettings.LocalPlayerFaction ? "VICTORY" : "DEFEAT";
            }

            // Mirrored to every faction's AI log because TWBLog compiles out —
            // without this, "did the match actually end, and how?" is
            // unanswerable after the fact.
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
                TheWaningBorder.AI.AILogger.Log((Faction)i, "VICTORY",
                    $"GAME OVER: {result} (winner={winner})");
            TWBLog.Log($"[Victory] Game over: {result} (winner={winner})");

            // End-of-match flow (2026-08-11): the old post-game UI was
            // removed with the UI redesign and nothing replaced it, so
            // matches "ended" invisibly and ran forever. Announce the
            // outcome, then return to the main menu.
            PlayerNotificationSystem.Notify($"GAME OVER — {result}");
            StartCoroutine(ReturnToMenuAfter(ReturnToMenuDelay));
        }

        /// <summary>Seconds between the outcome banner and the return to
        /// the main menu — long enough to read how it ended.</summary>
        private const float ReturnToMenuDelay = 10f;

        private System.Collections.IEnumerator ReturnToMenuAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                TheWaningBorder.Bootstrap.MainMenuBootstrap.MenuSceneName);
        }

        /// <summary>Retirement is TOTAL: every remaining unit and building
        /// of a defeated faction self-destructs. Health is zeroed and
        /// DeathSystem performs the destruction — synchronous DestroyEntity
        /// outside DeathSystem corrupts EndSimulation ECB playback (the
        /// unit-death contract).</summary>
        private void SelfDestructFactionAssets(Faction faction)
        {
            var q = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int killed = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var hp = _em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0) continue;
                hp.Value = 0;
                _em.SetComponentData(ents[i], hp);
                killed++;
            }
            TWBLog.Log($"[Victory] {faction} assets self-destruct ({killed} entities)");
        }

        /// <summary>
        /// Called by NodeVictorySystem when a culture wins by node victory
        /// (Alanthor cleanse-all-hold, Runai convert-all-hold, Feraldis
        /// destroy-all-instant). Posts the appropriate VICTORY / DEFEAT
        /// banner from the local player's perspective.
        /// </summary>
        public void TriggerNodeVictory(byte culture, Faction winner)
        {
            if (_gameOver) return;

            string cultureName = culture switch
            {
                Cultures.Runai    => "RUNAI",
                Cultures.Alanthor => "ALANTHOR",
                Cultures.Feraldis => "FERALDIS",
                _ => $"Culture {culture}",
            };

            _gameOver = true;

            if (GameStatsTracker.Instance != null)
                GameStatsTracker.Instance.EndGame();

            string result;
            if (GameSettings.IsObserver)
            {
                result = $"{cultureName} WINS (node victory)";
            }
            else
            {
                // Local player wins if their faction is the representative —
                // a follow-up will resolve shared-culture team wins properly.
                bool localWins = winner == GameSettings.LocalPlayerFaction;
                result = localWins
                    ? $"VICTORY — {cultureName} node win"
                    : $"DEFEAT — {cultureName} node win";
            }

            TWBLog.Log($"[Victory] Node victory: {result} (winner={winner})");

            // Same end-of-match flow as conquest (2026-08-11): visible
            // banner, then back to the main menu.
            PlayerNotificationSystem.Notify($"GAME OVER — {result}");
            StartCoroutine(ReturnToMenuAfter(ReturnToMenuDelay));
        }

        /// <summary>
        /// Called when the local player surrenders via the End Game button.
        /// </summary>
        public void Surrender()
        {
            if (_gameOver) return;

            float gameTime = Time.time - _gameStartTime;
            _aliveFactions.Remove(GameSettings.LocalPlayerFaction);
            _eliminatedFactions.Add(GameSettings.LocalPlayerFaction);

            if (GameStatsTracker.Instance != null)
            {
                GameStatsTracker.Instance.RecordElimination(GameSettings.LocalPlayerFaction, gameTime);
            }


            Faction winner = _aliveFactions.Count == 1
                ? GetSingleFaction(_aliveFactions)
                : GameSettings.LocalPlayerFaction; // No clear winner
            TriggerGameEnd(winner, true);
        }

        /// <summary>
        /// A completed building counts as MILITARY when it can train at least
        /// one combat unit. Resolved through the tech data
        /// (BuildCosts.IdFromEntity → TechCatalog building def → its
        /// `trains` list → each unit's class) rather than a hardcoded tag list,
        /// so a roster change cannot silently make a Barracks stop counting and
        /// eliminate a faction that still has one.
        ///
        /// Halls are excluded by the caller — they are their own lifeline.
        /// </summary>
        private bool IsMilitaryBuilding(Entity building)
        {
            string id = BuildCosts.IdFromEntity(_em, building);
            if (string.IsNullOrEmpty(id)) return false;
            if (!TechCatalog.TryGetBuilding(id, out var def)) return false;
            if (def?.trains == null) return false;

            for (int i = 0; i < def.trains.Length; i++)
                if (TrainsCombatUnit(def.trains[i])) return true;
            return false;
        }

        /// <summary>
        /// Whether a unit id is a combat unit, for the military-building test.
        ///
        /// Deliberately FAIL-SAFE: anything not on the known non-combat list —
        /// including a unit the catalog cannot resolve, or a class added later —
        /// counts as combat. Elimination is irreversible, so the cost of the two
        /// mistakes is wildly asymmetric: a false negative keeps a dead faction
        /// around a little longer, a false positive deletes a live player who
        /// still had a Barracks. Bias hard toward survival.
        /// </summary>
        private static bool TrainsCombatUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            if (!TechCatalog.TryGetUnit(unitId, out var unit) || unit == null)
                return true;   // unknown → assume it fights

            switch ((unit.unitClass ?? string.Empty).ToLowerInvariant())
            {
                case "worker":
                case "villager":
                case "economy":
                case "miner":
                case "support":
                case "scout":
                case "caravan":
                case "trade":
                    return false;
                default:
                    return true;   // melee / ranged / siege / magic / cavalry / new
            }
        }

        private static Faction GetSingleFaction(HashSet<Faction> set)
        {
            foreach (var f in set) return f;
            return Faction.Blue;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
