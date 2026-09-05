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

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Data;

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
        private const float CheckInterval = 2f;   // same cadence the Mono used
        private const float GracePeriod = 10f;    // same start grace

        private SimCadence.Periodic _acc;
        private int _epoch = -1;
        private float _matchSeconds;
        private readonly HashSet<Faction> _everSeenAlive = new();
        private Entity _verdictEntity;

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
            }

            // Tick-locked match clock: nothing accrues before tick 0 or
            // during a stall (same contract as CurseTerritorySystem).
            var lockstep = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            if (lockstep != null && !lockstep.IsSimulationRunning) return;
            _matchSeconds += SystemAPI.Time.DeltaTime;

            if (!_acc.Due(SystemAPI.Time.DeltaTime, CheckInterval)) return;
            if (_matchSeconds < GracePeriod) return;

            var em = EntityManager;

            // Verdict singleton, created lazily per match.
            if (_verdictEntity == Entity.Null || !em.Exists(_verdictEntity)
                || !em.HasComponent<MatchVerdictState>(_verdictEntity))
            {
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<MatchVerdictState>());
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
                q.Dispose();
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

            // ── Lifelines: Hall OR military building OR builder — the same
            // survival rule the Mono computed (2026-08-07 rewrite), read at
            // an identical tick on every peer now. ──
            const int MaxFactions = 9;
            var hasHall = new bool[MaxFactions];
            var hasMilitary = new bool[MaxFactions];
            var hasBuilder = new bool[MaxFactions];

            var bq = SystemAPI.QueryBuilder()
                .WithAll<BuildingTag, FactionTag>().Build();
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    int fi = (int)facs[i].Value;
                    if (fi < 0 || fi >= MaxFactions) continue;
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
                    if (fi < 0 || fi >= MaxFactions || hasBuilder[fi]) continue;
                    if (em.HasComponent<Health>(ents[i])
                        && em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                    hasBuilder[fi] = true;
                }
            }

            // ── Retire the fallen, in faction order (deterministic). ──
            var newly = new List<Faction>();
            foreach (var f in alive)
            {
                int fi = (int)f;
                bool canRebuild = hasHall[fi] || hasMilitary[fi] || hasBuilder[fi];
                if (canRebuild) { _everSeenAlive.Add(f); continue; }
                if (!_everSeenAlive.Contains(f)) continue;  // never spawned yet
                newly.Add(f);
            }
            newly.Sort();

            foreach (var f in newly)
            {
                alive.Remove(f);
                em.GetBuffer<EliminatedFactionRecord>(_verdictEntity)
                  .Add(new EliminatedFactionRecord { Value = f, AtSimSeconds = _matchSeconds });
                SelfDestructFactionAssets(em, f);
                TheWaningBorder.AI.AILogger.Log(f, "VICTORY",
                    $"ELIMINATED at {_matchSeconds:0}s - no Hall, no military building, no builders.");
            }

            // ── Decided when no hostile pair remains among the living. ──
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

        /// <summary>Health = 0 for every asset of a retired faction —
        /// DeathSystem owns the destruction (unit-death contract). Runs on
        /// every peer at the same tick, so the wipe is lockstep-identical.</summary>
        private static void SelfDestructFactionAssets(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadWrite<Health>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            q.Dispose();
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
                case "miner":
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
