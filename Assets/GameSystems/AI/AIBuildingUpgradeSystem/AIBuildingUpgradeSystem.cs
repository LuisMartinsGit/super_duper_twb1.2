// AIBuildingUpgradeSystem.cs
// Culture-agnostic AI driver for the building upgrade system.
//
// Each AI brain that's Era >= 2 with a non-None culture picks ONE
// upgradeable building per tick (slowest cadence so it doesn't dominate
// the build queue) and tries UpgradeBuildingCommandHelper.Execute on it.
// The walk is Smelter-first, then a round-robin over PriorityOrder (every
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

        static readonly ComponentType[] QT_SmelterTagFactionTag =
        {
            ComponentType.ReadOnly<SmelterTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_SmelterTagFactionTag;

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

        static readonly ComponentType[] QT_SmelterTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<SmelterTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_SmelterTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_VaultTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<VaultTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_VaultTagBuildingUpgradeableFactionTag;

        static readonly ComponentType[] QT_ShrineTagBuildingUpgradeableFactionTag =
        {
            ComponentType.ReadOnly<ShrineTag>(),
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_ShrineTagBuildingUpgradeableFactionTag;

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
        // The walk is now: Smelter strictly first (the veilsteel engine
        // compounds), then a ROUND-ROBIN start index across the rest so every
        // line gets a turn.
        // 2026-08-10 (endgame completeness): the choice buildings
        // (VaultOfAlmierra / ShrineOfRidan — both carry BuildingUpgradeable
        // and have cost rows) and the Wall hub line join the rotation so the
        // AI eventually levels EVERYTHING it owns. The wall entry is
        // forward-wired: hubs don't carry BuildingUpgradeable or a
        // BuildingUpgradeConfig cost row yet, so it no-ops until the wall
        // ladder ships — wall Tower/Gate CONVERSIONS stay with
        // WallUpgradeSystem and are NOT driven from here.
        private static readonly string[] PriorityOrder =
            { "GatherersHut", "Hall", "Barracks", "Hut", "ArcheryRange",
              "Alanthor_RoyalStable", "Alanthor_SiegeYard", "Alanthor_Tower",
              "VaultOfAlmierra", "ShrineOfRidan", "Alanthor_Wall" };


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
                float think = Cfg.thinkInterval
                            * AISimpleDifficulty.GetProfile(brain.Difficulty).SupportThinkScale;

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

                // Walk the priority order from a rotating start so no single
                // building line (log-proven: the hut line) monopolizes the loop.
                var tickState = em.GetComponentData<AIBuildingUpgradeTickState>(brainEntity);
                int rotation = tickState.Rotation;
                tickState.Rotation = (byte)((rotation + 1) % PriorityOrder.Length);
                em.SetComponentData(brainEntity, tickState);

                TryUpgradeOne(em, faction, rotation, reservesOk);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// True while the faction owns at least one Smelter and NONE of them
        /// has reached max level — i.e. the veilsteel engine is not running
        /// at full rate yet, so the reserve that protects its levels applies.
        ///
        /// Was "ANY Smelter below max" (2026-08-18): with a cap of five
        /// Smelters, and a new one dropping the fleet back below max every
        /// time it is built, that condition effectively never cleared. The
        /// reserve then blocked every veilsteel-costing upgrade for the whole
        /// match — the Guild ladder (L2 costs 5 veilsteel, L3 costs 20) needed
        /// 65 banked to spend 5, so the supply economy never grew while
        /// veilsteel-free tower upgrades sailed past it 61 to 4. One maxed
        /// Smelter is the engine this was protecting; after that the drip is
        /// at full rate and the rest of the estate may spend.
        /// </summary>
        private static bool SmelterBelowMax(EntityManager em, Faction faction)
        {
            var query = QC_SmelterTagFactionTag.Get(em, QT_SmelterTagFactionTag);
            using var ents = query.ToEntityArray(Allocator.Temp);
            bool ownsAny = false;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                ownsAny = true;
                byte lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : (byte)0;
                if (lvl >= BuildingUpgradeConfig.MaxLevel) return false;   // engine is running
            }
            return ownsAny;
        }

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

        private static void TryUpgradeOne(EntityManager em, Faction faction, int rotation,
            bool reservesOk)
        {
            // Below the reserve floor only the supply engine is eligible —
            // see the exemption note at the call site.
            if (!reservesOk)
            {
                TryUpgradeBuildingType(em, faction, "GatherersHut");
                return;
            }

            // The Smelter jumps the queue: its levels multiply the veilsteel
            // drip that every OTHER upgrade past L1 wants to spend.
            if (TryUpgradeBuildingType(em, faction, "Alanthor_Smelter")) return;

            for (int p = 0; p < PriorityOrder.Length; p++)
            {
                int idx = (rotation + p) % PriorityOrder.Length;
                if (TryUpgradeBuildingType(em, faction, PriorityOrder[idx])) return;
            }
        }

        /// <summary>
        /// Find the LOWEST-LEVEL building of the given type owned by the
        /// faction. Lowest level = highest marginal benefit per upgrade
        /// click (uncultured Hall → L1 unlocks the multi-target chain).
        /// Returns true if the upgrade was queued.
        /// </summary>
        private static bool TryUpgradeBuildingType(EntityManager em, Faction faction, string buildingId)
        {
            EntityQuery query;
            switch (buildingId)
            {
                case "Hall":
                    query = QC_HallTagBuildingUpgradeableFactionTag.Get(em, QT_HallTagBuildingUpgradeableFactionTag);
                    break;
                case "Barracks":
                    query = QC_BarracksTagBuildingUpgradeableFactionTag.Get(em, QT_BarracksTagBuildingUpgradeableFactionTag);
                    break;
                case "Hut":
                    query = QC_HutTagBuildingUpgradeableFactionTag.Get(em, QT_HutTagBuildingUpgradeableFactionTag);
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
                case "Alanthor_Smelter":
                    query = QC_SmelterTagBuildingUpgradeableFactionTag.Get(em, QT_SmelterTagBuildingUpgradeableFactionTag);
                    break;
                case "VaultOfAlmierra":
                    query = QC_VaultTagBuildingUpgradeableFactionTag.Get(em, QT_VaultTagBuildingUpgradeableFactionTag);
                    break;
                case "ShrineOfRidan":
                    query = QC_ShrineTagBuildingUpgradeableFactionTag.Get(em, QT_ShrineTagBuildingUpgradeableFactionTag);
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
                if (em.HasComponent<BuildingUpgrading>(ents[i])) continue;

                byte lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : (byte)0;
                if (lvl >= BuildingUpgradeConfig.MaxLevel) continue;

                bool better = highestFirst ? lvl > bestLevel : lvl < bestLevel;
                if (better) { bestLevel = lvl; best = ents[i]; }
            }
            if (best == Entity.Null) return false;

            // Army first: never spend the military line's iron on levels.
            if (FactionEconomy.TryGetBank(em, faction, out var upgradeBank)
                && em.GetComponentData<FactionResources>(upgradeBank).Iron < Cfg.upgradeIronReserve)
                return false;

            // Veilsteel engine first: while the faction's Smelter is below max
            // level, upgrades that COST veilsteel must leave the Smelter's
            // reserve untouched (L2+L3 need 90 total; the L1 drip is 6/min).
            // The Smelter's own upgrade is exempt — it IS the reserve's purpose.
            if (buildingId != "Alanthor_Smelter"
                && BuildingUpgradeConfig.TryGetCost(buildingId, (byte)(bestLevel + 1), out var nextCost)
                && nextCost.Veilsteel > 0
                && SmelterBelowMax(em, faction)
                && FactionEconomy.TryGetResources(em, faction, out var vsRes)
                && vsRes.Veilsteel < nextCost.Veilsteel + Cfg.smelterVeilsteelReserve)
                return false;

            var result = UpgradeBuildingCommandHelper.Execute(em, best,
                TheWaningBorder.Core.Commands.CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
            {
                AILogger.Log(faction, "BUILDING",
                    $"Upgrading {buildingId} to L{bestLevel + 1}");
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
