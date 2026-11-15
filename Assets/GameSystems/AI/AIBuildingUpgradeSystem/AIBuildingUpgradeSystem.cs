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
//
// ONE DECISION PER TYPE (2026-10-19, docs/Design/Overview.md § Building
// levels). A level is faction-wide per building type: finishing it raises
// every building of the type, and new ones are built at it. So the AI never
// walks a type's buildings looking for the "lowest" one any more — it asks
// once per type:
//   * is a level of this type already queued / running anywhere? then no;
//   * is it WORTH it? A level is priced against soldiers: its SO price in
//     reference-unit prices (referenceUnitId's SO cost) must not exceed the
//     buildings it raises times soldiersPer<Category>Building — more
//     Barracks, a level worth more (ValueGate). An overflowing bank
//     (surplusIgnoresValue) and the housing Hut (gated on need) skip it;
//   * where does the clock run? PickHost: an idle building of the type
//     before one busy training, the shortest queue otherwise.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

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

        // Runai ladders (2026-10-13, docs/Design/Age_1_Runai.md §4): without
        // them in the rotation no Runai AI ever reached a level-2 or level-3
        // unit or tech (Veilmarch 8p batch: 0 of 20 Runai factions).
        static readonly ComponentType[] QT_GrazingGroundsUpgradeable =
        {
            ComponentType.ReadOnly<GrazingGroundsTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_GrazingGroundsUpgradeable;
        static readonly ComponentType[] QT_ArrowyardUpgradeable =
        {
            ComponentType.ReadOnly<ArrowyardTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_ArrowyardUpgradeable;
        static readonly ComponentType[] QT_WagonworksUpgradeable =
        {
            ComponentType.ReadOnly<SiegeWorkshopTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_WagonworksUpgradeable;
        static readonly ComponentType[] QT_SanctuaryUpgradeable =
        {
            ComponentType.ReadOnly<RunaiSanctuaryTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_SanctuaryUpgradeable;

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

        // Feraldis trainers (2026-10-19): the Hall of Axes, Rig Yard and
        // Engine Works level L1-L3 like every other production line.
        static readonly ComponentType[] QT_HallOfAxesUpgradeable =
        {
            ComponentType.ReadOnly<HallOfAxesTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_HallOfAxesUpgradeable;
        static readonly ComponentType[] QT_PastureUpgradeable =
        {
            ComponentType.ReadOnly<PastureTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_PastureUpgradeable;
        static readonly ComponentType[] QT_FerSiegeYardUpgradeable =
        {
            ComponentType.ReadOnly<FerSiegeYardTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_FerSiegeYardUpgradeable;

        // The iron Mine levels L1-L3 for every culture (2026-10-19).
        static readonly ComponentType[] QT_MineUpgradeable =
        {
            ComponentType.ReadOnly<MineTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_MineUpgradeable;

        // The Alanthor Farm levels L1-L3 (decision 57): an economy ladder.
        static readonly ComponentType[] QT_FarmUpgradeable =
        {
            ComponentType.ReadOnly<FarmTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_FarmUpgradeable;

        // The Veilstone Mine levels (2026-11-10, decision 48: a real income ladder).
        static readonly ComponentType[] QT_VeilstoneMineUpgradeable =
        {
            ComponentType.ReadOnly<VeilstoneMineTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_VeilstoneMineUpgradeable;

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
        //   * the Survey techs' iron / veilstone drips all scale with it,
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
            { "Barracks", "GatherersHut", "ArcheryRange", "Mine", "Alanthor_RoyalStable", "Fortress",
              "Alanthor_SiegeYard", "Alanthor_Farm", "Hut", "Alanthor_Tower",
              "VaultOfAlmierra", "Alanthor_Wall",
              "Runai_GrazingGrounds", "Runai_Arrowyard", "Runai_SiegeWorkshop", "Runai_Sanctuary",
              "Feraldis_HallOfAxes", "Feraldis_Pasture", "Feraldis_SiegeYard" };


        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            float time = TheWaningBorder.Core.SimClock.Now;
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

                // HOUSING BEFORE THE TURN (2026-11-14, Game_AI.md § 6ac): a
                // House level near the cap is bought outside the level-up gap
                // and the rotation, and saved for when the bank cannot pay.
                TryUpgradeHousing(em, faction, time);
                // ...and so is a Farm level that repays its price (§ 6ac): it
                // is income, and waited behind ten other types in batch B.
                TryUpgradeBuildingType(em, faction, "Alanthor_Farm", supplyEngine: true, outsideGap: true);
                TryUpgradeBuildingType(em, faction, "Mine", supplyEngine: true, outsideGap: true);

                // PHASES (Game_AI.md § 2a): the tier's gap between level-ups.
                if (!AIPacing.LevelUpTurn(em, faction))
                {
                    AIPivotalReserve.Clear(faction, CapitalReserveKey);
                    continue;
                }

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

                // A LINE THAT CANNOT LEVEL DOES NOT COST THE TURN (2026-11-14).
                // The walk used to stop at the first entry that did nothing, and
                // most entries do nothing for a given faction (another culture's
                // buildings, a line with no building yet, one already at its top):
                // the Royal Stable and Siege Yard reached level 3 in 11 of 84
                // faction-games. It now goes on down the rotation until it has
                // made its level-ups for this think.
                int made = 0;
                for (int a = 0; a < PriorityOrder.Length && made < attempts && AIPacing.LevelUpTurn(em, faction); a++)
                    if (TryUpgradeOne(em, faction, (rotation + a) % PriorityOrder.Length,
                            reservesOk, overflowing, veilstoneHeld, reserveCapital, profile, time))
                        made++;

                // ECONOMY LEVELS FIRST (Game_AI.md 5h): a level is income,
                // which is what the army is waiting on.
                for (int e = 0; e < profile.EconomyUpgradesPerThink && AIPacing.LevelUpTurn(em, faction); e++)
                    if (!TryEconomyUpgrade(em, faction, e, profile.ArmyBeforeSaves, profile.EarlyExtractorLevels))
                        break;
            }
        }

        /// <summary>The economy ladders TryEconomyUpgrade rotates through, in
        /// pass order (index = the query it picks a host from).</summary>
        private static readonly string[] EconomyLevelIds =
            { "GatherersHut", "Mine", "VeilstoneMine", "Alanthor_Farm" };

        /// <summary>AIPivotalReserve key of the capital's next level.</summary>
        private const string CapitalReserveKey = "CapitalLevel";

        /// <summary>The next Farm level's added supplies a minute (every
        /// finished Farm of the faction: Workers inside x field share x the
        /// per-Worker gain) repay its price within farmLevelPaybackMinutes.</summary>
        private static bool FarmLevelPaysBack(EntityManager em, Faction faction, in Cost price, byte nextLevel)
        {
            if (Cfg.farmLevelPaybackMinutes <= 0f) return false;
            var nextDef = BuildingUpgradeConfig.LevelDef(Cultures.Alanthor, Farm.BuildingId, nextLevel);
            if (nextDef == null) return false;
            int gainPerWorker = nextDef.suppliesPerWorkerPerMinute - Farm.PerWorkerPerMinute(em, faction);
            if (gainPerWorker <= 0) return false;

            var q = QC_FarmUpgradeable.Get(em, QT_FarmUpgradeable);
            using var farms = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int gain = 0;
            for (int i = 0; i < farms.Length; i++)
            {
                if (facs[i].Value != faction || em.HasComponent<UnderConstruction>(farms[i])) continue;
                gain += Farm.SuppliesPerMinute(WallGarrison.OccupantCount(em, farms[i]), gainPerWorker,
                    FarmRing.CachedFreePermille(em, farms[i]));
            }
            float priceSum = price.Supplies + price.Iron + price.Veilstone;
            if (gain * Cfg.farmLevelPaybackMinutes >= priceSum) return true;
            if (AILogger.Enabled)
                AIBudget.NoteEconomyDeferred(faction, "upgrade Alanthor_Farm",
                    $"+{gain} supplies/min x {Cfg.farmLevelPaybackMinutes:F0} min < price {priceSum:F0}");
            return false;
        }

        /// <summary>The next Mine level's added ore a minute (every finished
        /// Mine of the faction, the level SOs' slotIncomePerMinute rungs)
        /// repays its price within farmLevelPaybackMinutes. <paramref name="judged"/>
        /// is false when the culture authors no Mine level SOs (then the
        /// ordinary value gate decides).</summary>
        private static bool MineLevelPaysBack(EntityManager em, Faction faction, in Cost price, byte nextLevel,
            out bool judged)
        {
            judged = false;
            if (Cfg.mineLevelPaybackMinutes <= 0f) return false;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            var nextDef = BuildingUpgradeConfig.LevelDef(culture, "Mine", nextLevel);
            var nowDef = BuildingUpgradeConfig.LevelDef(culture, "Mine", (byte)(nextLevel - 1));
            if (nextDef == null || nowDef == null) return false;
            judged = true;
            float step = nextDef.slotIncomePerMinute - nowDef.slotIncomePerMinute;
            if (step <= 0f) return false;
            var q = QC_MineUpgradeable.Get(em, QT_MineUpgradeable);
            using var mines = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int owned = 0;
            for (int i = 0; i < mines.Length; i++)
                if (facs[i].Value == faction && !em.HasComponent<UnderConstruction>(mines[i])) owned++;
            float gain = owned * step;
            float priceSum = price.Supplies + price.Iron + price.Veilstone;
            if (gain * Cfg.mineLevelPaybackMinutes >= priceSum) return true;
            if (AILogger.Enabled)
                AIBudget.NoteEconomyDeferred(faction, "upgrade Mine",
                    $"+{gain:F0} ore/min x {Cfg.mineLevelPaybackMinutes:F0} min < price {priceSum:F0}");
            return false;
        }

        /// <summary>AIPivotalReserve key of the next House level.</summary>
        private const string HousingReserveKey = "HousingLevel";

        /// <summary>
        /// THE HOUSE LEVEL IS THE POPULATION CAP (2026-11-14, Game_AI.md
        /// § 6ac). Twenty Houses at L1 hold a faction at 110; only House
        /// levels reach the 300 cap. Measured on Moatwatch (0.0.85, 24
        /// faction-games): every faction had its twenty Houses by minute 14,
        /// one House L2 was bought by minute 15.5, and the level waited in a
        /// ten-type rotation behind the tier's level-up gap while every supply
        /// went to units as it arrived. So while free housing is within
        /// hutUpgradeHeadroomMax, the House level is taken here, before the
        /// gap and the rotation: bought the moment the bank covers it,
        /// otherwise a savings goal (non-strict, so the army still breathes
        /// on the reserve's duty cycle). The tier's phase clocks still apply.
        /// </summary>
        private static void TryUpgradeHousing(EntityManager em, Faction faction, float time)
        {
            if (time < SimpleAISystem.TierOf(faction).LevelUpsFromSeconds
                || !PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int popMax)
                || popMax - pop > Cfg.hutUpgradeHeadroomMax)
            {
                AIPivotalReserve.Clear(faction, HousingReserveKey);
                return;
            }
            var query = QC_HutTagBuildingUpgradeableFactionTag.Get(em, QT_HutTagBuildingUpgradeableFactionTag);
            Entity host = PickHost(em, faction, query);
            if (host == Entity.Null
                || !UpgradeBuildingCommandHelper.TryGetNextCost(em, host, out var price, out byte next)
                || !AIPacing.LevelPhaseOpen(em, faction, next))
            {
                AIPivotalReserve.Clear(faction, HousingReserveKey);
                return;
            }
            if (!FactionEconomy.CanAfford(em, faction, price))
            {
                if (!AIPivotalReserve.Has(faction, HousingReserveKey))
                    AILogger.Log(faction, "ECON",
                        $"House L{next} saving ({price.Supplies}s {price.Iron}i {price.Veilstone}v) - pop {pop}/{popMax}");
                // AT THE CAP THE SAVING IS STRICT: nothing can be trained until
                // the level is bought, so an economy-distress pause (which drops
                // every non-strict goal) must not spend the price elsewhere --
                // measured: Green on Moatwatch saved from 12:39 and still sat at
                // 110/110 at minute 15, "savings paused (rebuilding Mine)".
                bool atCap = popMax - pop <= Cfg.hutUpgradeStrictHeadroom;
                AIPivotalReserve.Set(faction, HousingReserveKey, price, strict: atCap);
                return;
            }
            AIPivotalReserve.Clear(faction, HousingReserveKey);
            if (UpgradeBuildingCommandHelper.Execute(em, host,
                    TheWaningBorder.Core.Commands.CommandSource.AI) == UpgradeBuildingResult.Ok)
                AILogger.Log(faction, "BUILDING", $"Upgrading Hut to L{next} (housing, pop {pop}/{popMax})");
        }

        /// <summary>Seconds between "capital saving" lines per faction (log
        /// cadence, not tuning).</summary>
        private const float CapitalSavingLogSeconds = 60f;

        /// <summary>Per-faction next time the "capital saving" line may be
        /// written (host AI state, main thread).</summary>
        private static readonly System.Collections.Generic.Dictionary<int, float> _nextCapitalSavingLog
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>
        /// One economy level, ahead of the rotation and without yielding to
        /// the army (Game_AI.md 5f / 5h): the Gatherer's Hut, the iron Mine
        /// (2026-10-19) and the Veilstone Mine take turns by pass, each
        /// falling back to the next.
        /// Respects the savings hold (the capital's price first) and the
        /// army's veilstone earmark.
        /// </summary>
        private static bool TryEconomyUpgrade(EntityManager em, Faction faction, int pass,
            bool incomeOutranksPot, bool earlyExtractorLevels = false)
        {
            for (int k = 0; k < EconomyLevelIds.Length; k++)
            {
                int kind = (pass + k) % EconomyLevelIds.Length;
                string id = EconomyLevelIds[kind];
                // ONE decision per type: the level raises every hut / mine.
                Entity best = PickHost(em, faction, kind == 0
                    ? QC_GathererHutUpgradeableNotRaider.Get(em, QT_GathererHutUpgradeableNotRaider)
                    : kind == 1
                        ? QC_MineUpgradeable.Get(em, QT_MineUpgradeable)
                        : kind == 2
                            ? QC_VeilstoneMineUpgradeable.Get(em, QT_VeilstoneMineUpgradeable)
                            : QC_FarmUpgradeable.Get(em, QT_FarmUpgradeable));
                if (best == Entity.Null) continue;
                if (!UpgradeBuildingCommandHelper.TryGetNextCost(em, best, out var cost, out byte next)) continue;
                if (next <= 1) continue;   // L1 is the free age-up level
                // A hoard pauses Mine levels too (developer 2026-11-11); the Mines keep working.
                if ((kind == 1 || kind == 2) && FactionEconomy.TryGetResources(em, faction, out var hb)
                    && (kind == 1 ? hb.Iron > TheWaningBorder.AI.SimpleAISystemConfig.I.ironNoNewMinesAbove
                                  : hb.Veilstone > TheWaningBorder.AI.SimpleAISystemConfig.I.veilstoneNoNewMinesAbove))
                    continue;
                // EARLY EXTRACTOR LEVELS (decision 49, Hard / Expert): a Mine or a
                // Veilstone Mine levels past the level clock and the value gate,
                // so the node pays at full level from early on, before its
                // reserve runs dry (nodes are finite, decision 56).
                bool early = earlyExtractorLevels && (id == "Mine" || id == "VeilstoneMine");
                if (!early && !AIPacing.LevelAllowed(em, faction, next)) continue;
                if (!early && !ValueGate(em, faction, best, cost, Cfg.soldiersPerEconomyBuilding, surplus: false)) continue;
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
                    AIBudget.NoteEconomyDeferred(faction, "upgrade " + id, deferred);
                    return false;
                }
                if (!AIBudget.LeavesSavings(em, faction, cost)) continue;   // the pot is not this level's
                if (UpgradeBuildingCommandHelper.Execute(em, best,
                        TheWaningBorder.Core.Commands.CommandSource.AI) != UpgradeBuildingResult.Ok) continue;
                AIPacing.NoteLevelUp(em, faction);
                AILogger.Log(faction, "ECON", $"upgrade {id} -> L{next} ({cost.Supplies}s {cost.Iron}i)");
                return true;
            }
            return false;
        }

        /// <summary>
        /// WHERE a type's level-up runs (2026-10-19). Null when the type can
        /// not be levelled now: one is already queued or running on any
        /// building of the type (faction-wide, one at a time), or the type is
        /// at its top. Otherwise the building whose production it delays
        /// least — idle and empty-queued first, then the shortest queue;
        /// <paramref name="preferred"/> (the home capital) wins a tie. Ties
        /// otherwise fall to query order, the same on every run.
        /// </summary>
        public static Entity PickHost(EntityManager em, Faction faction, EntityQuery q,
            Entity preferred = default)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            Entity best = Entity.Null;
            int bestLoad = int.MaxValue;
            bool typeChecked = false;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (!typeChecked)
                {
                    typeChecked = true;
                    string ladder = UpgradeBuildingCommandHelper.ResolveBuildingId(em, e);
                    if (TheWaningBorder.Systems.Buildings.FactionBuildingLevels
                            .UpgradeInFlight(em, faction, ladder, out _))
                        return Entity.Null;
                    if (TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, ladder)
                            >= BuildingUpgradeConfig.MaxLevel)
                        return Entity.Null;
                }
                if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, e)) continue;

                int load = 0;
                if (em.HasComponent<ProductionState>(e) && em.GetComponentData<ProductionState>(e).Busy != 0)
                    load += 2;
                if (em.HasBuffer<ProductionQueueItem>(e))
                    load += 2 * em.GetBuffer<ProductionQueueItem>(e, true).Length;
                if (e != preferred) load += 1;
                if (load < bestLoad) { bestLoad = load; best = e; }
            }
            return best;
        }

        /// <summary>
        /// Is ONE faction-wide level worth its price against soldiers? The
        /// price, in reference-unit prices (Cfg.referenceUnitId's SO cost, all
        /// four resources summed), must not exceed the finished buildings of
        /// the type it raises times <paramref name="soldiersPerBuilding"/>.
        /// Read from the SOs every time, so a price raise moves the bar with
        /// no code change. An overflowing bank skips it when
        /// Cfg.surplusIgnoresValue (the basics cap leaves nothing to train).
        /// </summary>
        private static bool ValueGate(EntityManager em, Faction faction, Entity host, in Cost price,
            float soldiersPerBuilding, bool surplus)
        {
            if (soldiersPerBuilding <= 0f) return true;
            if (surplus && Cfg.surplusIgnoresValue) return true;
            float soldier = ReferenceSoldierPrice();
            if (soldier <= 0f) return true;
            string ladder = UpgradeBuildingCommandHelper.ResolveBuildingId(em, host);
            int owned = TheWaningBorder.Systems.Buildings.FactionBuildingLevels.CountOwned(em, faction, ladder);
            float inSoldiers = (price.Supplies + price.Iron + price.Veilstone) / soldier;
            float worth = owned * soldiersPerBuilding;
            if (worth >= inSoldiers) return true;
            if (AILogger.Enabled)
                AIBudget.NoteEconomyDeferred(faction, "upgrade " + ladder,
                    $"worth {worth:F1} soldiers ({owned} x {soldiersPerBuilding:F1}) < price {inSoldiers:F1} soldiers");
            return false;
        }

        /// <summary>The reference soldier's SO price (all four resources
        /// summed); 0 when the id is unset or unknown (gate off).</summary>
        private static float ReferenceSoldierPrice()
        {
            string id = Cfg.referenceUnitId;
            if (string.IsNullOrEmpty(id) || !TechCatalog.TryGetUnit(id, out var def) || def?.cost == null)
                return 0f;
            var c = def.cost;
            return c.Supplies + c.Iron + c.Veilstone;
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static bool HasCulture(EntityManager em, Faction faction)
        {
            return CultureConfig.GetCompletedCulture(em, faction) != Cultures.None;
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
                            supplyEngine: true)
                        || TryUpgradeBuildingType(em, faction, "Alanthor_Farm", surplus, veilstoneHeld,
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
                if (TryUpgradeBuildingType(em, faction, "Alanthor_Farm", surplus, veilstoneHeld,
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
        /// (the level SO's trainTimeMultiplier), so it raises unit
        /// output and never yields to the army it feeds. A roster table, not
        /// tuning: the ids are the buildings' SO ids.</summary>
        private static bool RaisesUnitOutput(string buildingId)
            => System.Array.IndexOf(ProductionLineIds, buildingId) >= 0;

        /// <summary>The production lines of every culture, for the pass below
        /// the reserve floor (Runai and Feraldis trainers joined 2026-10-19).</summary>
        private static readonly string[] ProductionLineIds =
            { "Barracks", "ArcheryRange", "Alanthor_RoyalStable", "Alanthor_SiegeYard",
              "Runai_GrazingGrounds", "Runai_Arrowyard", "Runai_SiegeWorkshop",
              "Feraldis_HallOfAxes", "Feraldis_Pasture", "Feraldis_SiegeYard" };

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
            // THE POT (2026-11-15, AIBudget.SetSavingsGoal): the price is put
            // aside from the income whatever the posture. The hold below only
            // asked the spenders to wait, and they did not.
            // EVERY TIER SAVES; a lower one slower (its savingsIncomeShare)
            // and, sometimes, for the wrong thing (SoundObjectiveMayTakePot).
            if (verdict == CapitalUpgrade.Saving
                && UpgradeBuildingCommandHelper.TryGetNextCost(em, home, out var potPrice, out _))
            {
                if (AIBudget.SavingsKey(faction) == CapitalReserveKey
                    || SoundObjectiveMayTakePot(faction, profile))
                    AIBudget.SetSavingsGoal(faction, CapitalReserveKey, potPrice, profile.SavingsIncomeShare);
                // Saving for something else: the capital does not hold the
                // rotation up meanwhile.
                else verdict = CapitalUpgrade.NotNeeded;
            }
            else if (verdict == CapitalUpgrade.Queued)
                AIBudget.ClearSavingsGoal(faction, CapitalReserveKey);
            // THE CAPITAL'S PRICE IS A SAVINGS GOAL (Game_AI.md 5h). A rusher
            // spent every supply on units as it arrived and its capital sat at
            // L1 all match: the price never formed, and the +25 % / +50 % on the
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

            // The FACTION's Fortress level — every Fortress stands there
            // (2026-10-19, faction-wide levels).
            byte lvl = TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, "Fortress");
            if (!AIPacing.LevelAllowed(em, faction, (byte)(lvl + 1))) return CapitalUpgrade.NotNeeded;
            if (lvl >= Cfg.capitalPriorityLevel || lvl >= BuildingUpgradeConfig.MaxLevel)
                return CapitalUpgrade.NotNeeded;
            // Already paid for and waiting / in progress on ANY Fortress:
            // nothing to buy.
            if (TheWaningBorder.Systems.Buildings.FactionBuildingLevels
                    .UpgradeInFlight(em, faction, "Fortress", out _))
                return CapitalUpgrade.NotNeeded;
            // The clock runs on the least busy Fortress, the home one on a tie
            // (an expansion Fortress levels the home seat just the same).
            var host = PickHost(em, faction, query, preferred: home);
            if (host != Entity.Null) home = host;

            // UNITS BEFORE ECONOMY (Game_AI.md 5h): a priority level past
            // capitalEssentialLevel (L2 raises the home income by 25 % and stays
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
                AIPacing.NoteLevelUp(em, faction);
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
        /// ONE faction-wide decision for a building type (2026-10-19): if the
        /// type can level now (nothing of it levelling, below its top), is
        /// worth its price against soldiers (ValueGate) and passes the army
        /// gates, queue it on the least busy building of the type (PickHost).
        /// Returns true if the upgrade was queued.
        /// </summary>
        /// <param name="supplyEngine">The Gatherer's Hut levelled as the
        /// supply engine (below the reserves, or while the capital's price
        /// forms) — exempt from the army-first gate and the savings hold,
        /// because it is the income both are waiting on.</param>
        private static bool TryUpgradeBuildingType(EntityManager em, Faction faction, string buildingId,
            bool surplus = false, bool veilstoneHeld = false, bool supplyEngine = false, bool outsideGap = false)
        {
            // A tier that levels late (Game_AI.md § 2).
            if (TheWaningBorder.Core.SimClock.Now < SimpleAISystem.TierOf(faction).LevelUpsFromSeconds) return false;
            EntityQuery query;
            // HOUSING IS CRITICAL WHEN THE CAP IS NEAR (2026-11-04, § 6y): a
            // House level then yields to nothing — not to the army (which
            // cannot grow without it) and not to the iron reserve (it costs
            // none). Measured: 52 "upgrade Hut yields" in ten matches, L2
            // at a median minute 44, L3 at 54, with 20 L1 Houses capping a
            // faction at 210.
            bool housingCritical = false;
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
                    housingCritical = true;
                    query =QC_HutTagBuildingUpgradeableFactionTag.Get(em, QT_HutTagBuildingUpgradeableFactionTag);
                    break;
                case "GatherersHut":
                    // Feraldis huts are Raider Camps — they gather nothing,
                    // so levelling them buys none of the drips this exists
                    // for. Excluded so the pass moves on to something useful.
                    query = QC_GathererHutUpgradeableNotRaider.Get(em, QT_GathererHutUpgradeableNotRaider);
                    break;
                case "Mine":
                    query = QC_MineUpgradeable.Get(em, QT_MineUpgradeable);
                    break;
                case "Alanthor_Farm":
                    query = QC_FarmUpgradeable.Get(em, QT_FarmUpgradeable);
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
                case "Runai_GrazingGrounds":
                    query = QC_GrazingGroundsUpgradeable.Get(em, QT_GrazingGroundsUpgradeable);
                    break;
                case "Runai_Arrowyard":
                    query = QC_ArrowyardUpgradeable.Get(em, QT_ArrowyardUpgradeable);
                    break;
                case "Runai_SiegeWorkshop":
                    query = QC_WagonworksUpgradeable.Get(em, QT_WagonworksUpgradeable);
                    break;
                case "Runai_Sanctuary":
                    query = QC_SanctuaryUpgradeable.Get(em, QT_SanctuaryUpgradeable);
                    break;
                case "Feraldis_HallOfAxes":
                    query = QC_HallOfAxesUpgradeable.Get(em, QT_HallOfAxesUpgradeable);
                    break;
                case "Feraldis_Pasture":
                    query = QC_PastureUpgradeable.Get(em, QT_PastureUpgradeable);
                    break;
                case "Feraldis_SiegeYard":
                    query = QC_FerSiegeYardUpgradeable.Get(em, QT_FerSiegeYardUpgradeable);
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

            // ONE decision per type (2026-10-19). The old per-building walk —
            // lowest level first, Gatherer's Huts highest first so one hut at a
            // time reached the L3 survey gate — is moot: every building of a
            // type stands at the faction's level, and a new one is built at it.
            Entity best = PickHost(em, faction, query);
            if (best == Entity.Null) { DropLevelSaving(faction, buildingId); return false; }
            if (!UpgradeBuildingCommandHelper.TryGetNextCost(em, best, out var price, out byte nextLevel))
            {
                DropLevelSaving(faction, buildingId);
                return false;
            }
            int bestLevel = nextLevel - 1;

            // Army first: never spend the military line's iron on levels.
            // A PRODUCTION LINE'S LEVEL IS EXEMPT from every army-first gate
            // here (2026-10-04, Game_AI.md 5f, operator: "the buildings are
            // what allows the army to grow faster"): this iron floor, the
            // ArmyFirstYield below and the army's veilstone earmark.
            bool feedsArmy = RaisesUnitOutput(buildingId);
            // ...and its price is saved for (the pot, below) from the moment
            // the bank cannot pay it.
            if (!FactionEconomy.CanAfford(em, faction, price)
                && (feedsArmy || IsPoorObjective(buildingId)))
                SaveForLevel(em, faction, buildingId, price, bestLevel, sound: feedsArmy);
            // The level being saved for passes the army-first gates below as
            // a production level does: the pot is its money, not the army's.
            if (SavedLevelLine(faction) == buildingId) feedsArmy = true;

            // MORE BUILDINGS, A LEVEL WORTH MORE (2026-10-19): the price
            // against soldiers, scaled by how many buildings it raises. The
            // housing Hut is gated on need above instead.
            // A PRODUCTION LINE'S LEVEL IS ITS RESEARCH (2026-11-11, decision
            // 56 e): the weapon / armour / veteran tiers come only from these
            // levels now (decision 53), and the soldiers-for-price test refused
            // every one of them in both decision-55 test matches -- no AI army
            // ever got a tier. They pass on the level clock (AIPacing below).
            // A FARM LEVEL IS THE SUPPLY ENGINE (2026-11-14, Game_AI.md § 6ac):
            // judged on what it pays, not on soldiers. Measured on Moatwatch
            // (0.0.85): no Farm level in 24 faction-games, the level yielding
            // to the army up to 114 times a match while the Farms were full.
            bool farmEngine = false;
            if (buildingId == "Alanthor_Farm")
            {
                if (!FarmLevelPaysBack(em, faction, price, nextLevel)) return false;
                farmEngine = true;
                supplyEngine = true;
            }
            // ...and an Alanthor Mine level is the iron engine, judged the same
            // way (the army ran short of iron, not supplies, in batch C).
            else if (buildingId == "Mine")
            {
                if (MineLevelPaysBack(em, faction, price, nextLevel, out bool judged))
                {
                    farmEngine = true;
                    supplyEngine = true;
                }
                // Judged and refused, or no level SOs on the payback-only pass
                // outside the gap: no. Otherwise the ordinary value gate below.
                else if (judged || supplyEngine) return false;
            }

            if (buildingId != "Hut" && !feedsArmy && !farmEngine)
            {
                float perBuilding = feedsArmy ? Cfg.soldiersPerProductionBuilding
                    : buildingId == "GatherersHut" || buildingId == "Mine" || buildingId == "Alanthor_Farm"
                        ? Cfg.soldiersPerEconomyBuilding
                    : Cfg.soldiersPerOtherBuilding;
                if (!ValueGate(em, faction, best, price, perBuilding, surplus)) return false;
            }

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
            if (!feedsArmy && !housingCritical && price.Iron > 0
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
                // The level being saved for is not held by its own savings
                // goal; a strict goal (the age-up landmark, a lost trainer)
                // still comes first.
                bool savedForThis = SavedLevelLine(faction) == buildingId
                    && !AIPivotalReserve.StrictHold(em, faction);
                if (!savedForThis && AIPivotalReserve.ShouldHold(em, faction, gateCost)) return false;
                if (!feedsArmy && !housingCritical)
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

            if (outsideGap ? !AIPacing.LevelPhaseOpen(em, faction, (byte)(bestLevel + 1))
                           : !AIPacing.LevelAllowed(em, faction, (byte)(bestLevel + 1))) return false;
            // A level the pot is not saving for leaves the pot alone.
            if (!(SavedLevelLine(faction) == buildingId && AIBudget.SavingsKey(faction) == LevelReserveKey)
                && !AIBudget.LeavesSavings(em, faction, price))
                return false;
            var result = UpgradeBuildingCommandHelper.Execute(em, best,
                TheWaningBorder.Core.Commands.CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
            {
                AIPacing.NoteLevelUp(em, faction);
                AILogger.Log(faction, "BUILDING",
                    $"Upgrading {buildingId} to L{bestLevel + 1}");
                if (surplus) AILogger.Log(faction, "SURPLUS", $"upgrade {buildingId} to L{bestLevel + 1}");
                DropLevelSaving(faction, buildingId);
                return true;
            }
            return false;
        }

        // ── SAVING FOR A PRODUCTION LEVEL (2026-11-15) ──────────────────
        // Only the capital's level was a savings goal. A production line's
        // level was bought when the bank happened to hold its price, and a
        // bank the army drains never does: on Broken Oath v2 (0.0.89) no AI
        // levelled a Barracks, Range, Stable or Siege Yard in 52 minutes, so
        // no army got a weapon tier and no Trebuchet (Siege Yard L3) was ever
        // trainable. One production level at a time is now a savings goal:
        // the line keeps the goal until it is bought, and a line whose next
        // levels unlock a unit takes the goal from one that unlocks none.

        /// <summary>AIPivotalReserve key of the production level being saved for.</summary>
        private const string LevelReserveKey = "ProductionLevel";

        private static int _levelSavingEpoch = -1;
        private static readonly System.Collections.Generic.Dictionary<int, string> _levelSavingLine
            = new System.Collections.Generic.Dictionary<int, string>();

        private static string SavedLevelLine(Faction faction)
        {
            if (_levelSavingEpoch != SimCadence.Epoch)
            {
                _levelSavingEpoch = SimCadence.Epoch;
                _levelSavingLine.Clear();
                _poorChoiceUntil.Clear();
                _objectiveRolls.Clear();
            }
            return _levelSavingLine.TryGetValue((int)faction, out var id) ? id : null;
        }

        private static void DropLevelSaving(Faction faction, string buildingId)
        {
            if (SavedLevelLine(faction) != buildingId) return;
            _levelSavingLine.Remove((int)faction);
            AIPivotalReserve.Clear(faction, LevelReserveKey);
            AIBudget.ClearSavingsGoal(faction, LevelReserveKey);
        }

        private static void SaveForLevel(EntityManager em, Faction faction, string buildingId, in Cost price,
            int level, bool sound)
        {
            var tier = SimpleAISystem.TierOf(faction);
            string saved = SavedLevelLine(faction);
            if (saved != buildingId)
            {
                // The capital's price keeps the pot while it is being saved for.
                if (AIBudget.SavingsKey(faction) == CapitalReserveKey) return;
                if (saved != null)
                {
                    // A line keeps its goal until it is bought; only a sound
                    // line that unlocks a unit takes it from a sound one that
                    // unlocks none. A poor objective is never taken over.
                    if (!sound || IsPoorObjective(saved)) return;
                    if (!(LevelUnlocksUnit(em, faction, buildingId, level)
                          && !SavedLineUnlocksUnit(em, faction, saved))) return;
                }
                else if (sound ? !SoundObjectiveMayTakePot(faction, tier) : !PoorObjectiveWanted(faction))
                    return;
            }
            _levelSavingLine[(int)faction] = buildingId;
            _poorChoiceUntil.Remove((int)faction);
            AIBudget.SetSavingsGoal(faction, LevelReserveKey, price, tier.SavingsIncomeShare);
            if (saved != buildingId)
                AILogger.Log(faction, "ECON",
                    $"saving for {buildingId} L{level + 1} ({price.Supplies}s {price.Iron}i {price.Veilstone}v)");
        }

        // ── THE WRONG OBJECTIVE (2026-11-15) ────────────────────────────
        // "Easier tiers should take longer to save and save for the wrong
        // objectives sometimes." Each time the pot is free and a sound
        // objective asks for it, the tier's savingsWrongObjectiveChance is
        // rolled once; on a miss the sound objectives are turned away for
        // savingsGoalSeconds and the first poor one that cannot be paid for
        // (a House, Watch Tower or Vault level) takes the pot instead, and
        // keeps it until it is bought.

        /// <summary>Levels that win nothing: what a weak tier saves for by
        /// mistake. A roster of SO ids, not tuning.</summary>
        private static readonly string[] PoorObjectiveIds = { "Hut", "Alanthor_Tower", "VaultOfAlmierra" };

        private static bool IsPoorObjective(string buildingId)
            => System.Array.IndexOf(PoorObjectiveIds, buildingId) >= 0;

        private static readonly System.Collections.Generic.Dictionary<int, float> _poorChoiceUntil
            = new System.Collections.Generic.Dictionary<int, float>();
        private static readonly System.Collections.Generic.Dictionary<int, int> _objectiveRolls
            = new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>The tier has just chosen badly: a poor objective may take the pot.</summary>
        private static bool PoorObjectiveWanted(Faction faction)
        {
            SavedLevelLine(faction);   // per-match reset
            return _poorChoiceUntil.TryGetValue((int)faction, out float until)
                && TheWaningBorder.Core.SimClock.Now < until;
        }

        /// <summary>May a sound objective take the free pot? Rolls the tier's
        /// chance of a wrong choice once per free pot; deterministic (a hash
        /// of the faction and its roll count), so a seed replays the same.</summary>
        private static bool SoundObjectiveMayTakePot(Faction faction, in AIDifficultyProfile tier)
        {
            int key = (int)faction;
            string saved = SavedLevelLine(faction);   // also the per-match reset
            if (saved != null && IsPoorObjective(saved)) return false;   // a poor goal is being saved for
            if (PoorObjectiveWanted(faction)) return false;
            if (tier.SavingsWrongObjectiveChance <= 0f) return true;
            _objectiveRolls.TryGetValue(key, out int n);
            _objectiveRolls[key] = n + 1;
            uint h = Unity.Mathematics.math.hash(new Unity.Mathematics.int3(key, n, SimCadence.Epoch));
            if ((h & 0xFFFF) / 65536f >= tier.SavingsWrongObjectiveChance) return true;
            _poorChoiceUntil[key] = TheWaningBorder.Core.SimClock.Now + AIBudgetConfig.I.savingsGoalSeconds;
            AILogger.Log(faction, "ECON", "saving: a poor objective this time (the tier's wrong choice)");
            return false;
        }

        private static bool SavedLineUnlocksUnit(EntityManager em, Faction faction, string buildingId)
            => LevelUnlocksUnit(em, faction, buildingId,
                TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, buildingId));

        /// <summary>A unit this building trains for this faction's culture
        /// needs a higher level than <paramref name="level"/> (the unit SO's
        /// minBuildingLevel).</summary>
        private static bool LevelUnlocksUnit(EntityManager em, Faction faction, string buildingId, int level)
        {
            var def = TechCatalog.Building(buildingId);
            if (def?.trains == null) return false;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            for (int i = 0; i < def.trains.Length; i++)
            {
                string id = def.trains[i];
                if (string.IsNullOrEmpty(id)) continue;
                // Another culture's unit on a shared roster (the Barracks lists them all).
                byte of = id.StartsWith("Alanthor_") ? Cultures.Alanthor
                        : id.StartsWith("Feraldis_") ? Cultures.Feraldis
                        : id.StartsWith("Runai_") ? Cultures.Runai : Cultures.None;
                if (of != Cultures.None && of != culture) continue;
                if (TechCatalog.TryGetUnit(id, out var u) && u != null && u.minBuildingLevel > level) return true;
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
