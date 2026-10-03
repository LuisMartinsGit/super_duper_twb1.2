// TradingOutpostSystem.cs
// The Alanthor Trading Outpost's trade cycle (docs/Design/Veilstone_Economy.md §3.1).
//
// Every cycle each completed Outpost runs ITS recipe for its faction, per
// minute (the authored unit):
//
//   Buy Veilstone   (default)              -50 supplies -50 iron   -> +65 veilstone
//   Forge Veilsteel (research)             -50 veilstone           -> +10 veilsteel
//   Sell Veilsteel  (research)             -50 veilsteel           -> +300 iron +450 supplies
//
// The discount research (20 / 45 / 75 %) cuts the INPUTS. A cycle the faction
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
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.World;

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
        static CachedEntityQuery QC_Outposts, QC_MissingCarry;

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
        /// What a recipe spends and earns per minute for this faction, discount
        /// applied to the inputs. The cycle, the action panel and the income
        /// overlay all read this, so none of them can disagree.
        /// </summary>
        public static void PerMinute(Faction faction, TradeRecipe recipe,
            out TerritoryYield spend, out TerritoryYield earn)
        {
            spend = default;
            earn = default;
            var cfg = Cfg;
            if (cfg == null) return;
            float keep = 1f - Discount(faction);
            switch (recipe)
            {
                case TradeRecipe.ForgeVeilsteel:
                    spend.Veilstone = cfg.forgeVeilstone * keep;
                    earn.Veilsteel = cfg.forgeVeilsteel;
                    break;
                case TradeRecipe.SellVeilsteel:
                    spend.Veilsteel = cfg.sellVeilsteel * keep;
                    earn.Iron = cfg.sellIron;
                    earn.Supplies = cfg.sellSupplies;
                    break;
                default:
                    spend.Supplies = cfg.buySupplies * keep;
                    spend.Iron = cfg.buyIron * keep;
                    earn.Veilstone = cfg.buyVeilstone;
                    break;
            }
        }

        // ── The cycle ───────────────────────────────────────────────────

        protected override void OnUpdate()
        {
            var cfg = Cfg;
            if (cfg == null || cfg.cycleSeconds <= 0f) return;
            if (!_acc.Due(SystemAPI.Time.DeltaTime, cfg.cycleSeconds)) return;

            var em = EntityManager;
            var missing = QC_MissingCarry.Get(em, QT_MissingCarry);
            if (!missing.IsEmptyIgnoreFilter) em.AddComponent<TradingOutpostCarry>(missing);

            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var modes = q.ToComponentDataArray<TradingOutpostMode>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float minutes = cfg.cycleSeconds / 60f;

            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                if (!TheWaningBorder.Entities.TradingOutpost.HasLiveOutcrop(em, p.x, p.z)) continue;

                var faction = facs[i].Value;
                var recipe = modes[i].Recipe;
                if (!IsUnlocked(faction, recipe)) recipe = TradeRecipe.BuyVeilstone;
                PerMinute(faction, recipe, out var spendPm, out var earnPm);

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
                if (!FactionEconomy.Spend(em, faction, input)) continue;

                var output = new Cost
                {
                    Supplies  = Take(ref next.OutSupplies,  earnPm.Supplies  * minutes),
                    Iron      = Take(ref next.OutIron,      earnPm.Iron      * minutes),
                    Veilstone = Take(ref next.OutVeilstone, earnPm.Veilstone * minutes),
                    Veilsteel = Take(ref next.OutVeilsteel, earnPm.Veilsteel * minutes),
                };
                FactionEconomy.Add(em, faction, output);
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
