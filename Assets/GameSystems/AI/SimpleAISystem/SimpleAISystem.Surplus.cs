// SimpleAISystem.Surplus.cs
// Where an idle bank goes once the basics cap is hard: veilstone first, then
// upgrades. Partial of SimpleAISystem.cs. docs/Design/Game_AI.md 5e is the
// design; the thresholds are AIBudget.asset (surplusSupplies / surplusIron),
// the rest SimpleAISystem.asset (surplus*) and AIBuildingUpgradeSystem.asset.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-04, the developer's ruling)
//
// The "basics fill" let an Alanthor army train veilstone-free units past the
// basics share whenever only veilstone was short and supplies and iron piled
// up. A 60-minute headless batch turned every army into 84% Spearman/Archer
// and starved the role units. The ruling, verbatim: "The initial share on
// basic units is fixed, even if there is population for them. The plan must
// always be to grab more veilstone and, do upgrades".
//
// So the cap is hard (SimpleAISystem.Composition.cs), and the surplus it
// leaves is spent by the existing spenders, each extended where it lives:
//   * EnsureExtractors          an Outpost on EVERY free outcrop in one pass
//   * TickSurplus (this file)   the Outpost's speed then discount research,
//                               and one Outpost level-up (cheapest first)
//   * CollectClaimCandidates /  outcrop ground weighs more, claim rounds come
//     EnsureTerritoryClaim      round faster
//   * EnsureFortressExpansion   outcrop ground weighs more; a Fortress there
//                               may use the army's veilstone earmark
//   * TickEndgameResearchSweep  faster, ladder-free, Outpost and the plan's
//                               trainers first, no veilstone while held
//   * AIBuildingUpgradeSystem   several level-ups per think
// Every action logs one SURPLUS line, so a headless batch can count them.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>
        /// VEILSTONE SURPLUS: an aged-up Alanthor faction whose army is short
        /// of veilstone (AIBudget.IsMilitaryShort — the composition records
        /// it every think while it saves for a role unit) while the bank
        /// overflows with supplies and iron (AIBudget.BankOverflowing).
        /// Only Alanthor buy veilstone at Outposts (Veilstone_Economy.md).
        /// </summary>
        private static bool IsVeilstoneSurplus(EntityManager em, Faction faction)
            => FactionCultureOf(em, faction) == Cultures.Alanthor
               && FactionEra(em, faction) >= 2
               && AIBudget.VeilstoneHeldSurplus(em, faction);

        /// <summary>One SURPLUS line per action, unthrottled — each is a real
        /// purchase, so it cannot spam faster than the bank allows.</summary>
        private static void LogSurplus(Faction faction, string message)
            => AILogger.Log(faction, "SURPLUS", message);

        private readonly Dictionary<int, float> _nextSurplusLog = new Dictionary<int, float>();

        /// <summary>
        /// Every think, after the extractor walk and the Outpost modes: while
        /// veilstone-held, buy the Trading Outpost's research (speed first —
        /// it is the only one that raises veilstone bought per minute — then
        /// the discounts), and once per surplusLogInterval say the state.
        /// </summary>
        private void TickSurplus(EntityManager em, Faction faction, float now)
        {
            if (!TechCatalog.IsReady) return;
            if (!IsVeilstoneSurplus(em, faction)) return;

            TrySurplusTradeResearch(em, faction);
            TrySurplusOutpostUpgrade(em, faction);

            int key = (int)faction;
            if (!AILogger.Enabled) return;
            if (_nextSurplusLog.TryGetValue(key, out float next) && now < next) return;
            _nextSurplusLog[key] = now + Cfg.surplusLogInterval;

            FactionEconomy.TryGetResources(em, faction, out var bank);
            int outposts = 0;
            {
                var q = QC_Outposts.Get(em, QT_Outposts);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < facs.Length; i++) if (facs[i].Value == faction) outposts++;
            }
            int free = 0;
            if (RegionMap.Ready && TerritoryOwnership.Ready)
            {
                _nodeSiteOwned.Clear();
                _nodeSiteOwned.UnionWith(TerritoryOwnership.TerritoriesOf(faction));
                _nodeSites.Clear();
                CollectFreeNodes(em, faction, TheWaningBorder.Entities.TradingOutpost.BuildingId,
                    _nodeSiteOwned, _nodeSites);
                free = _nodeSites.Count;
            }
            var p = GetArmyPlan(em, faction);
            LogSurplus(faction,
                $"veilstone-held (supplies {bank.Supplies}, iron {bank.Iron}, veilstone {bank.Veilstone}, " +
                $"outposts {outposts}, free outcrops {free}, saving for {p.SavingFor ?? "a role unit"})");
        }

        /// <summary>
        /// The Trading Outpost's own research, from TradingOutpostSystem.asset
        /// (speedTechs, then discountTechs) — never an id list in the AI. One
        /// per think. These pass the savings hold and the army's veilstone
        /// earmark: they are the income the earmark is waiting on.
        /// </summary>
        private static void TrySurplusTradeResearch(EntityManager em, Faction faction)
        {
            var cfg = TheWaningBorder.Systems.Economy.TradingOutpostSystem.Cfg;
            if (cfg == null) return;
            Entity host = FindResearchHost<TradingOutpostTag>(em, faction);
            if (host == Entity.Null) return;
            if (TryTradeResearchFrom(em, faction, host, cfg.speedTechs)) return;
            TryTradeResearchFrom(em, faction, host, cfg.discountTechs);
        }

        /// <summary>
        /// Level ONE Trading Outpost per think while veilstone-held (2026-10-04,
        /// Veilstone_Economy.md §3.1): a level raises the post's trade rate,
        /// which is veilstone per minute. The cheapest next level first — the
        /// per-outcrop ramp makes a first post's level-up cheaper than a
        /// fourth's — and never one that dips into the army's veilstone
        /// earmark. L1 is the free age-up level and is never bought here.
        /// </summary>
        private static void TrySurplusOutpostUpgrade(EntityManager em, Faction faction)
        {
            var q = QC_Outposts.Get(em, QT_Outposts);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            Entity best = Entity.Null;
            Cost bestCost = default;
            byte bestLevel = 0;
            int bestTotal = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.IsUpgradeQueued(em, ents[i]))
                    continue;
                if (!TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.TryGetNextCost(
                        em, ents[i], out var cost, out byte next)) continue;
                if (next <= 1) continue;
                int total = cost.Supplies + cost.Iron + cost.Veilstone + cost.Veilsteel;
                if (total >= bestTotal) continue;
                best = ents[i]; bestCost = cost; bestLevel = next; bestTotal = total;
            }
            if (best == Entity.Null) return;
            if (!FactionEconomy.CanAfford(em, faction, bestCost)) return;
            if (!AIBudget.LeavesMilitaryVeilstone(em, faction, bestCost)) return;

            var result = TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.Execute(
                em, best, TheWaningBorder.Core.Commands.CommandSource.AI);
            if (result != UpgradeBuildingResult.Ok) return;
            InvalidateThinkMemo();
            AILogger.Log(faction, "BUILDING",
                $"Upgrading {TheWaningBorder.Entities.TradingOutpost.BuildingId} to L{bestLevel}");
            LogSurplus(faction, $"upgrade {TheWaningBorder.Entities.TradingOutpost.BuildingId} to L{bestLevel} " +
                                $"({bestCost.Supplies}s {bestCost.Iron}i)");
        }

        private static bool TryTradeResearchFrom(EntityManager em, Faction faction, Entity host, string[] ids)
        {
            if (ids == null) return false;
            var research = FactionResearchState.Instance;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            int level = 1;
            if (em.HasComponent<BuildingUpgradeState>(host))
                level = math.max(level, em.GetComponentData<BuildingUpgradeState>(host).Level);

            for (int i = 0; i < ids.Length; i++)
            {
                string techId = ids[i];
                if (string.IsNullOrEmpty(techId)) continue;
                if (!TechCatalog.TryGetTechnology(techId, out var tech) || tech == null) continue;
                if (research != null && research.HasResearched(faction, techId)) continue;
                if (IsResearchInFlight(em, faction, techId)) continue;
                if (!TechCatalog.CultureAllows(tech, culture)) continue;
                if (math.max(1, tech.minBuildingLevel) > level) continue;
                if (research != null && !research.MeetsPrerequisites(faction, tech.prerequisites)) continue;
                var cost = AICommon.ToCost(tech.cost);
                if (!FactionEconomy.CanAfford(em, faction, cost)) continue;

                TheWaningBorder.Core.Commands.CommandRouter.IssueResearch(
                    em, host, techId, TheWaningBorder.Core.Commands.CommandSource.AI);
                InvalidateThinkMemo();
                AILogger.Log(faction, "RESEARCH", $"{techId} queued (veilstone surplus)");
                LogSurplus(faction, $"research {techId} at {TheWaningBorder.Entities.TradingOutpost.BuildingId}");
                return true;
            }
            return false;
        }

        /// <summary>Host tiers of the research sweep (Game_AI.md 5e).</summary>
        private const int SweepHostTiers = 3;

        /// <summary>0 = Trading Outpost; 1 = trains a unit the composition
        /// plan has a share for; 2 = everything else.</summary>
        private static int SweepHostTier(EntityManager em, Entity building, ArmyPlan plan)
        {
            if (em.HasComponent<TradingOutpostTag>(building)) return 0;
            if (plan == null || !plan.Active) return 2;
            string buildingId = TheWaningBorder.Entities.BuildingIds.Of(building, em);
            if (string.IsNullOrEmpty(buildingId)
                || !TechCatalog.TryGetBuilding(buildingId, out var def) || def?.trains == null)
                return 2;
            for (int i = 0; i < def.trains.Length; i++)
            {
                int r = RowIndex(plan, def.trains[i]);
                if (r >= 0 && plan.Share[r] > 0f) return 1;
            }
            return 2;
        }
    }
}
