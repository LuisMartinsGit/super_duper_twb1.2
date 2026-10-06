// TradingOutpostSystem.cs
// The Alanthor Trading Outpost's trade cycle (docs/Design/Veilstone_Economy.md §3.1).
//
// Every cycle each completed Outpost runs ITS recipe for its faction, per
// minute (the authored unit):
//
//   Buy Veilstone   (default)              supplies + iron         -> veilstone
//   Forge Veilsteel (research)             veilstone               -> veilsteel
//   Sell Veilsteel  (research)             veilsteel               -> iron + supplies
//
// (Every rate is in TradingOutpostSystem.asset; Buy is 100 veilstone a minute
// since 2026-10-03.) The discount research (Trade Agreements) cuts the INPUTS;
// the speed research (Swift Caravans) multiplies the whole trade, inputs and
// outputs alike. A cycle the faction
// cannot afford is skipped whole — nothing is spent and nothing is paid — so a
// broke player's Outposts go quiet rather than draining the bank negative. An
// Outpost whose outcrop has been cursed idles until it is pacified.
//
// Lockstep: one SimCadence clock for every Outpost (not per-building timers
// that start on whatever frame construction finished), Outposts walked in query
// order, and the fractional carry lives on the entity — so every peer spends
// and pays the same amounts on the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.World;
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
            ComponentType.ReadOnly<TradingOutpostMode>(),
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

        // ── The recipes, per minute ──────────────────────────────────────

        /// <summary>True when this faction may run this recipe (Buy always;
        /// the other two once their research is done).</summary>
        public static bool IsUnlocked(Faction faction, TradeRecipe recipe)
        {
            var cfg = Cfg;
            if (cfg == null) return recipe == TradeRecipe.BuyVeilstone;
            string tech = recipe switch
            {
                TradeRecipe.ForgeVeilsteel => cfg.forgeTech,
                TradeRecipe.SellVeilsteel => cfg.sellTech,
                _ => null,
            };
            if (string.IsNullOrEmpty(tech)) return true;
            var research = FactionResearchState.Instance;
            return research != null && research.HasResearched(faction, tech);
        }

        /// <summary>The input discount this faction has researched, 0-1.</summary>
        public static float Discount(Faction faction)
        {
            var cfg = Cfg;
            var research = FactionResearchState.Instance;
            if (cfg?.discountTechs == null || cfg.discountPercent == null || research == null) return 0f;
            float best = 0f;
            int n = Mathf.Min(cfg.discountTechs.Length, cfg.discountPercent.Length);
            for (int i = 0; i < n; i++)
                if (research.HasResearched(faction, cfg.discountTechs[i]))
                    best = Mathf.Max(best, cfg.discountPercent[i]);
            return Mathf.Clamp01(best / 100f);
        }

        /// <summary>
        /// The trade-speed multiplier this faction has researched (1 = none):
        /// 1 + the sum of every researched speed tech's TradeSpeed percent / 100.
        /// The percent is DATA on the tech SO (TechCatalog.TechEffect).
        /// </summary>
        public static float SpeedMultiplier(Faction faction)
        {
            var cfg = Cfg;
            var research = FactionResearchState.Instance;
            if (cfg?.speedTechs == null || research == null) return 1f;
            float pct = 0f;
            for (int i = 0; i < cfg.speedTechs.Length; i++)
                if (!string.IsNullOrEmpty(cfg.speedTechs[i])
                    && research.HasResearched(faction, cfg.speedTechs[i]))
                    pct += TechCatalog.TechEffect(cfg.speedTechs[i], TradeSpeedStat);
            return Mathf.Max(0f, 1f + pct / 100f);
        }

        /// <summary>The effectsList stat a speed tech carries.</summary>
        public const string TradeSpeedStat = "TradeSpeed";

        /// <summary>
        /// What a recipe spends and earns per minute for this faction, discount
        /// applied to the inputs and the speed research to both sides. The cycle, the action panel and the income
        /// overlay all read this, so none of them can disagree.
        /// </summary>
        public static void PerMinute(Faction faction, TradeRecipe recipe,
            out TerritoryYield spend, out TerritoryYield earn)
        {
            spend = default;
            earn = default;
            var cfg = Cfg;
            if (cfg == null) return;
            float speed = SpeedMultiplier(faction);
            float keep = (1f - Discount(faction)) * speed;
            switch (recipe)
            {
                case TradeRecipe.ForgeVeilsteel:
                    spend.Veilstone = cfg.forgeVeilstone * keep;
                    earn.Veilsteel = cfg.forgeVeilsteel * speed;
                    break;
                case TradeRecipe.SellVeilsteel:
                    spend.Veilsteel = cfg.sellVeilsteel * keep;
                    earn.Iron = cfg.sellIron * speed;
                    earn.Supplies = cfg.sellSupplies * speed;
                    break;
                case TradeRecipe.Hold:
                    break;   // idle: nothing in, nothing out
                default:
                    spend.Supplies = cfg.buySupplies * keep;
                    spend.Iron = cfg.buyIron * keep;
                    earn.Veilstone = cfg.buyVeilstone * speed;
                    break;
            }
        }

        /// <summary>
        /// The post's own LEVEL multiplier on its trade (2026-10-04): the
        /// `tradeRateMultiplier` of its Alanthor level SO
        /// (TradingOutpost_Lvl1..3), spend and earn alike — a levelled post
        /// trades faster, it does not trade cheaper. 1 before its first level
        /// or for a rung with no multiplier authored.
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

        /// <summary>
        /// What ONE post spends and earns per minute: its own recipe (Buy when
        /// that one is not researched), the faction's discount and speed, and
        /// the post's level. The cycle, the selection panel and the income
        /// overlay all read this.
        /// </summary>
        public static void PerMinuteFor(EntityManager em, Entity outpost,
            out TerritoryYield spend, out TerritoryYield earn)
        {
            spend = default;
            earn = default;
            if (!em.HasComponent<FactionTag>(outpost)) return;
            var faction = em.GetComponentData<FactionTag>(outpost).Value;
            var recipe = OutpostSites.RecipeOf(em, outpost);
            if (!IsUnlocked(faction, recipe)) recipe = TradeRecipe.BuyVeilstone;
            PerMinute(faction, recipe, out spend, out earn);
            float m = LevelMultiplier(em, outpost);
            if (m != 1f) { spend = Scale(spend, m); earn = Scale(earn, m); }
        }

        private static TerritoryYield Scale(TerritoryYield y, float m) => new TerritoryYield
        {
            Supplies = y.Supplies * m, Iron = y.Iron * m,
            Veilstone = y.Veilstone * m, Veilsteel = y.Veilsteel * m,
        };

        // ── The cycle ───────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            var cfg = Cfg;
            if (cfg == null || cfg.cycleSeconds <= 0f) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, cfg.cycleSeconds)) return;

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
                    em.AddComponentData(bare[i],
                        OutpostSites.SiteFor(em, bare[i], at));
                }
            }

            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var modes = q.ToComponentDataArray<TradingOutpostMode>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float minutes = cfg.cycleSeconds / 60f;

            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                if (!OutpostSites.HasLiveOutcrop(em, p.x, p.z)) continue;

                var faction = facs[i].Value;
                // A post on Hold trades nothing (its carry is kept as is).
                if (modes[i].Recipe == TradeRecipe.Hold) continue;
                // Recipe (Buy when its trade is not researched), discount,
                // speed and THIS post's level — four posts round one outcrop
                // each trade on their own.
                PerMinuteFor(em, ents[i], out var spendPm, out var earnPm);

                var carry = em.GetComponentData<TradingOutpostCarry>(ents[i]);
                var next = carry;
                var input = new Cost
                {
                    Supplies  = Take(ref next.InSupplies,  spendPm.Supplies  * minutes),
                    Iron      = Take(ref next.InIron,      spendPm.Iron      * minutes),
                    Veilstone = Take(ref next.InVeilstone, spendPm.Veilstone * minutes),
                    Veilsteel = Take(ref next.InVeilsteel, spendPm.Veilsteel * minutes),
                };
                if (!FactionEconomy.CanAfford(em, faction, input)) continue;   // carry untouched
                if (!FactionEconomy.Spend(em, faction, input, TheWaningBorder.Economy.SpendCategory.Trade)) continue;

                var output = new Cost
                {
                    Supplies  = Take(ref next.OutSupplies,  earnPm.Supplies  * minutes),
                    Iron      = Take(ref next.OutIron,      earnPm.Iron      * minutes),
                    Veilstone = Take(ref next.OutVeilstone, earnPm.Veilstone * minutes),
                    Veilsteel = Take(ref next.OutVeilsteel, earnPm.Veilsteel * minutes),
                };
                FactionEconomy.Add(em, faction, output, TheWaningBorder.Economy.IncomeSource.Trade);
                em.SetComponentData(ents[i], next);
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
