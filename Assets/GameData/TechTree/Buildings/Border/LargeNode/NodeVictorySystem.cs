// NodeVictorySystem.cs
// Tracks per-culture node-victory progress. Spec §8:
//   - Alanthor wins when every main node is Cleansed-by-Alanthor and that
//     state is held for NodeVictoryHoldTime (5 min).
//   - Runai wins when every main node is Converted-by-Runai and held 5 min.
//   - Feraldis wins INSTANTLY when every main node is Destroyed AND the
//     most-recent destroyer's culture is Feraldis (no hold timer).
//
// "Held" means: the hold timer ticks only while the all-claimed condition
// holds and resets to 0 the moment any node falls out (reverts, gets
// converted by a rival, gets destroyed, etc.).
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.BorderConstants;

using TheWaningBorder.Core;
using TheWaningBorder.Systems.Core;
namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Polls every main node and updates the singleton NodeVictoryState.
    /// Triggers VictoryConditionSystem on a win.
    ///
    /// SystemBase (managed) so we can call into the managed
    /// VictoryConditionSystem MonoBehaviour at trigger time.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NodeStateDeathInterceptSystem))]
    [UpdateAfter(typeof(NodeStateReversionSystem))]
    public partial class NodeVictorySystem : SystemBase
    {
        private EntityQuery _nodeQuery;
        private EntityQuery _victoryQuery;
        private EntityQuery _factionProgressQuery;

        protected override void OnCreate()
        {
            _nodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderNodeState>(),
                ComponentType.ReadOnly<BorderMainNodeTag>()
            );
            _victoryQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<NodeVictoryState>()
            );
            _factionProgressQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>()
            );
            RequireForUpdate(_victoryQuery);
        }

        protected override void OnUpdate()
        {
            using var victoryEntities = _victoryQuery.ToEntityArray(Allocator.Temp);
            Entity victoryEntity = victoryEntities[0];
            var victory = EntityManager.GetComponentData<NodeVictoryState>(victoryEntity);

            if (victory.VictoryFired != 0) return;

            int totalNodes = _nodeQuery.CalculateEntityCount();
            // No nodes seeded yet (pre-bootstrap or sandbox) — nothing to score.
            if (totalNodes <= 0)
            {
                EntityManager.SetComponentData(victoryEntity, victory);
                return;
            }

            // ── Curse & Shardroot canon: WELL DOMINATION, per-PLAYER ─────
            // Tally each faction's verb-claimed wells: Cleansed (Purified) /
            // Converted (Pacified) / Destroyed, keyed by BorderNodeState
            // .OwnerFaction. A player wins when ALL wells are simultaneously
            // theirs (Feraldis instantly; ritual verbs after a short grace).
            const int MaxFactions = 9; // Blue..White + Border
            var cleansedBy = new NativeArray<int>(MaxFactions, Allocator.Temp);
            var convertedBy = new NativeArray<int>(MaxFactions, Allocator.Temp);
            var destroyedBy = new NativeArray<int>(MaxFactions, Allocator.Temp);

            using (var states = _nodeQuery.ToComponentDataArray<BorderNodeState>(Allocator.Temp))
            {
                for (int i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    int owner = (int)s.OwnerFaction;
                    if (owner < 0 || owner >= MaxFactions) continue;
                    switch (s.State)
                    {
                        case NodeState.Cleansed:
                            if (s.OwnerCulture == Cultures.Alanthor) cleansedBy[owner]++;
                            break;
                        case NodeState.Converted:
                            if (s.OwnerCulture == Cultures.Runai) convertedBy[owner]++;
                            break;
                        case NodeState.Destroyed:
                            if (s.OwnerCulture == Cultures.Feraldis) destroyedBy[owner]++;
                            break;
                    }
                }
            }

            float dt = (float)SystemAPI.Time.DeltaTime;

            // Feraldis instant win + candidate/match-point scan.
            Faction cleanseCandidate = Faction.Border;   // Border = none
            Faction convertCandidate = Faction.Border;
            Faction matchPoint = Faction.Border;
            int matchPointCount = 0;

            for (int f = 0; f < MaxFactions; f++)
            {
                if ((Faction)f == Faction.Border) continue;

                if (destroyedBy[f] == totalNodes)
                {
                    FireVictory(Cultures.Feraldis, (Faction)f, ref victory);
                    EntityManager.SetComponentData(victoryEntity, victory);
                    cleansedBy.Dispose(); convertedBy.Dispose(); destroyedBy.Dispose();
                    return;
                }
                if (cleansedBy[f] == totalNodes) cleanseCandidate = (Faction)f;
                if (convertedBy[f] == totalNodes) convertCandidate = (Faction)f;

                // Match point: N−1 wells claimed under one verb (N ≥ 2).
                int best = cleansedBy[f];
                if (convertedBy[f] > best) best = convertedBy[f];
                if (destroyedBy[f] > best) best = destroyedBy[f];
                if (totalNodes >= 2 && best == totalNodes - 1 && best > matchPointCount)
                {
                    matchPoint = (Faction)f;
                    matchPointCount = best;
                }
            }
            cleansedBy.Dispose(); convertedBy.Dispose(); destroyedBy.Dispose();

            // Match-point broadcast (once per approach; re-arms when the
            // leader falls back below N−1 or a new leader appears).
            if (matchPoint != Faction.Border)
            {
                if (victory.MatchPointFaction != matchPoint)
                {
                    victory.MatchPointFaction = matchPoint;
                    SimSignals.Notify(
                        string.Format(Loc.T("{0} holds all but ONE well — stop them!"), matchPoint));
                }
            }
            else
            {
                victory.MatchPointFaction = Faction.Border;
            }

            // Purify-domination grace timer (per-faction candidate).
            if (cleanseCandidate != Faction.Border)
            {
                if (victory.CleansedCandidate != cleanseCandidate)
                    victory.AlanthorHoldTimer = 0f;
                victory.CleansedCandidate = cleanseCandidate;
                victory.AlanthorHoldTimer += dt;
                if (victory.AlanthorHoldTimer >= NodeVictoryHoldTime)
                {
                    FireVictory(Cultures.Alanthor, cleanseCandidate, ref victory);
                    EntityManager.SetComponentData(victoryEntity, victory);
                    return;
                }
            }
            else
            {
                victory.CleansedCandidate = Faction.Border;
                victory.AlanthorHoldTimer = 0f;
            }

            // Pacify-domination grace timer.
            if (convertCandidate != Faction.Border)
            {
                if (victory.ConvertedCandidate != convertCandidate)
                    victory.RunaiHoldTimer = 0f;
                victory.ConvertedCandidate = convertCandidate;
                victory.RunaiHoldTimer += dt;
                if (victory.RunaiHoldTimer >= NodeVictoryHoldTime)
                {
                    FireVictory(Cultures.Runai, convertCandidate, ref victory);
                    EntityManager.SetComponentData(victoryEntity, victory);
                    return;
                }
            }
            else
            {
                victory.ConvertedCandidate = Faction.Border;
                victory.RunaiHoldTimer = 0f;
            }

            EntityManager.SetComponentData(victoryEntity, victory);
        }

        private void FireVictory(byte culture, Faction winner, ref NodeVictoryState victory)
        {
            victory.VictoryFired = 1;

            TWBLog.Log($"[NodeVictorySystem] Well domination — " +
                      $"culture={CultureName(culture)} winner={winner}");

            if (VictoryConditionSystem.Instance != null)
                VictoryConditionSystem.Instance.TriggerNodeVictory(culture, winner);
        }

        private static string CultureName(byte culture) => culture switch
        {
            Cultures.None     => "None",
            Cultures.Runai    => "Runai",
            Cultures.Alanthor => "Alanthor",
            Cultures.Feraldis => "Feraldis",
            _ => $"Culture({culture})",
        };
    }
}
