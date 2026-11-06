// SimpleAISystem.Monuments.cs
// The AI and the monuments (docs/Design/Monuments_And_Petriarchy.md §7): one
// squad at a time runs into the ring of the nearest monument and stays until it
// is taken, and whatever the faction holds it explores once it can pay.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private sealed class MonumentSquad
        {
            public Entity Monument;
            public float3 Position;
            public readonly List<Entity> Members = new List<Entity>();
            public float NextOrder;
        }

        private readonly Dictionary<int, MonumentSquad> _monumentSquads = new Dictionary<int, MonumentSquad>();
        private readonly Dictionary<int, float> _monumentNextThink = new Dictionary<int, float>();
        private readonly Dictionary<(int, int), float> _monumentBlocked = new Dictionary<(int, int), float>();
        private readonly Dictionary<int, int> _monumentFailedSquads = new Dictionary<int, int>();
        private int _monumentEpoch = -1;

        private void TickMonuments(EntityManager em, Faction faction, ref SimpleAIState aiState, float now)
        {
            if (!Cfg.monumentEnabled) return;
            if (GameSettings.IsPassiveAI(faction)) return;
            if (_monumentEpoch != SimCadence.Epoch)
            {
                _monumentEpoch = SimCadence.Epoch;
                _monumentSquads.Clear();
                _monumentNextThink.Clear();
                _monumentBlocked.Clear();
                _monumentFailedSquads.Clear();
            }
            int key = (int)faction;
            if (_monumentNextThink.TryGetValue(key, out float next) && now < next) return;
            _monumentNextThink[key] = now + math.max(1f, Cfg.monumentThinkInterval);

            using var monuments = Monument.All(em, Allocator.Temp);
            if (monuments.Length == 0) return;

            for (int i = 0; i < monuments.Length; i++)
            {
                var s = em.GetComponentData<MonumentState>(monuments[i]);
                if (s.Holder != faction || Monument.HasExplored(s, faction)) continue;
                if (CommandRouter.IssueReligionPurchase(em, faction, ReligionPurchaseKind.ExploreRuins,
                        s.Territory.ToString(), CommandSource.AI))
                    AILogger.Log(faction, "MONUMENT", $"exploring the ruins in territory {s.Territory}");
            }

            if (_monumentSquads.TryGetValue(key, out var squad))
            {
                if (TendMonumentSquad(em, faction, squad, now)) return;
                ReleaseMonumentSquad(key, squad);
            }

            if (now < Cfg.monumentEarliestSeconds) return;
            if (FinishingVictim(faction, out _)) return;
            if (!VictoryAllowsMonumentSquad(faction)) return;
            RaiseMonumentSquad(em, faction, ref aiState, monuments, now);
        }

        /// <summary>False when the squad's work is over: dead, or the meter is full.</summary>
        private bool TendMonumentSquad(EntityManager em, Faction faction, MonumentSquad squad, float now)
        {
            for (int i = squad.Members.Count - 1; i >= 0; i--)
            {
                var m = squad.Members[i];
                if (!em.Exists(m) || !em.HasComponent<Health>(m) || em.GetComponentData<Health>(m).Value <= 0)
                {
                    _sanctumGuards.Remove(m);
                    squad.Members.RemoveAt(i);
                }
            }
            if (squad.Members.Count == 0 || !em.Exists(squad.Monument))
            {
                _monumentBlocked[((int)faction, squad.Monument.Index)] = now + Cfg.monumentBlockedSeconds;
                _monumentFailedSquads.TryGetValue((int)faction, out int failed);
                _monumentFailedSquads[(int)faction] = failed + 1;
                AILogger.Log(faction, "MONUMENT", "the squad is dead, the monument waits");
                return false;
            }

            var s = em.GetComponentData<MonumentState>(squad.Monument);
            if (s.Holder == faction)
            {
                AILogger.Log(faction, "MONUMENT", $"territory {s.Territory}'s monument is ours, the squad goes home");
                return false;
            }

            if (now >= squad.NextOrder)
            {
                AICommon.IssueGroupOrder(em, squad.Members, squad.Position, attackMove: false, Cfg.monumentArrivedRadius);
                squad.NextOrder = now + Cfg.monumentReorderSeconds;
            }
            return true;
        }

        private void ReleaseMonumentSquad(int key, MonumentSquad squad)
        {
            foreach (var m in squad.Members) _sanctumGuards.Remove(m);
            _monumentSquads.Remove(key);
        }

        private void RaiseMonumentSquad(EntityManager em, Faction faction, ref SimpleAIState aiState,
            NativeArray<Entity> monuments, float now)
        {
            int key = (int)faction;
            float3 home = HomeAnchor(em, faction);

            Entity best = Entity.Null;
            float3 bestPos = default;
            float bestDist = float.MaxValue;
            for (int i = 0; i < monuments.Length; i++)
            {
                var s = em.GetComponentData<MonumentState>(monuments[i]);
                if (s.Holder == faction) continue;
                if ((int)s.Holder < 8 && !Alliances.AreHostile(faction, s.Holder)) continue;
                if (_monumentBlocked.TryGetValue((key, monuments[i].Index), out float until) && now < until) continue;
                var p = em.GetComponentData<LocalTransform>(monuments[i]).Position;
                float d = math.distance(home.xz, p.xz);
                if (d < bestDist) { bestDist = d; best = monuments[i]; bestPos = p; }
            }
            if (best == Entity.Null) return;

            int threat = TacticalQuery.EnemyStrengthInRadius(em, faction, bestPos, Cfg.monumentThreatRadius)
                       + AIEngagement.StaticDefencePower(em, faction, bestPos, Cfg.monumentThreatRadius);
            int need = (int)math.ceil(threat * Cfg.monumentLaunchMargin);

            CollectSanctumFree(em, faction, bestPos);
            int floor = StandingArmyFloor(em, faction, ProfileOf(faction), aiState.DesiredMilitary);
            int draftable = math.max(0, _sanctumFree.Count - floor);

            var squad = new MonumentSquad { Monument = best, Position = bestPos };
            int power = 0;
            for (int i = 0; i < draftable && (squad.Members.Count < Cfg.monumentSquadMin || power < need); i++)
            {
                squad.Members.Add(_sanctumFree[i]);
                power += TacticalQuery.UnitStrength(em, _sanctumFree[i]);
            }
            if (squad.Members.Count < Cfg.monumentSquadMin || power < need) return;

            foreach (var m in squad.Members) _sanctumGuards.Add(m);
            AICommon.IssueGroupOrder(em, squad.Members, bestPos, attackMove: false, Cfg.monumentArrivedRadius);
            squad.NextOrder = now + Cfg.monumentReorderSeconds;
            _monumentSquads[key] = squad;
            AILogger.Log(faction, "MONUMENT",
                $"{squad.Members.Count} soldiers march on the monument at ({bestPos.x:0},{bestPos.z:0}), " +
                $"power {power} vs known {threat}");
        }
    }
}
