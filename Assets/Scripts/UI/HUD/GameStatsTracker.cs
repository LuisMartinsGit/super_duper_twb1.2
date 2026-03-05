// GameStatsTracker.cs
// Records periodic snapshots of faction resources and population for post-game timeline
// Location: Assets/Scripts/UI/HUD/GameStatsTracker.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Snapshot of a faction's state at a point in time.
    /// </summary>
    public struct FactionSnapshot
    {
        public float Time;
        public int Supplies;
        public int Iron;
        public int Crystal;
        public int Veilsteel;
        public int Glow;
        public int Population;
        public int PopulationMax;
    }

    /// <summary>
    /// Tracks resource and population data for all factions over time.
    /// Singleton — attach to a persistent game object.
    /// </summary>
    public class GameStatsTracker : MonoBehaviour
    {
        public static GameStatsTracker Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private float sampleInterval = 5f;

        /// <summary>
        /// Per-faction timeline data. Key = Faction, Value = list of snapshots over time.
        /// </summary>
        public Dictionary<Faction, List<FactionSnapshot>> FactionTimelines { get; private set; }
            = new Dictionary<Faction, List<FactionSnapshot>>();

        /// <summary>Game start time (Time.time at first sample).</summary>
        public float GameStartTime { get; private set; }

        /// <summary>Game end time (Time.time when game ended).</summary>
        public float GameEndTime { get; private set; }

        /// <summary>Whether the game has ended.</summary>
        public bool GameEnded { get; private set; }

        /// <summary>Records when each faction was eliminated (game time in seconds).</summary>
        public Dictionary<Faction, float> EliminationTimes { get; private set; }
            = new Dictionary<Faction, float>();

        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _banksQuery;
        private EntityQuery _populationQuery;
        private float _timer;
        private bool _initialized;

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
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated) return;

            _em = _world.EntityManager;
            _banksQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionResources>());
            _populationQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionPopulation>());

            GameStartTime = Time.time;
            _initialized = true;

            // Initialize timelines for all active factions
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                FactionTimelines[(Faction)i] = new List<FactionSnapshot>();
            }

            // Take initial sample
            TakeSample();
        }

        void Update()
        {
            if (!_initialized || GameEnded) return;

            _timer += Time.deltaTime;
            if (_timer >= sampleInterval)
            {
                _timer = 0f;
                TakeSample();
            }
        }

        /// <summary>
        /// Record a snapshot of all faction resources and population.
        /// </summary>
        private void TakeSample()
        {
            if (_world == null || !_world.IsCreated) return;

            float gameTime = Time.time - GameStartTime;

            // Get resources
            using var bankEntities = _banksQuery.ToEntityArray(Allocator.Temp);
            using var bankTags = _banksQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bankRes = _banksQuery.ToComponentDataArray<FactionResources>(Allocator.Temp);

            // Get population
            using var popEntities = _populationQuery.ToEntityArray(Allocator.Temp);
            using var popTags = _populationQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var pops = _populationQuery.ToComponentDataArray<FactionPopulation>(Allocator.Temp);

            // Build pop lookup
            var popLookup = new Dictionary<Faction, (int current, int max)>();
            for (int i = 0; i < popEntities.Length; i++)
            {
                popLookup[popTags[i].Value] = (pops[i].Current, pops[i].Max);
            }

            // Record snapshots
            for (int i = 0; i < bankEntities.Length; i++)
            {
                Faction faction = bankTags[i].Value;
                if (!FactionTimelines.ContainsKey(faction))
                    FactionTimelines[faction] = new List<FactionSnapshot>();

                var res = bankRes[i];
                popLookup.TryGetValue(faction, out var pop);

                FactionTimelines[faction].Add(new FactionSnapshot
                {
                    Time = gameTime,
                    Supplies = res.Supplies,
                    Iron = res.Iron,
                    Crystal = res.Crystal,
                    Veilsteel = res.Veilsteel,
                    Glow = res.Glow,
                    Population = pop.current,
                    PopulationMax = pop.max
                });
            }
        }

        /// <summary>
        /// Record when a faction was eliminated from the game.
        /// </summary>
        public void RecordElimination(Faction faction, float gameTime)
        {
            if (!EliminationTimes.ContainsKey(faction))
            {
                EliminationTimes[faction] = gameTime;
                Debug.Log($"[GameStatsTracker] {faction} eliminated at {gameTime:F1}s");
            }
        }

        /// <summary>
        /// Call when the game ends to take a final sample and mark the end time.
        /// </summary>
        public void EndGame()
        {
            if (GameEnded) return;

            TakeSample(); // Final snapshot
            GameEndTime = Time.time;
            GameEnded = true;

            Debug.Log($"[GameStatsTracker] Game ended. Duration: {GameEndTime - GameStartTime:F1}s, " +
                      $"Factions tracked: {FactionTimelines.Count}");
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
