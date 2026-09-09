// SimpleAISystem.Tactics.cs
// The TACTICAL layer: what an army does once it is in contact.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// THE TWO LAYERS
//
// SimpleAISystem.Military.cs is the DISPATCH layer: it forms armies
// (missions), picks their objectives, stages them, reinforces them, and
// retreats them. It answers "which army goes where".
//
// This file answers "what does that army do when it arrives", and until now
// nothing did. Dispatch sent the army out as a formation and then handed every
// unit to TargetingSystem's auto-acquire, which is a PER-UNIT rule: each unit
// independently grabs whatever enemy is nearest to itself. Two things follow,
// and together they are the whole reported symptom of an AI "controlling each
// unit individually":
//
//   1. FormationGroupSystem dissolves a member the instant it acquires a
//      target. So first contact deletes the army — from that moment there is
//      no formation, only fifteen units.
//   2. Nearest-to-ME is a different answer for every unit, so the army fans
//      out along the enemy line, each unit walking to its own private fight.
//      Nobody concentrates, wounded enemies escape, and the parts get beaten
//      in detail by a body that stayed together.
//
// AIEngagement.PickPriorityTarget was written to answer "which of them do I
// kill first" and had NO CALLERS — the focus-fire logic existed and was never
// wired to anything. This is that wiring, plus the cohesion rule that stops
// the army spreading to reach it.
//
// The lever is AttackCommand: TargetingSystem's acquire query is
// `.WithNone<AttackCommand>()`, so a unit under an explicit attack order is
// exempt from auto-acquire and holds the target the army chose. The combat
// systems strip the component when the target dies, which is exactly when the
// army should be choosing again.
// ─────────────────────────────────────────────────────────────────────────

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {




        /// <summary>
        /// Run the tactical layer for every mission this faction owns.
        /// Called after UpdateMissions, which has already pruned the dead and
        /// disbanded anything finished — so every mission seen here is live.
        /// </summary>
        private void TickArmyTactics(EntityManager em, Faction faction, float now)
        {
            var missions = MissionsFor(faction);
            for (int m = 0; m < missions.Count; m++)
            {
                var mission = missions[m];
                if (mission.Members.Count == 0) continue;

                // Staging armies are still forming up. Interrupting that with
                // target orders is what forward staging exists to prevent —
                // the army would trickle into the fight instead of arriving.
                if (mission.Phase == MissionPhase.Staging
                    || mission.Phase == MissionPhase.Mustering) continue;

                if (now < mission.NextTacticsTime) continue;
                mission.NextTacticsTime = now + Cfg.tacticsInterval;

                float3 centroid = ArmyCentroid(em, mission, out int counted);
                if (counted == 0) continue;

                // ── IS THIS ARMY IN CONTACT? ──
                // Decided BEFORE anything else, because out of contact this
                // layer must do NOTHING. An army on the march is already held
                // together by the formation it was dispatched in; recalling
                // its rear rank to the centroid would cancel the very march
                // order the dispatch layer just gave it, and the army would
                // walk on the spot.
                //
                // Hysteresis: engage on a target within FocusRadius, stay
                // engaged while one is within the wider ContactRadius.
                float reach = mission.Engaged ? Cfg.contactRadius : Cfg.focusRadius;
                var focus = AIEngagement.PickPriorityTarget(em, faction, centroid, reach);

                if (focus == Entity.Null)
                {
                    // Nothing in reach. If the army was fighting a moment ago,
                    // the local battle is won — put it back on the march as one
                    // formation rather than leaving units standing where the
                    // last enemy died.
                    if (mission.Engaged)
                    {
                        mission.Engaged = false;
                        mission.Focus = Entity.Null;
                        CommandRouter.IssueFormationAttackMove(
                            em, mission.Members, mission.TargetPos,
                            FormationShape.Box, CommandSource.AI);
                        AILogger.Log(faction, "TACTICS",
                            $"army disengaged ({mission.Members.Count} left), " +
                            $"resuming march on ({mission.TargetPos.x:F0},{mission.TargetPos.z:F0})");
                    }
                    continue;
                }

                // ── COHESION. ──
                // A unit that has wandered off is recalled before anything
                // else. Giving it a target would only pull it further out, and
                // an army strung across 40 m is a queue of single units for
                // whatever it walks into.
                var body = new System.Collections.Generic.List<Entity>(mission.Members.Count);
                var strays = new System.Collections.Generic.List<Entity>();
                float cohesionSq = Cfg.armyCohesionRadius * Cfg.armyCohesionRadius;
                for (int i = 0; i < mission.Members.Count; i++)
                {
                    var u = mission.Members[i];
                    if (!em.HasComponent<LocalTransform>(u)) continue;
                    float3 p = em.GetComponentData<LocalTransform>(u).Position;
                    float dx = p.x - centroid.x, dz = p.z - centroid.z;
                    if (dx * dx + dz * dz > cohesionSq) strays.Add(u);
                    else body.Add(u);
                }

                if (strays.Count > 0)
                    // Plain move, not attack-move: the point is to come back,
                    // not to find something else to fight on the way. This also
                    // clears any AttackCommand still dragging them outward.
                    CommandRouter.IssueFormationMove(
                        em, strays, centroid, FormationShape.Box, CommandSource.AI);

                if (body.Count == 0) continue;

                // ── FOCUS. ──
                // Re-issue only when the target CHANGES, or to units that have
                // lost their order (the combat systems strip AttackCommand when
                // a target dies, and reinforcements arrive without one).
                // Re-ordering every unit every tick resets the chase and the
                // army never actually reaches anybody.
                bool switched = focus != mission.Focus;
                mission.Focus = focus;

                if (!mission.Engaged)
                {
                    mission.Engaged = true;
                    AILogger.Log(faction, "TACTICS",
                        $"army in contact at ({centroid.x:F0},{centroid.z:F0}) — " +
                        $"{body.Count} in formation, {strays.Count} recalled");
                }

                for (int i = 0; i < body.Count; i++)
                {
                    var u = body[i];
                    if (!switched && TransientState.Active<AttackCommand>(em, u)
                        && em.GetComponentData<AttackCommand>(u).Target == focus)
                        continue;   // already on it
                    CommandRouter.IssueAttack(em, u, focus, CommandSource.AI);
                }
            }
        }

        /// <summary>Mean position of a mission's living members.</summary>
        private static float3 ArmyCentroid(EntityManager em, Mission mission, out int counted)
        {
            float3 sum = float3.zero;
            counted = 0;
            for (int i = 0; i < mission.Members.Count; i++)
            {
                var u = mission.Members[i];
                if (!em.HasComponent<LocalTransform>(u)) continue;
                sum += em.GetComponentData<LocalTransform>(u).Position;
                counted++;
            }
            return counted > 0 ? sum / counted : float3.zero;
        }
    }
}
