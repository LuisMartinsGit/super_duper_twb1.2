// AIBuildingUpgradeSystem.cs
// Culture-agnostic AI driver for the building upgrade system.
//
// Each AI brain that's Era >= 2 with a non-None culture picks ONE
// upgradeable building per tick (slowest cadence so it doesn't dominate
// the build queue) and tries UpgradeBuildingCommandHelper.Execute on it.
// The walk is a round-robin over PriorityOrder (every
// line the faction can own, choice buildings and the wall hub included)
// so the AI eventually levels EVERYTHING to L3.
//
// Reserves a small buffer of resources before upgrading so upgrades
// don't bankrupt the AI mid-rush. Reserves are loose — if the AI
// genuinely can't afford it, the command helper rejects gracefully.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIBuildingUpgradeSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_AIBrain =
        {
            ComponentType.ReadOnly<AIBrain>(),
        };
        static CachedEntityQuery QC_AIBrain;

        static readonly ComponentType[] QT_HallTagFactionTagFactionProgress =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagFactionProgress;

        static readonly ComponentType[] QT_HallTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_HallTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_BarracksTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<BarracksTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_BarracksTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_HutTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<HutTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_HutTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_ArcheryRangeTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<ArcheryRangeTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_ArcheryRangeTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_RoyalStableTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<RoyalStableTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_RoyalStableTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_SiegeYardTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<SiegeYardTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_SiegeYardTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_WatchTowerTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<WatchTowerTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_WatchTowerTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_VaultTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<VaultTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_VaultTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_WallHubTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<WallHubTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_WallHubTagBuildingUpgradeableFactionTag;

        // Exclude via ComponentType.Exclude — same query the old
        // EntityQueryBuilder built, but built ONCE. The builder ran
        // .Build(em) on every upgrade tick, which registers a fresh query
        // with the world each time — the exact leak the 2026-09-03 sweep
        // removed everywhere else, hidden behind different spelling.
        static readonly ComponentType[] QT_GathererHutUpgradeableNotRaider =
        {
            ComponentType.ReadOnly<GathererHutTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<RaiderCampTag>(),
        };
        static CachedEntityQuery QC_GathererHutUpgradeableNotRaider;

        static readonly ComponentType[] QT_TradingOutpostUpgradeable =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_TradingOutpostUpgradeable;

        #endregion

        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIBuildingUpgradeSystem.asset now.</summary>
        static AIBuildingUpgradeSystemConfig Cfg => AIBuildingUpgradeSystemConfig.I;

        #endregion



        // Priority order — cheapest, highest-impact first.
        //
        // GATHERERSHUT IS FIRST, and it was missing entirely until
        // 2026-08-07: across every logged match the AI upgraded Hall,
        // Barracks and Hut and NEVER ONCE upgraded a Gatherer's Hut. That is
        // the most valuable upgrade in the game for Alanthor and it was
        // simply not on the list:
        //   * the guild-level ladder adds +5 / +10 / +20 supplies per tick,
        //   * the Survey techs' iron / veilstone / VEILSTEEL drips all scale
        //     with it — and the veilsteel drip requires a FULLY upgraded hut,
        //     so an un-upgraded economy can never produce veilsteel at all,
        //   * levelling gives the hut the HP to survive a raid long enough
        //     for reinforcements to arrive.
        // Researching the Survey ladder while leaving huts at L1 buys the
        // techs and throws away most of what they pay for.
        //
        // 2026-08-09 (log-proven, 47-min match): a FIXED walk of this list let
        // the hut line monopolize the loop — Blue kept founding new L1 huts,
        // so "lowest-level GatherersHut" always existed, the Hall saw its
        // first level at minute 24, and the Archery Range / Royal Stable /
        // Siege Yard / Watch Tower NEVER levelled (they were not even listed).
        // The walk is now a ROUND-ROBIN start index across the list so every
        // line gets a turn.
        // 2026-08-10 (endgame completeness): the choice buildings
        // (VaultOfAlmierra — carries BuildingUpgradeable
        // and have cost rows) and the Wall hub line join the rotation so the
        // AI eventually levels EVERYTHING it owns. The wall entry is
        // forward-wired: hubs don't carry BuildingUpgradeable or a
        // BuildingUpgradeConfig cost row yet, so it no-ops until the wall
        // ladder ships — wall Tower/Gate CONVERSIONS stay with
        // WallUpgradeSystem and are NOT driven from here.
        private static readonly string[] PriorityOrder =
            { "GatherersHut", "Fortress", "Barracks", "Hut", "ArcheryRange",
              "Alanthor_RoyalStable", "Alanthor_SiegeYard", "Alanthor_Tower",
              "VaultOfAlmierra", "Alanthor_Wall" };


        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            // Snapshot brains — we make structural changes (BuildingUpgrading
            // gets added) so we can't iterate via SystemAPI.Query.
            var brainQuery = QC_AIBrain.Get(em, QT_AIBrain);
            using var brainEntities = brainQuery.ToEntityArray(Allocator.Temp);

            for (int b = 0; b < brainEntities.Length; b++)
            {
                var brainEntity = brainEntities[b];
                if (!em.Exists(brainEntity)) continue;
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;

                Faction faction = brain.Owner;

                // Per-brain throttle, scaled by difficulty.
                var profile = AISimpleDifficulty.GetProfile(brain.Difficulty);
                float think = Cfg.thinkInterval * profile.SupportThinkScale;

                if (em.HasComponent<AIBuildingUpgradeTickState>(brainEntity))
                {
                    var tick = em.GetComponentData<AIBuildingUpgradeTickState>(brainEntity);
                    if (time < tick.NextThinkTime) continue;
                    tick.NextThinkTime = time + think;
                    em.SetComponentData(brainEntity, tick);
                }
                else
                {
                    em.AddComponentData(brainEntity, new AIBuildingUpgradeTickState
                    {
                        NextThinkTime = time + think,
                    });
                    continue; // skip first tick
                }

                // Era 2+ + culture picked? UpgradeBuildingCommandHelper does
                // the same gate but rejecting at this layer cuts query cost.
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;
                if (!em.HasComponent<FactionEra>(bank)) continue;
                if (em.GetComponentData<FactionEra>(bank).Value < 2) continue;
                if (!HasCulture(em, faction)) continue;

                // Reserve check — keep some resources for non-upgrade use.
                //
                // The ECONOMY ENGINE IS EXEMPT (2026-08-18, log-proven): a
                // Guild level is what RAISES supply income (+5/+10/+20 a
                // tick), so gating it behind a supply floor is a death
                // spiral — the 40-minute log ends with every faction under
                // 40 supplies, thousands of banked iron and veilstone, four
                // Guild upgrades across the whole match, and the research
                // ladder and sect adoption both stalled on empty wallets.
                // Below the floor the pass still runs, but only for the
                // engine that ends the shortage; affordability is still
                // enforced by UpgradeBuildingCommandHelper.
                if (!FactionEconomy.TryGetResources(em, faction, out var res)) continue;
                bool reservesOk = res.Supplies  >= Cfg.reserveSupplies
                               && res.Iron      >= Cfg.reserveIron
                               && res.Veilstone >= Cfg.reserveVeilstone;

                // SURPLUS (2026-10-04, Game_AI.md 5e): an overflowing bank
                // queues several level-ups a think, not one — the basics cap
                // is hard, so idle supplies and iron go to upgrades. While the
                // army waits on veilstone, a level priced in veilstone must
                // leave the army's earmark in the bank.
                bool overflowing = AIBudget.BankOverflowing(em, faction);
                bool veilstoneHeld = overflowing
                    && AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone);
                int attempts = overflowing ? System.Math.Max(1, Cfg.surplusUpgradesPerThink) : 1;

                // Walk the priority order from a rotating start so no single
                // building line (log-proven: the hut line) monopolizes the loop.
                var tickState = em.GetComponentData<AIBuildingUpgradeTickState>(brainEntity);
                int rotation = tickState.Rotation;
                tickState.Rotation = (byte)((rotation + attempts) % PriorityOrder.Length);
                em.SetComponentData(brainEntity, tickState);

                // The capital's price is a savings goal on tiers that say so
                // (Game_AI.md 5h) -- never while the base is defended.
                bool defending = em.HasComponent<SimpleAIState>(brainEntity)
                    && em.GetComponentData<SimpleAIState>(brainEntity).Posture == AIPosture.Defend;
                // ...and never while the economy is in distress (Game_AI.md
                // § 5i): a collapsed supply income, an economy under attack or
                // a lost extractor owed a rebuild pauses the saving goal, so
                // the trickle goes to the rebuild and the army instead.
                bool reserveCapital = profile.ReserveForCapitalLevel && !defending
                    && !AIPivotalReserve.IsSuspended(faction);
                if (!reserveCapital) AIPivotalReserve.Clear(faction, CapitalReserveKey);

                for (int a = 0; a < attempts; a++)
                    if (!TryUpgradeOne(em, faction, (rotation + a) % PriorityOrder.Length,
                            reservesOk, overflowing, veilstoneHeld, reserveCapital, profile, time))
                        break;

                // ECONOMY LEVELS FIRST (Game_AI.md 5h): a level is income,
                // which is what the army is waiting on.
                for (int e = 0; e < profile.EconomyUpgradesPerThink; e++)
                    if (!TryEconomyUpgrade(em, faction, e, profile.ArmyBeforeSaves))
                        break;
            }
        }

        /// <summary>AIPivotalReserve key of the capital's next level.</summary>
        private const string CapitalReserveKey = "CapitalLevel";

        /// <summary>Seconds between "capital saving" lines per faction (log
        /// cadence, not tuning).</summary>
        private const float CapitalSavingLogSeconds = 60f;

        /// <summary>Per-faction next time the "capital saving" line may be
        /// written (host AI state, main thread).</summary>
        private static readonly System.Collections.Generic.Dictionary<int, float> _nextCapitalSavingLog
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>
        /// One economy level, ahead of the rotation and without yielding to
        /// the army (Game_AI.md 5f / 5h): the Gatherer's Hut (highest first,
        /// so huts reach the L3 survey gate) on even passes, the cheapest
        /// Trading Outpost on odd ones, each falling back to the other.
        /// Respects the savings hold (the capital's price first) and the
        /// army's veilstone earmark.
        /// </summary>
        private static bool TryEconomyUpgrade(EntityManager em, Faction faction, int pass,
            bool incomeOutranksPot)
        {
            bool outpostFirst = (pass & 1) == 1;
            for (int k = 0; k < 2; k++)
            {
                bool outpost = (k == 0) == outpostFirst;
                Entity best = outpost ? CheapestOutpost(em, faction) : HighestHut(em, faction);
                if (best == Entity.Null) continue;
                if (!UpgradeBuildingCommandHelper.TryGetNextCost(em, best, out var cost, out byte next)) continue;
                if (next <= 1) continue;   // L1 is the free age-up level
                // Outside the budget's lump-sum reservation (the Fortress pot,
                // Game_AI.md 5c): a level paid from the bank directly used
                // to spend it on every release of the pivotal hold.
                // ...except on a tier whose economy is what pays for the
                // Fortress (armyBeforeSaves, Game_AI.md 5f): an INCOME level
                // raises the income the pot is filled from, so it passes the
                // pot and the non-strict holds; the bank still has to pay.
                if (incomeOutranksPot)
                {
                    if (!FactionEconomy.CanAfford(em, faction, cost)) continue;
                }
                else
                {
                    if (!AIBudget.CanAffordOutsideReservation(em, faction, cost)) continue;
                    if (AIPivotalReserve.ShouldHold(em, faction, cost)) continue;
                }
                if (!AIBudget.LeavesMilitaryVeilstone(em, faction, cost)) continue;
                // UNITS BEFORE ECONOMY (Game_AI.md 5h): on a tier that says
                // so, an economy level waits while the money could still
                // start units in an idle trainer of an army below target.
                string deferred = AIBudget.EconomyDeferral(em, faction, cost);
                if (deferred != null)
                {
                    AIBudget.NoteEconomyDeferred(faction,
                        "upgrade " + (outpost ? "Alanthor_TradingOutpost" : "GatherersHut"), deferred);
                    return false;
                }
                if (UpgradeBuildingCommandHelper.Execute(em, best,
                        TheWaningBorder.Core.Commands.CommandSource.AI) != UpgradeBuildingResult.Ok) continue;
                string id = outpost ? "Alanthor_TradingOutpost" : "GatherersHut";
                AILogger.Log(faction, "ECON", $"upgrade {id} -> L{next} ({cost.Supplies}s {cost.Iron}i)");
                return true;
            }
            return false;
        }

        private static Entity HighestHut(EntityManager em, Faction faction)
            => PickUpgradeable(em, faction,
                QC_GathererHutUpgradeableNotRaider.Get(em, QT_GathererHutUpgradeableNotRaider), cheapest: false);

        private static Entity CheapestOutpost(EntityManager em, Faction faction)
            => PickUpgradeable(em, faction,
                QC_TradingOutpostUpgradeable.Get(em, QT_TradingOutpostUpgradeable), cheapest: true);

        /// <summary>The highest-level building (or, <paramref name="cheapest"/>,
        /// the one with the cheapest next level) of a query that can be
        /// levelled now: finished, nothing queued, below max.</summary>
        private static Entity PickUpgradeable(EntityManager em, Faction faction, EntityQuery q, bool cheapest)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            Entity best = Entity.Null;
            int bestScore = int.MinValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (UpgradeBuildingCommandHelper.IsUpgradeQueued(em, ents[i])) continue;
                if (!UpgradeBuildingCommandHelper.TryGetNextCost(em, ents[i], out var cost, out byte next)) continue;
                if (next <= 1 || next > BuildingUpgradeConfig.MaxLevel) continue;
                int score = cheapest
                    ? -(cost.Supplies + cost.Iron + cost.Veilstone + cost.Veilsteel)
                    : next;
                if (score > bestScore) { bestScore = score; best = ents[i]; }
            }
            return best;
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static bool HasCulture(EntityManager em, Faction faction)
        {
            var query = QC_HallTagFactionTagFactionProgress.Get(em, QT_HallTagFactionTagFactionProgress);
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.GetComponentData<FactionProgress>(ents[i]).Culture != Cultures.None) return true;
            }
            return false;
        }

        /// <summary>One level-up (or none). True when one was queued.</summary>
        private static bool TryUpgradeOne(EntityManager em, Faction faction, int rotation,
            bool reservesOk, bool surplus, bool veilstoneHeld, bool reserveCapital,
            in AIDifficultyProfile profile, float time)
        {
            // THE HOME CAPITAL FIRST (2026-10-03). King Lexor trains only at
            // a Lv3 capital (levels no longer raise a territory limit —
            // there is none since 2026-10-04). The round-robin gave the
            // capital one turn in ten, the Fortress turn went to the LOWEST
            // Fortress (any new expansion Fortress), and the supply reserve
            // shut it out entirely for a poor faction: the 0.0.33 batch had
            // capitals sitting at L1 for 50 minutes (Red on Sundered Crown,
            // L1 from 11:20 to the end) and 1,333 "King Lexor blocked (needs
            // Lv3 Shelter)" lines, while expansion Fortresses levelled 1->3
            // in under three minutes. Below capitalPriorityLevel the capital
            // is upgraded ahead of the rotation and past the reserves; when
            // the bank cannot pay for it, only the supply engine may level
            // meanwhile, so the price can form.
            switch (TryUpgradeCapital(em, faction, surplus, reserveCapital, profile, time))
            {
                case CapitalUpgrade.Queued:
                    return true;
                case CapitalUpgrade.Saving:
                    return TryUpgradeBuildingType(em, faction, "GatherersHut", surplus, veilstoneHeld,
                        supplyEngine: true);
            }

            // Below the reserve floor only the supply engine is eligible —
            // see the exemption note at the call site.
            // The production lines pass too (2026-10-04, Game_AI.md 5f): the
            // reserve floor keeps money for the army, and a production level
            // is the army's own output — it never yields to it.
            if (!reservesOk)
            {
                if (TryUpgradeBuildingType(em, faction, "GatherersHut", surplus, veilstoneHeld,
                        supplyEngine: true))
                    return true;
                for (int p = 0; p < ProductionLineIds.Length; p++)
                {
                    int idx = (rotation + p) % ProductionLineIds.Length;
                    if (TryUpgradeBuildingType(em, faction, ProductionLineIds[idx], surplus, veilstoneHeld))
                        return true;
                }
                return false;
            }

            for (int p = 0; p < PriorityOrder.Length; p++)
            {
                int idx = (rotation + p) % PriorityOrder.Length;
                if (TryUpgradeBuildingType(em, faction, PriorityOrder[idx], surplus, veilstoneHeld))
                    return true;
            }
            return false;
        }

        private enum CapitalUpgrade : byte { NotNeeded, Queued, Saving }

        /// <summary>The production lines: a level cuts their train time
        /// (BuildingUpgradeConfig.TrainTimeMultiplier), so it raises unit
        /// output and never yields to the army it feeds. A roster table, not
        /// tuning: the ids are the buildings' SO ids.</summary>
        private static bool RaisesUnitOutput(string buildingId)
            => buildingId == "Barracks" || buildingId == "ArcheryRange"
            || buildingId == "Alanthor_RoyalStable" || buildingId == "Alanthor_SiegeYard";

        /// <summary>The same four lines, for the pass below the reserve floor.</summary>
        private static readonly string[] ProductionLineIds =
            { "Barracks", "ArcheryRange", "Alanthor_RoyalStable", "Alanthor_SiegeYard" };

        /// <summary>Level the home capital (the faction's Hall with the
        /// lowest NetworkId — the starting seat, the rule SimpleAISystem and
        /// AIAlanthorEndgameSystem use) while it is below
        /// capitalPriorityLevel.</summary>
        private static CapitalUpgrade TryUpgradeCapital(EntityManager em, Faction faction, bool surplus,
            bool reserveCapital, in AIDifficultyProfile profile, float time)
        {
            if (_capitalHoldEpoch != SimCadence.Epoch)
            {
                _capitalHoldEpoch = SimCadence.Epoch;
                _capitalHoldSince.Clear();
                _capitalRestUntil.Clear();
            }
            int fk = (int)faction;
            var verdict = TryUpgradeCapitalCore(em, faction, surplus, out var home);
            // THE CAPITAL'S PRICE IS A SAVINGS GOAL (Game_AI.md 5h). A rusher
            // spent every supply on units as it arrived and its capital sat at
            // L1 all match: the price never formed, and the x2 / x4 on the
            // home territory never came.
            if (verdict == CapitalUpgrade.Saving && reserveCapital
                && UpgradeBuildingCommandHelper.TryGetNextCost(em, home, out var price, out byte next))
            {
                // ...BUT NOT FOREVER (2026-10-05): a savings goal that holds
                // the bank for capitalReserveMaxHoldSeconds unbroken is
                // released for capitalReserveRestSeconds, so production and
                // units are not starved while the price forms. The capital is
                // still bought whenever the bank can pay; while resting the
                // upgrade rotation runs as usual.
                if (CapitalReserveResting(faction, profile, time, next))
                {
                    AIPivotalReserve.Clear(faction, CapitalReserveKey);
                    return CapitalUpgrade.NotNeeded;
                }
                AIPivotalReserve.Set(faction, CapitalReserveKey, price);
                int key = (int)faction;
                if (!_nextCapitalSavingLog.TryGetValue(key, out float at) || time >= at
                    || time < at - CapitalSavingLogSeconds)   // a new match restarted the clock
                {
                    _nextCapitalSavingLog[key] = time + CapitalSavingLogSeconds;
                    AILogger.Log(faction, "ECON",
                        $"capital L{next} saving ({price.Supplies}s {price.Iron}i {price.Veilstone}v)");
                }
            }
            else
            {
                AIPivotalReserve.Clear(faction, CapitalReserveKey);
                _capitalHoldSince.Remove(fk);
            }
            return verdict;
        }

        // Host-only AI state, keyed by faction, reset per match (SimCadence).
        private static int _capitalHoldEpoch = -1;
        private static readonly System.Collections.Generic.Dictionary<int, float> _capitalHoldSince
            = new System.Collections.Generic.Dictionary<int, float>();
        private static readonly System.Collections.Generic.Dictionary<int, float> _capitalRestUntil
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>True while the capital's savings goal is released (its
        /// hold ran capitalReserveMaxHoldSeconds unbroken; it rests for
        /// capitalReserveRestSeconds). Starts the hold clock otherwise.</summary>
        private static bool CapitalReserveResting(Faction faction, in AIDifficultyProfile profile,
            float time, byte next)
        {
            int key = (int)faction;
            if (_capitalRestUntil.TryGetValue(key, out float rest) && time < rest) return true;
            if (!_capitalHoldSince.TryGetValue(key, out float since))
            {
                _capitalHoldSince[key] = time;
                return false;
            }
            if (profile.CapitalReserveMaxHoldSeconds <= 0f
                || time - since < profile.CapitalReserveMaxHoldSeconds)
                return false;
            _capitalHoldSince.Remove(key);
            float restFor = System.Math.Max(0f, profile.CapitalReserveRestSeconds);
            _capitalRestUntil[key] = time + restFor;
            AILogger.Log(faction, "ECON",
                $"capital L{next} saving released after {profile.CapitalReserveMaxHoldSeconds:F0}s " +
                $"(units and production first for {restFor:F0}s)");
            return true;
        }

        private static CapitalUpgrade TryUpgradeCapitalCore(EntityManager em, Faction faction, bool surplus,
            out Entity home)
        {
            var query = QC_HallTagBuildingUpgradeableFactionTag.Get(em, QT_HallTagBuildingUpgradeableFactionTag);
            using var ents = query.ToEntityArray(Allocator.Temp);
            home = Entity.Null;
            long bestNid = long.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                long nid = em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i])
                    ? em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i]).NetworkId
                    : long.MaxValue - 1;
                if (home != Entity.Null && nid >= bestNid) continue;
                bestNid = nid;
                home = ents[i];
            }
            if (home == Entity.Null) return CapitalUpgrade.NotNeeded;
            if (em.HasComponent<UnderConstruction>(home)) return CapitalUpgrade.NotNeeded;

            byte lvl = em.HasComponent<BuildingUpgradeState>(home)
                ? em.GetComponentData<BuildingUpgradeState>(home).Level : (byte)0;
            if (lvl >= Cfg.capitalPriorityLevel || lvl >= BuildingUpgradeConfig.MaxLevel)
                return CapitalUpgrade.NotNeeded;
            // Already paid for and waiting / in progress: nothing to buy.
            if (UpgradeBuildingCommandHelper.IsUpgradeQueued(em, home)) return CapitalUpgrade.NotNeeded;

            // UNITS BEFORE ECONOMY (Game_AI.md 5h): a priority level past
            // capitalEssentialLevel (L2 doubles the home income and stays
            // essential) is economy drive -- on a tier that says so it
            // waits, unsaved, while the money could still become units. The
            // rotation may still level it later, under the same gate.
            if (lvl + 1 > Cfg.capitalEssentialLevel
                && UpgradeBuildingCommandHelper.TryGetNextCost(em, home, out var drivePrice, out _))
            {
                string deferred = AIBudget.EconomyDeferral(em, faction, drivePrice);
                if (deferred != null)
                {
                    AIBudget.NoteEconomyDeferred(faction, $"capital L{lvl + 1}", deferred);
                    return CapitalUpgrade.NotNeeded;
                }
            }

            var result = UpgradeBuildingCommandHelper.Execute(em, home,
                TheWaningBorder.Core.Commands.CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
            {
                AILogger.Log(faction, "BUILDING", $"Upgrading the capital to L{lvl + 1} (priority)");
                AILogger.Log(faction, "ECON", $"upgrade Fortress (capital) -> L{lvl + 1}");
                if (surplus) AILogger.Log(faction, "SURPLUS", $"upgrade Fortress (capital) to L{lvl + 1}");
                return CapitalUpgrade.Queued;
            }
            return result == UpgradeBuildingResult.CannotAfford
                ? CapitalUpgrade.Saving
                : CapitalUpgrade.NotNeeded;
        }

        /// <summary>
        /// Find the LOWEST-LEVEL building of the given type owned by the
        /// faction. Lowest level = highest marginal benefit per upgrade
        /// click (Fortress L1 unlocks the multi-target chain).
        /// Returns true if the upgrade was queued.
        /// </summary>
        /// <param name="supplyEngine">The Gatherer's Hut levelled as the
        /// supply engine (below the reserves, or while the capital's price
        /// forms) — exempt from the army-first gate and the savings hold,
        /// because it is the income both are waiting on.</param>
        private static bool TryUpgradeBuildingType(EntityManager em, Faction faction, string buildingId,
            bool surplus = false, bool veilstoneHeld = false, bool supplyEngine = false)
        {
            EntityQuery query;
            switch (buildingId)
            {
                case "Fortress":
                    query = QC_HallTagBuildingUpgradeableFactionTag.Get(em, QT_HallTagBuildingUpgradeableFactionTag);
                    break;
                case "Barracks":
                    query = QC_BarracksTagBuildingUpgradeableFactionTag.Get(em, QT_BarracksTagBuildingUpgradeableFactionTag);
                    break;
                case "Hut":
                    // A Hut level buys population and nothing else. Measured
                    // 2026-10-04 (Headless26): with ~60 of 300 used, House
                    // L2/L3 ate 1,000-2,600 supplies a minute until popMax hit
                    // the ceiling, and that stop was the "income jump at 30
                    // minutes". Only level Huts when housing is actually short.
                    if (PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int popMax)
                        && popMax - pop > Cfg.hutUpgradeHeadroomMax)
                        return false;
                    query =QC_HutTagBuildingUpgradeableFactionTag.Get(em, QT_HutTagBuildingUpgradeableFactionTag);
                    break;
                case "GatherersHut":
                    // Feraldis huts are Raider Camps — they gather nothing,
                    // so levelling them buys none of the drips this exists
                    // for. Excluded so the pass moves on to something useful.
                    query = QC_GathererHutUpgradeableNotRaider.Get(em, QT_GathererHutUpgradeableNotRaider);
                    break;
                case "ArcheryRange":
                    query = QC_ArcheryRangeTagBuildingUpgradeableFactionTag.Get(em, QT_ArcheryRangeTagBuildingUpgradeableFactionTag);
                    break;
                case "Alanthor_RoyalStable":
                    query = QC_RoyalStableTagBuildingUpgradeableFactionTag.Get(em, QT_RoyalStableTagBuildingUpgradeableFactionTag);
                    break;
                case "Alanthor_SiegeYard":
                    query = QC_SiegeYardTagBuildingUpgradeableFactionTag.Get(em, QT_SiegeYardTagBuildingUpgradeableFactionTag);
                    break;
                case "Alanthor_Tower":
                    query = QC_WatchTowerTagBuildingUpgradeableFactionTag.Get(em, QT_WatchTowerTagBuildingUpgradeableFactionTag);
                    break;
                case "VaultOfAlmierra":
                    query = QC_VaultTagBuildingUpgradeableFactionTag.Get(em, QT_VaultTagBuildingUpgradeableFactionTag);
                    break;
                case "Alanthor_Wall":
                    // Wall hubs: many instances, lowest-level-first (default
                    // direction below). Matches nothing until hubs carry
                    // BuildingUpgradeable — see the PriorityOrder note.
                    query = QC_WallHubTagBuildingUpgradeableFactionTag.Get(em, QT_WallHubTagBuildingUpgradeableFactionTag);
                    break;
                default:
                    return false;
            }

            using var ents = query.ToEntityArray(Allocator.Temp);

            // Selection direction. Default is lowest-level-first (highest
            // marginal benefit per click). GATHERER'S HUTS INVERT THIS
            // (2026-08-09, log-proven): the veilsteel drip only flows from
            // MAX-LEVEL huts with VeilsteelSurvey, and with new L1 huts founded
            // all game, lowest-first meant 31 straight L2 upgrades and not one
            // hut ever reaching L3 — the entire hut-side veilsteel economy
            // stayed locked for 47 minutes. Highest-first pushes huts through
            // to the gate one at a time instead of levelling the whole estate
            // in lockstep.
            bool highestFirst = buildingId == "GatherersHut";

            Entity best = Entity.Null;
            int bestLevel = highestFirst ? -1 : int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                // Queued counts as busy: BuildingUpgrading only appears once
                // the item reaches the head of the production queue, so
                // testing it alone would keep re-picking a building whose
                // upgrade is already paid for and waiting.
                if (UpgradeBuildingCommandHelper.IsUpgradeQueued(em, ents[i])) continue;

                byte lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : (byte)0;
                if (lvl >= BuildingUpgradeConfig.MaxLevel) continue;

                bool better = highestFirst ? lvl > bestLevel : lvl < bestLevel;
                if (better) { bestLevel = lvl; best = ents[i]; }
            }
            if (best == Entity.Null) return false;

            // Army first: never spend the military line's iron on levels.
            // A PRODUCTION LINE'S LEVEL IS EXEMPT from every army-first gate
            // here (2026-10-04, Game_AI.md 5f, operator: "the buildings are
            // what allows the army to grow faster"): this iron floor, the
            // ArmyFirstYield below and the army's veilstone earmark.
            bool feedsArmy = RaisesUnitOutput(buildingId);

            // UNITS BEFORE ECONOMY (Game_AI.md 5h): every level that does not
            // raise unit output waits, on a tier that says so, while the
            // money could still start units (the supply engine excepted: it
            // is the income the capital's essential price is waiting on; a Hut
            // level is housing, levelled only when housing is short).
            if (!feedsArmy && !supplyEngine && buildingId != "Hut"
                && UpgradeBuildingCommandHelper.TryGetNextCost(em, best, out var driveCost, out _))
            {
                string deferred = AIBudget.EconomyDeferral(em, faction, driveCost);
                if (deferred != null)
                {
                    AIBudget.NoteEconomyDeferred(faction, "upgrade " + buildingId, deferred);
                    return false;
                }
            }
            if (!feedsArmy
                && FactionEconomy.TryGetBank(em, faction, out var upgradeBank)
                && em.GetComponentData<FactionResources>(upgradeBank).Iron < Cfg.upgradeIronReserve)
                return false;

            // ARMY FIRST (2026-10-04, Game_AI.md 5f). A level that does not
            // raise unit output -- everything but the four production lines
            // -- yields to an army below its target that could spend the
            // money (AIBudget.ArmyFirstYield), and every level respects the
            // resource-aware savings hold (the lost-trainer rebuild's strict
            // reserve among them). The capital's priority levels and the
            // supply engine are exempt (they never reach this call / pass
            // supplyEngine).
            if (!supplyEngine
                && UpgradeBuildingCommandHelper.TryGetNextCost(em, best, out var gateCost, out _))
            {
                if (AIPivotalReserve.ShouldHold(em, faction, gateCost)) return false;
                if (!feedsArmy)
                {
                    string why = AIBudget.ArmyFirstYield(em, faction, gateCost);
                    if (why != null)
                    {
                        AIBudget.NoteArmyFirstYield(faction, "upgrade " + buildingId, why);
                        return false;
                    }
                }
            }

            // The army waits on veilstone: a level priced in it must leave
            // the army's earmark in the bank (Game_AI.md 5e).
            if (veilstoneHeld && !feedsArmy
                && UpgradeBuildingCommandHelper.TryGetNextCost(em, best, out var nextCost, out _)
                && !AIBudget.LeavesMilitaryVeilstone(em, faction, nextCost))
                return false;

            var result = UpgradeBuildingCommandHelper.Execute(em, best,
                TheWaningBorder.Core.Commands.CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
            {
                AILogger.Log(faction, "BUILDING",
                    $"Upgrading {buildingId} to L{bestLevel + 1}");
                if (surplus) AILogger.Log(faction, "SURPLUS", $"upgrade {buildingId} to L{bestLevel + 1}");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Per-brain tick throttle for the building upgrade loop.
    /// </summary>
    public struct AIBuildingUpgradeTickState : IComponentData
    {
        public float NextThinkTime;
        /// <summary>Round-robin start index into PriorityOrder — advances every
        /// think tick so no building line can monopolize the single upgrade slot.</summary>
        public byte Rotation;
    }
}
