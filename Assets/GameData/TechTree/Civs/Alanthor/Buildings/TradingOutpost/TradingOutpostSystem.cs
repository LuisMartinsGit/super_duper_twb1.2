// TradingOutpostSystem.cs
// The Alanthor Trading Outpost's trickle (docs/Design/Veilstone_Economy.md §3.1,
// rewritten 2026-11-09, decision 42).
//
// Every cycle each completed Outpost pays its faction a trickle of the resource
// of the node it stands beside — supplies beside a supply node, iron beside an
// iron node, veilstone beside an uncursed veilstone outcrop — at the per-minute
// rate on TradingOutpostSystem.asset, times the node's purity and the post's
// level (tradeRateMultiplier on TradingOutpost_Lvl1..3).
//
// It is NEVER drawn from the node. It pays only while the node lives (exists,
// is not spent, is not Cursed) and lies in ground the post's faction holds.
// A spent node collapses with every post beside it (NodeCollapse), so "stop
// extracting in time" is the player's decision (§3.5).
//
// Lockstep: one SimCadence clock for every Outpost, Outposts walked in query
// order, the fractional carry on the entity — every peer pays the same amounts
// on the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.World;
using TheWaningBorder.World.Regions;
// Alias: inside a SystemBase the source generator reads "...Entities.X(...)"
// as an Entities.ForEach chain (DC0062), so the namespace is never spelled out.
using OutpostSites = TheWaningBorder.Entities.TradingOutpost;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class TradingOutpostSystem : SystemBase
    {
        private static TradingOutpostSystemConfig _cfg;
        /// <summary>The numbers, from TradingOutpostSystem.asset beside this file.</summary>
        public static TradingOutpostSystemConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<TradingOutpostSystemConfig>());

        private SimCadence.Periodic _acc;

        static readonly ComponentType[] QT_Outposts =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static readonly ComponentType[] QT_MissingCarry =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.Exclude<TradingOutpostCarry>(),
        };
        static readonly ComponentType[] QT_MissingSite =
        {
            ComponentType.ReadOnly<TradingOutpostTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<TradingOutpostSite>(),
        };
        static CachedEntityQuery QC_Outposts, QC_MissingCarry, QC_MissingSite;

        protected override void OnCreate()
        {
            RequireForUpdate<TradingOutpostTag>();
        }

        // ── Unlock ───────────────────────────────────────────────────────

        /// <summary>True once this faction has researched the Outpost's unlock
        /// (Trade Charters). The build menu, the router and the AI read it.</summary>
        public static bool IsUnlocked(Faction faction)
        {
            var cfg = Cfg;
            if (cfg == null || string.IsNullOrEmpty(cfg.unlockTech)) return true;
            var research = FactionResearchState.Instance;
            return research != null && research.HasResearched(faction, cfg.unlockTech);
        }

        // ── The rate ─────────────────────────────────────────────────────

        /// <summary>
        /// The post's own LEVEL multiplier on its trickle: the
        /// `tradeRateMultiplier` of its Alanthor level SO
        /// (TradingOutpost_Lvl1..3). 1 before its first level or for a rung
        /// with no multiplier authored.
        /// </summary>
        public static float LevelMultiplier(EntityManager em, Entity outpost)
        {
            if (!em.HasComponent<BuildingUpgradeState>(outpost) || !em.HasComponent<FactionTag>(outpost))
                return 1f;
            byte level = em.GetComponentData<BuildingUpgradeState>(outpost).Level;
            if (level == 0) return 1f;
            var def = BuildingUpgradeConfig.LevelDef(em, em.GetComponentData<FactionTag>(outpost).Value,
                OutpostSites.BuildingId, level);
            return def != null && def.tradeRateMultiplier > 0f ? def.tradeRateMultiplier : 1f;
        }

        /// <summary>The faction's Trading Outpost research (decision 49): Merchant
        /// Houses replaces Trade Ledgers; 1 with neither.</summary>
        public static float ResearchMultiplier(Faction faction)
        {
            var cfg = Cfg;
            var r = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (cfg == null || r == null) return 1f;
            if (!string.IsNullOrEmpty(cfg.merchantHousesTech) && r.HasResearched(faction, cfg.merchantHousesTech))
                return cfg.merchantHousesMultiplier > 0f ? cfg.merchantHousesMultiplier : 1f;
            if (!string.IsNullOrEmpty(cfg.ledgersTech) && r.HasResearched(faction, cfg.ledgersTech))
                return cfg.ledgersMultiplier > 0f ? cfg.ledgersMultiplier : 1f;
            return 1f;
        }

        /// <summary>The base per-minute trickle beside a node of this kind.</summary>
        public static float BaseRate(OutpostSites.NodeKind kind)
        {
            var cfg = Cfg;
            if (cfg == null) return 0f;
            return kind switch
            {
                OutpostSites.NodeKind.Supply => cfg.trickleSuppliesPerMinute,
                OutpostSites.NodeKind.Iron => cfg.trickleIronPerMinute,
                OutpostSites.NodeKind.Veilstone => cfg.trickleVeilstonePerMinute,
                _ => 0f,
            };
        }

        /// <summary>
        /// What ONE post pays per minute right now: its node's resource at the
        /// trickle rate, times the node's purity and the post's level — or
        /// nothing while it is unfinished or its node is dead, cursed or in
        /// ground its faction does not hold. The cycle, the selection panel and
        /// the income overlay all read this, so none of them can disagree.
        /// </summary>
        public static TerritoryYield PerMinuteFor(EntityManager em, Entity outpost)
        {
            var y = new TerritoryYield();
            if (!em.Exists(outpost) || em.HasComponent<UnderConstruction>(outpost)) return y;
            if (!em.HasComponent<FactionTag>(outpost) || !em.HasComponent<LocalTransform>(outpost)) return y;
            var faction = em.GetComponentData<FactionTag>(outpost).Value;
            var p = em.GetComponentData<LocalTransform>(outpost).Position;
            if (!OutpostSites.TryGetLiveNode(em, p.x, p.z, faction, out var node, out var kind)) return y;
            float rate = BaseRate(kind)
                         * TerritoryResources.PurityMultiplier(em, node)
                         * LevelMultiplier(em, outpost)
                         * ResearchMultiplier(faction);
            switch (kind)
            {
                case OutpostSites.NodeKind.Supply: y.Supplies = rate; break;
                case OutpostSites.NodeKind.Iron: y.Iron = rate; break;
                case OutpostSites.NodeKind.Veilstone: y.Veilstone = rate; break;
            }
            return y;
        }

        // ── The cycle ───────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            var cfg = Cfg;
            if (cfg == null || cfg.cycleSeconds <= 0f) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, cfg.cycleSeconds)) return;
            if (!RegionMap.Ready) return;

            var em = EntityManager;
            var missing = QC_MissingCarry.Get(em, QT_MissingCarry);
            if (!missing.IsEmptyIgnoreFilter) em.AddComponent<TradingOutpostCarry>(missing);

            // Posts raised through the ECB factory path carry no site record
            // yet: resolve it here (query order, sim state only — every peer
            // stamps the same values on the same tick).
            var noSite = QC_MissingSite.Get(em, QT_MissingSite);
            if (!noSite.IsEmptyIgnoreFilter)
            {
                using var bare = noSite.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < bare.Length; i++)
                {
                    var at = em.GetComponentData<LocalTransform>(bare[i]).Position;
                    em.AddComponentData(bare[i], OutpostSites.SiteFor(em, bare[i], at));
                }
            }

            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            float minutes = cfg.cycleSeconds / 60f;

            for (int i = 0; i < ents.Length; i++)
            {
                var earn = PerMinuteFor(em, ents[i]);
                if (earn.IsEmpty) continue;
                var carry = em.GetComponentData<TradingOutpostCarry>(ents[i]);
                var pay = new Cost
                {
                    Supplies  = Take(ref carry.Supplies,  earn.Supplies  * minutes),
                    Iron      = Take(ref carry.Iron,      earn.Iron      * minutes),
                    Veilstone = Take(ref carry.Veilstone, earn.Veilstone * minutes),
                };
                em.SetComponentData(ents[i], carry);
                if (!pay.IsZero)
                    FactionEconomy.Add(em, facs[i].Value, pay, TheWaningBorder.Economy.IncomeSource.Trade);
            }
        }

        /// <summary>Add this cycle's fractional amount to the carry and hand
        /// back the whole units now due.</summary>
        private static int Take(ref float carry, float amount)
        {
            carry += amount;
            int whole = Mathf.FloorToInt(carry);
            carry -= whole;
            return whole;
        }
    }
}
