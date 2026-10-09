// SimpleAISystem.Shardroot.cs
// THE AI GOES FOR THE SHARDROOT (2026-10-07, docs/Design/Game_AI.md § 6l).
// Developer: "No one goes for the shardroot and no one uses king lexor's
// heightened form." Nothing in the AI ever looked at the artifact: units
// picked it up only when a wave happened to fight on top of it, then the
// wave code marched the carrier on with the army until it died. The
// Shardbound King (Lexor bearing the artifact) is the only heightened form
// he has, so both halves of that report were this one gap.
//
// Per faction, every AIShardroot.thinkInterval:
//   * OUR CARRIER — pull it out of every mission and bring it home by the
//     road this personality takes (Curse_And_Shardroot.md § 3.1b):
//       - THE KING (PersonalityBlock.shardrootToKing: Rush, Aggressive,
//         Balanced) — the nearest own Hall, which hands the artifact to the
//         living king (ShardrootSystem.AwakenHero): the Shardbound King;
//       - THE TEMPLE (Turtle, Defensive, Economic, TechBoom) — the nearest
//         finished Temple of Ridan, which enshrines it and empowers the army.
//     A road that is closed (no living king / no finished Temple) gives way
//     to the other; with neither, the Hall (the placeholder champion). The
//     choice is written on the carrier (ShardrootBearer.Intent) so the other
//     building's delivery does not intercept it on the way.
//   * ON THE GROUND, or HELD BY AN ENEMY / THE CURSE — carrier, Shardbound
//     King or enshrining Temple (everyone targets the bearer) — where we have
//     seen it —
//     a strike party of free combat units (the RP hunt's eligibility rules),
//     weighed against what stands round it (AIEngagement.AssessAssault) and
//     sent only with launchMargin to spare. The king goes with it and is
//     walked onto the pickup himself: a hero ordered onto it has right of way
//     over his own escort (ShardrootCarrySystem), so he carries it straight
//     away and no Hall trip is needed.
//   * Otherwise an idle king rides with the army's biggest mission, so his
//     fight abilities (Liquid Courage, Honour thy Pledge, Shardbound Fury)
//     find fights instead of waiting at home "not in combat".

using System.Collections.Generic;
using Unity.Collections;
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
        private sealed class ShardrootStrike
        {
            public float NextThink;
            public float LaunchedAt = -1f;
            public float3 Target;
            public float NextLog;
            public bool KingRetreat;
            public readonly List<Entity> Roster = new List<Entity>();
        }

        private readonly Dictionary<int, ShardrootStrike> _shardroot = new Dictionary<int, ShardrootStrike>();
        private readonly List<Entity> _shardrootFree = new List<Entity>(32);

        private ShardrootStrike ShardrootFor(Faction f)
        {
            int key = (int)f;
            if (!_shardroot.TryGetValue(key, out var s)) { s = new ShardrootStrike(); _shardroot[key] = s; }
            return s;
        }

        private void TickShardroot(EntityManager em, Faction faction, ref SimpleAIState aiState, float now)
        {
            var cfg = AIShardroot.Cfg;
            if (!cfg.enabled) return;
            var s = ShardrootFor(faction);
            // The king's retreat is judged every think, not on the slower
            // Shardroot cadence: he can lose a lot in five seconds.
            Entity livingKing = AIShardroot.LivingKing(em, faction);
            TickKingRetreat(em, faction, livingKing, s);
            if (now < s.NextThink) return;
            s.NextThink = now + math.max(1f, cfg.thinkInterval);

            // A retreating king is nobody's fighter: he neither joins the
            // army nor leads a strike until he has healed.
            Entity king = s.KingRetreat ? Entity.Null : livingKing;
            var where = AIShardroot.Locate(em, faction, out Entity holder, out float3 pos);

            // ── Ours: bring it home. ──
            if (where == AIShardroot.Where.Mine)
            {
                s.Roster.Clear(); s.LaunchedAt = -1f;
                if (em.HasComponent<ShardboundHeroTag>(holder)) { KingJoinsArmy(em, faction, king, now); return; }
                ReleaseFromMissions(faction, holder);
                BringShardrootHome(em, faction, livingKing, holder, pos);
                return;
            }

            bool wanted = (where == AIShardroot.Where.Ground || where == AIShardroot.Where.Hostile)
                && now >= cfg.earliestSeconds
                && AICommon.IsKnownGround(faction, pos);
            if (!wanted)
            {
                s.Roster.Clear(); s.LaunchedAt = -1f;
                KingJoinsArmy(em, faction, king, now);
                return;
            }

            // Free combat units, as for the first-RP hunt.
            _shardrootFree.Clear();
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using (var ents = mq.ToEntityArray(Allocator.Temp))
            using (var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    if (!IsCombatClass(tags[i].Class)) continue;
                    Entity e = ents[i];
                    if (e == king) continue;
                    if (IsVerbUnit(em, e) || IsClaimSquadMember(e)) continue;
                    if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                    if (TransientState.Active<MoveCommand>(em, e)) continue;
                    if (TransientState.Active<AttackCommand>(em, e)) continue;
                    if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                    if (s.Roster.Contains(e)) continue;
                    _shardrootFree.Add(e);
                }

            // A strike on the way: reinforce while it is fresh and still winning.
            bool fresh = s.LaunchedAt >= 0f && now - s.LaunchedAt < cfg.reinforceSeconds
                && math.distance(s.Target.xz, pos.xz) < cfg.assessRadius * 2f;
            if (fresh)
            {
                PruneDeadUnits(em, s.Roster);
                if (_shardrootFree.Count >= math.max(1, cfg.minPartySize / 2))
                {
                    AICommon.IssueGroupOrder(em, _shardrootFree, pos, attackMove: true, Cfg.waveArrivedRadius);
                    s.Roster.AddRange(_shardrootFree);
                }
                s.Target = pos;
                SendKingOnto(em, king, pos, where);
                return;
            }

            if (_shardrootFree.Count < math.max(1, cfg.minPartySize))
            {
                LogShardroot(faction, s, now, $"Shardroot seen at ({pos.x:0},{pos.z:0}) — {_shardrootFree.Count} free units, too few to go");
                KingJoinsArmy(em, faction, king, now);
                return;
            }
            var a = AIEngagement.AssessAssault(em, faction, _shardrootFree, pos, cfg.assessRadius);
            if (a.EnemyPower > 0 && a.MyPower < a.EnemyPower * math.max(1f, cfg.launchMargin))
            {
                int need = (int)math.ceil(_shardrootFree.Count * cfg.launchMargin * a.EnemyPower / math.max(1f, a.MyPower));
                int want = math.min(CountAliveMilitary(em, faction) + math.max(1, need - _shardrootFree.Count),
                    math.max(1, ProfileOf(faction).SustainArmyCap));
                if (aiState.DesiredMilitary < want) aiState.DesiredMilitary = want;
                LogShardroot(faction, s, now,
                    $"saving an army for the Shardroot at ({pos.x:0},{pos.z:0}): power {a.MyPower} vs {a.EnemyPower}");
                KingJoinsArmy(em, faction, king, now);
                return;
            }

            AICommon.IssueGroupOrder(em, _shardrootFree, pos, attackMove: true, Cfg.waveArrivedRadius);
            s.Roster.Clear(); s.Roster.AddRange(_shardrootFree);
            s.LaunchedAt = now; s.Target = pos;
            SendKingOnto(em, king, pos, where);
            AILogger.Log(faction, "SHARDROOT",
                $"{_shardrootFree.Count} units{(king != Entity.Null && AIShardroot.Cfg.kingLeadsStrike ? " and the king" : "")} " +
                $"strike for the Shardroot ({(where == AIShardroot.Where.Ground ? "on the ground" : em.HasComponent<TempleOfRidanTag>(holder) ? "enshrined in an enemy Temple" : "held by an enemy")}) at " +
                $"({pos.x:0},{pos.z:0}) — power {a.MyPower} vs {a.EnemyPower}");
        }

        /// <summary>Our carrier goes home by the personality's road (§ 3.1b):
        /// King (Hall) or Temple, the other when the preferred one is closed,
        /// the Hall when neither is open. The road is stamped on the carrier
        /// so only that building's delivery takes it.</summary>
        private void BringShardrootHome(EntityManager em, Faction faction, Entity king,
            Entity holder, float3 pos)
        {
            bool hasHall = AIShardroot.TryNearestHall(em, faction, pos, out float3 hall);
            bool hasTemple = AIShardroot.TryNearestTemple(em, faction, pos, out Entity temple, out float3 templePos);
            bool kingRoad = hasHall && king != Entity.Null;
            bool preferKing = PersonalityOf(faction).shardrootToKing;

            byte intent;
            if (preferKing && kingRoad) intent = ShardrootIntent.Hall;
            else if (!preferKing && hasTemple) intent = ShardrootIntent.Temple;
            else if (kingRoad) intent = ShardrootIntent.Hall;
            else if (hasTemple) intent = ShardrootIntent.Temple;
            else if (hasHall) intent = ShardrootIntent.Hall;   // the placeholder champion
            else return;

            if (em.HasComponent<ShardrootBearer>(holder))
            {
                var b = em.GetComponentData<ShardrootBearer>(holder);
                if (b.Intent != intent)
                {
                    b.Intent = intent;
                    em.SetComponentData(holder, b);
                    AILogger.Log(faction, "SHARDROOT", intent == ShardrootIntent.Temple
                        ? "the Shardroot goes to the Temple — to be enshrined"
                        : king != Entity.Null
                            ? "the Shardroot goes to the Hall — for the king"
                            : "the Shardroot goes to the Hall — no king lives");
                }
            }
            if (TransientState.Active<UserMoveOrder>(em, holder)) return;

            if (intent == ShardrootIntent.Temple)
            {
                // Measured to the wall, as the enshrine is (ShardrootCarrySystem).
                if (TargetGeometry.SurfaceDistXZ(em, pos, templePos, temple)
                    <= TheWaningBorder.Core.Config.BorderConstants.ShardrootDepositRadius * 0.5f) return;
                CommandRouter.IssueMove(em, holder, templePos, CommandSource.AI);
                AILogger.Log(faction, "SHARDROOT",
                    $"we carry the Shardroot — bringing it to the Temple at ({templePos.x:0},{templePos.z:0})");
                return;
            }
            if (math.distance(hall.xz, pos.xz) <= ShardrootState.HallDeliverRadius * 0.5f) return;
            CommandRouter.IssueMove(em, holder, hall, CommandSource.AI);
            AILogger.Log(faction, "SHARDROOT",
                $"we carry the Shardroot — bringing it to the Hall at ({hall.x:0},{hall.z:0})");
        }

        /// <summary>The king walks ONTO a pickup (plain move: his
        /// DesiredDestination inside the pickup radius gives him right of way
        /// over his escort), or attack-moves on an enemy carrier.</summary>
        private static void SendKingOnto(EntityManager em, Entity king, float3 pos, AIShardroot.Where where)
        {
            if (king == Entity.Null || !AIShardroot.Cfg.kingLeadsStrike) return;
            if (where == AIShardroot.Where.Ground) CommandRouter.IssueMove(em, king, pos, CommandSource.AI);
            else CommandRouter.IssueAttackMove(em, king, pos, CommandSource.AI);
        }

        /// <summary>An idle king rides with the biggest mission in the field.</summary>
        private void KingJoinsArmy(EntityManager em, Faction faction, Entity king, float now)
        {
            if (king == Entity.Null || !AIShardroot.Cfg.kingJoinsArmy) return;
            if (TransientState.Active<AttackMoveTag>(em, king)) return;
            if (TransientState.Active<MoveCommand>(em, king)) return;
            if (TransientState.Active<AttackCommand>(em, king)) return;
            if (TransientState.Active<UserMoveOrder>(em, king)) return;
            Mission best = null;
            foreach (var m in MissionsFor(faction))
                if (m.Members.Count >= AIShardroot.Cfg.kingJoinMinArmy && (best == null || m.Members.Count > best.Members.Count))
                    best = m;
            if (best == null) return;
            if (!em.HasComponent<LocalTransform>(king)) return;
            float3 kp = em.GetComponentData<LocalTransform>(king).Position;
            if (math.distance(kp.xz, best.LastCentroid.xz) < 12f) return;
            CommandRouter.IssueAttackMove(em, king, best.LastCentroid, CommandSource.AI);
            AILogger.Log(faction, "SHARDROOT",
                $"King Lexor rides out to the army ({best.Members.Count} strong) at ({best.LastCentroid.x:0},{best.LastCentroid.z:0})");
        }

        /// <summary>
        /// THE KING RETREATS (2026-10-09, Game_AI.md § 6l): at
        /// kingRetreatHpFraction of his health King Lexor leaves every
        /// mission and walks to the nearest Hall, re-ordered each think if
        /// anything turned him round, until he has healed to
        /// kingRecoveredHpFraction. Losing him loses the Shardroot he bears.
        /// </summary>
        private void TickKingRetreat(EntityManager em, Faction faction, Entity king, ShardrootStrike s)
        {
            var cfg = AIShardroot.Cfg;
            if (king == Entity.Null || cfg.kingRetreatHpFraction <= 0f
                || !em.HasComponent<Health>(king) || !em.HasComponent<LocalTransform>(king))
            { s.KingRetreat = false; return; }
            var hp = em.GetComponentData<Health>(king);
            float frac = hp.Max > 0 ? hp.Value / (float)hp.Max : 1f;
            bool bound = em.HasComponent<ShardboundKing>(king);

            if (!s.KingRetreat)
            {
                if (frac > cfg.kingRetreatHpFraction) return;
                s.KingRetreat = true;
                AILogger.Log(faction, "SHARDROOT",
                    $"King Lexor{(bound ? " (Shardbound)" : "")} at {frac * 100f:F0}% health -- pulled back to heal");
            }
            else if (frac >= cfg.kingRecoveredHpFraction)
            {
                s.KingRetreat = false;
                AILogger.Log(faction, "SHARDROOT",
                    $"King Lexor healed to {frac * 100f:F0}% -- back to the fight");
                return;
            }

            if (TransientState.Active<UserMoveOrder>(em, king)) return;
            ReleaseFromMissions(faction, king);
            float3 kp = em.GetComponentData<LocalTransform>(king).Position;
            if (!AIShardroot.TryNearestHall(em, faction, kp, out float3 hall)) return;
            if (math.distance(kp.xz, hall.xz) <= ShardrootState.HallDeliverRadius) return;
            if (TransientState.Active<MoveCommand>(em, king)
                && math.distance(em.GetComponentData<MoveCommand>(king).Destination.xz, hall.xz) < 1f) return;
            CommandRouter.IssueMove(em, king, hall, CommandSource.AI);
        }

        /// <summary>Our carrier stops being a soldier: no mission re-orders it.</summary>
        private void ReleaseFromMissions(Faction faction, Entity unit)
        {
            foreach (var m in MissionsFor(faction))
            {
                m.Members.Remove(unit);
                m.Flankers.Remove(unit);
            }
        }

        private void LogShardroot(Faction faction, ShardrootStrike s, float now, string msg)
        {
            if (now < s.NextLog) return;
            s.NextLog = now + 60f;
            AILogger.Log(faction, "SHARDROOT", msg);
        }

    }
}
