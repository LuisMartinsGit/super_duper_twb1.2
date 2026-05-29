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
// Location: Assets/Scripts/Systems/Crystal/

using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.UI.HUD;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Crystal
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
                ComponentType.ReadOnly<CrystalNodeState>(),
                ComponentType.ReadOnly<CrystalMainNodeTag>()
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

            // Tally per-state counts. We only need exact match on totalNodes
            // for each state to qualify, so a single pass suffices.
            int cleansedByAlanthor = 0;
            int convertedByRunai = 0;
            int destroyed = 0;

            using var states = _nodeQuery.ToComponentDataArray<CrystalNodeState>(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                switch (s.State)
                {
                    case NodeState.Cleansed:
                        if (s.OwnerCulture == Cultures.Alanthor) cleansedByAlanthor++;
                        break;
                    case NodeState.Converted:
                        if (s.OwnerCulture == Cultures.Runai) convertedByRunai++;
                        break;
                    case NodeState.Destroyed:
                        destroyed++;
                        break;
                }
            }

            float dt = (float)SystemAPI.Time.DeltaTime;

            // Alanthor hold timer
            if (cleansedByAlanthor == totalNodes)
            {
                victory.AlanthorHoldTimer += dt;
                if (victory.AlanthorHoldTimer >= NodeVictoryHoldTime)
                {
                    FireVictory(Cultures.Alanthor, ref victory);
                    EntityManager.SetComponentData(victoryEntity, victory);
                    return;
                }
            }
            else
            {
                victory.AlanthorHoldTimer = 0f;
            }

            // Runai hold timer
            if (convertedByRunai == totalNodes)
            {
                victory.RunaiHoldTimer += dt;
                if (victory.RunaiHoldTimer >= NodeVictoryHoldTime)
                {
                    FireVictory(Cultures.Runai, ref victory);
                    EntityManager.SetComponentData(victoryEntity, victory);
                    return;
                }
            }
            else
            {
                victory.RunaiHoldTimer = 0f;
            }

            // Feraldis instant win — last killing blow on the last active node.
            if (destroyed == totalNodes && victory.LastDestroyerCulture == Cultures.Feraldis)
            {
                FireVictory(Cultures.Feraldis, ref victory);
            }

            EntityManager.SetComponentData(victoryEntity, victory);
        }

        private void FireVictory(byte culture, ref NodeVictoryState victory)
        {
            victory.VictoryFired = 1;

            // Pick a representative faction for the winning culture so the
            // existing VictoryConditionSystem can post its result (its API
            // is Faction-keyed, not culture-keyed). Multi-player culture
            // sharing is a follow-up — for now first match wins.
            Faction winner = FirstFactionOfCulture(culture);

            TWBLog.Log($"[NodeVictorySystem] Node victory triggered — " +
                      $"culture={CultureName(culture)} winner={winner}");

            if (VictoryConditionSystem.Instance != null)
                VictoryConditionSystem.Instance.TriggerNodeVictory(culture, winner);
        }

        private Faction FirstFactionOfCulture(byte culture)
        {
            using var factions = _factionProgressQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progress = _factionProgressQuery.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < factions.Length; i++)
            {
                if (progress[i].Culture == culture)
                    return factions[i].Value;
            }
            // Fallback when no faction has yet locked into the culture (e.g.
            // during pre-Culture-Progression-Age testing) — local player.
            return GameSettings.LocalPlayerFaction;
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
