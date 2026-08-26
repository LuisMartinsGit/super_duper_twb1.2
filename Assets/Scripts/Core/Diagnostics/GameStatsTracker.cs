// GameStatsTracker.cs
// Match statistics: per-faction snapshots, the event timeline and the
// end-of-match record.
//
// Lives in Core/Diagnostics, NOT UI. It records what the simulation did
// -- eliminations, culture picks, building milestones -- and the
// simulation is what calls it. Sitting under UI/HUD made every system
// that records a stat depend on the presentation layer, which is the one
// dependency a deterministic simulation must not have. Nothing in UI/
// referenced it at all.
// Records periodic snapshots of faction resources and population for post-game timeline

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Diagnostics
{
    /// <summary>
    /// Snapshot of a faction's state at a point in time.
    /// </summary>
    public struct FactionSnapshot
    {
        public float Time;
        public int Supplies;
        public int Iron;
        public int Veilstone;
        public int Veilsteel;
        public int Glow;
        public int Population;
        public int PopulationMax;
    }

    /// <summary>
    /// Kinds of milestone events shown as symbols on the post-game charts.
    /// </summary>
    public enum GameEventKind : byte
    {
        SpecialBuilding, // choice building (Shrine / Vault / Keep) completed
        CultureChosen,   // age-up to Era 2 completed
        TempleLevelUp,   // Temple of Ridan reached a new level
        NodeConverted,   // Border node cleansed / converted
        Eliminated,      // faction knocked out
    }

    /// <summary>
    /// A timestamped milestone for one faction. Value carries kind-specific
    /// detail (temple level, culture id); 0 when unused.
    /// </summary>
    public struct GameEvent
    {
        public float Time;         // game time in seconds since start
        public GameEventKind Kind;
        public int Value;
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

        /// <summary>Per-faction milestone events, in the order they occurred.</summary>
        public Dictionary<Faction, List<GameEvent>> FactionEvents { get; private set; }
            = new Dictionary<Faction, List<GameEvent>>();

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

                // The Border is a map feature, not a player — it never appears
                // in post-game charts.
                if (faction == Faction.Border) continue;

                if (!FactionTimelines.ContainsKey(faction))
                    FactionTimelines[faction] = new List<FactionSnapshot>();

                var res = bankRes[i];
                popLookup.TryGetValue(faction, out var pop);

                FactionTimelines[faction].Add(new FactionSnapshot
                {
                    Time = gameTime,
                    Supplies = res.Supplies,
                    Iron = res.Iron,
                    Veilstone = res.Veilstone,
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
                AddEvent(faction, GameEventKind.Eliminated, 0, gameTime);
            }
        }

        /// <summary>
        /// Record a milestone event for a faction at the current game time.
        /// Static so game systems can call it without null-check ceremony —
        /// silently drops the event when no tracker is alive (menus, tests).
        /// </summary>
        public static void RecordEvent(Faction faction, GameEventKind kind, int value = 0)
        {
            var inst = Instance;
            if (inst == null || !inst._initialized || inst.GameEnded) return;
            if (faction == Faction.Border) return;
            inst.AddEvent(faction, kind, value, Time.time - inst.GameStartTime);
        }

        private void AddEvent(Faction faction, GameEventKind kind, int value, float gameTime)
        {
            if (!FactionEvents.TryGetValue(faction, out var list))
            {
                list = new List<GameEvent>();
                FactionEvents[faction] = list;
            }
            list.Add(new GameEvent { Time = gameTime, Kind = kind, Value = value });
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

            // The richest record of a match lives right here and used to die
            // with the GameObject at teardown: a full per-faction economy
            // timeline plus elimination times. Written out so a tester's logs
            // answer "how did the game actually go" without a screen-share.
            WriteTimelineCsv();
        }

        /// <summary>
        /// Dump every faction's sampled economy to Timeline.csv in the current
        /// match's log folder. One row per faction per sample; opens directly
        /// in a spreadsheet. Never throws into the game.
        /// </summary>
        private void WriteTimelineCsv()
        {
            try
            {
                var sb = new System.Text.StringBuilder(4096);
                sb.AppendLine("TimeSeconds,Faction,Supplies,Iron,Veilstone,Veilsteel,Glow,Population,PopulationMax");

                foreach (var pair in FactionTimelines)
                {
                    var list = pair.Value;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i];
                        sb.Append(s.Time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                          .Append(',').Append(pair.Key)
                          .Append(',').Append(s.Supplies)
                          .Append(',').Append(s.Iron)
                          .Append(',').Append(s.Veilstone)
                          .Append(',').Append(s.Veilsteel)
                          .Append(',').Append(s.Glow)
                          .Append(',').Append(s.Population)
                          .Append(',').Append(s.PopulationMax)
                          .AppendLine();
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Faction,EliminatedAtSeconds");
                foreach (var pair in EliminationTimes)
                    sb.Append(pair.Key).Append(',')
                      .Append(pair.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                      .AppendLine();

                System.IO.File.WriteAllText(
                    TheWaningBorder.Core.Diagnostics.MatchLogSession.File("Timeline.csv"),
                    sb.ToString());
            }
            catch { /* diagnostics must never throw into the game */ }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
