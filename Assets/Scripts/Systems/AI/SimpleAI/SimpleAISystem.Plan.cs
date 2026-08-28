// SimpleAISystem.Plan.cs
// Choosing and committing to a strategic plan, and reading the other players
// to pick it. Partial of SimpleAISystem.cs.
//
// See AIPlan.cs for what a plan IS and why the game needed one.
//
// ─────────────────────────────────────────────────────────────────────────
// COUNTER-PLAY
//
// A plan chosen only from my own state is still four AIs playing solitaire at
// the same table. The board is what makes a match: somebody booms, somebody
// sees it and punishes it, somebody else turtles against the one massing.
//
// Each candidate plan is SCORED against the board, and the best score wins —
// rather than a ladder of ifs, because a ladder's first branch always wins and
// the rest become decoration. Personality biases the score, so two AIs reading
// the same board still answer it differently.
//
// The read uses public facts: how much ground each player holds, and how big
// their army is. Territory is on the map for anyone to see. Army size is the
// generous half — a human would need to scout for it. That is a deliberate
// difficulty affordance, not an oversight; it is the same class of assist as
// the AI knowing where the resource nodes are.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data.AI;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        private sealed class PlanState
        {
            public AIPlan Plan;
            public float Since;
            public bool Started;
        }

        private readonly Dictionary<int, PlanState> _plans = new Dictionary<int, PlanState>();

        /// <summary>This faction's plan. Boom until the first evaluation.</summary>
        private AIPlan PlanOf(Faction f)
            => _plans.TryGetValue((int)f, out var p) ? p.Plan : AIPlan.Boom;

        private AIPlanProfile PlanProfileOf(Faction f) => AIPlans.Of(PlanOf(f));

        /// <summary>
        /// Re-read the board and commit to a plan. Cheap: runs on the think
        /// tick, but only re-decides once the current plan's commit window has
        /// elapsed.
        /// </summary>
        private void TickStrategicPlan(EntityManager em, Faction faction,
            ref SimpleAIState aiState, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, bool ageUpOverdue, float now)
        {
            int key = (int)faction;
            if (!_plans.TryGetValue(key, out var st))
                _plans[key] = st = new PlanState { Plan = AIPlan.Boom, Since = now };

            // COMMITMENT. A plan is held for its whole window even if the board
            // shifts — that is what makes it a plan and not a reflex, and it is
            // what gives a watcher long enough to notice what this AI is doing.
            // The one exception is being attacked at home: no strategy survives
            // the enemy standing in your base.
            bool emergency = aiState.Posture == AIPosture.Defend;
            if (st.Started && !emergency
                && now - st.Since < AIPlans.Of(st.Plan).CommitSeconds) return;

            var board = ReadBoard(em, faction);
            var pick = ScorePlans(board, personality, ageUpOverdue, emergency,
                out string reason);

            if (st.Started && pick == st.Plan)
            {
                // Same answer: restart the clock rather than re-deciding every
                // tick from here on.
                st.Since = now;
                return;
            }

            st.Plan = pick;
            st.Since = now;
            st.Started = true;

            var prof = AIPlans.Of(pick);
            AILogger.Log(faction, "PLAN", $"{prof.Banner} — {reason}");
            TWBLog.Log($"[AI {faction}] PLAN: {prof.Banner} ({reason})");
        }

        /// <summary>What every player currently has. The AI's read of the game.</summary>
        private struct BoardRead
        {
            public int MyTerritories, MyArmy;
            /// <summary>Biggest single rival, by each measure. They need not be
            /// the same player — the AI answers the worst case on each axis,
            /// which is what a human does when they say "someone is booming
            /// and someone else has a deathball".</summary>
            public int TopEnemyTerritories, TopEnemyArmy;
            /// <summary>The land leader's army, so "booming" can be told from
            /// "winning": lots of ground AND a small army is a bet worth
            /// punishing; lots of ground and a big army is just ahead.</summary>
            public int LandLeaderArmy;
        }

        private BoardRead ReadBoard(EntityManager em, Faction me)
        {
            var read = new BoardRead();

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var army = new int[8];
            for (int i = 0; i < facs.Length; i++)
            {
                if (!IsCombatClass(tags[i].Class)) continue;
                int f = (int)facs[i].Value;
                if (f >= 0 && f < 8) army[f]++;
            }

            read.MyArmy = army[(int)me];
            read.MyTerritories = TerritoryOwnership.Ready
                ? TerritoryOwnership.CountOf(me) : 0;

            int landLeader = -1, landLeaderCount = -1;
            for (int f = 0; f < 8; f++)
            {
                var other = (Faction)f;
                if (other == me || !Alliances.AreHostile(me, other)) continue;

                int terr = TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(other) : 0;
                // A faction with no army and no ground is dead or absent, not a
                // rival — counting it would make everyone look safe.
                if (terr == 0 && army[f] == 0) continue;

                if (army[f] > read.TopEnemyArmy) read.TopEnemyArmy = army[f];
                if (terr > read.TopEnemyTerritories) read.TopEnemyTerritories = terr;
                if (terr > landLeaderCount) { landLeaderCount = terr; landLeader = f; }
            }
            read.LandLeaderArmy = landLeader >= 0 ? army[landLeader] : 0;
            return read;
        }

        /// <summary>Score every plan against the board; highest wins.</summary>
        private static AIPlan ScorePlans(in BoardRead b,
            AISettingsSO.PersonalityBlock personality, bool ageUpOverdue,
            bool emergency, out string reason)
        {
            // ── SOME THINGS ARE NOT PREFERENCES. ──
            // An enemy standing in your base has exactly one answer, and no
            // personality gets a vote on it. Scored, a Rush AI's affinity beat
            // the emergency bonus and it marched off to raid while its Hall
            // came down.
            if (emergency)
            {
                reason = "under attack at home";
                return AIPlan.Fortress;
            }

            var scores = new float[5];
            var why = new string[5];

            // ── Baseline. Something always has to be worth doing. ──
            scores[(int)AIPlan.Boom] = 12f; why[(int)AIPlan.Boom] = "board is quiet";
            scores[(int)AIPlan.Mass] = 15f; why[(int)AIPlan.Mass] = "army is the best use of income";
            scores[(int)AIPlan.Rush] = 10f; why[(int)AIPlan.Rush] = "pressure is cheap";
            scores[(int)AIPlan.Tech] = 14f; why[(int)AIPlan.Tech] = "advancement is open";
            scores[(int)AIPlan.Fortress] = 5f; why[(int)AIPlan.Fortress] = "holding is safe";

            // ── PUNISH THE GREEDY. Someone holds more ground than me and has
            //    not paid for it in troops: that is the moment to hit them. ──
            int landLead = b.TopEnemyTerritories - b.MyTerritories;
            if (landLead >= 1 && b.LandLeaderArmy <= b.MyArmy)
            {
                scores[(int)AIPlan.Rush] += 25f + landLead * 12f;
                why[(int)AIPlan.Rush] = $"land leader is {landLead} region(s) up "
                                      + $"with only {b.LandLeaderArmy} troops — punish it";
                scores[(int)AIPlan.Boom] -= 15f;
            }

            // ── ANSWER A DEATHBALL. Someone's army clearly outweighs mine. ──
            if (b.TopEnemyArmy >= b.MyArmy * 2 && b.TopEnemyArmy >= 6)
            {
                // Big enough that even a Rush personality does not throw its
                // army into it — at the first weights tried, one did.
                scores[(int)AIPlan.Fortress] += 55f;
                why[(int)AIPlan.Fortress] = $"enemy fields {b.TopEnemyArmy} vs my {b.MyArmy}";
                scores[(int)AIPlan.Boom] -= 30f;
                scores[(int)AIPlan.Rush] -= 35f;
            }
            else if (b.TopEnemyArmy > b.MyArmy && b.TopEnemyArmy >= 4)
            {
                // Behind but not overwhelmed — match them rather than hide.
                scores[(int)AIPlan.Mass] += 30f;
                why[(int)AIPlan.Mass] = $"behind on army ({b.MyArmy} vs {b.TopEnemyArmy}) — match it";
            }

            // ── PRESS AN ADVANTAGE. A lead you never spend is not a lead. ──
            if (b.MyArmy >= b.TopEnemyArmy + 4 && b.MyArmy >= 6)
            {
                scores[(int)AIPlan.Mass] += 35f;
                why[(int)AIPlan.Mass] = $"army lead {b.MyArmy} vs {b.TopEnemyArmy} — build and commit";
                scores[(int)AIPlan.Fortress] -= 25f;
            }

            // ── TAKE THE MAP WHEN NOBODY IS CONTESTING IT. ──
            if (b.TopEnemyArmy <= 3 && b.MyArmy >= 2)
            {
                scores[(int)AIPlan.Boom] += 16f;
                why[(int)AIPlan.Boom] = "nobody fields an army — take the map";
            }

            // ── ADVANCE. Overdue age-up is worth a window, and ONLY a window:
            //    this used to be a permanent 65% of income to a wallet that
            //    never lends, which is what starved every army in the match. ──
            if (ageUpOverdue)
            {
                scores[(int)AIPlan.Tech] += 34f;
                why[(int)AIPlan.Tech] = "age-up overdue — take the window";
                if (b.TopEnemyArmy > b.MyArmy) scores[(int)AIPlan.Tech] -= 20f;
            }

            for (int i = 0; i < scores.Length; i++)
                scores[i] += AIPlans.Affinity(personality.personality, (AIPlan)i);

            int best = 0;
            for (int i = 1; i < scores.Length; i++)
                if (scores[i] > scores[best]) best = i;

            reason = why[best];
            return (AIPlan)best;
        }
    }
}
