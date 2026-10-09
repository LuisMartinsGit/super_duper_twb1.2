// EliminationSystem.cs
// THE DETERMINISTIC HALF OF VICTORY (2026-09-06, MP harness catch #15).
//
// The first elimination victory the MP harness ever produced ("Green wins
// at 2554s") was seen by exactly ONE of four peers: VictoryConditionSystem
// is a MonoBehaviour whose survival polls fire on FRAMES. Under lockstep
// catch-up a frame spans 0..N ticks, so each peer sampled the world at a
// different tick; a knife-edge lifeline (last Hall razed, rebuild landing
// seconds later) read dead on one peer and alive on the rest. Elimination
// is sticky and self-destructs the loser's assets — a SIM MUTATION driven
// by per-peer bookkeeping. The winner's client quit; the other three
// stalled to the disconnect timeout, 17 minutes of "peer lost".
//
// This system owns that decision now, inside SimulationSystemGroup on a
// SimCadence-phased cadence: every peer computes lifelines at the SAME
// tick, appends to the SAME elimination buffer, self-destructs the SAME
// assets (Health = 0 — DeathSystem owns destruction per the unit-death
// contract), and stamps the SAME verdict singleton.
// VictoryConditionSystem keeps everything presentational: it OBSERVES the
// buffer and the verdict and drives banners / stats / MatchLifecycle from
// the local player's perspective.
//
// TWO RULES (docs/Design/Territory_Claims.md § 7, 2026-10-08):
//   1. no lifeline - no Hall/Fortress, no military building, no worker;
//   2. no territory - a faction that has held ground at least once and then
//      holds NONE for noTerritoryGraceSeconds of continuous sim time.
// Both retire the faction the same way: an EliminatedFactionRecord and
// Health = 0 on every asset it still owns, so no remnant can stand on a
// territory and claim or hold it. Timing lives on EliminationSystem.asset.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.World.Regions;

/// <summary>Sim-authoritative match verdict. Decided flips exactly once,
/// on the same tick on every lockstep peer.</summary>
public struct MatchVerdictState : IComponentData
{
    public byte Decided;
    public Faction Winner;
    public float DecidedAtSimSeconds;
}

/// <summary>One faction's elimination, in the order they fell. Appended
/// deterministically; VictoryConditionSystem announces them.</summary>
public struct EliminatedFactionRecord : IBufferElementData
{
    public Faction Value;
    public float AtSimSeconds;
}

namespace TheWaningBorder.Systems.Core
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class EliminationSystem : SystemBase
    {
        /// <summary>Faction slots the per-faction arrays cover (Blue..White
        /// plus Border). Sizes data, not tuning.</summary>
        private const int MaxFactions = 9;

        private static readonly ComponentType[] QT_Verdict =
            { ComponentType.ReadOnly<MatchVerdictState>() };
        private static CachedEntityQuery QC_Verdict;

        private static readonly ComponentType[] QT_FactionHealth =
            { ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadWrite<Health>() };
        private static CachedEntityQuery QC_FactionHealth;

        private SimCadence.Periodic _acc;
        private int _epoch = -1;
        private float _matchSeconds;
        private readonly HashSet<Faction> _everSeenAlive = new();
        private Entity _verdictEntity;

        // The no-territory rule (Territory_Claims.md § 7). Sim state on the
        // lockstep clock, reset per match with the rest.
        /// <summary>The faction has held at least one territory this match.</summary>
        private readonly bool[] _everHeldTerritory = new bool[MaxFactions];
        /// <summary>Match second its last territory was seen gone; -1 while
        /// it holds ground.</summary>
        private readonly float[] _landlessSince = new float[MaxFactions];
        // Rule 3 (2026-10-09): a faction that has had a building and now has
        // none, since when (sim seconds), or -1.
        private readonly bool[] _everHadBuilding = new bool[MaxFactions];
        private readonly float[] _buildinglessSince = new float[MaxFactions];

        protected override void OnUpdate()
        {
            if (GameSettings.IsSandbox || GameSettings.Mode == GameMode.Scenario) return;

            // Per-match reset (system object outlives matches).
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _matchSeconds = 0f;
                _everSeenAlive.Clear();
                _verdictEntity = Entity.Null;
                for (int i = 0; i < MaxFactions; i++)
                {
                    _everHeldTerritory[i] = false;
                    _landlessSince[i] = -1f;
                    _everHadBuilding[i] = false;
                    _buildinglessSince[i] = -1f;
                }
            }

            // Tick-locked match clock: nothing accrues before tick 0 or
            // during a stall (same contract as CurseTerritorySystem).
            var lockstep = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            if (lockstep != null && !lockstep.IsSimulationRunning) return;
            _matchSeconds += SystemAPI.Time.DeltaTime;

            var cfg = EliminationSystemConfig.I;
            if (cfg == null) return;   // Require already logged the data bug
            if (!_acc.Due(SystemAPI.Time.DeltaTime, cfg.checkIntervalSeconds)) return;
            if (_matchSeconds < cfg.startGraceSeconds) return;

            var em = EntityManager;

            // Verdict singleton, created lazily per match.
            if (_verdictEntity == Entity.Null || !em.Exists(_verdictEntity)
                || !em.HasComponent<MatchVerdictState>(_verdictEntity))
            {
                var q = QC_Verdict.Get(em, QT_Verdict);
                if (q.IsEmptyIgnoreFilter)
                {
                    _verdictEntity = em.CreateEntity(typeof(MatchVerdictState));
                    em.AddBuffer<EliminatedFactionRecord>(_verdictEntity);
                }
                else
                {
                    using var ents = q.ToEntityArray(Allocator.Temp);
                    _verdictEntity = ents[0];
                }
            }

            var verdict = em.GetComponentData<MatchVerdictState>(_verdictEntity);
            if (verdict.Decided != 0) return;

            // ── Alive set = registered players minus recorded eliminations ──
            var eliminated = new HashSet<Faction>();
            {
                var buf = em.GetBuffer<EliminatedFactionRecord>(_verdictEntity);
                for (int i = 0; i < buf.Length; i++) eliminated.Add(buf[i].Value);
            }
            var alive = new List<Faction>();
            for (int i = 0; i < GameSettings.TotalPlayers; i++)
            {
                var f = (Faction)i;
                if (f == Faction.Border) continue;
                if (!eliminated.Contains(f)) alive.Add(f);
            }

            // ── Lifelines: Hall OR military building OR worker — the same
            // survival rule the Mono computed (2026-08-07 rewrite), read at
            // an identical tick on every peer now. ──
            var hasHall = new bool[MaxFactions];
            var hasBuilding = new bool[MaxFactions];
            var hasMilitary = new bool[MaxFactions];
            var hasWorker = new bool[MaxFactions];

            var bq = SystemAPI.QueryBuilder()
                .WithAll<BuildingTag, FactionTag>().Build();
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    int fi = (int)facs[i].Value;
                    if (fi < 0 || fi >= MaxFactions) continue;
                    // Rule 3 counts sites too (plans carry no BuildingTag).
                    hasBuilding[fi] = true;
                    if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                    if (em.HasComponent<HallTag>(ents[i])) { hasHall[fi] = true; continue; }
                    if (!hasMilitary[fi] && IsMilitaryBuilding(em, ents[i]))
                        hasMilitary[fi] = true;
                }
            }

            var wq = SystemAPI.QueryBuilder()
                .WithAll<CanBuild, FactionTag>().Build();
            using (var ents = wq.ToEntityArray(Allocator.Temp))
            using (var facs = wq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    int fi = (int)facs[i].Value;
                    if (fi < 0 || fi >= MaxFactions || hasWorker[fi]) continue;
                    if (em.HasComponent<Health>(ents[i])
                        && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                    hasWorker[fi] = true;
                }
            }

            // ── Territory held, per faction (Territory_Claims.md § 7). The
            // meter is sim state advanced by TerritoryClaimSystem on the
            // lockstep clock; walked in territory-index order. A map with no
            // region partition cannot apply the rule. ──
            var territories = new int[MaxFactions];
            bool territoryRule = cfg.noTerritoryGraceSeconds > 0f
                && TerritoryOwnership.Ready && RegionMap.Count > 0;
            if (territoryRule)
            {
                int count = RegionMap.Count;
                for (int t = 0; t < count; t++)
                {
                    int owner = TerritoryOwnership.OwnerOf(t);
                    if (owner >= 0 && owner < MaxFactions) territories[owner]++;
                }
            }

            // ── Retire the fallen, in faction order (deterministic). ──
            var newly = new List<Faction>();
            var reason = new Dictionary<Faction, string>();
            foreach (var f in alive)
            {
                int fi = (int)f;
                if (fi < 0 || fi >= MaxFactions) continue;
                bool canRebuild = hasHall[fi] || hasMilitary[fi] || hasWorker[fi];
                if (canRebuild) _everSeenAlive.Add(f);

                if (!canRebuild)
                {
                    if (!_everSeenAlive.Contains(f)) continue;  // never spawned yet
                    newly.Add(f);
                    reason[f] = "no Hall, no military building, no workers.";
                    continue;
                }

                // ── Rule 3: no buildings (2026-10-09, § 7) — the zombie
                // remnant of three units holding a territory forever. ──
                if (hasBuilding[fi]) { _everHadBuilding[fi] = true; _buildinglessSince[fi] = -1f; }
                else if (_everHadBuilding[fi] && cfg.noTerritoryGraceSeconds > 0f)
                {
                    if (_buildinglessSince[fi] < 0f)
                    {
                        _buildinglessSince[fi] = _matchSeconds;
                        TheWaningBorder.AI.AILogger.Log(f, "VICTORY",
                            $"NO BUILDINGS at {_matchSeconds:0}s - eliminated in " +
                            $"{cfg.noTerritoryGraceSeconds:0}s unless one is placed.");
                    }
                    else if (_matchSeconds - _buildinglessSince[fi] >= cfg.noTerritoryGraceSeconds)
                    {
                        newly.Add(f);
                        reason[f] = $"no buildings for {_matchSeconds - _buildinglessSince[fi]:0}s.";
                        continue;
                    }
                }

                if (!territoryRule) continue;
                if (territories[fi] > 0)
                {
                    if (_landlessSince[fi] >= 0f)
                        TheWaningBorder.AI.AILogger.Log(f, "VICTORY",
                            $"ground held again at {_matchSeconds:0}s - the no-territory clock stops.");
                    _everHeldTerritory[fi] = true;
                    _landlessSince[fi] = -1f;
                    continue;
                }
                // Never held ground yet (the start territory is claimed by its
                // Fortress on the first claim ticks): not landless.
                if (!_everHeldTerritory[fi]) continue;
                if (_landlessSince[fi] < 0f)
                {
                    _landlessSince[fi] = _matchSeconds;
                    TheWaningBorder.AI.AILogger.Log(f, "VICTORY",
                        $"LANDLESS at {_matchSeconds:0}s - eliminated in " +
                        $"{cfg.noTerritoryGraceSeconds:0}s unless a territory is held again.");
                    continue;
                }
                if (_matchSeconds - _landlessSince[fi] < cfg.noTerritoryGraceSeconds) continue;
                newly.Add(f);
                reason[f] = $"no territory held for {_matchSeconds - _landlessSince[fi]:0}s.";
            }
            // ── Ascension (§ 7 exception, Curse_And_Shardroot.md §3.1c): the
            // enshrining Temple outlasted its countdown — every faction
            // hostile to the ascendant falls. ──
            if (TryAscendant(em, out var ascendant))
                foreach (var f in alive)
                    if (f != ascendant && Alliances.AreHostile(f, ascendant) && !newly.Contains(f))
                    {
                        newly.Add(f);
                        reason[f] = $"{ascendant} ascended with the Shardroot.";
                    }
            newly.Sort();

            foreach (var f in newly)
            {
                alive.Remove(f);
                em.GetBuffer<EliminatedFactionRecord>(_verdictEntity)
                  .Add(new EliminatedFactionRecord { Value = f, AtSimSeconds = _matchSeconds });
                SelfDestructFactionAssets(em, f);
                TheWaningBorder.AI.AILogger.Log(f, "VICTORY",
                    $"ELIMINATED at {_matchSeconds:0}s - {reason[f]}");
            }

            // ── Decided when no hostile pair remains among the living. ──
            // A slot that never spawned (an Empty lobby slot still counted in
            // TotalPlayers) is no opponent: it can never be eliminated, so it
            // kept a one-team match running forever (2026-10-09, 0.0.38 MP).
            alive.RemoveAll(f => !_everSeenAlive.Contains(f));
            bool anyHostilePair = false;
            for (int a = 0; a < alive.Count && !anyHostilePair; a++)
                for (int b = a + 1; b < alive.Count; b++)
                    if (Alliances.AreHostile(alive[a], alive[b])) { anyHostilePair = true; break; }

            if (!anyHostilePair && alive.Count > 0 && _everSeenAlive.Count > 1)
            {
                verdict.Decided = 1;
                verdict.Winner = alive[0];
                verdict.DecidedAtSimSeconds = _matchSeconds;
                em.SetComponentData(_verdictEntity, verdict);
            }
        }

        private static readonly ComponentType[] QT_Shardroot = { ComponentType.ReadOnly<ShardrootState>() };
        private static CachedEntityQuery QC_Shardroot;

        private static bool TryAscendant(EntityManager em, out Faction ascendant)
        {
            ascendant = Faction.Border;
            var q = QC_Shardroot.Get(em, QT_Shardroot);
            if (q.IsEmptyIgnoreFilter) return false;
            using var s = q.ToComponentDataArray<ShardrootState>(Allocator.Temp);
            if (s[0].AscensionDone == 0 || s[0].AscensionFaction == Faction.Border) return false;
            ascendant = s[0].AscensionFaction;
            return true;
        }

        /// <summary>Health = 0 for every asset of a retired faction —
        /// DeathSystem owns the destruction (unit-death contract). Runs on
        /// every peer at the same tick, so the wipe is lockstep-identical.
        /// Both elimination rules end here: nothing of an eliminated faction
        /// survives to stand on, claim or hold a territory.</summary>
        private static void SelfDestructFactionAssets(EntityManager em, Faction faction)
        {
            var q = QC_FactionHealth.Get(em, QT_FactionHealth);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var hp = em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0) continue;
                hp.Value = 0;
                em.SetComponentData(ents[i], hp);
            }
        }

        // Ported verbatim from VictoryConditionSystem so the two halves can
        // never disagree about what "military" means.
        private static bool IsMilitaryBuilding(EntityManager em, Entity building)
        {
            string id = BuildCosts.IdFromEntity(em, building);
            if (string.IsNullOrEmpty(id)) return false;
            if (!TechCatalog.TryGetBuilding(id, out var def)) return false;
            if (def?.trains == null) return false;
            for (int i = 0; i < def.trains.Length; i++)
                if (TrainsCombatUnit(def.trains[i])) return true;
            return false;
        }

        private static bool TrainsCombatUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            if (!TechCatalog.TryGetUnit(unitId, out var unit) || unit == null)
                return true;   // unknown -> assume it fights
            switch ((unit.unitClass ?? string.Empty).ToLowerInvariant())
            {
                case "worker":
                case "villager":
                case "economy":
                case "support":
                case "scout":
                case "caravan":
                    return false;
                default:
                    return true;
            }
        }
    }
}
