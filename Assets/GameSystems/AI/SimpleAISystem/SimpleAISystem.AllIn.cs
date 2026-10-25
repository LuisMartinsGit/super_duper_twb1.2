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
            { _allInGuard[i] = -1; _allInLogged[i] = false; _prey[i] = -1; _maxedLogged[i] = false; _finish[i] = -1; }
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
            bool holderHeld = TryHostileShardrootHolder(em, faction, out var holder);
            bool holderYields = holderHeld && beaten && holder != beatenF
                && WarShelved(faction, holder, now) && !HolderAscending(em, holder);
            if (holderHeld && !holderYields)
            {
                prey = (int)holder;
                if (prey != _prey[k])
                    AILogger.Log(faction, "HUNT", $"{holder} holds the Shardroot — everyone against it, all-in");
            }
            else if (beaten)
            {
                prey = (int)beatenF;
                if (prey != _finish[k])
                    AILogger.Log(faction, "FINISH",
                        $"{beatenF} is beaten ({beatenWhy}) — every wave for its capital until it falls" +
                        (holderYields ? $"; {holder}'s Shardroot siege is shelved" : ""));
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
            if (!lateGame && prey < 0)
            {
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
            if (ratio <= 0f || !AllInArmed(faction)) return ratio;
            // FINISHING (§ 6u): the army holds a winning fight longer still.
            if (FinishingVictim(faction, out _))
                return math.max(ratio, math.max(Cfg.allInRetreatRatio, Cfg.finishRetreatRatio));
            return math.max(ratio, Cfg.allInRetreatRatio);
        }
    }
}
