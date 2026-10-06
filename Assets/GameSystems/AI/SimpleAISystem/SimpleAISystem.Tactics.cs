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
//
// ─────────────────────────────────────────────────────────────────────────
// IN-FIGHT SKILLS (2026-10-04, docs/Design/Game_AI.md § 6e)
//
// "An expert AI should be able to win fights when outnumbered through
// kiting / flanking / clever use of unit abilities. It does none of these."
// The tier's AITacticsSkill (difficulty profile) now decides, per engaged
// army, on top of the focus above:
//
//   * COUNTER TARGETING — the army scores every hostile in contact by danger,
//     nearly-dead, high value AND its counter relationship to this army (its
//     bonusVsTags against our tags, ours against its), keeps the best few,
//     and each member picks among THOSE by its own counter edge and walk. The
//     army still concentrates; the spearmen take the horses.
//   * RANGED BEHIND MELEE — a ranged member only takes a target it can shoot
//     from where it stands; otherwise it holds a firing line behind the melee
//     front instead of walking through it.
//   * FLANKING — on contact, a share of the fast melee swings round the
//     enemy's lighter side to a point behind its line, then strikes the
//     nearest bodies from there (the side / rear the flanking damage rule
//     pays for). They rejoin the army when the fight ends.
//   * FALL BACK — an engaged army reading enemy power above retreatRatio x
//     its own walks back to the nearest friendly tower / Fortress (or toward
//     the capital), and turns round when the odds there drop to
//     reengageRatio, instead of dying one unit at a time.
//
// Kiting and ability casting are per-unit and live in AITacticsMicroSystem.
// ─────────────────────────────────────────────────────────────────────────

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using AbilityCastState = TheWaningBorder.Abilities.AbilityCastState;

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
            if (missions.Count == 0) return;
            // The tier's in-fight skills. A faction with no brain has none and
            // plays the plain focus below (the AI always has one).
            AITactics.TryGetSkill(em, faction, out var skill);

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

                // ── FALLING BACK ──
                // A fall-back in progress owns the army until it re-engages
                // or gives up; nothing below may hand out attack orders.
                if (mission.FallingBack)
                {
                    TickFallback(em, faction, mission, centroid, now, in skill);
                    continue;
                }

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
                    // last enemy died. Flankers rejoin here: they are members.
                    if (mission.Engaged)
                    {
                        mission.Engaged = false;
                        mission.Focus = Entity.Null;
                        EndFlank(mission);
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
                // Pooled (2026-09-25): two lists per engaged army per second.
                // Both are handed to the router, which copies what it keeps.
                // Swinging flankers are out of the body on purpose; striking
                // flankers fight where they landed and are never "strays";
                // a kiting shooter is mid-step and is left alone.
                var body = _tacticsBody; body.Clear();
                var strays = _tacticsStrays; strays.Clear();
                float cohesionSq = Cfg.armyCohesionRadius * Cfg.armyCohesionRadius;
                for (int i = 0; i < mission.Members.Count; i++)
                {
                    var u = mission.Members[i];
                    if (!em.HasComponent<LocalTransform>(u)) continue;
                    bool flanker = mission.FlankPhase != 0 && mission.Flankers.Contains(u);
                    if (flanker && mission.FlankPhase == 1) continue;
                    if (AITactics.IsKiting(em, u)) continue;
                    float3 p = em.GetComponentData<LocalTransform>(u).Position;
                    float dx = p.x - centroid.x, dz = p.z - centroid.z;
                    if (!flanker && dx * dx + dz * dz > cohesionSq) strays.Add(u);
                    else body.Add(u);
                }

                if (strays.Count > 0)
                    // Plain move, not attack-move: the point is to come back,
                    // not to find something else to fight on the way. This also
                    // clears any AttackCommand still dragging them outward.
                    CommandRouter.IssueFormationMove(
                        em, strays, centroid, FormationShape.Box, CommandSource.AI);

                if (body.Count == 0) continue;

                if (!mission.Engaged)
                {
                    mission.Engaged = true;
                    AILogger.Log(faction, "TACTICS",
                        $"army in contact at ({centroid.x:F0},{centroid.z:F0}) — " +
                        $"{body.Count} in formation, {strays.Count} recalled");
                }

                // ── THE ODDS, LIVE. ──
                // One read per engaged army per tick serves the fall-back
                // decision, the flank's enemy centre, and the fight site the
                // sect-power / ability casters aim at.
                AITactics.LivePowerAround(em, faction, centroid, AITactics.Cfg.powerRadius, _tacticsScratch,
                    out int enemyPower, out int ownStatic, out float3 enemyCentroid, out int enemyCount);
                int myPower = ownStatic;
                for (int i = 0; i < mission.Members.Count; i++)
                    myPower += AITactics.LiveStrength(em, mission.Members[i]);
                AITactics.ReportFightSite(em, faction, centroid, myPower, enemyPower);

                // ── FALL BACK? ──
                if (TryStartFallback(em, faction, mission, centroid, enemyCentroid,
                        myPower, enemyPower, now, in skill))
                    continue;

                // ── FLANK. ──
                TickFlank(em, faction, mission, body, centroid, enemyCentroid, enemyCount, now, in skill);

                // ── FOCUS. ──
                bool skilled = skill.counterTargetWeight > 0f || skill.focusFireWeight > 0f
                               || skill.rangedBehindMelee;
                if (skilled)
                {
                    AssignSkilledTargets(em, faction, mission, body, centroid, enemyCentroid, reach, now, in skill);
                    continue;
                }

                // Plain focus (no skill): one target for the whole body.
                // Re-issue only when the target CHANGES, or to units that have
                // lost their order (the combat systems strip AttackCommand when
                // a target dies, and reinforcements arrive without one).
                // Re-ordering every unit every tick resets the chase and the
                // army never actually reaches anybody.
                bool switched = focus != mission.Focus;
                mission.Focus = focus;
                for (int i = 0; i < body.Count; i++)
                {
                    var u = body[i];
                    if (em.HasComponent<AbilityCastState>(u)) continue;
                    if (StrikingFlanker(em, mission, u)) continue;
                    if (!switched && TransientState.Active<AttackCommand>(em, u)
                        && em.GetComponentData<AttackCommand>(u).Target == focus)
                        continue;   // already on it
                    CommandRouter.IssueAttack(em, u, focus, CommandSource.AI);
                }
            }
        }

        private readonly System.Collections.Generic.List<Entity> _tacticsBody =
            new System.Collections.Generic.List<Entity>(32);
        private readonly System.Collections.Generic.List<Entity> _tacticsStrays =
            new System.Collections.Generic.List<Entity>(16);
        private readonly System.Collections.Generic.List<Entity> _tacticsScratch =
            new System.Collections.Generic.List<Entity>(64);

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

        // ─────────────────────────────────────────────────────────────────
        // COUNTER-AWARE FOCUS + RANGED BEHIND MELEE
        // ─────────────────────────────────────────────────────────────────

        private readonly AIArmyProfile _armyProfile = new AIArmyProfile();
        private readonly System.Collections.Generic.List<Entity> _topK =
            new System.Collections.Generic.List<Entity>(8);
        private readonly System.Collections.Generic.List<float> _topKScore =
            new System.Collections.Generic.List<float>(8);

        /// <summary>
        /// The skilled focus: the army scores every hostile in contact
        /// (AITactics.ArmyScore — danger, finish, value, counters), keeps the
        /// best focusTopK, and each member takes the best of THOSE by its own
        /// counter edge and distance. Ranged members restricted to what they
        /// can reach from behind the line. Deterministic: ties go to the lower
        /// entity index; members keep their target unless the new one is
        /// switchMargin better.
        /// </summary>
        private void AssignSkilledTargets(EntityManager em, Faction faction, Mission mission,
            System.Collections.Generic.List<Entity> body, float3 centroid, float3 enemyCentroid,
            float reach, float now, in AITacticsSkill skill)
        {
            var tc = AITactics.Cfg;
            AITactics.BuildProfile(em, body, _armyProfile);

            // ── The army's short list. ──
            AIStrengthMap.HostileUnitCandidates(em, faction, centroid, reach, _tacticsScratch);
            _topK.Clear(); _topKScore.Clear();
            int k = math.max(1, tc.focusTopK);
            float r2 = reach * reach;
            Entity top = Entity.Null;
            AITactics.FocusReason topReason = AITactics.FocusReason.Danger;
            for (int i = 0; i < _tacticsScratch.Count; i++)
            {
                var e = _tacticsScratch[i];
                if (!AITactics.IsLiveHostileUnit(em, faction, e)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                if (math.distancesq(p.xz, centroid.xz) > r2) continue;
                float s = AITactics.ArmyScore(em, e, centroid, _armyProfile, in skill, out var why);
                // Insert sorted (descending score, ascending index on ties).
                int at = _topK.Count;
                while (at > 0 && (s > _topKScore[at - 1]
                                  || (s == _topKScore[at - 1] && e.Index < _topK[at - 1].Index))) at--;
                if (at >= k) continue;
                _topK.Insert(at, e); _topKScore.Insert(at, s);
                if (at == 0) { top = e; topReason = why; }
                if (_topK.Count > k) { _topK.RemoveAt(k); _topKScore.RemoveAt(k); }
            }
            if (_topK.Count == 0) return;
            top = _topK[0];

            if (top != mission.Focus)
            {
                mission.Focus = top;
                if (AITactics.LogDue(em, faction, "focus", tc.logIntervalSeconds))
                    AILogger.Log(faction, "TARGET",
                        $"focus {AITactics.IdOf(em, top)} ({ReasonWord(topReason)}) — " +
                        $"{_topK.Count} on the short list, {body.Count} in the body");
            }

            // ── The melee front and the line behind it. ──
            float3 meleeSum = float3.zero; int meleeN = 0;
            for (int i = 0; i < body.Count; i++)
                if (AITactics.IsMelee(em, body[i]))
                {
                    meleeSum += em.GetComponentData<LocalTransform>(body[i]).Position; meleeN++;
                }
            float2 axis = math.normalizesafe(enemyCentroid.xz - centroid.xz);
            bool haveLine = skill.rangedBehindMelee && meleeN > 0 && math.lengthsq(axis) > 1e-4f;
            float3 meleeFront = meleeN > 0 ? meleeSum / meleeN : centroid;
            float2 perp = new float2(-axis.y, axis.x);
            int rangedIndex = 0;

            for (int i = 0; i < body.Count; i++)
            {
                var u = body[i];
                if (em.HasComponent<AbilityCastState>(u)) continue;
                if (StrikingFlanker(em, mission, u)) continue;
                float3 up = em.GetComponentData<LocalTransform>(u).Position;
                uint uMask = AITactics.TagsOf(em, u);
                var uBonus = em.HasComponent<BonusVsTags>(u) ? em.GetComponentData<BonusVsTags>(u) : default;
                float range = AITactics.RangeOf(em, u);
                bool ranged = range > 0f;

                Entity current = TransientState.Active<AttackCommand>(em, u)
                    ? em.GetComponentData<AttackCommand>(u).Target : Entity.Null;
                Entity best = Entity.Null;
                float bestS = float.MinValue, curS = float.MinValue;
                for (int c = 0; c < _topK.Count; c++)
                {
                    var e = _topK[c];
                    float3 ep = em.GetComponentData<LocalTransform>(e).Position;
                    if (ranged && haveLine
                        && math.distance(ep.xz, up.xz) > range + tc.rangedReachSlack) continue;
                    float s = AITactics.MemberScore(em, u, uMask, uBonus, up, e, _topKScore[c], in skill);
                    if (e == current) curS = s;
                    if (s > bestS) { bestS = s; best = e; }
                }

                if (best == Entity.Null)
                {
                    // A shooter with nothing it can reach from here holds the
                    // firing line behind the melee front — spread along it by
                    // its index among the shooters — rather than walking
                    // through its own front rank to get a shot.
                    if (!haveLine) continue;
                    int slots = math.max(1, tc.rangedLineSlots);
                    float lateral = (rangedIndex % slots - (slots - 1) * 0.5f) * tc.rangedLineSpacing;
                    rangedIndex++;
                    float2 spot = meleeFront.xz - axis * tc.rangedStandoff + perp * lateral;
                    if (math.distance(spot, up.xz) <= tc.rangedLineTolerance) continue;
                    if (em.HasComponent<DesiredDestination>(u))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(u);
                        if (dd.Has != 0 && math.distance(dd.Position.xz, spot) <= tc.rangedLineTolerance) continue;
                    }
                    CommandRouter.IssueMove(em, u, new float3(spot.x, up.y, spot.y), CommandSource.AI);
                    continue;
                }

                if (best == current) continue;
                if (current != Entity.Null && curS > float.MinValue && bestS < curS + tc.switchMargin) continue;
                CommandRouter.IssueAttack(em, u, best, CommandSource.AI);
            }
        }

        private static string ReasonWord(AITactics.FocusReason r)
            => r == AITactics.FocusReason.Counter ? "counter"
             : r == AITactics.FocusReason.Finish ? "finish"
             : r == AITactics.FocusReason.Value ? "high value"
             : "danger";

        // ─────────────────────────────────────────────────────────────────
        // FLANKING
        // ─────────────────────────────────────────────────────────────────

        private readonly System.Collections.Generic.List<Entity> _flankPick =
            new System.Collections.Generic.List<Entity>(16);

        /// <summary>A flanker that landed and is still on the target it
        /// struck from the rear — leave it there until that body dies.</summary>
        private static bool StrikingFlanker(EntityManager em, Mission mission, Entity u)
            => mission.FlankPhase == 2 && mission.Flankers.Contains(u)
               && TransientState.Active<AttackCommand>(em, u);

        private static void EndFlank(Mission mission)
        {
            mission.FlankPhase = 0;
            mission.Flankers.Clear();
        }

        /// <summary>
        /// Send, run and land the flank. Deterministic: eligible members are
        /// taken fastest first (ties by entity index); the side is the one
        /// with FEWER enemies on it (the exposed wing), ties to the right.
        /// </summary>
        private void TickFlank(EntityManager em, Faction faction, Mission mission,
            System.Collections.Generic.List<Entity> body, float3 centroid, float3 enemyCentroid,
            int enemyCount, float now, in AITacticsSkill skill)
        {
            var tc = AITactics.Cfg;

            // Drop the dead from the flank roster.
            for (int i = mission.Flankers.Count - 1; i >= 0; i--)
                if (!em.Exists(mission.Flankers[i])) mission.Flankers.RemoveAt(i);
            if (mission.FlankPhase != 0 && mission.Flankers.Count == 0) EndFlank(mission);

            // ── Swinging: strike once there (or late). ──
            if (mission.FlankPhase == 1)
            {
                float3 sum = float3.zero; int n = 0;
                for (int i = 0; i < mission.Flankers.Count; i++)
                {
                    var f = mission.Flankers[i];
                    if (!em.HasComponent<LocalTransform>(f)) continue;
                    sum += em.GetComponentData<LocalTransform>(f).Position; n++;
                }
                bool there = n > 0
                    && math.distance((sum / n).xz, mission.FlankPoint.xz) <= tc.flankArriveRadius;
                bool late = now - mission.FlankStart > tc.flankTimeout;
                if (!there && !late) return;

                // Strike the nearest hostile to each flanker — from behind the
                // line that is its rear rank, usually the shooters.
                AIStrengthMap.HostileUnitCandidates(em, faction, mission.FlankPoint,
                    tc.flankLateral + tc.flankDepth, _tacticsScratch);
                int struck = 0;
                for (int i = 0; i < mission.Flankers.Count; i++)
                {
                    var f = mission.Flankers[i];
                    if (!em.HasComponent<LocalTransform>(f)) continue;
                    float3 fp = em.GetComponentData<LocalTransform>(f).Position;
                    Entity best = Entity.Null; float bd = float.MaxValue;
                    for (int c = 0; c < _tacticsScratch.Count; c++)
                    {
                        var e = _tacticsScratch[c];
                        if (!AITactics.IsLiveHostileUnit(em, faction, e)) continue;
                        float d = math.distancesq(em.GetComponentData<LocalTransform>(e).Position.xz, fp.xz);
                        if (d < bd || (d == bd && e.Index < best.Index)) { bd = d; best = e; }
                    }
                    if (best == Entity.Null) continue;
                    CommandRouter.IssueAttack(em, f, best, CommandSource.AI);
                    struck++;
                }
                mission.FlankPhase = 2;
                AILogger.Log(faction, "TACTICS",
                    $"flank group {mission.Flankers.Count} strikes{(late && !there ? " (late)" : "")} — {struck} engaged");
                return;
            }
            if (mission.FlankPhase != 0) return;   // striking: they fight with the army

            // ── Send one? ──
            if (skill.flankFraction <= 0f || now < mission.NextFlankTime) return;
            if (body.Count < tc.flankMinArmy || enemyCount < tc.flankMinEnemies) return;
            mission.NextFlankTime = now + tc.flankRetrySeconds;

            float2 axis = math.normalizesafe(enemyCentroid.xz - centroid.xz);
            if (math.lengthsq(axis) < 1e-4f) return;
            float meanSpeed = 0f;
            for (int i = 0; i < body.Count; i++) meanSpeed += AITactics.SpeedOf(em, body[i]);
            meanSpeed /= body.Count;

            _flankPick.Clear();
            for (int i = 0; i < body.Count; i++)
            {
                var u = body[i];
                if (!AITactics.IsMelee(em, u) || AITactics.IsHero(em, u)) continue;
                if (!AITactics.IsFast(em, u, meanSpeed)) continue;
                _flankPick.Add(u);
            }
            int want = (int)math.floor(body.Count * skill.flankFraction);
            if (want < tc.flankMinGroup || _flankPick.Count < tc.flankMinGroup) return;
            _flankPick.Sort((a, b) =>
            {
                float sa = AITactics.SpeedOf(em, a), sb = AITactics.SpeedOf(em, b);
                return sa != sb ? sb.CompareTo(sa) : a.Index.CompareTo(b.Index);
            });
            if (_flankPick.Count > want) _flankPick.RemoveRange(want, _flankPick.Count - want);

            // The exposed wing: fewer enemies on that side of their centre.
            float2 perp = new float2(-axis.y, axis.x);
            int plus = 0, minus = 0;
            for (int i = 0; i < _tacticsScratch.Count; i++)
            {
                var e = _tacticsScratch[i];
                if (!AITactics.IsLiveHostileUnit(em, faction, e)) continue;
                float side = math.dot(em.GetComponentData<LocalTransform>(e).Position.xz - enemyCentroid.xz, perp);
                if (side > 0f) plus++; else if (side < 0f) minus++;
            }
            float sideSign = plus <= minus ? 1f : -1f;
            float2 point = enemyCentroid.xz + perp * (sideSign * tc.flankLateral) + axis * tc.flankDepth;

            mission.Flankers.Clear();
            mission.Flankers.AddRange(_flankPick);
            for (int i = 0; i < _flankPick.Count; i++) body.Remove(_flankPick[i]);
            mission.FlankPhase = 1;
            mission.FlankStart = now;
            mission.FlankPoint = new float3(point.x, enemyCentroid.y, point.y);

            // Plain move: they must not stop to brawl on the way round.
            CommandRouter.IssueFormationMove(em, mission.Flankers, mission.FlankPoint,
                FormationShape.Box, CommandSource.AI);

            // The angle they will hit from, against the enemy's front (which
            // faces us): 0 = head-on, 90 = side, 180 = rear.
            float2 hit = math.normalizesafe(point - enemyCentroid.xz);
            float angle = math.degrees(math.acos(math.clamp(math.dot(hit, -axis), -1f, 1f)));
            AILogger.Log(faction, "TACTICS",
                $"flank group {mission.Flankers.Count} -> {angle:F0} deg ({(sideSign > 0f ? "right" : "left")} wing, " +
                $"{(sideSign > 0f ? plus : minus)} enemies on it) via ({point.x:F0},{point.y:F0})");
        }

        // ─────────────────────────────────────────────────────────────────
        // FALL BACK / REGROUP
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Losing (enemy power above retreatRatio x mine): walk the whole army
        /// back to the nearest friendly tower / Fortress that is not deeper
        /// into the enemy, else fallbackDistance toward the capital. Never at
        /// home (base defence owns that), never for a handful of units.
        /// </summary>
        private bool TryStartFallback(EntityManager em, Faction faction, Mission mission,
            float3 centroid, float3 enemyCentroid, int myPower, int enemyPower, float now,
            in AITacticsSkill skill)
        {
            var tc = AITactics.Cfg;
            if (skill.retreatRatio <= 0f) return false;
            if (mission.Members.Count < tc.fallbackMinArmy) return false;
            if (enemyPower <= 0 || enemyPower <= myPower * skill.retreatRatio) return false;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            bool hasHall = hall != Entity.Null && em.HasComponent<LocalTransform>(hall);
            float3 hallPos = hasHall ? em.GetComponentData<LocalTransform>(hall).Position : centroid;
            if (hasHall && math.distance(centroid.xz, hallPos.xz) <= tc.fallbackSafeHomeRadius) return false;

            float3 dest;
            string where;
            float toEnemy = math.distance(centroid.xz, enemyCentroid.xz);
            if (AIStrengthMap.NearestFriendlyDefence(em, faction, centroid, tc.fallbackAnchorSearch,
                    out float3 anchor, out int anchorPower)
                && math.distance(anchor.xz, enemyCentroid.xz) > toEnemy)
            {
                dest = anchor;
                where = $"tower/fortress (power {anchorPower})";
            }
            else
            {
                float2 home = hasHall
                    ? math.normalizesafe(hallPos.xz - centroid.xz)
                    : math.normalizesafe(centroid.xz - enemyCentroid.xz);
                if (math.lengthsq(home) < 1e-4f) return false;
                float2 p = centroid.xz + home * tc.fallbackDistance;
                dest = new float3(p.x, centroid.y, p.y);
                where = hasHall ? "toward the capital" : "away from the enemy";
            }

            mission.FallingBack = true;
            mission.FallbackPos = dest;
            mission.FallbackStart = now;
            mission.Engaged = false;
            mission.Focus = Entity.Null;
            EndFlank(mission);
            CommandRouter.IssueFormationMove(em, mission.Members, dest, FormationShape.Box, CommandSource.AI);
            AILogger.Log(faction, "TACTICS",
                $"retreat (power {myPower} vs {enemyPower}, ratio {enemyPower / (float)math.max(1, myPower):F2} > {skill.retreatRatio:F2}) " +
                $"— {mission.Members.Count} fall back {where} at ({dest.x:F0},{dest.z:F0})");
            return true;
        }

        /// <summary>
        /// While falling back: after fallbackHoldSeconds, re-engage when the
        /// odds around the army have dropped to reengageRatio (towers count
        /// for us now) or the enemy did not follow; after fallbackTimeout with
        /// the enemy still stronger, fall back to the capital, and stop
        /// managing it once there.
        /// </summary>
        private void TickFallback(EntityManager em, Faction faction, Mission mission,
            float3 centroid, float now, in AITacticsSkill skill)
        {
            var tc = AITactics.Cfg;
            float elapsed = now - mission.FallbackStart;
            if (elapsed < tc.fallbackHoldSeconds) return;

            AITactics.LivePowerAround(em, faction, centroid, tc.powerRadius, _tacticsScratch,
                out int enemyPower, out int ownStatic, out _, out _);
            int myPower = ownStatic;
            for (int i = 0; i < mission.Members.Count; i++)
                myPower += AITactics.LiveStrength(em, mission.Members[i]);
            if (enemyPower > 0) AITactics.ReportFightSite(em, faction, centroid, myPower, enemyPower);

            bool arrived = math.distance(centroid.xz, mission.FallbackPos.xz) <= tc.fallbackArriveRadius;
            bool odds = enemyPower <= myPower * math.max(0f, skill.reengageRatio);
            bool timedOut = elapsed > tc.fallbackTimeout;

            if ((enemyPower == 0 && (arrived || timedOut)) || (enemyPower > 0 && odds))
            {
                mission.FallingBack = false;
                CommandRouter.IssueFormationAttackMove(
                    em, mission.Members, mission.TargetPos, FormationShape.Box, CommandSource.AI);
                AILogger.Log(faction, "TACTICS",
                    $"re-engage (power {myPower} vs {enemyPower}) — {mission.Members.Count} march on " +
                    $"({mission.TargetPos.x:F0},{mission.TargetPos.z:F0})");
                return;
            }
            if (!timedOut) return;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall))
            {
                mission.FallingBack = false;
                return;
            }
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;
            if (math.distance(mission.FallbackPos.xz, hallPos.xz) <= tc.fallbackArriveRadius)
            {
                // Already home: base defence owns it from here.
                mission.FallingBack = false;
                AILogger.Log(faction, "TACTICS",
                    $"fall-back ended at the capital (power {myPower} vs {enemyPower})");
                return;
            }
            mission.FallbackPos = hallPos;
            mission.FallbackStart = now;
            CommandRouter.IssueFormationMove(em, mission.Members, hallPos, FormationShape.Box, CommandSource.AI);
            AILogger.Log(faction, "TACTICS",
                $"retreat (power {myPower} vs {enemyPower}) — still outmatched, falling back to the capital");
        }
    }
}
