// SimpleAISystem.March.cs
// An army's long march goes round the curse (2026-10-07, docs/Design/
// Game_AI.md § 6k; the planner is AICurseRoute). March() replaces a single
// formation move with a chain of waypoints when the straight line crosses
// cursed ground; TickRoute walks the chain as the army's centre reaches each
// point. An army in a fight drops its route (the tactics layer owns it then)
// and gets a fresh one when it resumes its march.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private readonly List<float3> _routeScratch = new List<float3>(16);

        /// <summary>March <paramref name="m"/> to <paramref name="dest"/>,
        /// round the curse when the straight line crosses it.</summary>
        private void March(EntityManager em, Faction faction, Mission m, float3 dest, bool attackMove, float3 from)
        {
            m.Route.Clear();
            m.RouteIdx = 0;
            m.RouteAttack = attackMove;
            if (AICurseRoute.TryRoute(em, from, dest, _routeScratch))
            {
                m.Route.AddRange(_routeScratch);
                AILogger.Log(faction, "ROUTE",
                    $"{m.Members.Count} march round the curse to ({dest.x:0},{dest.z:0}) — " +
                    $"{m.Route.Count - 1} waypoint(s)");
                IssueLeg(em, m, m.Route.Count == 1 && attackMove);
                return;
            }
            if (attackMove)
                CommandRouter.IssueFormationAttackMove(em, m.Members, dest, FormationShape.Box, CommandSource.AI);
            else
                CommandRouter.IssueFormationMove(em, m.Members, dest, FormationShape.Box, CommandSource.AI);
        }

        private static void IssueLeg(EntityManager em, Mission m, bool attack)
        {
            var p = m.Route[m.RouteIdx];
            if (attack)
                CommandRouter.IssueFormationAttackMove(em, m.Members, p, FormationShape.Box, CommandSource.AI);
            else
                CommandRouter.IssueFormationMove(em, m.Members, p, FormationShape.Box, CommandSource.AI);
        }

        /// <summary>Advance a routed march once the army reaches its waypoint.</summary>
        private void TickRoute(EntityManager em, Mission m, float3 centroid)
        {
            if (m.Route.Count == 0 || m.Engaged) return;
            if (math.distance(centroid.xz, m.Route[m.RouteIdx].xz) > AICurseRouteConfig.I.arriveMeters) return;
            m.RouteIdx++;
            if (m.RouteIdx >= m.Route.Count) { m.Route.Clear(); m.RouteIdx = 0; return; }
            bool last = m.RouteIdx == m.Route.Count - 1;
            IssueLeg(em, m, last && m.RouteAttack);
        }
    }
}
