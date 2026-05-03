// VictoryConditionSystem.cs
// Polls ECS world to detect faction elimination and trigger victory/defeat
// Location: Assets/Scripts/Systems/Core/VictoryConditionSystem.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Periodically checks whether each faction still owns completed buildings.
    /// When a faction loses all completed buildings it is eliminated.
    /// When only one faction remains, the game ends with a victory/defeat outcome.
    /// </summary>
    public class VictoryConditionSystem : MonoBehaviour
    {
        public static VictoryConditionSystem Instance { get; private set; }

        private const float CheckInterval = 2f;
        private const float GracePeriod = 10f;

        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _buildingsQuery;
        private float _timer;
        private float _gameStartTime;
        private bool _initialized;
        private bool _gameOver;
        private HashSet<Faction> _aliveFactions = new HashSet<Faction>();
        private HashSet<Faction> _eliminatedFactions = new HashSet<Faction>();

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

            _gameStartTime = Time.time;

            // Sandbox / BattalionTest mode: no victory conditions
            if (GameSettings.IsSandbox || GameSettings.Mode == GameMode.BattalionTest)
            {
                _initialized = false;
                return;
            }

            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                Faction faction = (Faction)i;

                // Skip observer faction - they are not a participant
                if (GameSettings.IsObserver && faction == GameSettings.LocalPlayerFaction)
                    continue;

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

            // Count completed buildings per faction
            var buildingCounts = new Dictionary<Faction, int>();
            foreach (var faction in _aliveFactions)
            {
                buildingCounts[faction] = 0;
            }

            using var entities = _buildingsQuery.ToEntityArray(Allocator.Temp);
            using var factionTags = _buildingsQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Faction faction = factionTags[i].Value;

                // Skip factions we're not tracking
                if (!_aliveFactions.Contains(faction)) continue;

                // Skip buildings still under construction
                if (_em.HasComponent<UnderConstruction>(entities[i])) continue;

                buildingCounts[faction]++;
            }

            // Detect newly eliminated factions
            float gameTime = Time.time - _gameStartTime;
            var newlyEliminated = new List<Faction>();

            foreach (var kvp in buildingCounts)
            {
                if (kvp.Value == 0)
                {
                    newlyEliminated.Add(kvp.Key);
                }
            }

            foreach (var faction in newlyEliminated)
            {
                _aliveFactions.Remove(faction);
                _eliminatedFactions.Add(faction);

                if (GameStatsTracker.Instance != null)
                {
                    GameStatsTracker.Instance.RecordElimination(faction, gameTime);
                }


                // If local player was eliminated, show defeat immediately
                if (faction == GameSettings.LocalPlayerFaction)
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

            string result;
            if (GameSettings.IsObserver)
                result = $"{winner} WINS";
            else if (localPlayerDefeated)
                result = "DEFEAT";
                result = winner == GameSettings.LocalPlayerFaction ? "VICTORY" : "DEFEAT";


            EndGameButton.GameEndedBySystem = true;

            if (PostGameStatsUI.Instance != null)
            {
                PostGameStatsUI.Instance.ShowWithResult(result, winner);
            }
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
