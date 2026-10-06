// SimpleAISystem.EconomyDrive.cs
// The economic drive (docs/Design/Game_AI.md § 5h): the Vault of Almierra
// for every AI, the per-faction difficulty profile the static helpers read,
// and the ECON / PRODUCTION measurement lines a headless batch counts.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-04, Headless33 / Headless34)
//
// Building on held ground was never the bottleneck — sites go up within a
// minute of a claim. Levels were (the capital's above all, a x2 / x4 on every
// slot of the home territory), and the Vault, which compounds idle money,
// was never touched by any AI. The capital's saving goal and the economy
// level pass live in AIBuildingUpgradeSystem; the queue depth in
// TryTrainUnitWithReason; this file holds the rest.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using BuildingIdOf = TheWaningBorder.Entities.BuildingIds;   // avoids the DC0062 Entities.ForEach misread

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        // ── The per-faction difficulty profile ─────────────────────────────
        //
        // Several static helpers (the training pre-flight, the age-up saving
        // test) decide per faction without a brain in hand. The think loop
        // records each brain's profile here before anything else runs; a
        // faction never recorded (a human) reads the zeroed profile, whose
        // economic-drive fields all mean "today's behaviour".

        private const int ProfileSlots = 16;   // array size, not tuning: Faction is a byte enum (0..7 + Border)
        private static readonly AIDifficultyProfile[] _profileOf = new AIDifficultyProfile[ProfileSlots];

        /// <summary>The faction whose think is running (set with its profile,
        /// first thing in the think) — for helpers that take no faction.</summary>
        private static Faction _thinkFaction;

        private static readonly AIPersonality[] _personalityOf = new AIPersonality[ProfileSlots];

        private static void NoteProfile(Faction faction, in AIDifficultyProfile profile, AIPersonality personality)
        {
            _thinkFaction = faction;
            int k = (int)faction;
            if (k >= 0 && k < ProfileSlots) { _profileOf[k] = profile; _personalityOf[k] = personality; }
        }

        /// <summary>The faction's personality block (AISettings.asset), for
        /// helpers that take no brain — the tower and wall scales (Game_AI.md § 3).</summary>
        private static AISettingsSO.PersonalityBlock PersonalityOf(Faction faction)
        {
            int k = (int)faction;
            bool ok = k >= 0 && k < ProfileSlots;
            return AISettings.Get().For(ok ? _personalityOf[k] : AIPersonality.Balanced,
                ok ? _profileOf[k].PersonalityWeight : 1f);
        }

        private static AIDifficultyProfile ProfileOf(Faction faction)
        {
            int k = (int)faction;
            return k >= 0 && k < ProfileSlots ? _profileOf[k] : default;
        }

        /// <summary>Is this trainer at the tier's production-queue depth
        /// (or the building's own cap)? Depth 0 = the building's cap only.</summary>
        private static bool AtQueueCap(EntityManager em, Faction faction, Entity trainer)
        {
            if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, trainer)) return true;
            int depth = ProfileOf(faction).ProductionQueueDepth;
            return depth > 0
                && TheWaningBorder.Core.Commands.CommandRouter.GetProductionQueueLength(em, trainer) >= depth;
        }

        // ── Match-scoped state ─────────────────────────────────────────────

        private int _econEpoch = -1;
        private readonly Dictionary<int, float> _nextEconPass = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextEconLog = new Dictionary<int, float>();
        private readonly Dictionary<long, float> _claimedAt = new Dictionary<long, float>();
        private readonly HashSet<Entity> _econReported = new HashSet<Entity>();
        private readonly Dictionary<Entity, float> _vaultDepositAt = new Dictionary<Entity, float>();
        private readonly HashSet<int> _econOwnedScratch = new HashSet<int>();
        private readonly List<long> _econDropScratch = new List<long>();

        private void ResetEconomyDriveIfNewMatch()
        {
            if (_econEpoch == SimCadence.Epoch) return;
            _econEpoch = SimCadence.Epoch;
            _nextEconPass.Clear();
            _nextEconLog.Clear();
            _claimedAt.Clear();
            _econReported.Clear();
            _vaultDepositAt.Clear();
        }

        /// <summary>
        /// Once a think: claim bookkeeping (cheap), then — every
        /// econPassInterval — the Vault decision and the node-built scan, and
        /// every econLogInterval the crew and production summary lines.
        /// </summary>
        private void TickEconomyDrive(EntityManager em, Faction faction, AIPosture posture,
            in AIDifficultyProfile profile, float now)
        {
            ResetEconomyDriveIfNewMatch();
            int key = (int)faction;

            TrackClaims(faction, now);

            if (!_nextEconPass.TryGetValue(key, out float nextPass) || now >= nextPass)
            {
                _nextEconPass[key] = now + System.Math.Max(0.1f, Cfg.econPassInterval);
                TickVault(em, faction, posture, profile, now);
                ScanBuiltExtractors(em, faction, now);
            }

            if (!AILogger.Enabled) return;
            if (_nextEconLog.TryGetValue(key, out float nextLog) && now < nextLog) return;
            _nextEconLog[key] = now + System.Math.Max(1f, Cfg.econLogInterval);
            LogCrewAndProduction(em, faction, profile);
        }

        // ── ECON: node built after claim ───────────────────────────────────

        private static long ClaimKey(Faction faction, int region) => ((long)(int)faction << 32) | (uint)region;

        /// <summary>Stamp the time each territory joined the faction; forget
        /// it when lost, so a re-claim is timed afresh.</summary>
        private void TrackClaims(Faction faction, float now)
        {
            if (!TerritoryOwnership.Ready) return;
            var owned = _econOwnedScratch;
            owned.Clear();
            owned.UnionWith(TerritoryOwnership.TerritoriesOf(faction));
            foreach (int r in owned)
            {
                long k = ClaimKey(faction, r);
                if (!_claimedAt.ContainsKey(k)) _claimedAt[k] = now;
            }
            _econDropScratch.Clear();
            foreach (var kv in _claimedAt)
                if ((int)(kv.Key >> 32) == (int)faction && !owned.Contains((int)(kv.Key & 0xffffffff)))
                    _econDropScratch.Add(kv.Key);
            for (int i = 0; i < _econDropScratch.Count; i++) _claimedAt.Remove(_econDropScratch[i]);
        }

        private void ScanBuiltExtractors(EntityManager em, Faction faction, float now)
        {
            if (!RegionMap.Ready) return;
            ScanBuilt(em, AIQueryCache.TagFactionXf<GathererHutTag>(em), faction, now);
            ScanBuilt(em, AIQueryCache.TagFactionXf<MineTag>(em), faction, now);
            ScanBuilt(em, AIQueryCache.TagFactionXf<VeilstoneMineTag>(em), faction, now);
            ScanBuilt(em, AIQueryCache.TagFactionXf<TradingOutpostTag>(em), faction, now);
        }

        private void ScanBuilt(EntityManager em, EntityQuery q, Faction faction, float now)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (_econReported.Contains(ents[i])) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                _econReported.Add(ents[i]);
                int r = RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z);
                if (r == RegionMap.None) continue;
                if (!_claimedAt.TryGetValue(ClaimKey(faction, r), out float at)) continue;
                string id = BuildingIdOf.Of(ents[i], em) ?? "extractor";
                AILogger.Log(faction, "ECON",
                    $"node {id} built in {(int)(now - at)}s after claim ({RegionMap.NameOf(r)})");
            }
        }

        // ── ECON: idle crew / PRODUCTION: parallel ─────────────────────────

        static readonly ComponentType[] QT_EconProduction =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<ProductionQueueItem>(),
        };
        static CachedEntityQuery QC_EconProduction;

        private static void LogCrewAndProduction(EntityManager em, Faction faction,
            in AIDifficultyProfile profile)
        {
            int crew = CountAliveWorkers(em, faction);
            int idle = AICommon.CountIdleWorkers(em, faction);
            int sites = CountFactionBuildingsUnderConstruction(em, faction);
            AILogger.Log(faction, "ECON", $"idle crew {idle} of {crew} ({sites} site(s) open)");

            // Production buildings only (the four army lines): how many are
            // training right now, and the deepest queue among them.
            var q = QC_EconProduction.Get(em, QT_EconProduction);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int lines = 0, training = 0, deepest = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var e = ents[i];
                if (!(em.HasComponent<BarracksTag>(e) || em.HasComponent<ArcheryRangeTag>(e)
                      || em.HasComponent<RoyalStableTag>(e) || em.HasComponent<SiegeYardTag>(e))) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                lines++;
                int units = TheWaningBorder.Core.Commands.CommandRouter.GetTrainQueueLength(em, e);
                if (units > 0) training++;
                int len = TheWaningBorder.Core.Commands.CommandRouter.GetProductionQueueLength(em, e);
                if (len > deepest) deepest = len;
            }
            if (lines == 0) return;
            LogProductionCapacity(faction, profile);
            string cap = profile.ProductionQueueDepth > 0 ? profile.ProductionQueueDepth.ToString() : "uncapped";
            AILogger.Log(faction, "PRODUCTION",
                $"parallel {training} buildings training (of {lines}), queue depth {deepest} (cap {cap})");
        }

        // Per match, per faction: the capacity line was written.
        private static int _capacityLogEpoch = -1;
        private static readonly HashSet<int> _capacityLogged = new HashSet<int>();

        /// <summary>"PRODUCTION: capacity home x/line, province y, extras at
        /// z% busy (tier)" — once per match per faction (Game_AI.md 5g).</summary>
        private static void LogProductionCapacity(Faction faction, in AIDifficultyProfile profile)
        {
            if (_capacityLogEpoch != SimCadence.Epoch)
            {
                _capacityLogEpoch = SimCadence.Epoch;
                _capacityLogged.Clear();
            }
            if (!_capacityLogged.Add((int)faction)) return;
            AILogger.Log(faction, "PRODUCTION",
                $"capacity home {System.Math.Max(1, profile.HomeProductionPerLine)}/line, " +
                $"province {System.Math.Max(1, profile.ProvinceProductionPerTerritory)}, " +
                $"extras at {profile.ProductionSaturationThreshold * 100f:F0}% busy " +
                $"for {profile.ProductionSaturationSeconds:F0}s ({profile.Tier})");
        }

        /// <summary>UNITS BEFORE ECONOMY for a discretionary non-production
        /// building (Game_AI.md 5h): null when it may be placed, otherwise
        /// the deferral (logged when <paramref name="note"/>). Housing,
        /// extractors, the Fortress, the landmark and the Temple never ask;
        /// production never does either.</summary>
        private static string BuildingDeferral(EntityManager em, Faction faction, string buildingId, bool note)
        {
            var cost = TheWaningBorder.Data.BuildCosts.Exists(buildingId)
                ? TheWaningBorder.Data.BuildCosts.For(em, faction, buildingId)
                : (TechCatalog.TryGetBuilding(buildingId, out var def) && def != null
                    ? AICommon.ToCost(def.cost) : default);
            string deferred = AIBudget.EconomyDeferral(em, faction, cost);
            if (deferred != null && note) AIBudget.NoteEconomyDeferred(faction, "build " + buildingId, deferred);
            return deferred;
        }

        // ── VAULT ──────────────────────────────────────────────────────────

        static readonly ComponentType[] QT_EconVault =
        {
            ComponentType.ReadOnly<VaultTag>(),
            ComponentType.ReadOnly<VaultStorage>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_EconVault;

        private static readonly string[] VaultResourceNames = { "none", "supplies", "iron", "veilstone", "veilsteel" };

        /// <summary>
        /// Every AI uses its Vault (Game_AI.md § 5h). Withdraw on the timer,
        /// on need (tiers with vaultWithdrawOnNeed), on damage or under
        /// Defend; deposit the largest idle surplus the design lets it hold.
        /// One command per Vault per pass, through CommandRouter
        /// (CommandSource.AI) so both sides of the move land on every peer.
        /// </summary>
        private void TickVault(EntityManager em, Faction faction, AIPosture posture,
            in AIDifficultyProfile profile, float now)
        {
            var q = QC_EconVault.Get(em, QT_EconVault);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var vault = ents[i];
                if (em.HasComponent<UnderConstruction>(vault)) continue;
                var store = em.GetComponentData<VaultStorage>(vault);
                if (store.LockTimer > 0f) continue;

                if (store.ResourceType > 0 && store.StoredAmount >= 1f)
                {
                    string why = VaultWithdrawReason(em, faction, vault, store, posture, profile, now);
                    if (why == null) continue;
                    int amount = (int)store.StoredAmount;
                    TheWaningBorder.Core.Commands.CommandRouter.IssueVaultTransfer(
                        em, vault, 0, 0, deposit: false, TheWaningBorder.Core.Commands.CommandSource.AI);
                    _vaultDepositAt.Remove(vault);
                    InvalidateThinkMemo();
                    AILogger.Log(faction, "VAULT",
                        $"withdraw {amount} {ResName(store.ResourceType)} ({why})");
                    continue;
                }

                if (posture == AIPosture.Defend) continue;
                // An economy in distress banks nothing (Game_AI.md § 5i).
                if (EconomyDistressed(faction)) continue;
                if (profile.VaultDepositShare <= 0f) continue;
                if (!PickVaultDeposit(em, faction, profile, out int type, out int amt)) continue;
                TheWaningBorder.Core.Commands.CommandRouter.IssueVaultTransfer(
                    em, vault, type, amt, deposit: true, TheWaningBorder.Core.Commands.CommandSource.AI);
                _vaultDepositAt[vault] = now;
                InvalidateThinkMemo();
                AILogger.Log(faction, "VAULT", $"deposit {amt} {ResName(type)}");
            }
        }

        private static string ResName(int type)
            => type >= 0 && type < VaultResourceNames.Length ? VaultResourceNames[type] : "?";

        /// <summary>Why the stored amount should come out now, or null.</summary>
        private string VaultWithdrawReason(EntityManager em, Faction faction, Entity vault,
            in VaultStorage store, AIPosture posture, in AIDifficultyProfile profile, float now)
        {
            if (posture == AIPosture.Defend) return "home under threat";
            // Stored supplies are what a collapsing economy rebuilds with
            // (Game_AI.md § 5i).
            if (store.ResourceType == 1 && EconomyDistressed(faction)) return "economy in distress";
            if (em.HasComponent<Health>(vault))
            {
                var hp = em.GetComponentData<Health>(vault);
                if (hp.Max > 0 && hp.Value < hp.Max * Cfg.vaultDamagedFraction) return "vault damaged";
            }
            // A deposit this brain did not see (a previous match state, a
            // human's earlier deposit) is timed from now.
            if (!_vaultDepositAt.TryGetValue(vault, out float at)) { _vaultDepositAt[vault] = now; at = now; }
            if (profile.VaultHoldSeconds > 0f && now - at >= profile.VaultHoldSeconds) return "hold time";

            if (!profile.VaultWithdrawOnNeed) return null;
            int res = store.ResourceType - 1;   // VaultStorage 1..4 -> AIBudget 0..3
            if (res >= AIBudget.ResSupplies && res <= AIBudget.ResVeilsteel && AIBudget.IsMilitaryShort(faction, res))
                return "the army is short of it";
            var shortSet = AIPivotalReserve.ShortResources(em, faction);
            bool goalShort = res == AIBudget.ResSupplies ? shortSet.Supplies
                           : res == AIBudget.ResIron ? shortSet.Iron
                           : res == AIBudget.ResVeilstone ? shortSet.Veilstone
                           : res == AIBudget.ResVeilsteel && shortSet.Veilsteel;
            return goalShort ? "a savings goal needs it" : null;
        }

        /// <summary>The deposit: the resource with the largest idle surplus
        /// the design lets the Vault hold (Age_0.md § Vault — supplies
        /// always, iron / veilstone / veilsteel after their unlock tech),
        /// above the keep floor and every pending savings goal, never what
        /// the army is waiting on.</summary>
        private static bool PickVaultDeposit(EntityManager em, Faction faction,
            in AIDifficultyProfile profile, out int type, out int amount)
        {
            type = 0; amount = 0;
            if (!FactionEconomy.TryGetResources(em, faction, out var bank)) return false;
            var goals = AIPivotalReserve.PendingTotal(faction);
            var research = FactionResearchState.Instance;

            int bestType = 0, bestSurplus = 0;
            for (int t = 1; t <= 4; t++)
            {
                if (t == 2 && !Researched(research, faction, VaultIronTech)) continue;
                if (t == 3 && !Researched(research, faction, VaultVeilstoneTech)) continue;
                if (t == 4 && !Researched(research, faction, VaultVeilsteelTech)) continue;
                if (AIBudget.IsMilitaryShort(faction, t - 1)) continue;
                int have = t == 1 ? bank.Supplies : t == 2 ? bank.Iron : t == 3 ? bank.Veilstone : bank.Veilsteel;
                int keep = t == 1 ? Cfg.vaultKeepSupplies + goals.Supplies
                         : t == 2 ? Cfg.vaultKeepIron + goals.Iron
                         : t == 3 ? Cfg.vaultKeepVeilstone + goals.Veilstone
                         : Cfg.vaultKeepVeilsteel + goals.Veilsteel;
                int surplus = have - keep;
                if (surplus <= bestSurplus) continue;
                // IDLE means idle (Game_AI.md 5f): money an army below its
                // target could spend right now is not surplus.
                var asCost = t == 1 ? Cost.Of(supplies: surplus) : t == 2 ? Cost.Of(iron: surplus)
                           : t == 3 ? Cost.Of(veilstone: surplus) : Cost.Of(veilsteel: surplus);
                if (AIBudget.ArmyFirstYield(em, faction, asCost) != null) continue;
                // UNITS BEFORE ECONOMY (Game_AI.md 5h): on a tier that says
                // so, money an idle trainer could still turn into units is
                // not idle either.
                string deferred = AIBudget.EconomyDeferral(em, faction, asCost);
                if (deferred != null)
                {
                    AIBudget.NoteEconomyDeferred(faction, "vault deposit " + ResName(t), deferred);
                    continue;
                }
                bestSurplus = surplus; bestType = t;
            }
            if (bestType == 0) return false;
            int amt = (int)(bestSurplus * UnityEngine.Mathf.Clamp01(profile.VaultDepositShare));
            if (amt < Cfg.vaultMinDeposit) return false;
            type = bestType; amount = amt;
            return true;
        }

        /// <summary>The Vault's resource-unlock techs (Age_0.md § Vault).
        /// Ids of the tech SOs, a roster table — not tuning.</summary>
        private const string VaultIronTech = "IronSubsidies";
        private const string VaultVeilstoneTech = "VeilstoneMonetization";
        private const string VaultVeilsteelTech = "VeilsteelBonds";

        private static bool Researched(FactionResearchState research, Faction faction, string tech)
            => research != null && research.HasResearched(faction, tech);

        // ── The curse hunt waits for the economy ───────────────────────────

        private readonly Dictionary<int, float> _nextHuntDeferLog = new Dictionary<int, float>();

        /// <summary>
        /// True while this tier defers the first-Religion-Point hunt until
        /// its home capital stands at curseHuntMinCapitalLevel (Game_AI.md
        /// § 5h). Logged once per econLogInterval.
        /// </summary>
        private bool HuntDeferredForEconomy(EntityManager em, Faction faction, float now)
        {
            int need = ProfileOf(faction).CurseHuntMinCapitalLevel;
            if (need <= 0) return false;
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            int level = hall != Entity.Null && em.HasComponent<BuildingUpgradeState>(hall)
                ? em.GetComponentData<BuildingUpgradeState>(hall).Level : 0;
            if (level >= need) return false;
            int key = (int)faction;
            if (!_nextHuntDeferLog.TryGetValue(key, out float next) || now >= next)
            {
                _nextHuntDeferLog[key] = now + System.Math.Max(1f, Cfg.econLogInterval);
                AILogger.Log(faction, "RELIGION",
                    $"hunt deferred (economy first): capital L{level}, hunts from L{need}");
            }
            return true;
        }

        /// <summary>The hunt never fights at parity: the army's power must
        /// beat the node's by religionHuntPowerMargin (every tier).</summary>
        private static bool HuntHasMargin(in EngagementAssessment a)
            => a.MyPower >= a.EnemyPower * System.Math.Max(1f, Cfg.religionHuntPowerMargin);
    }
}
