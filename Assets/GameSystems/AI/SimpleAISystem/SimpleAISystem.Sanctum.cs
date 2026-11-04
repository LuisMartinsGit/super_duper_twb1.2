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
            /// <summary>Forming at home: soldiers reserved, waiting for the
            /// head count and the power to launch (2026-11-02, § 6t).</summary>
            public bool Mustering;
            public float MusterSince;
            public float3 MusterAt;
            public float NextOrder;
            public float3 OrderedAt;
            public float NextFort;
            public readonly List<Entity> Members = new List<Entity>();
            /// <summary>The next reinforcement packet, gathering at home (2026-11-04).</summary>
            public readonly List<Entity> Packet = new List<Entity>();
            public float PacketSince;
            /// <summary>When it was ready to go; it gathers for at most musterTimeoutSeconds.</summary>
            public float PacketReadyAt;
            public float3 PacketMuster;
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
                if (!TendSanctumArmy(em, faction, culture, armies[i], ref aiState, now))
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
        private bool TendSanctumArmy(EntityManager em, Faction faction, byte culture, SanctumArmy a,
            ref SimpleAIState aiState, float now)
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
            for (int i = a.Packet.Count - 1; i >= 0; i--)
            {
                var m = a.Packet[i];
                if (!em.Exists(m) || !em.HasComponent<Health>(m) || em.GetComponentData<Health>(m).Value <= 0
                    || TransientState.Active<DeathAnimationState>(em, m))
                {
                    _sanctumGuards.Remove(m);
                    a.Packet.RemoveAt(i);
                }
            }

            if (a.Mustering) return TendMusteringSanctumArmy(em, faction, a, ref aiState, now);

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

            // Back up to the minimum, from the free army, AS A PACKET.
            TendSanctumPacket(em, faction, a, name, aiState.DesiredMilitary, now);

            // Orders: clear the ground of hostile soldiers (one keeps the
            // Sanctum contested), otherwise stand round the crystal.
            bool hostile = TryNearestHostileSoldierIn(em, faction, a.Territory, a.Crystal, out float3 enemy);
            float3 goal = hostile ? enemy : a.Crystal;
            bool moved = math.distance(goal.xz, a.OrderedAt.xz) > Cfg.sanctumRegoalMeters;
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

        // ── mustering (2026-11-02, Game_AI.md § 6t) ─────────────────────
        //
        // Red (YielLymwerra, 2026-11-02) logged "raising an army for
        // Stormcairn: N of 30 free, power N vs known 0" 38 times over 34
        // minutes and never sent a soldier: the rule wanted 30 FREE units at
        // one instant, and waves, raids, reinforcements, tower garrisons and
        // the economy defence drafted the same pool first. The army now
        // forms over time: whoever is free is reserved at once (no other
        // draft takes a Sanctum guard), the muster tops up every think, and
        // it launches the moment it has the head count and the power.

        private bool TendMusteringSanctumArmy(EntityManager em, Faction faction, SanctumArmy a,
            ref SimpleAIState aiState, float now)
        {
            int key = (int)faction;
            string name = RegionMap.NameOf(a.Territory);
            int min = SanctumArmyMinSize;

            int owner = TerritoryOwnership.OwnerOf(a.Territory);
            bool stale = TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(a.Territory)
                || (owner >= 0 && owner < 8 && owner != (int)faction && !Alliances.AreHostile(faction, (Faction)owner));
            if (stale)
            {
                ReleaseSanctumArmy(a);
                _sanctumWanted.Remove(key);
                _sanctumBlocked[(key, a.Territory)] = now + Cfg.sanctumRetrySeconds;
                AILogger.Log(faction, "SANCTUM", $"the muster for {name} stands down — the ground no longer qualifies");
                return false;
            }

            // Top up from the free pool; the standing floor stays home.
            CollectSanctumFree(em, faction, a.Crystal);
            int floor = StandingArmyFloor(em, faction, ProfileOf(faction), aiState.DesiredMilitary);
            int draftable = math.max(0, _sanctumFree.Count - floor);
            int added = 0;
            for (int i = 0; i < draftable && a.Members.Count < min; i++)
            {
                a.Members.Add(_sanctumFree[i]);
                _sanctumGuards.Add(_sanctumFree[i]);
                added++;
            }

            int threat = TacticalQuery.EnemyStrengthInRadius(em, faction, a.Crystal, Cfg.sanctumThreatRadius)
                       + AIEngagement.StaticDefencePower(em, faction, a.Crystal, Cfg.sanctumThreatRadius);
            int need = (int)math.ceil(threat * Cfg.sanctumLaunchMargin);
            int power = 0;
            for (int i = 0; i < a.Members.Count; i++) power += TacticalQuery.UnitStrength(em, a.Members[i]);

            if (owner == (int)faction || (a.Members.Count >= min && power >= need))
            {
                a.Mustering = false;
                _sanctumWanted.Remove(key);
                AICommon.IssueGroupOrder(em, a.Members, a.Crystal, attackMove: true, Cfg.waveArrivedRadius);
                a.OrderedAt = a.Crystal;
                a.NextOrder = now + Cfg.sanctumReorderSeconds;
                AILogger.Log(faction, "SANCTUM",
                    $"{a.Members.Count} soldiers march to take {name}{(owner >= 0 && owner != (int)faction ? " from a rival" : "")} " +
                    $"at ({a.Crystal.x:0},{a.Crystal.z:0}) — power {power} vs known {threat}, mustered in {now - a.MusterSince:0}s");
                return true;
            }

            if (Cfg.sanctumMusterMaxSeconds > 0f && now - a.MusterSince > Cfg.sanctumMusterMaxSeconds)
            {
                AILogger.Log(faction, "SANCTUM",
                    $"the muster for {name} gave up after {now - a.MusterSince:0}s ({a.Members.Count}/{min}, " +
                    $"power {power} vs known {threat}) — released; {name} waits {Cfg.sanctumRetrySeconds:0}s");
                ReleaseSanctumArmy(a);
                _sanctumWanted.Remove(key);
                _sanctumBlocked[(key, a.Territory)] = now + Cfg.sanctumRetrySeconds;
                return false;
            }

            // Still short: production builds the rest (the cap rises by it).
            _sanctumWanted[key] = min;
            int want = CountAliveMilitary(em, faction) + math.max(min - a.Members.Count, 1);
            want = math.min(want, math.max(1, ProfileOf(faction).SustainArmyCap + SanctumArmyCapBonus(faction)));
            if (aiState.DesiredMilitary < want) aiState.DesiredMilitary = want;

            // Everyone gathers at the muster point; newcomers at once.
            if (added > 0 || now >= a.NextOrder)
            {
                _sanctumFree.Clear();
                float r2 = Cfg.sanctumHoldRadius * Cfg.sanctumHoldRadius;
                for (int i = 0; i < a.Members.Count; i++)
                {
                    var m = a.Members[i];
                    if (TransientState.Active<UserMoveOrder>(em, m)) continue;
                    if (TransientState.Active<AttackCommand>(em, m)) continue;
                    if (em.HasComponent<LocalTransform>(m)
                        && math.distancesq(em.GetComponentData<LocalTransform>(m).Position.xz, a.MusterAt.xz) <= r2)
                        continue;
                    _sanctumFree.Add(m);
                }
                if (_sanctumFree.Count > 0)
                    AICommon.IssueGroupOrder(em, _sanctumFree, a.MusterAt, attackMove: true, Cfg.waveArrivedRadius);
                a.NextOrder = now + Cfg.sanctumReorderSeconds;
            }
            LogSanctum(faction, now,
                $"mustering for {name}: {a.Members.Count}/{min} ({added} joined), power {power} vs known {threat}, " +
                $"{now - a.MusterSince:0}s in");
            return true;
        }

        private void ReleaseSanctumArmy(SanctumArmy a)
        {
            foreach (var m in a.Members) _sanctumGuards.Remove(m);
            a.Members.Clear();
            foreach (var m in a.Packet) _sanctumGuards.Remove(m);
            a.Packet.Clear();
        }

        /// <summary>
        /// REINFORCEMENTS ARE ARMIES (2026-11-04). The garrison's refill used to
        /// send whoever was free the moment a soldier fell: 1-4 men every ten
        /// seconds, strung along the road and killed one by one. Free soldiers now
        /// gather at a muster point at home until the packet reaches its size, or
        /// reinforceMaxHold runs out with at least two, and leave as one formation.
        /// </summary>
        private void TendSanctumPacket(EntityManager em, Faction faction, SanctumArmy a, string name, int desired, float now)
        {
            int min = SanctumArmyMinSize;
            int deficit = min - a.Members.Count;
            if (deficit <= 0 && a.Packet.Count == 0) return;

            // Only a full packet is raised, and only from soldiers above the
            // standing floor: nobody is reserved at home waiting for company.
            int want = math.max(Cfg.reinforceMinGroup,
                (int)math.ceil(a.Members.Count * Cfg.reinforcePacketFraction));
            if (a.Packet.Count == 0)
            {
                if (deficit < want) return;
                CollectSanctumFree(em, faction, a.Crystal);
                int floor = StandingArmyFloor(em, faction, ProfileOf(faction), desired);
                if (_sanctumFree.Count - floor < want) return;
                for (int i = 0; i < want; i++)
                {
                    a.Packet.Add(_sanctumFree[i]);
                    _sanctumGuards.Add(_sanctumFree[i]);
                }
                a.PacketSince = now;
                a.PacketReadyAt = 0f;
                float3 home = HomeAnchor(em, faction);
                float3 toward = a.Crystal - home;
                toward.y = 0f;
                float dist = math.length(toward);
                a.PacketMuster = dist > 1f
                    ? StandableToward(home + toward / dist * math.min(Cfg.musterDistance, dist * 0.5f), home)
                    : home;
                AICommon.IssueGroupOrder(em, a.Packet, a.PacketMuster, attackMove: true, Cfg.waveArrivedRadius);
            }

            // Ready: give the stragglers the muster's own time to fall in.
            if (a.PacketReadyAt <= 0f) a.PacketReadyAt = now;
            int gathered = 0;
            float gatherR = GatherRadius(a.Packet.Count);
            float r2 = gatherR * gatherR;
            foreach (var m in a.Packet)
                if (em.HasComponent<LocalTransform>(m)
                    && math.distancesq(em.GetComponentData<LocalTransform>(m).Position.xz, a.PacketMuster.xz) <= r2)
                    gathered++;
            if (gathered < a.Packet.Count * Cfg.musterGatherFraction
                && now - a.PacketReadyAt < Cfg.musterTimeoutSeconds)
                return;

            AILogger.Log(faction, "SANCTUM",
                $"a packet of {a.Packet.Count} marches to reinforce {name} ({a.Members.Count}/{min}, " +
                $"formed in {now - a.PacketSince:0}s, {gathered} on the muster)");
            AICommon.IssueGroupOrder(em, a.Packet, a.Crystal, attackMove: true, Cfg.waveArrivedRadius);
            a.Members.AddRange(a.Packet);
            a.Packet.Clear();
            a.OrderedAt = a.Crystal;
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
                if (army.Members.Count == 0)
                {
                    LogSanctum(faction, now,
                        $"raising an army for {name}{(bestHostile ? " (held by a rival)" : "")}: " +
                        $"0 of {min} free, power 0 vs known {threat}");
                    return;
                }
                // THE ARMY FORMS OVER TIME (2026-11-02, § 6t): reserve whoever
                // is free now, gather at the muster point, top up every think.
                foreach (var m in army.Members) _sanctumGuards.Add(m);
                army.Mustering = true;
                army.MusterSince = now;
                float3 toward = bestPos - home;
                toward.y = 0f;
                float dist = math.length(toward);
                army.MusterAt = dist > 1f
                    ? StandableToward(home + toward / dist * math.min(Cfg.musterDistance, dist * 0.5f), home)
                    : home;
                AICommon.IssueGroupOrder(em, army.Members, army.MusterAt, attackMove: true, Cfg.waveArrivedRadius);
                army.OrderedAt = army.MusterAt;
                army.NextOrder = now + Cfg.sanctumReorderSeconds;
                armies.Add(army);
                AILogger.Log(faction, "SANCTUM",
                    $"mustering an army for {name}{(bestHostile ? " (held by a rival)" : "")}: " +
                    $"{army.Members.Count}/{min} reserved (power {power} vs known {threat}) at " +
                    $"({army.MusterAt.x:0},{army.MusterAt.z:0}) — no wave, raid or claim drafts them");
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
                if (em.HasComponent<SiegeTag>(e)) continue;       // engines garrison nothing (§ 6x)
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
