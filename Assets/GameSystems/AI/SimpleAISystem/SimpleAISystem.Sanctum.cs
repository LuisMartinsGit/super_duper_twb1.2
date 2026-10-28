// SimpleAISystem.Sanctum.cs
// THE SANCTUM ARMY (2026-10-20, docs/Design/Game_AI.md § 6t).
// Developer: "AI should be actively trying to seize the sanctum territories" —
// then: "dedicated army, must stay and defend the sanctum. minimum size of 30.
// workers are dispatched to build fortifications there."
//
// No AI ever took a Sanctum: a Sanctum is claimed by HEAD COUNT
// (sanctumClaimUnits military units standing in it, uncontested, for
// sanctumClaimSeconds — Territory_Claims.md § 11.4), the claim squads are
// capped far below that, and a seen Sanctum (no resource node) dropped off the
// claim candidates entirely.
//
// Per faction, every sanctumThinkInterval, once aged up (claims open then):
//   * TEND each Sanctum army — prune the dead, top it back up to the minimum
//     from the free army, hunt any hostile soldier standing in the territory
//     (one keeps it contested, § 11.4), otherwise stand round the crystal.
//     Once the Sanctum is ours the army STAYS as its garrison, and workers
//     raise the culture's towers there (Alanthor Watch Towers, Runai
//     Waystations; Feraldis has no tower).
//   * RAISE a new army for the best Sanctum not ours (free first, nearest
//     first; a rival's when the army can beat what is known there), when the
//     free army holds at least the minimum. Short of it, the army target is
//     raised so production builds the soldiers.
//
// Members are reserved through IsClaimSquadMember, the one predicate every
// wave, raid, defence and claim drafter consults.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private sealed class SanctumArmy
        {
            public int Territory;
            public float3 Crystal;
            public bool Claimed;
            public float NextOrder;
            public float3 OrderedAt;
            public float NextFort;
            public readonly List<Entity> Members = new List<Entity>();
        }

        private readonly Dictionary<int, List<SanctumArmy>> _sanctumArmies = new Dictionary<int, List<SanctumArmy>>();
        private readonly HashSet<Entity> _sanctumGuards = new HashSet<Entity>();
        private readonly Dictionary<int, float> _sanctumNextThink = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _sanctumNextLog = new Dictionary<int, float>();
        private readonly Dictionary<(int, int), float> _sanctumBlocked = new Dictionary<(int, int), float>();
        private readonly Dictionary<int, int> _sanctumWanted = new Dictionary<int, int>();
        private readonly List<Entity> _sanctumFree = new List<Entity>(64);
        private int _sanctumEpoch = int.MinValue;

        private static readonly ComponentType[] QT_SanctumCrystals =
        {
            ComponentType.ReadOnly<SanctumCrystalTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_SanctumCrystals;

        private static readonly ComponentType[] QT_SanctumUnits =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_SanctumUnits;

        private static readonly ComponentType[] QT_SanctumBuildings =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_SanctumBuildings;

        /// <summary>Held by a Sanctum army — no other drafter takes it.</summary>
        private bool IsSanctumGuard(Entity e) => _sanctumGuards.Contains(e);

        private List<SanctumArmy> SanctumArmiesOf(Faction f)
        {
            if (_sanctumEpoch != SimCadence.Epoch)
            {
                _sanctumEpoch = SimCadence.Epoch;
                _sanctumArmies.Clear(); _sanctumGuards.Clear(); _sanctumNextThink.Clear();
                _sanctumNextLog.Clear(); _sanctumBlocked.Clear(); _sanctumWanted.Clear();
            }
            if (!_sanctumArmies.TryGetValue((int)f, out var l)) { l = new List<SanctumArmy>(); _sanctumArmies[(int)f] = l; }
            return l;
        }

        /// <summary>
        /// Army cap this faction needs on top of its tier's for the Sanctum
        /// armies: the guards it keeps, plus the minimum of one army it is
        /// trying to raise. Added to SustainArmyCap every think.
        /// </summary>
        private int SanctumArmyCapBonus(Faction f)
        {
            if (!Cfg.sanctumArmyEnabled) return 0;
            int n = 0;
            foreach (var a in SanctumArmiesOf(f)) n += a.Members.Count;
            if (_sanctumWanted.TryGetValue((int)f, out int w)) n += w;
            return n;
        }

        private int SanctumArmyMinSize =>
            math.max(Cfg.sanctumArmyMinSize, TerritoryOwnership.SanctumClaimUnits);

        private void TickSanctumArmies(EntityManager em, Faction faction, ref SimpleAIState aiState, float now)
        {
            if (!Cfg.sanctumArmyEnabled) return;
            var armies = SanctumArmiesOf(faction);
            int key = (int)faction;
            if (_sanctumNextThink.TryGetValue(key, out float next) && now < next) return;
            _sanctumNextThink[key] = now + math.max(1f, Cfg.sanctumThinkInterval);
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            if (culture == Cultures.None) { _sanctumWanted.Remove(key); return; }   // claims open at age-up

            for (int i = armies.Count - 1; i >= 0; i--)
                if (!TendSanctumArmy(em, faction, culture, armies[i], now))
                {
                    foreach (var m in armies[i].Members) _sanctumGuards.Remove(m);
                    armies.RemoveAt(i);
                }

            // One army forming at a time: the next waits for the last to land.
            bool forming = false;
            foreach (var a in armies) forming |= !a.Claimed;
            int maxArmies = Cfg.sanctumMaxArmies > 0 ? Cfg.sanctumMaxArmies : int.MaxValue;
            if (forming || armies.Count >= maxArmies) { _sanctumWanted.Remove(key); return; }
            // FINISHING A BEATEN FACTION (§ 6u): no NEW Sanctum army drafts
            // 30+ soldiers out of the push; the garrisons already standing stay.
            if (FinishingVictim(faction, out _)) { _sanctumWanted.Remove(key); return; }

            RaiseSanctumArmy(em, faction, ref aiState, armies, now);
        }

        // ── tending ─────────────────────────────────────────────────────

        /// <summary>One army's think. False = disband it.</summary>
        private bool TendSanctumArmy(EntityManager em, Faction faction, byte culture, SanctumArmy a, float now)
        {
            for (int i = a.Members.Count - 1; i >= 0; i--)
            {
                var m = a.Members[i];
                if (!em.Exists(m) || !em.HasComponent<Health>(m) || em.GetComponentData<Health>(m).Value <= 0
                    || TransientState.Active<DeathAnimationState>(em, m))
                {
                    _sanctumGuards.Remove(m);
                    a.Members.RemoveAt(i);
                }
            }

            bool ours = TerritoryOwnership.OwnerOf(a.Territory) == (int)faction;
            string name = RegionMap.NameOf(a.Territory);
            if (ours && !a.Claimed)
            {
                a.Claimed = true;
                AILogger.Log(faction, "SANCTUM", $"{name} is ours — {a.Members.Count} soldiers stay to hold it");
            }
            else if (!ours && a.Claimed)
            {
                a.Claimed = false;
                AILogger.Log(faction, "SANCTUM", $"{name} lost — the garrison ({a.Members.Count}) retakes it");
            }

            // A beaten army that never took its Sanctum goes home; the ground
            // waits sanctumRetrySeconds before another try.
            if (!ours && a.Members.Count < Cfg.sanctumDisbandBelow)
            {
                _sanctumBlocked[((int)faction, a.Territory)] = now + Cfg.sanctumRetrySeconds;
                AILogger.Log(faction, "SANCTUM",
                    $"the army for {name} is broken ({a.Members.Count} left) — falling back");
                return false;
            }

            // Back up to the minimum, from the free army.
            int min = SanctumArmyMinSize;
            if (a.Members.Count < min)
            {
                CollectSanctumFree(em, faction, a.Crystal);
                int added = 0;
                for (int i = 0; i < _sanctumFree.Count && a.Members.Count < min; i++)
                {
                    a.Members.Add(_sanctumFree[i]);
                    _sanctumGuards.Add(_sanctumFree[i]);
                    added++;
                }
                if (added > 0)
                {
                    a.NextOrder = 0f;   // the newcomers march now
                    AILogger.Log(faction, "SANCTUM", $"{added} soldiers sent to reinforce {name} ({a.Members.Count}/{min})");
                }
            }

            // Orders: clear the ground of hostile soldiers (one keeps the
            // Sanctum contested), otherwise stand round the crystal.
            bool hostile = TryNearestHostileSoldierIn(em, faction, a.Territory, a.Crystal, out float3 enemy);
            float3 goal = hostile ? enemy : a.Crystal;
            bool moved = math.distance(goal.xz, a.OrderedAt.xz) > 10f;
            if (now >= a.NextOrder || moved)
            {
                _sanctumFree.Clear();
                float r2 = Cfg.sanctumHoldRadius * Cfg.sanctumHoldRadius;
                for (int i = 0; i < a.Members.Count; i++)
                {
                    var m = a.Members[i];
                    if (TransientState.Active<UserMoveOrder>(em, m)) continue;
                    if (TransientState.Active<AttackCommand>(em, m)) continue;   // already fighting
                    if (!hostile && em.HasComponent<LocalTransform>(m)
                        && math.distancesq(em.GetComponentData<LocalTransform>(m).Position.xz, a.Crystal.xz) <= r2)
                        continue;   // already on station
                    _sanctumFree.Add(m);
                }
                if (_sanctumFree.Count > 0)
                    AICommon.IssueGroupOrder(em, _sanctumFree, goal, attackMove: true, Cfg.waveArrivedRadius);
                a.OrderedAt = goal;
                a.NextOrder = now + Cfg.sanctumReorderSeconds;
            }

            // Fortify the held Sanctum.
            if (ours && now >= a.NextFort)
            {
                a.NextFort = now + Cfg.sanctumFortCheckInterval;
                string tower = culture == Cultures.Alanthor ? TowerId
                             : culture == Cultures.Runai ? "Hut"      // the Waystation, a shooting tower
                             : null;
                if (tower != null && CountOwnIn(em, faction, tower, a.Territory) < Cfg.sanctumFortTowers)
                {
                    if (TryBuildInTerritory(em, faction, tower, a.Territory, a.Crystal,
                            AIBudgetCategory.Military, honourReservation: true, out string why))
                        AILogger.Log(faction, "SANCTUM", $"workers raise a {tower} at {name}");
                    else
                        LogSanctum(faction, now, $"fortifying {name}: {tower} not placed ({why})");
                }
            }
            return true;
        }

        // ── raising ─────────────────────────────────────────────────────

        private void RaiseSanctumArmy(EntityManager em, Faction faction, ref SimpleAIState aiState,
            List<SanctumArmy> armies, float now)
        {
            int key = (int)faction;
            float3 home = HomeAnchor(em, faction);

            // The best Sanctum not ours: free first, nearest first.
            var cq = QC_SanctumCrystals.Get(em, QT_SanctumCrystals);
            using var crystals = cq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int best = -1; float3 bestPos = default; float bestScore = float.MinValue; bool bestHostile = false;
            for (int i = 0; i < crystals.Length; i++)
            {
                float3 p = crystals[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None || !TerritoryResources.IsSanctum(t)) continue;
                int owner = TerritoryOwnership.OwnerOf(t);
                if (owner == (int)faction) continue;
                if (owner >= 0 && owner < 8 && !Alliances.AreHostile(faction, (Faction)owner)) continue;   // an ally's
                if (TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(t)) continue;   // gains for nobody (§ 6.9.4)
                if (_sanctumBlocked.TryGetValue((key, t), out float until) && now < until) continue;
                bool taken = false;
                foreach (var a in armies) taken |= a.Territory == t;
                if (taken) continue;
                bool rivals = owner >= 0;
                float score = -math.distance(home.xz, p.xz) - (rivals ? Cfg.sanctumHostilePenalty : 0f);
                if (score > bestScore) { bestScore = score; best = t; bestPos = p; bestHostile = rivals; }
            }
            if (best < 0) { _sanctumWanted.Remove(key); return; }

            string name = RegionMap.NameOf(best);
            int min = SanctumArmyMinSize;
            int threat = TacticalQuery.EnemyStrengthInRadius(em, faction, bestPos, Cfg.sanctumThreatRadius)
                       + AIEngagement.StaticDefencePower(em, faction, bestPos, Cfg.sanctumThreatRadius);
            int need = (int)math.ceil(threat * Cfg.sanctumLaunchMargin);

            CollectSanctumFree(em, faction, bestPos);
            // The standing floor stays home (Game_AI.md 6a).
            int floor = StandingArmyFloor(em, faction, ProfileOf(faction), aiState.DesiredMilitary);
            int draftable = math.max(0, _sanctumFree.Count - floor);

            var army = new SanctumArmy { Territory = best, Crystal = bestPos };
            int power = 0;
            for (int i = 0; i < draftable && (army.Members.Count < min || power < need); i++)
            {
                army.Members.Add(_sanctumFree[i]);
                power += TacticalQuery.UnitStrength(em, _sanctumFree[i]);
            }
            if (army.Members.Count < min || power < need)
            {
                // Short: ask production for the difference.
                int shortBy = math.max(min - army.Members.Count, 1);
                _sanctumWanted[key] = min;
                int want = CountAliveMilitary(em, faction) + shortBy;
                want = math.min(want, math.max(1, ProfileOf(faction).SustainArmyCap + SanctumArmyCapBonus(faction)));
                if (aiState.DesiredMilitary < want) aiState.DesiredMilitary = want;
                LogSanctum(faction, now,
                    $"raising an army for {name}{(bestHostile ? " (held by a rival)" : "")}: " +
                    $"{army.Members.Count} of {min} free, power {power} vs known {threat}");
                return;
            }

            _sanctumWanted.Remove(key);
            foreach (var m in army.Members) _sanctumGuards.Add(m);
            AICommon.IssueGroupOrder(em, army.Members, bestPos, attackMove: true, Cfg.waveArrivedRadius);
            army.OrderedAt = bestPos;
            army.NextOrder = now + Cfg.sanctumReorderSeconds;
            armies.Add(army);
            AILogger.Log(faction, "SANCTUM",
                $"{army.Members.Count} soldiers march to take {name}{(bestHostile ? " from a rival" : "")} " +
                $"at ({bestPos.x:0},{bestPos.z:0}) — power {power} vs known {threat}");
        }

        // ── helpers ─────────────────────────────────────────────────────

        /// <summary>Free combat units (nobody else's), nearest to <paramref name="to"/> first.</summary>
        private void CollectSanctumFree(EntityManager em, Faction faction, float3 to)
        {
            _sanctumFree.Clear();
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
            {
                Entity e = roster.E[i];
                if (roster.UnderConstruction(i) || roster.Verb(i) || roster.NoControl(i)) continue;
                if (IsClaimSquadMember(e) || IsEnrolledInMission(faction, e)) continue;
                if (em.HasComponent<ShardrootTag>(e)) continue;   // the bearer has its own road
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                _sanctumFree.Add(e);
            }
            _sanctumFree.Sort((a, b) =>
            {
                float da = em.HasComponent<LocalTransform>(a) ? math.distancesq(em.GetComponentData<LocalTransform>(a).Position.xz, to.xz) : float.MaxValue;
                float db = em.HasComponent<LocalTransform>(b) ? math.distancesq(em.GetComponentData<LocalTransform>(b).Position.xz, to.xz) : float.MaxValue;
                int c = da.CompareTo(db);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
        }

        /// <summary>The hostile SOLDIER (one that weighs on a meter, § 2.1)
        /// standing in <paramref name="t"/> nearest <paramref name="from"/>.</summary>
        private static bool TryNearestHostileSoldierIn(EntityManager em, Faction faction, int t, float3 from, out float3 at)
        {
            at = default;
            var q = QC_SanctumUnits.Get(em, QT_SanctumUnits);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                var f = facs[i].Value;
                if (f == faction || f == Faction.Border || !Alliances.AreHostile(faction, f)) continue;
                var p = xfs[i].Position;
                if (RegionMap.RegionAt(p.x, p.z) != t) continue;
                // Only a soldier in vision (2026-10-25, Game_AI.md § 6v): the
                // march from home used to beeline onto a soldier nobody saw.
                if (!VisibleNow(faction, p)) continue;
                if (!TheWaningBorder.Systems.World.TerritoryClaimSystem.CountsForClaim(em, ents[i])) continue;
                float d = math.distancesq(p.xz, from.xz);
                if (d < bestD) { bestD = d; at = p; }
            }
            return bestD < float.MaxValue;
        }

        /// <summary>Our buildings of <paramref name="buildingId"/> in
        /// territory <paramref name="t"/>, construction sites included.</summary>
        private static int CountOwnIn(EntityManager em, Faction faction, string buildingId, int t)
        {
            var q = QC_SanctumBuildings.Get(em, QT_SanctumBuildings);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var p = xfs[i].Position;
                if (RegionMap.RegionAt(p.x, p.z) != t) continue;
                if (TheWaningBorder.Entities.BuildingIds.Of(ents[i], em) == buildingId) n++;
            }
            return n;
        }

        private void LogSanctum(Faction faction, float now, string msg)
        {
            if (_sanctumNextLog.TryGetValue((int)faction, out float next) && now < next) return;
            _sanctumNextLog[(int)faction] = now + 60f;
            AILogger.Log(faction, "SANCTUM", msg);
        }
    }
}
