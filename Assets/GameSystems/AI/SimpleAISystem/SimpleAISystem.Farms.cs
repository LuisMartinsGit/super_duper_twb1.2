// SimpleAISystem.Farms.cs
// FARMS AND THE MARKET FOR THE ALANTHOR AI (Unification decision 55,
// docs/Design/Age_1_Alanthor.md § The Farm / § The Market). One decision
// every SimpleAISystemConfig.farmThinkInterval seconds, from the economy
// think (SimpleAISystem.cs, case 5):
//
//   FARMS   — keep farmSteadyCount Farms; while banked supplies are below
//             farmShortSupplies (supply income is short), build on up to the
//             Farm SO's cap. One Farm at a time: no new one while one is still
//             a plan or a site.
//   SITE    — (decision 57) the site search scores farmSiteCandidates legal
//             sites by their crop ring's free share (FarmRing) and takes the
//             best, refusing any below farmMinFreePermille; every other
//             building keeps off the faction's Farm rings in its normal
//             passes (farmRingKeepClear) — SimpleAISystem.Building.cs,
//             TryFindBuildPosition. Farm LEVELS are AIBuildingUpgradeSystem's
//             economy ladder.
//   STAFF   — send idle Workers into Farms with a free slot (farmStaffPerThink
//             a decision), never the last farmFreeWorkerReserve idle Workers,
//             which stay free to build. A staffed Worker drops out of the
//             worker count, so the worker rule retrains the build crew; past
//             that, one extra Worker is queued for the empty slots while the
//             bank holds farmTrainWorkerMinSupplies.
//   MARKET  — once aged up and the bank holds marketBuildMinSupplies, raise
//             the one Market. Each decision SETS the rates from the surplus
//             (decision 56 e; the 0.0.84 test's creeping +20/-20 steps left
//             54,000 supplies idle beside an empty iron bank): the supplies
//             above marketSupplyFloor are spent over marketSpendMinutes,
//             split between iron and veilstone by how far each is below its
//             target (marketIronShort / marketVeilstoneShort). Nothing short
//             or no surplus: both rates go to zero.
//
// Every number is in SimpleAISystem.asset; the Market's prices and steps are
// the Market SO's. Orders go through CommandRouter (CommandSource.AI) like
// every AI order. Log categories FARM and MARKET.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private static readonly Dictionary<Faction, float> _nextFarmThink = new Dictionary<Faction, float>();
        private static int _farmThinkEpoch = -1;

        private static readonly ComponentType[] QT_FarmWorkers =
        {
            ComponentType.ReadOnly<WorkerTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_FarmWorkers;

        private static readonly ComponentType[] QT_Farms =
        {
            ComponentType.ReadOnly<FarmTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Farms;

        /// <summary>The Farm and Market decision (see the file header).</summary>
        private void TickFarmsAndMarket(EntityManager em, Faction faction, float now)
        {
            if (_farmThinkEpoch != SimCadence.Epoch)
            {
                _farmThinkEpoch = SimCadence.Epoch;
                _nextFarmThink.Clear();
            }
            if (_nextFarmThink.TryGetValue(faction, out float next) && now < next) return;
            _nextFarmThink[faction] = now + math.max(1f, Cfg.farmThinkInterval);

            if (CultureConfig.GetCompletedCulture(em, faction) != Cultures.Alanthor) return;
            if (!FactionEconomy.TryGetResources(em, faction, out var bank)) return;

            TickFarms(em, faction, bank);
            TickMarket(em, faction, bank);
        }

        // ── Farms ────────────────────────────────────────────────────────

        private void TickFarms(EntityManager em, Faction faction, in FactionResources bank)
        {
            int cap = TechCatalog.Building(Farm.BuildingId).maxPerFaction;
            int owned = CountFactionBuildings<FarmTag>(em, faction);   // plans and sites included
            int finished = CountFinished<FarmTag>(em, faction);
            // The tier's extractor share sets how many Farms it keeps (2026-11-11):
            // a lower tier's weaker economy, never a flat ceiling.
            // ...to the power 1.3 (iteration 9: Beginner, Easy and Normal all kept
            // ~3 Farms and their armies bunched; 1.5 starved Hard on Fiendstone):
            // Expert 6, Hard 5, Normal 3, Easy 2, Beginner 2.
            // ...now farmWantExponent on SimpleAISystem.asset (2026-11-14, Game_AI.md
            // § 6ac: 1.0 -- Expert 6, Hard 5, Normal 4, Easy 3 -- the Farm is the
            // supply engine that lets a faction fill its population by minute 30).
            int want = math.clamp((int)math.ceil(cap * math.pow(math.saturate(ProfileOf(faction).ExtractorShare),
                math.max(0.1f, Cfg.farmWantExponent))), 1, cap);

            // Two Farms may rise at once (iteration 3: one at a time left the
            // Farms full only by minute 20-25).
            // THE NEXT FARM IS SAVED FOR (2026-11-11, overnight tuning): every
            // supply went into units the moment it came in, so Expert Defensive's
            // Farms were refused "bank short" 30 times and the sixth stood only at
            // minute 21. While under the target, its price is a strict goal.
            if (owned >= want) AIPivotalReserve.Clear(faction, FarmReserveKey);
            if (owned < want && owned - finished < 2)
            {
                var farmCost = TheWaningBorder.Data.BuildCosts.For(em, faction, Farm.BuildingId);
                if (!FactionEconomy.CanAfford(em, faction, farmCost))
                    AIPivotalReserve.Set(faction, FarmReserveKey, farmCost, strict: true);
                if (TryBuildBuildingWithReason(em, faction, Farm.BuildingId, out string why))
                {
                    AIPivotalReserve.Clear(faction, FarmReserveKey);
                    AILogger.Log(faction, "FARM", $"placed Farm {owned + 1}/{want} (supplies {bank.Supplies})");
                }
                else if (AILogger.Enabled)
                    AILogger.Log(faction, "FARM", $"Farm {owned + 1}/{want} refused: {why}");
            }

            if (finished == 0) return;
            int vacancies = StaffFarms(em, faction, out int sent);
            if (sent > 0)
                AILogger.Log(faction, "FARM", $"sent {sent} Worker(s) into Farms, {vacancies} slot(s) still empty");

            // Empty slots and no spare Worker: queue more, while supplies allow.
            // OUTSIDE THE ECONOMY WALLET (2026-11-14, Game_AI.md § 6ac): a Farm
            // Worker repays its price in about a minute, faster than anything
            // else the AI buys, yet on Moatwatch (0.0.85) the 300-supply gate and
            // the wallet share left Farm slots empty for six minutes and more.
            int queuedWorkers = CountQueuedByPredicate(em, faction, isWorker: true);
            int queueWant = math.min(vacancies, math.max(1, Cfg.farmWorkersQueuedMax));
            if (vacancies > 0
                && AICommon.CountIdleWorkers(em, faction) <= Cfg.farmFreeWorkerReserve
                && queuedWorkers < queueWant)
            {
                int trained = 0;
                for (int q = queuedWorkers; q < queueWant; q++)
                {
                    if (FactionEconomy.TryGetResources(em, faction, out var now2)
                        && now2.Supplies < Cfg.farmTrainWorkerMinSupplies) break;
                    if (!TryTrainUnitWithReason(em, faction, "Worker", out _)) break;
                    trained++;
                }
                if (trained > 0)
                    AILogger.Log(faction, "FARM", $"training {trained} Worker(s) for {vacancies} empty Farm slot(s)");
            }
        }

        private const string FarmReserveKey = "Farm";

        /// <summary>Empty slots in the faction's finished Farms (the worker rule
        /// lets this many more Workers be trained).</summary>
        private static int FarmVacancies(EntityManager em, Faction faction)
        {
            var fq = QC_Farms.Get(em, QT_Farms);
            if (fq.IsEmptyIgnoreFilter) return 0;
            using var farms = fq.ToEntityArray(Allocator.Temp);
            using var ffacs = fq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int free = 0;
            for (int i = 0; i < farms.Length; i++)
            {
                if (ffacs[i].Value != faction || em.HasComponent<UnderConstruction>(farms[i])) continue;
                free += math.max(0, WallGarrison.SlotCount(em, farms[i]) - WallGarrison.OccupantCount(em, farms[i]));
            }
            return free;
        }

        /// <summary>Sends idle Workers (beyond the reserve) into the nearest
        /// Farms with room. Returns the empty slots left; <paramref name="sent"/>
        /// the Workers ordered in.</summary>
        private static int StaffFarms(EntityManager em, Faction faction, out int sent)
        {
            sent = 0;
            var fq = QC_Farms.Get(em, QT_Farms);
            using var farms = fq.ToEntityArray(Allocator.Temp);
            using var ffacs = fq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var fxfs = fq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Free slots per finished Farm, less the Workers already walking there.
            var room = new List<(Entity Farm, float3 Pos, int Free)>();
            for (int i = 0; i < farms.Length; i++)
            {
                if (ffacs[i].Value != faction || em.HasComponent<UnderConstruction>(farms[i])) continue;
                int free = WallGarrison.SlotCount(em, farms[i]) - WallGarrison.OccupantCount(em, farms[i]);
                if (free > 0) room.Add((farms[i], fxfs[i].Position, free));
            }
            if (room.Count == 0) return 0;

            var wq = QC_FarmWorkers.Get(em, QT_FarmWorkers);
            using var ws = wq.ToEntityArray(Allocator.Temp);
            using var wfacs = wq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var wxfs = wq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var idle = new List<(Entity W, float3 Pos)>();
            for (int i = 0; i < ws.Length; i++)
            {
                if (wfacs[i].Value != faction) continue;
                var w = ws[i];
                if (em.HasComponent<GarrisonApproach>(w) && WallGarrison.IsApproaching(em, w))
                {
                    var b = em.GetComponentData<GarrisonApproach>(w).Building;
                    for (int r = 0; r < room.Count; r++)
                        if (room[r].Farm == b) room[r] = (room[r].Farm, room[r].Pos, room[r].Free - 1);
                    continue;
                }
                if (AICommon.IsCommittedWorker(em, w)) continue;
                if (!Farm.IsFarmhand(em, w)) continue;
                idle.Add((w, wxfs[i].Position));
            }

            int vacancies = 0;
            for (int r = 0; r < room.Count; r++) vacancies += math.max(0, room[r].Free);

            int spare = idle.Count - math.max(0, Cfg.farmFreeWorkerReserve);
            int budget = math.min(spare, math.max(0, Cfg.farmStaffPerThink));
            // Lockstep: a fixed order, entity index.
            idle.Sort((a, b) => a.W.Index.CompareTo(b.W.Index));
            for (int i = 0; i < idle.Count && sent < budget && vacancies > 0; i++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int r = 0; r < room.Count; r++)
                {
                    if (room[r].Free <= 0) continue;
                    float d = math.distancesq(room[r].Pos.xz, idle[i].Pos.xz);
                    if (d < bestD) { bestD = d; best = r; }
                }
                if (best < 0) break;
                if (!WallGarrison.CanGarrison(em, idle[i].W, room[best].Farm)) continue;
                CommandRouter.IssueGarrisonWall(em, idle[i].W, room[best].Farm, CommandSource.AI);
                room[best] = (room[best].Farm, room[best].Pos, room[best].Free - 1);
                vacancies--;
                sent++;
            }
            return vacancies;
        }

        // ── The Market ───────────────────────────────────────────────────

        private void TickMarket(EntityManager em, Faction faction, in FactionResources bank)
        {
            var market = Market.Of(em, faction);
            if (market == Entity.Null)
            {
                if (bank.Supplies < Cfg.marketBuildMinSupplies) return;
                if (CountFactionBuildings<MarketTag>(em, faction) > 0) return;   // still a plan
                if (TryBuildBuildingWithReason(em, faction, Market.BuildingId, out string why))
                    AILogger.Log(faction, "MARKET", $"placed the Market (supplies {bank.Supplies})");
                else if (AILogger.Enabled)
                    AILogger.Log(faction, "MARKET", $"Market refused: {why}");
                return;
            }
            if (em.HasComponent<UnderConstruction>(market) || !em.HasComponent<MarketRates>(market)) return;

            var r = em.GetComponentData<MarketRates>(market);

            // SURPLUS TO IRON (decision 59; AI use added 2026-11-15). The set
            // rates top out at a few hundred supplies a minute, and an AI
            // holding the whole map sat on 4,000 supplies and 7,000 veilstone
            // with 50 iron, training nothing (developer's Trine Hollow match,
            // 0.0.88). While it is short of iron and its army is not waiting
            // on supplies, everything above a small reserve becomes iron each
            // second; once iron is back above the target the mode is switched
            // off so the bank can build up for buildings again.
            bool wantAuto = bank.Iron < Cfg.marketIronShort
                            && !AIBudget.IsMilitaryShort(faction, AIBudget.ResSupplies);
            int reserve = Market.ClampReserve(Cfg.marketSurplusReserve);
            if ((r.AutoIron != 0) != wantAuto || (wantAuto && r.ReserveSupplies != reserve))
            {
                CommandRouter.IssueSetMarketSurplus(em, market, reserve, wantAuto, CommandSource.AI);
                AILogger.Log(faction, "MARKET",
                    $"surplus to iron {(wantAuto ? "ON" : "off")}, keeping {reserve} supplies " +
                    $"(bank {bank.Supplies}s {bank.Iron}i {bank.Veilstone}v)");
            }

            float surplus = math.max(0f, bank.Supplies - Cfg.marketSupplyFloor);
            // Never while the army waits on supplies (iteration 9: Hard Defensive
            // spent 16,000 supplies on 2,000 veilstone with its army starved).
            if (AIBudget.IsMilitaryShort(faction, AIBudget.ResSupplies)) surplus = 0f;
            float budget = surplus / math.max(0.25f, Cfg.marketSpendMinutes);   // supplies a minute
            float needIron = math.max(0f, Cfg.marketIronShort - bank.Iron);
            float needVeil = math.max(0f, Cfg.marketVeilstoneShort - bank.Veilstone);
            float needAll = needIron + needVeil;
            int iron = 0, veil = 0;
            if (budget > 0f && needAll > 0f)
            {
                iron = (int)(budget * (needIron / needAll) / math.max(1, Market.SuppliesPerIron));
                veil = (int)(budget * (needVeil / needAll) / math.max(1, Market.SuppliesPerVeilstone));
            }
            iron = Market.ClampRate(iron);
            veil = Market.ClampRate(veil);
            if (iron == r.IronPerMinute && veil == r.VeilstonePerMinute) return;

            CommandRouter.IssueSetMarketRate(em, market, iron, veil, CommandSource.AI);
            AILogger.Log(faction, "MARKET",
                $"rates iron {r.IronPerMinute}->{iron}/min, veilstone {r.VeilstonePerMinute}->{veil}/min " +
                $"(bank {bank.Supplies}s {bank.Iron}i {bank.Veilstone}v, costs {Market.SuppliesCostPerMinute(iron, veil)}s/min)");
        }

    }
}
