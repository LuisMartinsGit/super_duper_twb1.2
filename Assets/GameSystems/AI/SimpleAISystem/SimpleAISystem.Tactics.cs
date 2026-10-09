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
                        March(em, faction, mission, mission.TargetPos, true, centroid);
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
                    mission.Route.Clear();   // the fight owns the army now (§ 6k)
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
                    if (TransientState.Active<AbilityCastState>(em, u)) continue;
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
                if (TransientState.Active<AbilityCastState>(em, u)) continue;
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
            // THE ALL-IN FIGHTS ON (Game_AI.md § 6n): a committed army falls
            // back only when badly outmatched. Raids keep their own nerve.
            float retreatRatio = mission.Type == MissionType.Raid
                ? skill.retreatRatio : AllInRetreatRatio(faction, skill.retreatRatio);
            if (retreatRatio <= 0f) return false;
            if (mission.Members.Count < tc.fallbackMinArmy) return false;
            if (enemyPower <= 0 || enemyPower <= myPower * retreatRatio) return false;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            bool hasHall = hall != Entity.Null && em.HasComponent<LocalTransform>(hall);
            float3 hallPos = hasHall ? em.GetComponentData<LocalTransform>(hall).Position : centroid;
            if (hasHall && math.distance(centroid.xz, hallPos.xz) <= tc.fallbackSafeHomeRadius) return false;

            // THE NEAREST SAFE GROUND, NOT HOME (2026-10-07, Game_AI.md § 6h).
            // An own Fortress, tower or held territory no deeper toward the
            // enemy and not itself under threat; with none in reach, a staging
            // point straight away from the enemy. The army regroups there and
            // the reinforcement stream gathers on it (TickFallback).
            if (!TryFindSafeGround(em, faction, centroid, enemyCentroid, myPower, allowDeeper: false,
                    out float3 dest, out string where))
            {
                float2 away = math.normalizesafe(centroid.xz - enemyCentroid.xz);
                if (math.lengthsq(away) < 1e-4f)
                    away = hasHall ? math.normalizesafe(hallPos.xz - centroid.xz) : new float2(1f, 0f);
                float2 p = centroid.xz + away * tc.fallbackDistance;
                dest = new float3(p.x, centroid.y, p.y);
                where = "to a staging point away from the enemy";
            }

            mission.FallingBack = true;
            mission.FallbackPos = dest;
            mission.FallbackStart = now;
            mission.FallbackHolds = 0;
            mission.Deadline = math.max(mission.Deadline, now + tc.fallbackTimeout * (tc.fallbackMaxHolds + 1));
            mission.Engaged = false;
            mission.Focus = Entity.Null;
            EndFlank(mission);
            March(em, faction, mission, dest, false, centroid);
            AILogger.Log(faction, "TACTICS",
                $"retreat (power {myPower} vs {enemyPower}, ratio {enemyPower / (float)math.max(1, myPower):F2} > {retreatRatio:F2}) " +
                $"— {mission.Members.Count} fall back {where} at ({dest.x:F0},{dest.z:F0})");
            return true;
        }

        /// <summary>
        /// While falling back (Game_AI.md § 6h): after fallbackHoldSeconds,
        /// re-engage when the odds have dropped to reengageRatio or the enemy
        /// did not follow. Still outmatched where it stands: move on to the
        /// next safe ground. At the staging ground: take a softer objective
        /// near it if one is known (an undefended enemy economic building),
        /// else hold there while reinforcements gather, fallbackMaxHolds
        /// times; then give the objective up at the nearest own safe ground.
        /// Never a march to the capital for its own sake.
        /// </summary>
        private void TickFallback(EntityManager em, Faction faction, Mission mission,
            float3 centroid, float now, in AITacticsSkill skill)
        {
            var tc = AITactics.Cfg;
            float elapsed = now - mission.FallbackStart;
            if (elapsed < tc.fallbackHoldSeconds) return;

            AITactics.LivePowerAround(em, faction, centroid, tc.powerRadius, _tacticsScratch,
                out int enemyPower, out int ownStatic, out float3 enemyCentroid, out _);
            int myPower = ownStatic;
            for (int i = 0; i < mission.Members.Count; i++)
                myPower += AITactics.LiveStrength(em, mission.Members[i]);
            if (enemyPower > 0) AITactics.ReportFightSite(em, faction, centroid, myPower, enemyPower);

            bool arrived = math.distance(centroid.xz, mission.FallbackPos.xz) <= tc.fallbackArriveRadius;
            bool odds = enemyPower <= myPower * math.max(0f, skill.reengageRatio);
            bool timedOut = elapsed > tc.fallbackTimeout;

            if ((enemyPower == 0 && (arrived || timedOut) && mission.FallbackHolds == 0)
                || (enemyPower > 0 && odds))
            {
                mission.FallingBack = false;
                mission.FallbackHolds = 0;
                March(em, faction, mission, mission.TargetPos, true, centroid);
                AILogger.Log(faction, "TACTICS",
                    $"re-engage (power {myPower} vs {enemyPower}) — {mission.Members.Count} march on " +
                    $"({mission.TargetPos.x:F0},{mission.TargetPos.z:F0})");
                return;
            }

            // Followed and still losing: the next safe ground.
            float retreatRatio = mission.Type == MissionType.Raid
                ? skill.retreatRatio : AllInRetreatRatio(faction, skill.retreatRatio);
            if (enemyPower > 0 && retreatRatio > 0f && enemyPower > myPower * retreatRatio
                && TryFindSafeGround(em, faction, centroid, enemyCentroid, myPower, allowDeeper: false,
                       out float3 safer, out string saferWhat)
                && math.distance(safer.xz, mission.FallbackPos.xz) > tc.fallbackArriveRadius)
            {
                mission.FallbackPos = safer;
                mission.FallbackStart = now;
                March(em, faction, mission, safer, false, centroid);
                AILogger.Log(faction, "TACTICS",
                    $"still outmatched (power {myPower} vs {enemyPower}) — {mission.Members.Count} fall back " +
                    $"{saferWhat} at ({safer.x:F0},{safer.z:F0})");
                return;
            }

            if (!arrived && !timedOut) return;

            // A softer objective near the staging ground.
            if (TryFindSoftTarget(em, faction, centroid, myPower, now,
                    out float3 softPos, out Entity softEnt, out string softWhat))
            {
                mission.FallingBack = false;
                mission.FallbackHolds = 0;
                mission.Target = softEnt;
                mission.TargetPos = softPos;
                mission.Deadline = now + math.max(1f, Cfg.missionTimeoutSeconds);
                March(em, faction, mission, softPos, true, centroid);
                AILogger.Log(faction, "TACTICS",
                    $"retarget (power {myPower} vs {enemyPower}) — {mission.Members.Count} " +
                    $"strike {softWhat} at ({softPos.x:F0},{softPos.z:F0})");
                return;
            }

            if (!timedOut) return;

            // Hold the staging ground while reinforcements gather.
            if (++mission.FallbackHolds < math.max(1, tc.fallbackMaxHolds))
            {
                mission.FallbackStart = now;
                mission.Deadline = math.max(mission.Deadline, now + tc.fallbackTimeout + 5f);
                AILogger.Log(faction, "TACTICS",
                    $"holding the staging ground at ({mission.FallbackPos.x:F0},{mission.FallbackPos.z:F0}) — " +
                    $"power {myPower} vs {enemyPower}, gathering reinforcements " +
                    $"({mission.FallbackHolds}/{tc.fallbackMaxHolds})");
                return;
            }

            // Give the objective up at the nearest own safe ground; the
            // mission ends there (UpdateMissions sees the deadline).
            NoteWarFailure(faction, faction);
            mission.FallingBack = false;
            mission.FallbackHolds = 0;
            mission.Deadline = now;
            float3 refuge = RefugeFor(em, faction, centroid, myPower, out string refugeWhat);
            mission.FallbackPos = refuge;
            CommandRouter.IssueFormationMove(em, mission.Members, refuge, FormationShape.Box, CommandSource.AI);
            AILogger.Log(faction, "TACTICS",
                $"objective given up (power {myPower} vs {enemyPower}) — {mission.Members.Count} hold " +
                $"{refugeWhat} at ({refuge.x:F0},{refuge.z:F0})");
        }

        // ─────────────────────────────────────────────────────────────────
        // SAFE GROUND, REFUGE, SOFT TARGETS (2026-10-07, Game_AI.md § 6h)
        // ─────────────────────────────────────────────────────────────────

        static readonly ComponentType[] QT_OwnAnchorBuildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_OwnAnchorBuildings;

        /// <summary>
        /// The nearest safe spot within fallbackStageSearch of
        /// <paramref name="from"/>: an own capital, Fortress or Watch Tower,
        /// or the centre of a territory this faction holds — with hostile
        /// strength round it at most fallbackSafeShare of <paramref name="myPower"/>,
        /// and (unless <paramref name="allowDeeper"/>) no nearer the enemy than
        /// the army is now.
        /// </summary>
        private bool TryFindSafeGround(EntityManager em, Faction faction, float3 from, float3 enemyCentroid,
            int myPower, bool allowDeeper, out float3 dest, out string what)
        {
            var tc = AITactics.Cfg;
            float maxD2 = tc.fallbackStageSearch * tc.fallbackStageSearch;
            float fromEnemy = math.distance(from.xz, enemyCentroid.xz);
            float safeCap = math.max(1f, myPower * tc.fallbackSafeShare);
            float bestD2 = float.MaxValue;
            float3 best = default;
            string bestWhat = null;

            void Consider(float3 p, string kind)
            {
                float dx = p.x - from.x, dz = p.z - from.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > maxD2 || d2 >= bestD2) return;
                if (!allowDeeper && math.distance(p.xz, enemyCentroid.xz) < fromEnemy) return;
                int hostile = TacticalQuery.EnemyStrengthInRadius(em, faction, p, tc.fallbackSafeRadius)
                              + AIEngagement.StaticDefencePower(em, faction, p, tc.fallbackSafeRadius);
                if (hostile > safeCap) return;
                bestD2 = d2; best = p; bestWhat = kind;
            }

            var q = QC_OwnAnchorBuildings.Get(em, QT_OwnAnchorBuildings);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var e = ents[i];
                    if (em.HasComponent<HallTag>(e)) Consider(xfs[i].Position, "to the capital");
                    else if (em.HasComponent<FortressTag>(e)) Consider(xfs[i].Position, "to a Fortress");
                    else if (em.HasComponent<WatchTowerTag>(e)) Consider(xfs[i].Position, "to a tower");
                }
            }
            if (TheWaningBorder.World.Regions.RegionMap.Ready && TheWaningBorder.World.Regions.TerritoryOwnership.Ready)
            {
                for (int r = 0; r < TheWaningBorder.World.Regions.RegionMap.Count; r++)
                {
                    if (TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(r) != (int)faction) continue;
                    var sd = TheWaningBorder.World.Regions.RegionMap.SeedOf(r);
                    var p = new float3(sd.x, 0f, sd.y);
                    p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                    Consider(p, "into own territory");
                }
            }
            dest = best; what = bestWhat;
            return bestWhat != null;
        }

        /// <summary>Where an army that has given its objective up (or finished
        /// it with nothing left to press on to) goes: the nearest safe own
        /// ground, deeper or not; the capital only when nothing else serves.</summary>
        private float3 RefugeFor(EntityManager em, Faction faction, float3 from, int myPower, out string what)
        {
            if (TryFindSafeGround(em, faction, from, from, math.max(1, myPower), allowDeeper: true,
                    out float3 dest, out what))
                return dest;
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall != Entity.Null && em.HasComponent<LocalTransform>(hall))
            {
                what = "at the capital";
                return em.GetComponentData<LocalTransform>(hall).Position;
            }
            what = "where it stands";
            return from;
        }

        /// <summary>
        /// The nearest known hostile economic building within
        /// fallbackRetargetRadius whose surroundings hold at most
        /// fallbackRetargetShare of <paramref name="myPower"/> — outside its
        /// owner's capital territory first (§ 6h: surrounding holdings before
        /// the main base). Sightings only; never the curse.
        /// </summary>
        private bool TryFindSoftTarget(EntityManager em, Faction faction, float3 from, int myPower, float now,
            out float3 pos, out Entity ent, out string what)
        {
            pos = default; ent = Entity.Null; what = null;
            var tc = AITactics.Cfg;
            Entity brain = FindBrainEntity(em, faction);
            if (brain == Entity.Null || !em.HasBuffer<EnemySightingRecord>(brain)) return false;
            var buf = em.GetBuffer<EnemySightingRecord>(brain);
            float maxD2 = tc.fallbackRetargetRadius * tc.fallbackRetargetRadius;
            float cap = math.max(1f, myPower * tc.fallbackRetargetShare);
            float best = float.MaxValue;
            for (int i = 0; i < buf.Length; i++)
            {
                var sg = buf[i];
                if (sg.OwnerFaction == Faction.Border || !Alliances.AreHostile(faction, sg.OwnerFaction)) continue;
                if (sg.Category != IntelCategory.EcoBuilding) continue;
                if (sg.Enemy == Entity.Null || !em.Exists(sg.Enemy)) continue;
                if (!WarAllows(em, faction, sg.OwnerFaction)) continue;   // § 6i: the war's victim only
                float dx = sg.Position.x - from.x, dz = sg.Position.z - from.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > maxD2) continue;
                int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(sg.Position.x, sg.Position.z);
                if (IsWalledGround(em, sg.OwnerFaction, r)) continue;
                if (WaveTargetBlocked(faction, sg.Position, now)) continue;
                // Surrounding holdings before the main base: a building in its
                // owner's capital territory ranks after every other.
                float score = math.sqrt(d2) + (IsCapitalTerritory(em, sg.OwnerFaction, r) ? 10000f : 0f);
                if (score >= best) continue;
                int hostile = TacticalQuery.EnemyStrengthInRadius(em, faction, sg.Position, tc.fallbackSafeRadius)
                              + AIEngagement.StaticDefencePower(em, faction, sg.Position, tc.fallbackSafeRadius);
                if (hostile > cap) continue;
                best = score; pos = sg.Position; ent = sg.Enemy;
                what = $"{sg.OwnerFaction}'s economic building";
            }
            return ent != Entity.Null;
        }
    }
}
