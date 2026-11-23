// SimpleAISystem.Sanctum.cs
// SANCTUMS FOR THE AI (docs/Design/Game_AI.md § 6t; reworked 2026-11-16 by
// Unification decision 68).
//
// Developer, 2026-10-20: "AI should be actively trying to seize the sanctum
// territories" — "dedicated army, must stay and defend the sanctum".
// Developer, 2026-11-16: the dedicated army is gone. In the Trine Hollow v2
// match (0.0.90) each Hard AI reserved 30 of its 30-45 soldiers for a Sanctum
// muster for five to ten minutes at a time, lost them in a minute or two, and
// reserved the next 30 — Red, a Rush AI, launched no wave in 44 minutes. Now:
//
//   * A SANCTUM IS A WAVE OBJECTIVE (TrySanctumObjective, read by
//     TryLaunchAttack). A held Sanctum that is threatened, or one just lost,
//     comes first; otherwise the best Sanctum not ours takes the wave when the
//     victory plan is on the Sanctum road, or (Mixed road) when it is about as
//     near as the base the wave would strike. The wave must bring the claim's
//     head count, and it holds on the Sanctum until the claim lands
//     (Mission.SanctumTerritory).
//   * A TAKEN SANCTUM KEEPS A GARRISON (sanctumGarrisonSize): whoever of ours
//     stands in it when it becomes ours stays behind — the wave moves on with
//     the rest. The garrison hunts hostile soldiers in the territory, stands
//     round the crystal, tops itself up from our soldiers passing through, and
//     its workers raise towers there (Alanthor Watch Towers, Runai Waystations).
//   * A HELD SANCTUM IS DEFENDED: when the hostile strength in it outweighs its
//     garrison (sanctumLaunchMargin) the next wave is due at once and goes
//     there; a Sanctum lost to a rival is the next wave's objective for
//     sanctumRetakeSeconds.
//   * The all-in and the finishing push take the garrisons with them.
//
// Garrison members are reserved through IsClaimSquadMember, the one predicate
// every wave, raid, defence and claim drafter consults.

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
        /// <summary>The garrison of one held Sanctum.</summary>
        private sealed class SanctumArmy
        {
            public int Territory;
            public float3 Crystal;
            public bool Claimed;
            /// <summary>Hostile strength in the Sanctum outweighs the garrison:
            /// the next wave goes there (TrySanctumObjective).</summary>
            public bool Threatened;
            public float NextOrder;
            public float3 OrderedAt;
            public float NextFort;
            public readonly List<Entity> Members = new List<Entity>();
        }

        private readonly Dictionary<int, List<SanctumArmy>> _sanctumArmies = new Dictionary<int, List<SanctumArmy>>();
        private readonly HashSet<Entity> _sanctumGuards = new HashSet<Entity>();
        private readonly Dictionary<int, float> _sanctumNextThink = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _sanctumNextLog = new Dictionary<int, float>();
        /// <summary>(faction, territory) -> a wave was sent there; not again before this time.</summary>
        private readonly Dictionary<(int, int), float> _sanctumBlocked = new Dictionary<(int, int), float>();
        /// <summary>(faction, territory) -> a held Sanctum lost to a rival; retake it until this time.</summary>
        private readonly Dictionary<(int, int), float> _sanctumLost = new Dictionary<(int, int), float>();
        private readonly bool[] _sanctumAllInLogged = new bool[ProfileSlots];
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

        /// <summary>Held by a Sanctum garrison — no other drafter takes it.</summary>
        private bool IsSanctumGuard(Entity e) => _sanctumGuards.Contains(e);

        private List<SanctumArmy> SanctumArmiesOf(Faction f)
        {
            if (_sanctumEpoch != SimCadence.Epoch)
            {
                _sanctumEpoch = SimCadence.Epoch;
                _sanctumArmies.Clear(); _sanctumGuards.Clear(); _sanctumNextThink.Clear();
                _sanctumNextLog.Clear(); _sanctumBlocked.Clear(); _sanctumLost.Clear();
                System.Array.Clear(_sanctumAllInLogged, 0, _sanctumAllInLogged.Length);
            }
            if (!_sanctumArmies.TryGetValue((int)f, out var l)) { l = new List<SanctumArmy>(); _sanctumArmies[(int)f] = l; }
            return l;
        }

        /// <summary>Army cap this faction needs on top of its tier's for the
        /// soldiers its garrisons hold. Added to SustainArmyCap every think.</summary>
        private int SanctumArmyCapBonus(Faction f)
        {
            if (!Cfg.sanctumArmyEnabled) return 0;
            int n = 0;
            foreach (var a in SanctumArmiesOf(f)) n += a.Members.Count;
            return n;
        }

        /// <summary>Does this faction play for Sanctums at all (the tier's gate,
        /// Game_AI.md § 2; the victory plan replaces it, § 6aa)?</summary>
        private bool SanctumsOpenFor(Faction faction, float now)
        {
            if (!Cfg.sanctumArmyEnabled || GameSettings.IsPassiveAI(faction)) return false;
            var tier = ProfileOf(faction);
            return Cfg.victoryPlanEnabled
                || !(tier.SanctumMode == 2 || (tier.SanctumMode == 1 && now < tier.SanctumLateSeconds));
        }

        private void TickSanctumArmies(EntityManager em, Faction faction, ref SimpleAIState aiState, float now)
        {
            if (!SanctumsOpenFor(faction, now)) return;
            var armies = SanctumArmiesOf(faction);
            int key = (int)faction;

            // THE ALL-IN (§ 6n, decision 68 e): the garrisons join the push;
            // none is posted while it lasts.
            bool allIn = AllInArmed(faction);
            if (allIn)
            {
                if (armies.Count > 0)
                {
                    int freed = 0;
                    foreach (var a in armies) { freed += a.Members.Count; ReleaseSanctumArmy(a); }
                    armies.Clear();
                    if (allIn && key < _sanctumAllInLogged.Length && !_sanctumAllInLogged[key])
                    {
                        _sanctumAllInLogged[key] = true;
                        AILogger.Log(faction, "ALL-IN", $"Sanctum garrisons released into the army ({freed} unit(s))");
                    }
                }
                return;
            }

            if (_sanctumNextThink.TryGetValue(key, out float next) && now < next) return;
            _sanctumNextThink[key] = now + math.max(1f, Cfg.sanctumThinkInterval);
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            if (culture == Cultures.None) return;   // claims open at age-up

            for (int i = armies.Count - 1; i >= 0; i--)
                if (!TendSanctumGarrison(em, faction, culture, armies[i], ref aiState, now))
                {
                    ReleaseSanctumArmy(armies[i]);
                    armies.RemoveAt(i);
                }

            PostSanctumGarrisons(em, faction, armies, now);
        }

        // ── garrisons ───────────────────────────────────────────────────

        /// <summary>A garrison's think. False = disband it.</summary>
        private bool TendSanctumGarrison(EntityManager em, Faction faction, byte culture, SanctumArmy a,
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

            int key = (int)faction;
            int owner = TerritoryOwnership.OwnerOf(a.Territory);
            bool ours = owner == key;
            string name = RegionMap.NameOf(a.Territory);
            if (ours && !a.Claimed)
            {
                a.Claimed = true;
                _sanctumLost.Remove((key, a.Territory));
                AILogger.Log(faction, "SANCTUM", $"{name} is ours again — {a.Members.Count} soldiers hold it");
            }
            else if (!ours && a.Claimed)
            {
                a.Claimed = false;
                _sanctumLost[(key, a.Territory)] = now + Cfg.sanctumRetakeSeconds;
                aiState.NextWaveTime = math.min(aiState.NextWaveTime, now);
                AILogger.Log(faction, "SANCTUM",
                    $"{name} lost — the garrison ({a.Members.Count}) fights on; the next wave retakes it");
            }

            if (!ours && a.Members.Count < Cfg.sanctumDisbandBelow)
            {
                AILogger.Log(faction, "SANCTUM",
                    $"the garrison of {name} is broken ({a.Members.Count} left) — falling back; the next wave retakes it");
                return false;
            }

            // Back up to strength from our soldiers standing in the Sanctum.
            if (a.Members.Count < Cfg.sanctumGarrisonSize)
            {
                CollectOursInTerritory(em, faction, a.Territory, a.Crystal);
                int added = 0;
                for (int i = 0; i < _sanctumFree.Count && a.Members.Count < Cfg.sanctumGarrisonSize; i++)
                {
                    var e = _sanctumFree[i];
                    RemoveFromMissions(faction, e);
                    a.Members.Add(e);
                    _sanctumGuards.Add(e);
                    added++;
                }
                if (added > 0)
                    LogSanctum(faction, now, $"{added} soldier(s) passing through join the garrison of {name} ({a.Members.Count})");
            }

            // DEFENDED (decision 68): a threat the garrison cannot hold calls the next wave.
            int threat = TacticalQuery.EnemyStrengthInRadius(em, faction, a.Crystal, Cfg.sanctumThreatRadius);
            int power = 0;
            for (int i = 0; i < a.Members.Count; i++) power += TacticalQuery.UnitStrength(em, a.Members[i]);
            bool threatened = threat > 0 && threat * math.max(0.1f, Cfg.sanctumLaunchMargin) > power;
            if (threatened && !a.Threatened)
            {
                aiState.NextWaveTime = math.min(aiState.NextWaveTime, now);
                AILogger.Log(faction, "SANCTUM",
                    $"{name} is under threat (hostile {threat} vs garrison {power}) — the next wave goes there");
            }
            a.Threatened = threatened;

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

        /// <summary>Every Sanctum we hold without a garrison gets one from
        /// whoever of ours stands in it — normally the wave that just took it.</summary>
        private void PostSanctumGarrisons(EntityManager em, Faction faction, List<SanctumArmy> armies, float now)
        {
            int key = (int)faction;
            var cq = QC_SanctumCrystals.Get(em, QT_SanctumCrystals);
            if (cq.IsEmptyIgnoreFilter) return;
            using var crystals = cq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int c = 0; c < crystals.Length; c++)
            {
                float3 p = crystals[c].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None || !TerritoryResources.IsSanctum(t)) continue;
                if (TerritoryOwnership.OwnerOf(t) != key) continue;
                bool has = false;
                foreach (var a in armies) has |= a.Territory == t;
                if (has) continue;

                CollectOursInTerritory(em, faction, t, p);
                if (_sanctumFree.Count == 0) continue;
                var army = new SanctumArmy { Territory = t, Crystal = p, Claimed = true };
                for (int i = 0; i < _sanctumFree.Count && army.Members.Count < Cfg.sanctumGarrisonSize; i++)
                {
                    var e = _sanctumFree[i];
                    RemoveFromMissions(faction, e);
                    army.Members.Add(e);
                    _sanctumGuards.Add(e);
                }
                army.NextOrder = now;           // to the crystal on its first think
                army.OrderedAt = p;
                armies.Add(army);
                _sanctumLost.Remove((key, t));
                AILogger.Log(faction, "SANCTUM",
                    $"{RegionMap.NameOf(t)} is ours — {army.Members.Count} soldiers stay as its garrison, the rest move on");
            }
        }

        private void ReleaseSanctumArmy(SanctumArmy a)
        {
            foreach (var m in a.Members) _sanctumGuards.Remove(m);
            a.Members.Clear();
        }

        // ── the wave's Sanctum objective ────────────────────────────────

        /// <summary>
        /// A Sanctum for the next wave (decision 68), in order: a held Sanctum
        /// under threat; one we lost lately; on the Sanctum road the best one not
        /// ours; on the Mixed road the best one not ours when it lies no more than
        /// sanctumObjectivePreferMeters farther from home than the base target.
        /// Taking one needs a wave of the claim's head count.
        /// </summary>
        private bool TrySanctumObjective(EntityManager em, Faction faction, float3 origin,
            bool hasBaseTarget, float3 baseTargetPos, int waveSize, float now,
            out int territory, out float3 pos, out string why)
        {
            territory = -1; pos = default; why = null;
            if (!SanctumsOpenFor(faction, now) || !RegionMap.Ready || !TerritoryOwnership.Ready) return false;
            int key = (int)faction;
            var armies = SanctumArmiesOf(faction);

            // 1. Defend what we hold.
            SanctumArmy defend = null;
            float defendD = float.MaxValue;
            foreach (var a in armies)
            {
                if (!a.Threatened || !a.Claimed) continue;
                float d = math.distancesq(origin.xz, a.Crystal.xz);
                if (d < defendD) { defendD = d; defend = a; }
            }
            if (defend != null)
            {
                territory = -1;   // ours already: nothing to hold for
                pos = defend.Crystal;
                why = $"defend the Sanctum {RegionMap.NameOf(defend.Territory)}";
                return true;
            }

            // THE DOMINION CLOCK COMES FIRST (decision 114): while a hostile
            // faction counts down to dominion, the wave goes to the nearest
            // Sanctum its side holds - whatever the war, the road, the size of
            // the wave or the distance.
            if (HostileDominionRunning(em, faction, out var domHolder))
            {
                var dq = QC_SanctumCrystals.Get(em, QT_SanctumCrystals);
                if (!dq.IsEmptyIgnoreFilter)
                {
                    using var dcr = dq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                    int dBest = -1; float3 dPos = default; float dD = float.MaxValue;
                    for (int i = 0; i < dcr.Length; i++)
                    {
                        float3 p = dcr[i].Position;
                        int t = RegionMap.RegionAt(p.x, p.z);
                        if (t == RegionMap.None || !TerritoryResources.IsSanctum(t)) continue;
                        int owner = TerritoryOwnership.OwnerOf(t);
                        if (owner < 0 || owner >= 8 || !Alliances.AreHostile(faction, (Faction)owner)) continue;
                        float d = math.distancesq(origin.xz, p.xz);
                        if (d < dD) { dD = d; dBest = t; dPos = p; }
                    }
                    if (dBest >= 0)
                    {
                        territory = dBest; pos = dPos;
                        why = $"break {domHolder}'s dominion countdown at the Sanctum {RegionMap.NameOf(dBest)}";
                        return true;
                    }
                }
            }

            // A WAR IS NOT INTERRUPTED BY A SANCTUM (decision 86): off the
            // Sanctum road, while a war is on, the wave goes to its victim.
            if (Cfg.warHoldsUntilFall
                && !(Cfg.victoryPlanEnabled && VictoryPlanOf(faction).Path == VictoryPath.Sanctums)
                && TryGetWarVictim(em, faction, out var warOn))
            {
                LogSanctum(faction, now, $"no Sanctum for this wave: the war on {warOn} holds");
                return false;
            }

            bool canClaim = waveSize >= TerritoryOwnership.SanctumClaimUnits;
            if (!canClaim)
            {
                LogSanctum(faction, now, $"no Sanctum for this wave: {waveSize} soldiers, the claim needs {TerritoryOwnership.SanctumClaimUnits}");
                return false;
            }

            var cq = QC_SanctumCrystals.Get(em, QT_SanctumCrystals);
            if (cq.IsEmptyIgnoreFilter) return false;
            using var crystals = cq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // 2. Retake what we lost.
            int best = -1; float3 bestPos = default; float bestScore = float.MinValue; bool bestHostile = false;
            for (int i = 0; i < crystals.Length; i++)
            {
                float3 p = crystals[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None) continue;
                if (!_sanctumLost.TryGetValue((key, t), out float until) || now >= until) continue;
                if (!SanctumStillToTake(faction, t)) continue;
                float score = -math.distance(origin.xz, p.xz);
                if (score > bestScore) { bestScore = score; best = t; bestPos = p; }
            }
            if (best >= 0)
            {
                territory = best; pos = bestPos;
                why = $"retake the Sanctum {RegionMap.NameOf(best)}";
                return true;
            }

            // 3. Take a new one.
            if (!VictoryAllowsSanctumArmy(faction))
            {
                LogSanctum(faction, now, $"no Sanctum for this wave: the {VictoryPlanOf(faction).Path} road");
                return false;
            }
            bestScore = float.MinValue;
            for (int i = 0; i < crystals.Length; i++)
            {
                float3 p = crystals[i].Position;
                int t = RegionMap.RegionAt(p.x, p.z);
                if (t == RegionMap.None || !TerritoryResources.IsSanctum(t)) continue;
                if (!SanctumStillToTake(faction, t)) continue;
                if (_sanctumBlocked.TryGetValue((key, t), out float until) && now < until) continue;
                int owner = TerritoryOwnership.OwnerOf(t);
                bool rivals = owner >= 0;
                float score = -math.distance(origin.xz, p.xz) - (rivals ? Cfg.sanctumHostilePenalty : 0f);
                if (score > bestScore) { bestScore = score; best = t; bestPos = p; bestHostile = rivals; }
            }
            bool sanctumRoad = Cfg.victoryPlanEnabled && VictoryPlanOf(faction).Path == VictoryPath.Sanctums;
            if (best < 0 && sanctumRoad)
            {
                // A CURSED SANCTUM IS CLEARED, NOT SKIPPED (iteration 6, 2026-11-17):
                // it counts for nobody while the curse is on it, so dominion is out
                // of everyone's reach until someone kills its nodes — Broken Oath's
                // one Sanctum stayed cursed through every match. On the Sanctum road
                // with nothing clean left, the wave marches on its nearest node.
                float bestC = float.MinValue; int bestT = -1; float3 bestP = default;
                for (int i = 0; i < crystals.Length; i++)
                {
                    float3 p = crystals[i].Position;
                    int t = RegionMap.RegionAt(p.x, p.z);
                    if (t == RegionMap.None || !TerritoryResources.IsSanctum(t)) continue;
                    if (!TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(t)) continue;
                    int owner = TerritoryOwnership.OwnerOf(t);
                    if (owner == key || (owner >= 0 && owner < 8 && !Alliances.AreHostile(faction, (Faction)owner))) continue;
                    if (_sanctumBlocked.TryGetValue((key, t), out float u) && now < u) continue;
                    float sc = -math.distance(origin.xz, p.xz);
                    if (sc > bestC) { bestC = sc; bestT = t; bestP = p; }
                }
                if (bestT >= 0)
                {
                    if (TryNearestCurseNodeIn(em, bestT, false, faction, origin, out float3 node)) bestP = node;
                    _sanctumBlocked[(key, bestT)] = now + Cfg.sanctumRetrySeconds;
                    territory = bestT; pos = bestP;
                    why = $"clear the curse off the Sanctum {RegionMap.NameOf(bestT)} (the Sanctum road)";
                    return true;
                }
            }
            if (best < 0)
            {
                LogSanctum(faction, now, "no Sanctum for this wave: none left to take (ours, an ally's, cursed or tried lately)");
                return false;
            }

            float dSanctum = math.distance(origin.xz, bestPos.xz);
            float dBase = hasBaseTarget ? math.distance(origin.xz, baseTargetPos.xz) : float.MaxValue;
            if (!sanctumRoad && hasBaseTarget && dSanctum > dBase + Cfg.sanctumObjectivePreferMeters)
            {
                LogSanctum(faction, now,
                    $"no Sanctum for this wave: {RegionMap.NameOf(best)} is {dSanctum:0} m away, the base target {dBase:0} m");
                return false;
            }

            _sanctumBlocked[(key, best)] = now + Cfg.sanctumRetrySeconds;
            territory = best; pos = bestPos;
            why = $"take the Sanctum {RegionMap.NameOf(best)}{(bestHostile ? " from a rival" : "")}" +
                  (sanctumRoad ? " (the Sanctum road)" : $" ({dSanctum:0} m, the base target {(hasBaseTarget ? $"{dBase:0} m" : "none")})");
            return true;
        }

        /// <summary>Is <paramref name="t"/> a Sanctum this faction may still take:
        /// not ours, not an ally's, not cursed?</summary>
        private static bool SanctumStillToTake(Faction faction, int t)
        {
            int owner = TerritoryOwnership.OwnerOf(t);
            if (owner == (int)faction) return false;
            if (owner >= 0 && owner < 8 && !Alliances.AreHostile(faction, (Faction)owner)) return false;
            return !TheWaningBorder.Systems.Border.CurseGroundSystem.TerritoryHasCurse(t);   // gains for nobody (§ 6.9.4)
        }

        // ── helpers ─────────────────────────────────────────────────────

        /// <summary>Our combat units standing in territory <paramref name="t"/>
        /// that a garrison may take (mission members included — the garrison
        /// comes out of the wave), nearest to <paramref name="to"/> first.</summary>
        private void CollectOursInTerritory(EntityManager em, Faction faction, int t, float3 to)
        {
            _sanctumFree.Clear();
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
            {
                Entity e = roster.E[i];
                if (roster.UnderConstruction(i) || roster.Verb(i) || roster.NoControl(i)) continue;
                if (IsClaimSquadMember(e)) continue;               // a garrison, a claim squad, an economy responder
                if (em.HasComponent<ShardrootTag>(e)) continue;    // the bearer has its own road
                if (em.HasComponent<SiegeTag>(e)) continue;        // engines garrison nothing (§ 6x)
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                var p = roster.P[i];
                if (RegionMap.RegionAt(p.x, p.z) != t) continue;
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

        /// <summary>Free combat units (nobody else's), nearest to <paramref name="to"/>
        /// first. The monument squads draft from it (SimpleAISystem.Monuments.cs).</summary>
        private void CollectSanctumFree(EntityManager em, Faction faction, float3 to)
        {
            _sanctumFree.Clear();
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
            {
                Entity e = roster.E[i];
                if (roster.UnderConstruction(i) || roster.Verb(i) || roster.NoControl(i)) continue;
                if (IsClaimSquadMember(e) || IsEnrolledInMission(faction, e)) continue;
                if (em.HasComponent<ShardrootTag>(e)) continue;
                if (em.HasComponent<SiegeTag>(e)) continue;
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

        /// <summary>Take <paramref name="e"/> out of every mission it serves.</summary>
        private void RemoveFromMissions(Faction faction, Entity e)
        {
            var missions = MissionsFor(faction);
            for (int m = 0; m < missions.Count; m++)
            {
                int idx = missions[m].Members.IndexOf(e);
                if (idx >= 0) missions[m].Members.RemoveAt(idx);
            }
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
