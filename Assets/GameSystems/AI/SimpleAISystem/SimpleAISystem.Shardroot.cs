// SimpleAISystem.Shardroot.cs
// THE AI GOES FOR THE SHARDROOT (2026-10-07, docs/Design/Game_AI.md § 6l).
// Developer: "No one goes for the shardroot and no one uses king lexor's
// heightened form." Nothing in the AI ever looked at the artifact: units
// picked it up only when a wave happened to fight on top of it, then the
// wave code marched the carrier on with the army until it died. The
// Shardbound King (Lexor bearing the artifact) is the only heightened form
// he has, so both halves of that report were this one gap.
//
// Per faction, every AIShardroot.thinkInterval (docs/Design/Heroes.md §4):
//   * OURS: the bearer is an Ascended hero. PersonalityBlock.shardrootToKing keeps
//     it fighting at the head of the army; the others save the Temple's price and
//     order it to stow the artifact the moment they can pay. Meanwhile it fights.
//   * ON THE GROUND, or HELD BY AN ENEMY / THE CURSE, where we have seen it: a
//     strike party weighed against what stands round it, with our best hero. Only a
//     hero can take it, so the hero is walked onto the pickup.
//   * Otherwise an idle hero rides with the army's biggest mission.

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

        /// <summary>Adds the soldiers nearest the spot to the strike until it has
        /// <paramref name="want"/>, taking them off whatever they were doing.</summary>
        private void DraftForTheShardroot(EntityManager em, Faction faction, Entity king, float3 pos, int want, ShardrootStrike s)
        {
            var roster = OwnCombatRoster(em, faction);
            var busy = new List<(float Dist, int Order, Entity Unit)>();
            for (int i = 0; i < roster.Count; i++)
            {
                Entity e = roster.E[i];
                if (e == king || roster.Verb(i) || IsClaimSquadMember(e)) continue;
                if (s.Roster.Contains(e) || _shardrootFree.Contains(e)) continue;
                busy.Add((math.distancesq(em.GetComponentData<LocalTransform>(e).Position.xz, pos.xz), i, e));
            }
            busy.Sort((a, b) => a.Dist != b.Dist ? a.Dist.CompareTo(b.Dist) : a.Order.CompareTo(b.Order));
            for (int i = 0; i < busy.Count && _shardrootFree.Count < want; i++)
            {
                ReleaseFromMissions(faction, busy[i].Unit);
                _shardrootFree.Add(busy[i].Unit);
            }
        }

        private void TickShardroot(EntityManager em, Faction faction, ref SimpleAIState aiState, float now)
        {
            var cfg = AIShardroot.Cfg;
            if (!cfg.enabled) return;
            var s = ShardrootFor(faction);
            // The king's retreat is judged every think, not on the slower
            // Shardroot cadence: he can lose a lot in five seconds.
            Entity livingKing = AIShardroot.Locate(em, faction, out Entity bearer, out _) == AIShardroot.Where.Mine
                ? bearer : AIShardroot.LivingHero(em, faction);
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
                DecideTheBearer(em, faction, holder, pos, now);
                KingJoinsArmy(em, faction, king, now);
                return;
            }
            ClearShardrootSaving(faction);

            bool wanted = !GameSettings.IsPassiveAI(faction)
                && (where == AIShardroot.Where.Ground || where == AIShardroot.Where.Hostile)
                && now >= cfg.earliestSeconds
                && (AICommon.IsKnownGround(faction, pos) || ShardrootPickup.RevealedTo(em, holder, faction));
            if (!wanted)
            {
                s.Roster.Clear(); s.LaunchedAt = -1f;
                KingJoinsArmy(em, faction, king, now);
                return;
            }

            // THE TIER'S REACTION (Game_AI.md § 2a): whoever opened the ruins
            // has this long before a slow tier stirs.
            if (where == AIShardroot.Where.Ground && !AIPacing.ShardrootReacted(em, faction))
            {
                KingJoinsArmy(em, faction, king, now);
                return;
            }

            // Free combat units, as for the first-RP hunt.
            _shardrootFree.Clear();
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
            {
                Entity e = roster.E[i];
                if (e == king) continue;
                if (roster.Verb(i) || IsClaimSquadMember(e)) continue;
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<MoveCommand>(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                if (s.Roster.Contains(e)) continue;
                _shardrootFree.Add(e);
            }

            // TOLD WHERE IT LIES (Game_AI.md § 6l, 2026-11-06): do not wait for idle units,
            // pull the nearest soldiers out of whatever they are doing.
            bool told = where == AIShardroot.Where.Ground && ShardrootPickup.RevealedTo(em, holder, faction);
            if (told && _shardrootFree.Count < cfg.toldStrikeUnits)
                DraftForTheShardroot(em, faction, king, pos, cfg.toldStrikeUnits, s);

            // Never more than the tier's largest wave (Game_AI.md § 2a).
            int maxStrike = ProfileOf(faction).MaxWaveUnits;
            if (maxStrike > 0)
            {
                int room = math.max(0, maxStrike - s.Roster.Count);
                if (_shardrootFree.Count > room)
                    _shardrootFree.RemoveRange(room, _shardrootFree.Count - room);
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

            if (_shardrootFree.Count < (told ? 1 : math.max(1, cfg.minPartySize)))
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

        /// <summary>The bearer's owner chooses (Heroes.md §4): keep fighting
        /// Ascended, or stow it in the Temple for the Temple victory, saving the
        /// price while the hero fights on.</summary>
        private void DecideTheBearer(EntityManager em, Faction faction, Entity holder, float3 pos, float now)
        {
            if (PersonalityOf(faction).shardrootToKing
                || !AIShardroot.TryNearestTemple(em, faction, pos, out Entity temple, out _))
            {
                ClearShardrootSaving(faction);
                return;
            }
            var price = TheWaningBorder.Entities.ShardrootEmpowermentConfig.I.EnshrinePrice;
            SaveForShardroot(em, faction, price, now);
            if (!TheWaningBorder.Economy.FactionEconomy.CanAfford(em, faction, price)) return;
            if (em.HasComponent<ShardrootEnshrineOrder>(holder)) return;
            ReleaseFromMissions(faction, holder);
            CommandRouter.IssueEnshrineShardroot(em, holder, temple, CommandSource.AI);
            AILogger.Log(faction, "SHARDROOT", "the bearer goes to stow the Shardroot in the Temple");
        }

        /// <summary>Savings key of the Shardroot's price (AIPivotalReserve).</summary>
        private const string ShardrootReserveKey = "ShardrootPrice";
        /// <summary>Above the Fortress and age-up saves (both 1): the price
        /// is the faction's top saving while it holds the artifact.</summary>
        private const int ShardrootReservePriority = 2;
        private readonly Dictionary<int, float> _shardrootSaveLog = new Dictionary<int, float>();

        /// <summary>
        /// While the price is not in the bank: a STRICT pivotal goal (every
        /// research, army growth and non-exempt building holds for it, with no
        /// release cycle) and the top-priority budget reservation, re-armed
        /// every think. Cleared the moment the bank can pay, so the delivery
        /// spends it.
        /// </summary>
        private readonly Dictionary<int, float> _shardrootSaveSince = new Dictionary<int, float>();
        private readonly HashSet<int> _shardrootSoft = new HashSet<int>();

        private void SaveForShardroot(EntityManager em, Faction faction,
            TheWaningBorder.Core.Cost price, float now)
        {
            if (TheWaningBorder.Economy.FactionEconomy.CanAfford(em, faction, price))
            {
                ClearShardrootSaving(faction);
                return;
            }
            int key = (int)faction;
            if (!_shardrootSaveSince.TryGetValue(key, out float since))
            {
                since = now;
                _shardrootSaveSince[key] = now;
            }

            // THE OUTPOSTS WORK FOR THE PRICE (2026-11-02, Game_AI.md § 6l.2).
            // Red (YielLymwerra, 2026-11-02) sat at 208 veilsteel for 18
            // minutes under this save: the Trading Outposts stop forging past
            // outpostVeilsteelTarget, and nothing told them the bind wanted
            // 800. The price's shortfalls are a military shortage, so the
            // outpost planner forges veilsteel and buys veilstone toward it.
            AIBudget.NoteMilitaryShort(em, faction, price);

            // REACHABLE OR NOT. The strict hold (every purchase waits) is
            // worth it only while the bank will get there soon: the longest
            // per-resource shortfall / income under saveStrictEtaSeconds, and
            // the hold not yet past saveStrictMaxSeconds. Otherwise the price
            // is a SOFT save — the budget's duty cycle still banks toward it,
            // but the army, research and buildings go on. Red's bank reached
            // 25k supplies with a 30-man army because nothing was allowed to
            // spend, on a price its zero veilsteel income could never reach.
            var cfg = AIShardroot.Cfg;
            float eta = ShardrootPriceEta(em, faction, price, out string slowest);
            bool strict = cfg.saveStrictEtaSeconds > 0f && eta <= cfg.saveStrictEtaSeconds
                && cfg.saveStrictMaxSeconds > 0f && now - since <= cfg.saveStrictMaxSeconds;
            // THE ARMY NEVER WAITS FOR IT (2026-11-03): the save holds research
            // and buildings only — unit purchases are exempt — and it takes no
            // budget reservation, so the military wallet is never earmarked
            // for the artifact and the waves keep coming.
            AIPivotalReserve.Set(faction, ShardrootReserveKey, price, strict, exemptMilitary: true);
            AIBudget.ClearReservation(faction, ShardrootReservePriority);

            const string road = "enshrine";
            bool wasSoft = _shardrootSoft.Contains(key);
            if (wasSoft == strict)
            {
                if (strict) _shardrootSoft.Remove(key); else _shardrootSoft.Add(key);
                AILogger.Log(faction, "SHARDROOT", strict
                    ? $"the {road} price is within reach ({EtaText(eta)}) — a strict save on research and buildings " +
                      "(the army never waits): the bearer waits at home"
                    : $"the {road} price is out of reach ({slowest}) — a soft save: research and buildings " +
                      "go on, the outposts forge for it");
            }
            if (!_shardrootSaveLog.TryGetValue(key, out float next) || now >= next)
            {
                _shardrootSaveLog[key] = now + 60f;
                AILogger.Log(faction, "SHARDROOT",
                    $"saving for the {road} price " +
                    $"({price.Supplies}s/{price.Iron}i/{price.Veilstone}v/{price.Veilsteel}vs) — " +
                    (strict ? $"strict, {EtaText(eta)}; the bearer waits at home"
                            : $"soft ({slowest}); the bearer waits at home"));
            }
        }

        private static string EtaText(float eta)
            => float.IsInfinity(eta) ? "no income" : $"~{eta / 60f:0.0} min at current income";

        /// <summary>Seconds until the bank can pay <paramref name="price"/> at
        /// the budget's income rates: the longest per-resource shortfall /
        /// income; infinite when a short resource has no income at all.</summary>
        private static float ShardrootPriceEta(EntityManager em, Faction faction,
            TheWaningBorder.Core.Cost price, out string slowest)
        {
            slowest = "";
            if (!TheWaningBorder.Economy.FactionEconomy.TryGetResources(em, faction, out var bank))
                return float.PositiveInfinity;
            float eta = 0f;
            Slowest(price.Supplies - bank.Supplies, AIBudget.ResSupplies, "supplies", faction, ref eta, ref slowest);
            Slowest(price.Iron - bank.Iron, AIBudget.ResIron, "iron", faction, ref eta, ref slowest);
            Slowest(price.Veilstone - bank.Veilstone, AIBudget.ResVeilstone, "veilstone", faction, ref eta, ref slowest);
            Slowest(price.Veilsteel - bank.Veilsteel, AIBudget.ResVeilsteel, "veilsteel", faction, ref eta, ref slowest);
            return eta;
        }

        private static void Slowest(float shortBy, int resource, string name, Faction faction,
            ref float eta, ref string slowest)
        {
            if (shortBy <= 0f) return;
            float income = AIBudget.IncomePerSecond(faction, resource);
            float t = income > 1e-3f ? shortBy / income : float.PositiveInfinity;
            if (t <= eta) return;
            eta = t;
            slowest = float.IsInfinity(t)
                ? $"{name} {shortBy:0} short with no income"
                : $"{name} {shortBy:0} short at {income * 60f:0.0}/min, ~{t / 60f:0.0} min";
        }

        private void ClearShardrootSaving(Faction faction)
        {
            _shardrootSaveSince.Remove((int)faction);
            _shardrootSoft.Remove((int)faction);
            if (!AIPivotalReserve.Has(faction, ShardrootReserveKey)) return;
            AIPivotalReserve.Clear(faction, ShardrootReserveKey);
            AIBudget.ClearReservation(faction, ShardrootReservePriority);
        }

        /// <summary>The king walks ONTO a pickup (plain move: his
        /// DesiredDestination inside the pickup radius gives him right of way
        /// over his escort), or attack-moves on an enemy carrier.</summary>
        private static void SendKingOnto(EntityManager em, Entity king, float3 pos, AIShardroot.Where where)
        {
            if (king == Entity.Null) return;
            // Only a hero can take it, so this order is not optional on the ground.
            if (where == AIShardroot.Where.Ground) CommandRouter.IssueMove(em, king, pos, CommandSource.AI);
            else if (AIShardroot.Cfg.kingLeadsStrike) CommandRouter.IssueAttackMove(em, king, pos, CommandSource.AI);
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
                $"the hero rides out to the army ({best.Members.Count} strong) at ({best.LastCentroid.x:0},{best.LastCentroid.z:0})");
        }

        /// <summary>
        /// THE KING RETREATS (2026-10-09, Game_AI.md § 6l): at
        /// kingRetreatHpFraction of his health King Lexor leaves every
        /// mission and walks to the nearest Hall, re-ordered each think if
        /// anything turned him round, until he has healed to
        /// kingRecoveredHpFraction. Losing him loses the Shardroot he bears.
        /// </summary>
        private const float HomeRadius = 12f;

        private void TickKingRetreat(EntityManager em, Faction faction, Entity king, ShardrootStrike s)
        {
            var cfg = AIShardroot.Cfg;
            if (king == Entity.Null || cfg.kingRetreatHpFraction <= 0f
                || !em.HasComponent<Health>(king) || !em.HasComponent<LocalTransform>(king))
            { s.KingRetreat = false; return; }
            var hp = em.GetComponentData<Health>(king);
            float frac = hp.Max > 0 ? hp.Value / (float)hp.Max : 1f;
            bool bound = em.HasComponent<AscendedHero>(king);

            // The Shardbound King fights longer (2026-10-20): he leads the
            // waves now, and his retreat threshold is his own.
            float retreatAt = bound && cfg.boundKingRetreatHpFraction > 0f
                ? cfg.boundKingRetreatHpFraction : cfg.kingRetreatHpFraction;
            if (!s.KingRetreat)
            {
                if (frac > retreatAt) return;
                s.KingRetreat = true;
                AILogger.Log(faction, "SHARDROOT",
                    $"the hero{(bound ? " (Ascended)" : "")} at {frac * 100f:F0}% health -- pulled back to heal");
            }
            else if (frac >= cfg.kingRecoveredHpFraction)
            {
                s.KingRetreat = false;
                AILogger.Log(faction, "SHARDROOT",
                    $"the hero healed to {frac * 100f:F0}% -- back to the fight");
                return;
            }

            if (TransientState.Active<UserMoveOrder>(em, king)) return;
            ReleaseFromMissions(faction, king);
            float3 kp = em.GetComponentData<LocalTransform>(king).Position;
            if (!AIShardroot.TryNearestHall(em, faction, kp, out float3 hall)) return;
            if (math.distance(kp.xz, hall.xz) <= HomeRadius) return;
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
