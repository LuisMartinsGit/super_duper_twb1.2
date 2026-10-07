// SimpleAISystem.Support.cs
// LITHARCHS HEAL THE ARMY (2026-10-07, docs/Design/Game_AI.md § 6m).
// Developer: "AI does not use Litharchs as healers." Three gates stopped it:
// no Temple ever stood (fixed in the age-2 ladder), the composition layer
// never names a Support unit, and every draft site takes only combat
// classes, so a Litharch would have idled at home anyway.
//
// This pass, every AISupport.thinkInterval:
//   * TRAIN — with a finished Temple, keep one healer per
//     combatUnitsPerHealer combat units (up to maxHealers), one at a time.
//   * FOLLOW — a healer more than followDistance from the army's biggest
//     mission is walked to just behind its centre. Inside that distance it
//     is left alone: its own auto-heal search (LitharchHealingSystem) does
//     the healing, and a move order would cancel it.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Entities;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private readonly Dictionary<int, float> _nextSupportThink = new Dictionary<int, float>();
        private readonly List<Entity> _healers = new List<Entity>(8);

        private void TickSupport(EntityManager em, Faction faction, float3 homePos, float now)
        {
            var cfg = AISupport.Cfg;
            if (!cfg.enabled) return;
            int key = (int)faction;
            if (_nextSupportThink.TryGetValue(key, out float next) && now < next) return;
            _nextSupportThink[key] = now + math.max(1f, cfg.thinkInterval);

            AISupport.Healers(em, faction, _healers);

            // ── Train ──
            if (CountFinished<TempleOfRidanTag>(em, faction) > 0
                && _healers.Count < AISupport.Wanted(CountAliveMilitary(em, faction))
                && !AICommon.IsUnitQueued(em, faction, "Litharch"))
            {
                if (TryTrainUnitWithReason(em, faction, "Litharch", out string why))
                    AILogger.Log(faction, "SUPPORT", $"training a Litharch ({_healers.Count} healers)");
                else if (why != null)
                    AILogger.Log(faction, "SUPPORT", $"Litharch not trained: {why}");
            }

            // ── Follow ──
            if (_healers.Count == 0) return;
            Mission best = null;
            foreach (var m in MissionsFor(faction))
                if (m.Members.Count >= cfg.minArmyToFollow && (best == null || m.Members.Count > best.Members.Count))
                    best = m;
            if (best == null) return;
            float3 c = best.LastCentroid;
            float3 back = homePos - c; back.y = 0f;
            float len = math.length(back);
            float3 spot = len > 1f ? c + back / len * cfg.followBehind : c;
            int sent = 0;
            foreach (var h in _healers)
            {
                if (!em.HasComponent<LocalTransform>(h)) continue;
                if (TransientState.Active<UserMoveOrder>(em, h)) continue;   // already on the way
                float3 p = em.GetComponentData<LocalTransform>(h).Position;
                if (math.distance(p.xz, c.xz) <= cfg.followDistance) continue;
                CommandRouter.IssueMove(em, h, spot, CommandSource.AI);
                sent++;
            }
            if (sent > 0)
                AILogger.Log(faction, "SUPPORT",
                    $"{sent} Litharch(es) follow the army ({best.Members.Count} strong) to ({spot.x:0},{spot.z:0})");
        }
    }
}
