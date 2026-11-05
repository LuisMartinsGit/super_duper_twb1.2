// SimpleAISystem.AllIn.cs
// THE LATE-GAME ALL-IN (2026-10-08, docs/Design/Game_AI.md § 6n).
//
// Developer: "Late game comes at 30 minutes; from there onward players are
// expected to start falling." — "I expect 70-90% army commitment on late
// game." The 2026-10-08 8-player batch launched waves of a third of the army
// while the rest stood as floor, wall guards, claim squads and held
// reinforcements, and armies fell back the moment a fight turned.
//
// From allInAfterSeconds of match time the doctrine arms (once per match,
// logged "ALL-IN: ..."). This file holds the one switch and the home guard;
// each hold it relaxes reads AllInArmed() at its own site:
//   * StandingArmyFloor        -> the home guard (Military.cs)
//   * TickAttackWaves / TryLaunchAttack -> bar, Defend veto, strength hold
//   * ReinforceActiveWave      -> no company hold
//   * EnsureTerritoryClaim     -> squads released, no new rounds (Expansion.cs)
//   * TryClearAdjacentCurse    -> no idle curse sorties (CurseClear.cs)
//   * TickWallGuard            -> no posting (Posture.cs)
//   * EvaluatePosture          -> Defend keeps the field army out
//   * both retreat tests       -> at least allInRetreatRatio
//
// THE HUNT (2026-10-09, § 6o). Developer: "I need deaths to start occurring
// from minute 15 or even earlier" — graduated pressure, ~45-minute matches.
// From preyAfterSeconds a hostile holding at most preyMaxTerritories — or at
// most preyMaxShareOfHunter of the hunter's own territories — is PREY;
// a faction holding at least preyHunterMinTerritories that sees prey goes to
// war on the nearest one and arms the doctrine early, against it alone.
//
// Host-side AI state (AI acts only through CommandRouter), match-relative
// clock, reset per match by SimCadence.Epoch.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data.AI;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>Per faction: the home guard the all-in keeps, or -1 while
        /// the doctrine is not armed. Static because StandingArmyFloor is.</summary>
        private static readonly int[] _allInGuard = NewAllInGuard();
        /// <summary>Per faction: the doctrine has armed this match (log once).</summary>
        private static readonly bool[] _allInLogged = new bool[ProfileSlots];
        private static readonly bool[] _maxedLogged = new bool[ProfileSlots];

        /// <summary>§ 6q: population at maxedPopShare of the cap, or
        /// maxedBankSupplies banked.</summary>
        private static bool IsMaxed(EntityManager em, Faction faction)
        {
            if (Cfg.maxedPopShare > 0f
                && TheWaningBorder.Economy.PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int cap)
                && cap > 0 && pop >= cap * Cfg.maxedPopShare)
                return true;
            return Cfg.maxedBankSupplies > 0
                && TheWaningBorder.Economy.FactionEconomy.TryGetResources(em, faction, out var bank)
                && bank.Supplies >= Cfg.maxedBankSupplies;
        }

        /// <summary>§ 6p: a hostile PLAYER holds the Shardroot (carrier,
        /// Shardbound King or enshrining Temple). Never the curse.</summary>
        private static bool TryHostileShardrootHolder(EntityManager em, Faction faction, out Faction holder)
        {
            holder = faction;
            if (AIShardroot.Locate(em, faction, out Entity e, out _) != AIShardroot.Where.Hostile) return false;
            if (e == Entity.Null || !em.HasComponent<FactionTag>(e)) return false;
            var f = em.GetComponentData<FactionTag>(e).Value;
            if (f == Faction.Border || f == faction) return false;
            holder = f;
            return true;
        }

        /// <summary>§ 6p: the wave's objective when its victim holds the
        /// Shardroot — the holder itself (its position is public: the beacon).</summary>
        private static bool TryShardrootObjective(EntityManager em, Faction faction, Faction victim,
            out Entity target, out float3 pos)
        {
            target = Entity.Null; pos = default;
            if (AIShardroot.Locate(em, faction, out Entity e, out float3 p) != AIShardroot.Where.Hostile) return false;
            if (e == Entity.Null || !em.HasComponent<FactionTag>(e) || em.GetComponentData<FactionTag>(e).Value != victim)
                return false;
            target = e; pos = p;
            return true;
        }

        /// <summary>Per faction: the prey it hunts this think, or -1.</summary>
        private static readonly int[] _prey = NewAllInGuard();
        /// <summary>Per faction: the BEATEN faction it is finishing (§ 6u), or -1.</summary>
        private static readonly int[] _finish = NewAllInGuard();
        private static readonly float[] _finishSince = new float[ProfileSlots];
        private static int _allInEpoch = -1;

        private static int[] NewAllInGuard()
        {
            var a = new int[ProfileSlots];
            for (int i = 0; i < a.Length; i++) a[i] = -1;
            return a;
        }

        private static void ResetAllInIfNewMatch()
        {
            if (_allInEpoch == SimCadence.Epoch) return;
            _allInEpoch = SimCadence.Epoch;
            for (int i = 0; i < ProfileSlots; i++)
            { _allInGuard[i] = -1; _allInLogged[i] = false; _prey[i] = -1; _maxedLogged[i] = false; _finish[i] = -1;
              _threatMode[i] = false; _threatPrev[i] = -1; _fortify[i] = false; }
        }

        /// <summary>Is the late-game all-in armed for this faction? Valid
        /// after <see cref="TickAllInDoctrine"/> ran this think.</summary>
        private static bool AllInArmed(Faction faction)
        {
            int k = (int)faction;
            return k >= 0 && k < ProfileSlots && _allInGuard[k] >= 0;
        }

        /// <summary>
        /// Once per think, before anything drafts: arm the doctrine past
        /// allInAfterSeconds and size the home guard — the share of the army
        /// the personality does NOT commit (its allInCommitment), never fewer
        /// than allInHomeGuardMinUnits.
        /// </summary>
        private void TickAllInDoctrine(EntityManager em, Faction faction,
            AISettingsSO.PersonalityBlock personality, float now)
        {
            ResetAllInIfNewMatch();
            int k = (int)faction;
            if (k < 0 || k >= ProfileSlots) return;
            _warClock = now;
            // FINISH THE BEATEN (§ 6u, 2026-10-25): a hostile clearly beaten
            // by what this faction has SEEN is finished — ahead of the hunt
            // and the pile-on, and ahead of a Shardroot siege that has been
            // beaten off (shelved, § 6i) unless an ascension clock runs.
            bool beaten = TryFindBeaten(em, faction, now, out Faction beatenF, out string beatenWhy);
            // EVERYONE AGAINST THE HOLDER (§ 6p, 2026-10-09) outranks the hunt.
            int prey;
            // A VERDICT HOLDS (2026-11-04): a victim that read as beaten stopped
            // reading so whenever its last soldiers went out of sight, and the push
            // stood down with it. It stays beaten for finishHoldSeconds at least.
            if (!beaten && _finish[k] >= 0 && BoardScore(em, (Faction)_finish[k]) >= 0
                && now - _finishSince[k] < Cfg.finishHoldSeconds)
            {
                beaten = true;
                beatenF = (Faction)_finish[k];
            }
            // THE LOSING PLAYER AND THE TABLE'S THREAT (2026-11-03, § 6x).
            // Developer: "losing players should always attack who is attacking
            // them and fortify"; everyone else turns on a faction that is
            // winning the game (six cases, TryFindThreat) and goes back to its
            // old war when the threat passes (TrackThreatWar).
            bool losing = TryLosingTo(em, faction, now, out Faction attacker, out float pressureOn, out float pressureBack);
            if (losing != _fortify[k])
            {
                _fortify[k] = losing;
                AILogger.Log(faction, "LOSING", losing
                    ? $"under {attacker}'s attack (pressure {pressureOn:0} taken vs {pressureBack:0} dealt back) — every wave on {attacker}, fortifying"
                    : "no longer losing — fortification stands down");
            }
            Faction threatF = faction; int threatCases = 0; string threatWhy = null;
            bool threatFound = !losing && TryFindThreat(em, faction, now, out threatF, out threatCases, out threatWhy);
            bool holderHeld = TryHostileShardrootHolder(em, faction, out var holder);
            bool holderYields = holderHeld && beaten && holder != beatenF
                && WarShelved(faction, holder, now) && !HolderAscending(em, holder);
            if (losing)
            {
                prey = (int)attacker;
            }
            else if (threatFound)
            {
                prey = (int)threatF;
                if (prey != _prey[k])
                    AILogger.Log(faction, "THREAT", $"{threatF} is winning the game — {threatCases} case(s): {threatWhy} — everyone against it");
            }
            else if (holderHeld && !holderYields)
            {
                prey = (int)holder;
                if (prey != _prey[k])
                    AILogger.Log(faction, "HUNT", $"{holder} holds the Shardroot — everyone against it, all-in");
            }
            else if (beaten)
            {
                prey = (int)beatenF;
                if (prey != _finish[k])
                {
                    _finishSince[k] = now;
                    AILogger.Log(faction, "FINISH",
                        $"{beatenF} is beaten ({beatenWhy}) — every wave for its capital until it falls" +
                        (holderYields ? $"; {holder}'s Shardroot siege is shelved" : ""));
                }
            }
            else
            {
                prey = TryFindPrey(em, faction, now, out var p) ? (int)p : -1;
                if (prey != _prey[k] && prey >= 0)
                    AILogger.Log(faction, "HUNT",
                        $"{p} holds {TerritoriesHeld(p)} territor{(TerritoriesHeld(p) == 1 ? "y" : "ies")} — " +
                        $"we hold {TerritoriesHeld(faction)}: all-in on it");
            }
            _prey[k] = prey;
            TrackThreatWar(em, faction, k, prey, losing || threatFound, now);
            int finishWas = _finish[k];
            _finish[k] = beaten && prey == (int)beatenF ? prey : -1;
            if (finishWas >= 0 && _finish[k] != finishWas)
                AILogger.Log(faction, "FINISH", $"finishing {(Faction)finishWas} stops" +
                    (BoardScore(em, (Faction)finishWas) < 0 ? " — it is out of the game" : " — it no longer reads as beaten"));
            bool lateGame = Cfg.allInAfterSeconds > 0f && now >= Cfg.allInAfterSeconds;
            // A FULL ARMY ATTACKS (§ 6q): near the population cap, or with a
            // bank far above what production can spend, the doctrine arms.
            if (!lateGame && IsMaxed(em, faction))
            {
                lateGame = true;
                if (!_maxedLogged[k])
                {
                    _maxedLogged[k] = true;
                    AILogger.Log(faction, "ALL-IN", $"the army is full or the bank overflows at {(int)now}s — it marches");
                }
            }
            if ((!lateGame && prey < 0) || losing)
            {
                // A losing player does not all-in on its attacker: it answers
                // with ordinary waves and keeps its home guard (§ 6x).
                _allInGuard[k] = -1;
                return;
            }

            float commit = math.saturate(personality != null ? personality.allInCommitment : 0f);
            int alive = CountAliveMilitary(em, faction);
            int guard = math.max(math.max(0, Cfg.allInHomeGuardMinUnits),
                (int)math.ceil(alive * (1f - commit)));
            _allInGuard[k] = guard;

            if (!_allInLogged[k] && lateGame && now >= Cfg.allInAfterSeconds)
            {
                _allInLogged[k] = true;
                AILogger.Log(faction, "ALL-IN",
                    $"armed at {(int)now}s — commit {commit * 100f:0}% of the army " +
                    $"({personality?.personality}), home guard {guard} of {alive}; " +
                    "wall guards and claim squads released, reinforcements stream, " +
                    $"retreat only past x{Cfg.allInRetreatRatio:0.0}");
            }
        }

        /// <summary>The prey this faction hunts this think (§ 6o), if any.</summary>
        private static readonly bool[] _threatMode = new bool[ProfileSlots];
        private static readonly int[] _threatPrev = new int[ProfileSlots];
        private static readonly bool[] _fortify = new bool[ProfileSlots];

        /// <summary>Is this faction under a stronger attacker's pressure (§ 6x)?
        /// The wall ladder and the savings hold step aside for it.</summary>
        public static bool FortifyingFor(Faction faction)
        {
            int k = (int)faction;
            return k >= 0 && k < ProfileSlots && _fortify[k];
        }

        /// <summary>The losing player (§ 6x): the faction that hurt us most over
        /// losingPressureWindowSeconds (kills x1, razes x4 — the pressure
        /// ledger), at least losingPressureMinWeight and losingPressureRatio
        /// times what we dealt it back.</summary>
        private static bool TryLosingTo(EntityManager em, Faction faction, float now,
            out Faction attacker, out float on, out float back)
        {
            float window = math.max(30f, Cfg.losingPressureWindowSeconds);
            attacker = TheWaningBorder.Core.Diagnostics.FactionPressureLedger.TopAttacker(faction, window, now, out on);
            back = 0f;
            if (attacker == Faction.Border || (int)attacker < 0 || (int)attacker >= ProfileSlots) return false;
            if (on < Cfg.losingPressureMinWeight) return false;
            if (!Alliances.AreHostile(faction, attacker) || BoardScore(em, attacker) < 0) return false;
            back = TheWaningBorder.Core.Diagnostics.FactionPressureLedger.PressureOn(attacker, faction, window, now);
            return on >= back * math.max(1f, Cfg.losingPressureRatio);
        }

        private static readonly ComponentType[] QT_ShardboundKings =
        {
            ComponentType.ReadOnly<TheWaningBorder.Entities.ShardboundKing>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        private static CachedEntityQuery QC_ShardboundKings;

        /// <summary>THE TABLE'S THREAT (§ 6x, developer's six cases): a hostile
        /// faction that (1) holds more territories than the living players'
        /// mean plus threatTerritorySigma deviations, (2) has eliminated a
        /// player (top damager of its last ten minutes), (3) runs the
        /// enshrinement countdown, (4) has a Shardbound King, (6) runs the
        /// Sanctum dominion countdown — and (5) the more cases, the sooner;
        /// ties go to the nearest.</summary>
        private bool TryFindThreat(EntityManager em, Faction faction, float now,
            out Faction threat, out int cases, out string why)
        {
            threat = faction; cases = 0; why = null;
            if (!Cfg.threatRefocusEnabled) return false;
            if (now < Cfg.threatAfterSeconds) return false;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return false;

            var held = new int[ProfileSlots];
            int n = 0; float sum = 0f, sumSq = 0f;
            for (int f = 0; f < ProfileSlots; f++)
            {
                var v = (Faction)f;
                held[f] = -1;
                if (v == Faction.Border || BoardScore(em, v) < 0) continue;
                held[f] = TerritoriesHeld(v);
                n++; sum += held[f]; sumSq += held[f] * (float)held[f];
            }
            float mean = n > 0 ? sum / n : 0f;
            float sd = n > 1 ? math.sqrt(math.max(0f, sumSq / n - mean * mean)) : 0f;

            Faction asc = Faction.Border, dom = Faction.Border;
            TheWaningBorder.Systems.Border.ShardrootSystem.TryAscensionCountdown(em, out asc, out _);
            TheWaningBorder.Systems.Economy.SanctumSystem.TryCountdown(em, out dom, out _);
            var kings = new bool[ProfileSlots];
            {
                var q = QC_ShardboundKings.Get(em, QT_ShardboundKings);
                if (!q.IsEmptyIgnoreFilter)
                {
                    using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                    for (int i = 0; i < facs.Length; i++)
                        if ((int)facs[i].Value >= 0 && (int)facs[i].Value < ProfileSlots) kings[(int)facs[i].Value] = true;
                }
            }

            float3 home = HomeAnchor(em, faction);
            int bestCases = 0; float bestD = float.MaxValue;
            for (int f = 0; f < ProfileSlots; f++)
            {
                var v = (Faction)f;
                if (v == faction || held[f] < 0 || !Alliances.AreHostile(faction, v)) continue;
                int c = 0;
                var sb = new System.Text.StringBuilder(96);
                if (sd > 0f && held[f] >= math.max(1, Cfg.threatMinTerritories)
                    && held[f] > mean + math.max(0f, Cfg.threatTerritorySigma) * sd)
                { c++; sb.Append($"{held[f]} territories (table mean {mean:0.0} +{Cfg.threatTerritorySigma:0.#} sd {sd:0.0})"); }
                int elim = TheWaningBorder.Core.Diagnostics.FactionPressureLedger.EliminationsBy(v);
                if (elim > 0) { if (c > 0) sb.Append(", "); c++; sb.Append($"eliminated {elim} player(s)"); }
                if (asc == v) { if (c > 0) sb.Append(", "); c++; sb.Append("enshrinement countdown"); }
                if (kings[f]) { if (c > 0) sb.Append(", "); c++; sb.Append("a Shardbound King"); }
                if (dom == v) { if (c > 0) sb.Append(", "); c++; sb.Append("Sanctum dominion countdown"); }
                if (c == 0) continue;
                float d = TryVictimGround(em, v, home, new float3(float.MaxValue, 0f, float.MaxValue), out float3 at, out _)
                    ? math.distance(at.xz, home.xz) : float.MaxValue;
                if (c > bestCases || (c == bestCases && d < bestD))
                { bestCases = c; bestD = d; threat = v; cases = c; why = sb.ToString(); }
            }
            return bestCases > 0;
        }

        /// <summary>Remember the war a refocus interrupted and go back to it
        /// when the threat (or the losing spell) passes (§ 6x).</summary>
        private void TrackThreatWar(EntityManager em, Faction faction, int k, int prey, bool refocus, float now)
        {
            EnsureWarEpoch();
            if (refocus && !_threatMode[k])
            {
                _threatMode[k] = true;
                _threatPrev[k] = _wars.TryGetValue(k, out var w) && (int)w.Victim != prey ? (int)w.Victim : -1;
            }
            else if (!refocus && _threatMode[k])
            {
                _threatMode[k] = false;
                int prev = _threatPrev[k];
                _threatPrev[k] = -1;
                var pv = (Faction)prev;
                if (prev >= 0 && prev < ProfileSlots && BoardScore(em, pv) >= 0 && Alliances.AreHostile(faction, pv))
                {
                    _wars[k] = new War { Victim = pv, Since = now };
                    AILogger.Log(faction, "WAR", $"the threat has passed — back to the war on {pv}");
                }
                else _wars.Remove(k);
            }
        }

        private static bool HuntedPrey(Faction faction, out Faction prey)
        {
            int k = (int)faction;
            prey = faction;
            if (k < 0 || k >= ProfileSlots || _prey[k] < 0) return false;
            prey = (Faction)_prey[k];
            return true;
        }

        /// <summary>
        /// THE HUNT (§ 6o): past preyAfterSeconds, with this faction holding
        /// at least preyHunterMinTerritories, the nearest hostile player (to
        /// our capital, by its nearest held territory) holding at most
        /// preyMaxTerritories. Territory ownership is public, so no sighting
        /// is needed. Never the curse.
        /// </summary>
        private bool TryFindPrey(EntityManager em, Faction faction, float now, out Faction prey)
        {
            prey = faction;
            if (Cfg.preyAfterSeconds <= 0f || now < Cfg.preyAfterSeconds) return false;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready) return false;
            int mine = TerritoriesHeld(faction);
            if (mine < math.max(1, Cfg.preyHunterMinTerritories)) return false;
            int bar = math.max(Cfg.preyMaxTerritories,
                (int)math.floor(mine * math.max(0f, Cfg.preyMaxShareOfHunter)));
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<Unity.Transforms.LocalTransform>(hall)) return false;
            float3 home = em.GetComponentData<Unity.Transforms.LocalTransform>(hall).Position;
            // NEIGHBOURS FIRST (2026-11-03, § 6x / point 7): the prey must sit
            // within preyMaxDistanceRatio of the nearest hostile's ground, so
            // a strong neighbour is never ignored for a small faction far away.
            float nearestAny = float.MaxValue;
            for (int f = 0; f < ProfileSlots; f++)
            {
                var v = (Faction)f;
                if (v == faction || v == Faction.Border || !Alliances.AreHostile(faction, v)) continue;
                if (BoardScore(em, v) < 0) continue;
                if (TryVictimGround(em, v, home, new float3(float.MaxValue, 0f, float.MaxValue), out float3 g, out _))
                    nearestAny = math.min(nearestAny, math.distance(g.xz, home.xz));
            }
            float preyReach = nearestAny < float.MaxValue ? nearestAny * math.max(1f, Cfg.preyMaxDistanceRatio) : float.MaxValue;
            float best = float.MaxValue;
            for (int f = 0; f < ProfileSlots; f++)
            {
                var v = (Faction)f;
                if (v == faction || v == Faction.Border || !Alliances.AreHostile(faction, v)) continue;
                if (BoardScore(em, v) < 0) continue;
                int held = TerritoriesHeld(v);
                if (held <= 0 || held > bar) continue;
                if (WarShelved(faction, v, now)) continue;   // beat off our last war (§ 6i)
                if (!TryVictimGround(em, v, home, new float3(float.MaxValue, 0f, float.MaxValue),
                        out float3 at, out _)) continue;
                float d = math.distancesq(at.xz, home.xz);
                if (preyReach < float.MaxValue && d > preyReach * preyReach) continue;
                if (d < best || (d == best && f < (int)prey)) { best = d; prey = v; }
            }
            return best < float.MaxValue;
        }

        /// <summary>Is <paramref name="faction"/> finishing a beaten faction
        /// (§ 6u)? Valid after <see cref="TickAllInDoctrine"/> ran this think.</summary>
        private static bool FinishingVictim(Faction faction, out Faction victim)
        {
            int k = (int)faction;
            victim = faction;
            if (k < 0 || k >= ProfileSlots || _finish[k] < 0) return false;
            victim = (Faction)_finish[k];
            return true;
        }

        /// <summary>An ascension countdown runs for <paramref name="holder"/>'s
        /// enshrining Temple (Curse_And_Shardroot.md § 3.1c): never shelved.</summary>
        private static bool HolderAscending(EntityManager em, Faction holder)
            => TheWaningBorder.Systems.Border.ShardrootSystem.TryAscensionCountdown(em, out var temple, out _)
               && temple == holder;

        /// <summary>
        /// FINISH THE BEATEN (§ 6u, 2026-10-25). Developer: "AI does not fully
        /// pursue players. Both human players in this game had a terrible
        /// performance and the AI never moved in to finish them off." Past
        /// finishAfterSeconds, with an army of at least finishMinArmy, the
        /// nearest hostile player that is beaten BY WHAT THIS FACTION KNOWS:
        ///   * its territories (public) at most finishTerritoryShare of ours,
        ///   * the summed strength of its soldiers SEEN in the last
        ///     finishIntelMaxAgeSeconds at most finishArmyShare of our army's,
        ///   * and something of it seen in that window (fresh contact — no
        ///     verdict on a faction nobody has looked at lately).
        /// Both shares are divided by the tier's strengthWaveRatioScale, so a
        /// cautious tier waits for a weaker victim and Expert finishes sooner.
        /// A faction that beat off our last war (shelved, § 6i) is skipped.
        /// Never the curse; never an ally.
        /// </summary>
        private bool TryFindBeaten(EntityManager em, Faction faction, float now,
            out Faction victim, out string why)
        {
            victim = faction; why = null;
            if (ProfileOf(faction).SpareCapitals != 0) return false;   // Easy never goes for the kill
            if (Cfg.finishAfterSeconds <= 0f || now < Cfg.finishAfterSeconds) return false;
            if (!TheWaningBorder.World.Regions.RegionMap.Ready || !HasAgedUp(em, faction)) return false;
            if (CountAliveMilitary(em, faction) < math.max(1, Cfg.finishMinArmy)) return false;
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<Unity.Transforms.LocalTransform>(hall)) return false;
            float3 home = em.GetComponentData<Unity.Transforms.LocalTransform>(hall).Position;
            Entity brain = FindBrainEntity(em, faction);
            if (brain == Entity.Null || !em.HasBuffer<EnemySightingRecord>(brain)) return false;

            float scale = 1f / math.max(0.1f, ProfileOf(faction).StrengthWaveRatioScale);
            int mineLand = TerritoriesHeld(faction);
            int mineArmy = TacticalQuery.FactionStrengthInRadius(em, faction, float3.zero, 1e5f);
            float landBar = mineLand * math.max(0f, Cfg.finishTerritoryShare) * scale;
            float armyBar = mineArmy * math.max(0f, Cfg.finishArmyShare) * scale;
            float maxAge = math.max(1f, Cfg.finishIntelMaxAgeSeconds);
            float simNow = (float)TheWaningBorder.Core.SimClock.Now;
            var buf = em.GetBuffer<EnemySightingRecord>(brain);

            float best = float.MaxValue;
            for (int f = 0; f < ProfileSlots; f++)
            {
                var v = (Faction)f;
                if (v == faction || v == Faction.Border || !Alliances.AreHostile(faction, v)) continue;
                if (BoardScore(em, v) < 0) continue;            // eliminated (announced)
                if (WarShelved(faction, v, now)) continue;
                int land = TerritoriesHeld(v);
                if (land > landBar) continue;
                bool fresh = false;
                for (int i = 0; i < buf.Length && !fresh; i++)
                    fresh = buf[i].OwnerFaction == v && simNow - buf[i].LastSeenTime <= maxAge;
                if (!fresh) continue;
                KnownArmyOf(em, brain, v, maxAge, out int seenUnits, out int seenPower);
                if (seenPower > armyBar) continue;
                // Nearest by its capital as last seen, else its start (public),
                // else its nearest held territory (public).
                float3 at;
                if (!TryNearestHallSighting(em, brain, v, home, out at, out _, out _))
                {
                    Entity sh = FindEnemyStartHall(em, faction, home, v);
                    if (sh != Entity.Null && em.HasComponent<Unity.Transforms.LocalTransform>(sh))
                        at = em.GetComponentData<Unity.Transforms.LocalTransform>(sh).Position;
                    else if (!TryVictimGround(em, v, home, new float3(float.MaxValue, 0f, float.MaxValue), out at, out _))
                        continue;
                }
                float d = math.distancesq(at.xz, home.xz);
                if (d >= best) continue;
                best = d; victim = v;
                why = $"{land} territor{(land == 1 ? "y" : "ies")} vs our {mineLand}, " +
                      $"seen army {seenPower} ({seenUnits} unit(s)) vs our {mineArmy}";
            }
            return best < float.MaxValue;
        }

        /// <summary>The retreat ratio a mission uses: under the all-in, at
        /// least allInRetreatRatio (a committed army falls back only when it
        /// is badly outmatched). A ratio of 0 means "never retreats" and is
        /// kept.</summary>
        private static float AllInRetreatRatio(Faction faction, float ratio)
        {
            if (ratio <= 0f) return ratio;
            // FINISHING (§ 6u): the army holds a winning fight, all-in or not.
            if (FinishingVictim(faction, out _))
                return math.max(ratio, math.max(Cfg.allInRetreatRatio, Cfg.finishRetreatRatio));
            if (!AllInArmed(faction)) return ratio;
            return math.max(ratio, Cfg.allInRetreatRatio);
        }
    }
}
