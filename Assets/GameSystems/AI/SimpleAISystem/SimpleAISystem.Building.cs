// SimpleAISystem.Building.cs
// Building placement: siting rules, spacing, worker dispatch, pop headroom.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using UnityEngine;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_VeilstoneOutcroppingTagLocalTransform =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_VeilstoneOutcroppingTagLocalTransform;

        static readonly ComponentType[] QT_IronMineTagLocalTransform =
        {
            ComponentType.ReadOnly<IronMineTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_IronMineTagLocalTransform;

        #endregion

        private const int BuildAngleSamples = 24;
        // ─────────────────────────────────────────────────────────────────
        // BUILD BUILDING
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Place + dispatch workers for <paramref name="buildingId"/>. The
        /// placement ring is anchored on the faction Hall.
        /// </summary>
        /// <param name="anchorOverride">Where to centre the site search. Null
        /// means the home Hall, which is right for everything that extends a
        /// base — and wrong for the one thing that does not. A Hall claiming
        /// new ground has to be sited on the TARGET REGION: anchored at home,
        /// every candidate lands in territory already held, where
        /// HallCapReached refuses it and the AI can never expand.</param>
        private bool TryBuildBuilding(EntityManager em, Faction faction, string buildingId,
            float3? anchorOverride = null)
            => TryBuildBuildingWithReason(em, faction, buildingId, out _, anchorOverride);

        /// <summary>
        /// As <see cref="TryBuildBuilding"/>, but says WHY it refused.
        ///
        /// Every refusal in here used to be a bare `return false`. Two separate
        /// blockers were then diagnosed by inference from match metrics — the
        /// idle-worker gate among them — and one of those inferences was
        /// wrong. A build path this load-bearing states its own cause.
        /// </summary>
        private bool TryBuildBuildingWithReason(EntityManager em, Faction faction,
            string buildingId, out string reason, float3? anchorOverride = null)
        {
            // ONE PER PROVINCE, THEN ONLY WHEN SATURATED (2026-10-04,
            // Game_AI.md 5g): every production building past a province's
            // own (step 4) passes here, whichever path asked for it.
            reason = ProductionGate(em, faction, buildingId, _thinkNow, out bool extra, out string detail);
            if (reason != null) return false;
            bool ok = TryBuildBuildingCore(em, faction, buildingId, out reason, anchorOverride);
            if (ok && extra) NoteExtraProduction(faction, buildingId, detail);
            return ok;
        }

        /// <summary>The placement itself, past the production gate (the
        /// across-territories production search calls it per territory, so
        /// the gate and its log run once per request).</summary>
        private bool TryBuildBuildingCore(EntityManager em, Faction faction,
            string buildingId, out string reason, float3? anchorOverride = null)
        {
            reason = null;
            // A build order naming a landmark
            // means THIS faction's landmark (Age_0.md § Age-up by landmark).
            buildingId = ResolveLandmarkId(em, faction, buildingId);
            if (!TechCatalog.IsReady) { reason = "catalog not ready"; return false; }
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null)
            { reason = "no catalog def"; return false; }

            // Era gate (2026-08-11): minEra was UI-only, so the AI happily
            // built era-locked buildings — the Age-0 Archery Range this rule
            // now delays (ranged units are an Age-1 unlock, Combat_Pacing.md).
            if (def.minEra > 1)
            {
                int era = 1;
                if (FactionEconomy.TryGetBank(em, faction, out var eraBank)
                    && em.HasComponent<FactionEra>(eraBank))
                    era = em.GetComponentData<FactionEra>(eraBank).Value;
                if (era < def.minEra)
                {
                    reason = AILogger.Enabled ? $"era {era} < minEra {def.minEra}" : "era gate";
                    return false;
                }
            }

            // task-109 Phase 7 / AD-6 / R9: SimpleAISystem must never try to
            // place wall primitives. Alanthor AI does NOT build walls in v1
            // of the BFME2 rework — wall construction is deferred to a
            // follow-up task. This guard is a safety net so a future
            // AIBuildOrder entry that accidentally lists "Alanthor_Wall"
            // (or any wall-related id) doesn't propagate through the build
            // pipeline. The same skip is applied below in the existing-
            // building iteration so wall pieces never become target
            // candidates for AI repair/attack actions either.
            // The Palisade too (2026-10-03): every AI wall goes through the
            // endgame doctrine, which walls only the home territory and
            // Fortress territories (docs/Design/Game_AI.md § Walls).
            if (buildingId == "Alanthor_Wall"
                || buildingId == "Alanthor_WallTower"
                || buildingId == "Alanthor_WallGate"
                || buildingId == "Palisade")
            { reason = "wall primitive"; return false; }

            // The Temple costs a Religion Point (docs/Design/Religion.md §2);
            // without one the executor refuses it, so do not even try.
            if (buildingId == "TempleOfRidan"
                && !TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction,
                       TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp))
            { reason = "no Religion Point for the Temple"; return false; }

            // Choice-buildings are limited to one per faction.
            if (BuildingFactory.IsChoiceBuilding(buildingId))
            {
                var existing = BuildingFactory.GetFactionChoiceBuilding(em, faction);
                if (existing != null) { reason = "choice building already owned"; return false; }
            }

            // Need a Hall to anchor placement around.
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null) { reason = "no hall"; return false; }
            if (!em.HasComponent<LocalTransform>(hall)) { reason = "hall has no transform"; return false; }
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;
            float3 anchor = anchorOverride ?? hallPos;

            // The price the executor will charge THIS faction (BuildCosts.For:
            // the Hall's escalation, Deep Foundations) — the catalog cost only
            // for an id the cost table does not carry.
            var cost = TheWaningBorder.Data.BuildCosts.Exists(buildingId)
                ? TheWaningBorder.Data.BuildCosts.For(em, faction, buildingId)
                : AICommon.ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost))
            {
                // Refusal strings are built only when a log can read them —
                // this path refuses many times per think.
                reason = AILogger.Enabled
                    ? $"bank short ({cost.Supplies}s {cost.Iron}i {cost.Veilstone}v)" : "bank short";
                return false;
            }
            // THE ARMY'S VEILSTONE EARMARK (2026-10-03, AIBudget): a building
            // priced in veilstone — the expansion Fortress above all — is
            // paid from the veilstone the army has not claimed.
            // (A veilstone-surplus Fortress on outcrop ground may use it —
            // _surplusEarmarkCarve, Game_AI.md 5e: more ground is more
            // Outposts, the income the earmark is waiting on.)
            // A PRODUCTION BUILDING NEVER YIELDS TO THE ARMY (2026-10-04,
            // Game_AI.md 5f): it is what lets the army grow faster, and its
            // count is bounded by the province / saturation rule instead.
            if (!_surplusEarmarkCarve && !BuildSiteSnapshot.IsProductionId(buildingId)
                && !AIBudget.LeavesMilitaryVeilstone(em, faction, cost))
            {
                reason = AILogger.Enabled
                    ? $"veilstone earmarked for the army ({AIBudget.MilitaryVeilstoneCredit(faction)}v held, " +
                      $"{cost.Veilstone}v needed)"
                    : "veilstone earmarked for the army";
                return false;
            }

            int2 size = BuildingSizeConfig.GetSize(buildingId);

            // CHEAP GATES FIRST (2026-09-25 AI perf pass). The pivotal hold,
            // the crew and the open-site cap used to be tested AFTER the site
            // search — so every think that was going to be refused anyway for
            // "saving" or "5 sites open" first paid for a full ring scan of up
            // to ~3,000 candidates. Same gates, same verdicts; only the order
            // (and therefore which reason a double refusal reports) changed.
            if (!PassesBuildPreflight(em, faction, buildingId, cost, out reason)) return false;

            // AN EXTRACTOR WITH NO ANCHOR IS SITED ON ITS NODES (2026-10-03,
            // operator: "AI starves for supplies all game long"). The hut
            // pipeline and the goal list both asked for a Gatherer's Hut with
            // no anchor, so the search ringed the Hall — and the node gate
            // accepts only a candidate on a free supply node, which the Hall
            // never stands on. Every one of those requests failed ("territory
            // 144" / "nodegate 144" in the Veilmarch logs), leaving the 15 s
            // extractor walk, which buys veilstone and iron extractors first,
            // as the only thing that ever raised a hut: one hut in two
            // minutes, supplies starved for the whole match.
            if (anchorOverride == null
                && TheWaningBorder.World.Regions.TerritoryOwnership.IsExtractor(buildingId))
                return TryBuildOnFreeNode(em, faction, buildingId, size, hallPos, out reason);

            // PRODUCTION IN EVERY PROVINCE (2026-10-04, Game_AI.md 5g). An
            // un-anchored Barracks / Archery Range / Royal Stable / Siege Yard
            // is sited in whichever held territory has the fewest (frontier
            // first), searched inside that territory from its core — not in a
            // home ring that Headless29 showed full (1,678 failed searches).
            if (anchorOverride == null
                && _siteRegionLock == TheWaningBorder.World.Regions.RegionMap.None
                && TheWaningBorder.World.Regions.RegionMap.Ready
                && TheWaningBorder.World.Regions.TerritoryOwnership.Ready
                && BuildSiteSnapshot.IsProductionId(buildingId))
                return TryBuildProductionAcrossTerritories(em, faction, buildingId, out reason);

            // A search that just failed here, for this building, on this
            // ground, is not re-run until something that could change the
            // answer has changed — see SiteSearchRemembered.
            // (The lost-trainer rebuild searches with the wall's walkway
            // relaxed, so a normal search's failure says nothing about it.)
            if (buildingId != "FiendstoneKeep" && !_rebuildingLostTrainer
                && SiteSearchRemembered(em, faction, buildingId, anchor, out reason))
                return false;

            // Wall doctrine: the Fiendstone Keep is the chokepoint citadel.
            // When terrain shelters this base and ingress runs through a
            // sealable chokepoint, the Keep stands at the primary corridor
            // (behind the future wall line), not in the base ring.
            float3 pos;
            if (buildingId == "FiendstoneKeep"
                && AIWallPlanner.TryFindKeepChokeSpot(em, hallPos, size, out pos))
            {
                AILogger.Log(faction, "BUILDING",
                    $"FiendstoneKeep sited at the ingress chokepoint ({pos.x:F0},{pos.z:F0})");
            }
            else if (!TryFindBuildPosition(em, anchor, size, buildingId, faction, out pos,
                         out bool inconclusive))
            {
                reason = AILogger.Enabled
                    ? $"no legal {size.x}x{size.y} spot near ({anchor.x:F0},{anchor.z:F0}) " +
                      $"[{_siteRefusalTally}]"
                    : "no legal spot";
                // A search cut short by this think's candidate budget proved
                // nothing, so only a COMPLETE failure is remembered.
                if (inconclusive) reason = "site search budget spent this think";
                else RememberFailedSiteSearch(em, faction, buildingId, anchor, reason);
                return false;
            }

            // (The build-crew / open-site / pivotal-hold pre-flight that used to
            // sit here now runs BEFORE the search: PassesBuildPreflight.)

            // No AI-side Spend: PlaceBuildingDirect charges the BuildCosts
            // price on every peer (docs/Multiplayer_LAN_Readiness.md). The
            // CanAfford above stays as the decision gate.

            // F4 (2026-07-15): route through IssuePlaceBuilding, NOT
            // PlaceBuildingDirect — the direct call is the post-lockstep
            // executor, so every AI building existed on the host only and
            // clients watched an empty AI base. In multiplayer the foundation
            // is created on every peer two ticks later, so workers are
            // dispatched at the POSITION with a null target and auto-find the
            // site on arrival (same pattern as the human MP flow in
            // BuildCommandPannel).
            // A HALL NEEDS ITS WORKER ON SITE (Regions.md §2): walk a worker
            // there first and place on a later think, once it has arrived.
            // The worker then rides the command so the executor can re-check.
            // (No worker rides the command any more: the Hall — the only
            // building that needed one on site — is removed.)
            Entity claimWorker = Entity.Null;

            _lastPlacedPos = pos;
            bool queued = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                claimWorker, out Entity building, CommandSource.AI);
            // Whatever happened, buildings / the bank may have changed: every
            // memoised count is stale, and the site is taken for the rest of
            // this tick (in lockstep the foundation appears two ticks later).
            InvalidateThinkMemo();
            if (queued || building != Entity.Null)
                BuildSiteSnapshot.Current(em).NotePlaced(em, buildingId, pos, size);
            if (queued)
            {
                int sent = AICommon.DispatchWorkersTo(em, faction, Entity.Null, buildingId, pos, maxWorkers: 2);
                // A LOST TRAINER IS NEVER ORPHANED (Game_AI.md 6c): with no
                // idle worker, the nearest busy ones take it as their next site.
                if (sent == 0 && _rebuildingLostTrainer)
                    NoteLostTrainerPull(faction, buildingId,
                        AICommon.PullWorkersTo(em, faction, Entity.Null, buildingId, pos, maxWorkers: 2));
                // No rollback path here: the placement command is already
                // queued on every peer. Past the idle-worker pre-flight a
                // zero dispatch is a rare race; workers auto-chain to nearby
                // unfinished structures, so the site still gets picked up.
                return true;
            }
            if (building == Entity.Null) return false;

            // Dispatch idle workers to actually construct the thing — without
            // this the building is created with HP=1 and UnderConstruction but
            // never gains progress. The human player flow does the same step
            // explicitly via BuildCommandPanel.AssignWorkersToConstruction.
            int dispatched = AICommon.DispatchWorkersTo(em, faction, building, buildingId, pos, maxWorkers: 2);
            if (dispatched == 0 && _rebuildingLostTrainer)
            {
                dispatched = AICommon.PullWorkersTo(em, faction, building, buildingId, pos, maxWorkers: 2);
                NoteLostTrainerPull(faction, buildingId, dispatched);
            }
            if (dispatched == 0)
            {
                // NO IDLE WORKER IS NOT A ROLLBACK (2026-10-05). This used to
                // refund and destroy the placement, and with a 0.25 s think
                // the next tick placed it again: 55 pay-and-refund cycles of
                // one Gatherer's Hut in the first 15 s of a match, 146 in one
                // 15 s period later, and a bank that read 120 short whenever
                // the fast tier checked whether it could afford a soldier —
                // Expert bought a quarter of Easy's units in its first ten
                // minutes. A worker-raised building is a PLAN now
                // (Planned_Buildings.md): it costs nothing standing, idle
                // workers adopt it on their own, and here the nearest busy
                // worker takes it as its next job. Only a site that is not a
                // plan (a self-constructing landmark) stands without a crew,
                // and those need none.
                int pulled = AICommon.PullWorkersTo(em, faction, building, buildingId, pos, maxWorkers: 1);
                if (AILogger.Enabled && (!_noCrewLogAt.TryGetValue((int)faction, out float at) || _thinkNow >= at))
                {
                    _noCrewLogAt[(int)faction] = _thinkNow + 60f;   // log cadence, not tuning
                    AILogger.Log(faction, "BUILD",
                        $"{buildingId} placed with no idle worker — " +
                        (pulled > 0 ? "queued on a busy one" : "left for the next free worker"));
                }
            }
            return true;
        }

        /// <summary>Per-faction cadence of the "placed with no idle worker" line.</summary>
        private readonly Dictionary<int, float> _noCrewLogAt = new Dictionary<int, float>();

        /// <summary>
        /// The build pre-flight that does not depend on WHERE: the savings
        /// hold, a build crew, and the open-site cap. Run before the site
        /// search so a refusal costs nothing.
        /// </summary>
        private bool PassesBuildPreflight(EntityManager em, Faction faction,
            string buildingId, Cost cost, out string reason)
        {
            reason = null;

            // Pre-flight: the faction must have a build crew, and not already
            // have more sites open than that crew can work.
            //
            // THIS USED TO DEMAND AN *IDLE* WORKER, and that was fatal once the
            // crew shrank. Workers only build now (Regions.md §4), so the target
            // dropped from 14-45 to 3-5 — and since two workers are dispatched
            // per site, ONE building in flight left zero idle and every
            // subsequent request returned false. Silently: no log, no reason,
            // just a goal list that looked unaffordable.
            //
            // Measured over a 20-minute four-AI match with the small crew: not
            // one faction built a single military building. No Barracks, no
            // Archery Range, nothing. Every structure that did go up came from a
            // path that bypasses this call, and the AI logged "nothing
            // affordable" 57 times while holding 3,352 iron and 8,229 veilstone.
            //
            // The original concern — an orphan foundation nobody ever works —
            // is handled without the idle test: workers auto-chain to nearby
            // unfinished structures within line of sight, so a queued site gets
            // picked up as soon as a worker frees. What actually has to be
            // bounded is how many sites are open at once, which is what the
            // crew size means.
            // PIVOTAL HOLD (2026-08-31, round 2): pausing army TRAINING was
            // not enough — building placement kept eating every 600 supplies
            // the moment they accumulated (the 20-30 production-building
            // target is a bottomless sink), so "saving for <territory>"
            // still never filled. While the lump sum is being saved, the
            // only buildings the AI may place are the PIVOTAL CHAIN:
            // the Hall the hold exists for, and the age-up prerequisites
            // (batch 12: gating the Shrine/Vault/Keep choice building meant
            // NO faction ever reached era 2 across five batches — the era-0
            // army cap of 8 then locked the army-first claim gate, and the
            // whole economy sat at 3 territories. The age path never queues
            // behind a land grab, in either direction.)
            // THE ESSENTIALS PASS THROUGH (2026-09-03). AIPivotalReserve's
            // contract has always said "floors and the hut income pipeline
            // are exempt by design — saving up must never starve the economy
            // that does the saving", and this whitelist violated it: with a
            // claim pot always pending (there is always another region), the
            // hold was effectively permanent, so the AI shipped whole matches
            // with ZERO Gatherer's Huts and ZERO Barracks — supplies income
            // never grew, the army floor's trainer never existed, the army
            // pinned at 5, and the army-first claim gate then froze expansion
            // too. Housing, the income huts, and the FIRST Barracks (the army
            // floor's trainer) are bounded purchases the save must run above,
            // not instead of.
            // EVERY EXTRACTOR PASSES, not just the Gatherer's Hut
            // (2026-09-08). The list above named the hut and missed Mine,
            // VeilstoneMine, so a faction saving for a
            // territory claim refused to build the ore income for as long as the
            // save ran — and the save runs until supplies accumulate, which
            // is what the ore income is for. Log-proven in the 22:36 Hard-AI
            // match: six straight minutes of
            //     "VeilstoneMine: 5 node(s), last refusal: pivotal hold"
            //     "Mine: 3 node(s), last refusal: pivotal hold"
            // with the nodes free inside its own territory, ending the match
            // on 396 supplies and zero ore extractors while the human it was
            // playing had twelve. IsExtractor is the whole class, so a future
            // extractor cannot fall through the same hole.
            // RESOURCE-AWARE (2026-10-03): the hold applies only when the
            // building spends a resource the save is short on.
            if (TheWaningBorder.AI.AIPivotalReserve.ShouldHold(em, faction, cost)
                && buildingId != "Fortress"
                && buildingId != "VaultOfAlmierra"
                && buildingId != "FiendstoneKeep"
                && buildingId != "TempleOfRidan"
                && buildingId != "Hut"
                && !TheWaningBorder.World.Regions.TerritoryOwnership.IsExtractor(buildingId)
                && !(buildingId == "Barracks"
                     && CountFactionBuildings<BarracksTag>(em, faction) == 0)
                // A SATURATED LINE PASSES TOO (2026-09-12, Game_AI.md 6c).
                // Every trainer of this kind has a full queue, so this
                // building is the army's actual bottleneck -- exactly the
                // "bounded, self-repaying essential" the exemptions above
                // exist for. Holding it starves the army to buy land, and the
                // land is only worth holding if there is an army.
                && !ProductionLineSaturated(em, faction, buildingId)
                // A LOST SOLE TRAINER PASSES (2026-10-04, Game_AI.md 6c):
                // the replacement for a production line the faction no
                // longer has at all (EnsureLostTrainersRebuilt).
                && !_rebuildingLostTrainer)
            { reason = "pivotal hold (saving)"; return false; }

            int crew = CountAliveWorkers(em, faction);
            if (crew == 0) { reason = "no build crew"; return false; }
            // The lost-trainer rebuild ignores the open-site cap (2026-10-04):
            // a line with no building at all outranks every other site, and
            // its site is never orphaned — busy workers take it as their next
            // job (AICommon.PullWorkersTo). Measured Headless28: "38 sites
            // open, crew 3" refused a Siege Yard rebuild outright.
            if (_rebuildingLostTrainer) return true;
            int openSites = CountFactionBuildingsUnderConstruction(em, faction);
            int siteCap = math.max(2, crew);
            if (openSites >= siteCap)
            {
                reason = AILogger.Enabled ? $"{openSites} sites open, crew {crew}" : "sites open";
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // LOST SOLE TRAINER (2026-10-04, Game_AI.md 6c)
        //
        // A 60-minute batch logged "floor blocked: deficit N x Spearman — no
        // trainer" 67 times: a faction whose only Barracks was razed did not
        // put it back. The goal list does ask for it, but the goal list runs
        // LAST in a think, after the economy tick has already spent the bank,
        // and through the Military wallet -- so the one building the whole
        // army waits on lost to every hut and research ahead of it.
        //
        // A production line the faction HAD and now has none of -- no
        // finished building, no site, no plan -- is rebuilt first thing in
        // the think, bank-direct, past the savings hold and one site past the
        // open-site cap. The Barracks always (it hosts the basics and the
        // army floor); the other lines when the composition plan still wants
        // a unit that building trains. A line never owned is the opening's
        // business (the goal list's first-of-line rule), not this.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Production lines the rebuild watches, in priority order.
        /// A roster table, not tuning: the ids are the buildings' SO ids.</summary>
        private static readonly string[] LostTrainerLines =
            { "Barracks", "ArcheryRange", "Alanthor_RoyalStable", "Alanthor_SiegeYard" };

        /// <summary>True only while EnsureLostTrainersRebuilt is placing —
        /// read by PassesBuildPreflight.</summary>
        private bool _rebuildingLostTrainer;

        /// <summary>True only while a veilstone-surplus Fortress on outcrop
        /// ground is being placed (EnsureFortressExpansion, Game_AI.md 5e) —
        /// TryBuildBuildingWithReason then skips the army's veilstone
        /// earmark.</summary>
        private bool _surplusEarmarkCarve;

        /// <summary>Per faction, a bit per LostTrainerLines entry the faction
        /// has ever owned (site and plan included). Keyed lookups only.</summary>
        private readonly System.Collections.Generic.Dictionary<int, int> _linesEverOwned
            = new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>(faction, line) -> earliest time the rebuild may place again.</summary>
        private readonly System.Collections.Generic.Dictionary<(int, int), float> _lostTrainerRetryAt
            = new System.Collections.Generic.Dictionary<(int, int), float>();

        /// <summary>(faction, line) -> next time a refusal may be logged.</summary>
        private readonly System.Collections.Generic.Dictionary<(int, int), float> _lostTrainerLogAt
            = new System.Collections.Generic.Dictionary<(int, int), float>();

        private static int CountLine(EntityManager em, Faction faction, int line) => line switch
        {
            0 => CountFactionBuildings<BarracksTag>(em, faction),
            1 => CountFactionBuildings<ArcheryRangeTag>(em, faction),
            2 => CountFactionBuildings<RoyalStableTag>(em, faction),
            _ => CountFactionBuildings<SiegeYardTag>(em, faction),
        };

        /// <summary>Does the composition plan still want a unit this building
        /// trains (any row with a raw share, by the building SO's trains[])?</summary>
        private static bool CompositionWantsLine(EntityManager em, Faction faction, string lineId)
        {
            if (!TechCatalog.TryGetBuilding(lineId, out var bdef) || bdef?.trains == null) return false;
            var p = GetArmyPlan(em, faction);
            for (int r = 0; r < p.N; r++)
            {
                if (p.Rows[r] == null || p.Raw[r] <= 0f) continue;
                string unit = p.Rows[r].unitId;
                for (int k = 0; k < bdef.trains.Length; k++)
                    if (bdef.trains[k] == unit) return true;
            }
            return false;
        }

        /// <summary>(faction, line) -> when the faction found itself without
        /// that line (for the "Ns without one" figure in the log).</summary>
        private readonly System.Collections.Generic.Dictionary<(int, int), float> _lostTrainerSince
            = new System.Collections.Generic.Dictionary<(int, int), float>();

        /// <summary>Factions told once that their rebuilds are suspended for
        /// want of a capital (keyed lookups only).</summary>
        private readonly System.Collections.Generic.HashSet<int> _lostTrainerNoCapital
            = new System.Collections.Generic.HashSet<int>();

        /// <summary>AIPivotalReserve keys, one per LostTrainerLines entry.</summary>
        private static readonly string[] LostTrainerSaveKeys =
            { "LostTrainer:Barracks", "LostTrainer:ArcheryRange",
              "LostTrainer:Alanthor_RoyalStable", "LostTrainer:Alanthor_SiegeYard" };

        private static void ClearLostTrainerSaves(Faction faction)
        {
            for (int i = 0; i < LostTrainerSaveKeys.Length; i++)
                AIPivotalReserve.Clear(faction, LostTrainerSaveKeys[i]);
        }

        /// <summary>"lost sole trainer: X site queued on N busy worker(s)".</summary>
        private static void NoteLostTrainerPull(Faction faction, string buildingId, int pulled)
        {
            AILogger.Log(faction, "BUILDING", pulled > 0
                ? $"lost sole trainer: {buildingId} site queued on {pulled} busy worker(s) (none idle)"
                : $"lost sole trainer: {buildingId} site placed with no worker to take it");
        }

        /// <summary>
        /// Rebuild a lost production line before anything else in the think
        /// spends the bank. At most one placement per think.
        ///
        /// WHAT BLOCKED IT (2026-10-04, Headless28, 160 refusal lines): "no
        /// hall" 92 -- factions already without a capital, eliminated in all
        /// but name; "bank short" 41 -- a besieged faction whose supplies
        /// every other spender drained between thinks (Hollow Table Red:
        /// 31 minutes without a Barracks, its Fortress standing, 7,000 iron
        /// banked and supplies at 20-200 against a 220-supply Barracks);
        /// "no legal spot" 18 -- the wall band and corridor were 517 of 672
        /// candidates for Sundered Crown Yellow's Siege Yard, gone for the
        /// last 23 minutes; "no build crew" 5 and "38 sites open" 1. So:
        ///   * no capital: stop, quietly (one line, then nothing);
        ///   * bank short: a STRICT savings reserve for the line's price
        ///     (AIPivotalReserve), so the other spenders stop eating it;
        ///   * no spot: the search keeps off the wall's own cells only (the
        ///     walkway yields) and skips the failed-search memory; a failure
        ///     waits lostTrainerSearchRetrySeconds;
        ///   * no crew: train a Worker; the open-site cap does not apply and
        ///     the site is queued on busy workers when none is idle.
        /// </summary>
        private void EnsureLostTrainersRebuilt(EntityManager em, Faction faction, float now)
        {
            int key = (int)faction;

            // NO CAPITAL, NO REBUILD -- quietly. Every placement anchors on
            // the Hall; without one nothing can be placed, and the faction is
            // eliminated in all but name.
            if (FindFactionBuilding<HallTag>(em, faction) == Entity.Null)
            {
                ClearLostTrainerSaves(faction);
                if (_lostTrainerNoCapital.Add(key))
                    AILogger.Log(faction, "BUILDING",
                        "lost sole trainer: rebuilds suspended — no capital standing (quiet until one stands)");
                return;
            }
            _lostTrainerNoCapital.Remove(key);

            _linesEverOwned.TryGetValue(key, out int owned);
            bool placed = false;
            for (int line = 0; line < LostTrainerLines.Length; line++)
            {
                int bit = 1 << line;
                string id = LostTrainerLines[line];
                if (CountLine(em, faction, line) > 0)
                {
                    owned |= bit;
                    AIPivotalReserve.Clear(faction, LostTrainerSaveKeys[line]);
                    if (_lostTrainerSince.TryGetValue((key, line), out float lostAt))
                    {
                        _lostTrainerSince.Remove((key, line));
                        AILogger.Log(faction, "BUILDING",
                            $"lost sole trainer: {id} line restored after {now - lostAt:F0}s");
                    }
                    continue;
                }
                if ((owned & bit) == 0) continue;                // never owned: the opening's job
                if (line > 0 && !CompositionWantsLine(em, faction, id))
                {
                    AIPivotalReserve.Clear(faction, LostTrainerSaveKeys[line]);
                    continue;
                }
                if (!_lostTrainerSince.TryGetValue((key, line), out float since))
                    _lostTrainerSince[(key, line)] = since = now;
                if (placed) continue;
                if (_lostTrainerRetryAt.TryGetValue((key, line), out float at) && now < at) continue;

                bool ok;
                string why;
                _rebuildingLostTrainer = true;
                try { ok = TryBuildBuildingWithReason(em, faction, id, out why); }
                finally { _rebuildingLostTrainer = false; }

                if (ok)
                {
                    placed = true;
                    _lostTrainerRetryAt[(key, line)] = now + Cfg.lostTrainerRetrySeconds;
                    AIPivotalReserve.Clear(faction, LostTrainerSaveKeys[line]);
                    if (TechCatalog.TryGetBuilding(id, out var def) && def != null)
                        AIBudget.RecordSpend(faction, AIBudgetCategory.Military, AICommon.ToCost(def.cost));
                    AILogger.Log(faction, "BUILDING",
                        $"lost sole trainer: {id} gone — rebuilding first (bank-direct, past the hold; " +
                        $"{now - since:F0}s without one)");
                    continue;
                }

                string action = null;
                if (why != null && why.StartsWith("bank short"))
                {
                    // SAVE FOR IT: a strict reserve stops every discretionary
                    // spender (buildings, research, levels, the army past its
                    // essentials) from eating the price between thinks.
                    if (Cfg.lostTrainerSaveStrict
                        && !AIPivotalReserve.Has(faction, LostTrainerSaveKeys[line]))
                    {
                        Cost cost = default;
                        if (TheWaningBorder.Data.BuildCosts.Exists(id))
                            cost = TheWaningBorder.Data.BuildCosts.For(em, faction, id);
                        else if (TechCatalog.TryGetBuilding(id, out var cdef) && cdef != null)
                            cost = AICommon.ToCost(cdef.cost);
                        AIPivotalReserve.Set(faction, LostTrainerSaveKeys[line], cost, strict: true);
                        action = "saving for it (strict reserve)";
                    }
                }
                else if (why == "no build crew")
                {
                    action = TryTrainUnit(em, faction, "Worker")
                        ? "training a Worker for it" : "no Worker trainable";
                }
                else if (why != null && why.StartsWith("no legal"))
                {
                    _lostTrainerRetryAt[(key, line)] = now + Cfg.lostTrainerSearchRetrySeconds;
                }

                if (action != null
                    || !_lostTrainerLogAt.TryGetValue((key, line), out float logAt) || now >= logAt)
                {
                    _lostTrainerLogAt[(key, line)] = now + 60f;
                    AILogger.Log(faction, "BUILDING",
                        $"lost sole trainer: {id} gone — rebuild refused ({why ?? "no reason"}; " +
                        $"{now - since:F0}s without one)" + (action != null ? $" — {action}" : ""));
                }
            }
            _linesEverOwned[key] = owned;
        }

        // ─────────────────────────────────────────────────────────────────
        // FAILED-SEARCH MEMORY (2026-09-25 AI perf pass)
        //
        // A site search that fails keeps failing until the ground changes, and
        // several callers retried it every think: the hut pipeline, the
        // housing loop, the goal list, a funded claim (which bypasses its own
        // interval). Each retry was a full scan. A failure is now remembered
        // per (faction, building, anchor cell) for failedSiteSearchCooldown
        // seconds — and forgotten early the moment a building is razed
        // anywhere or territory changes hands, the two things that free ground.
        // ─────────────────────────────────────────────────────────────────

        private struct SiteFail
        {
            public float Until;
            public int BuildingCount;
            public int TerritoryVersion;
            public string Reason;
        }

        private readonly System.Collections.Generic.Dictionary<(int, string, int, int), SiteFail>
            _siteFails = new System.Collections.Generic.Dictionary<(int, string, int, int), SiteFail>();

        private static (int, string, int, int) SiteKey(Faction f, string id, float3 anchor)
            => ((int)f, id, (int)math.floor(anchor.x / 8f), (int)math.floor(anchor.z / 8f));

        private bool SiteSearchRemembered(EntityManager em, Faction faction, string buildingId,
            float3 anchor, out string reason)
        {
            reason = null;
            if (!_siteFails.TryGetValue(SiteKey(faction, buildingId, anchor), out var f)) return false;
            if (_thinkNow >= f.Until
                || BuildSiteSnapshot.Current(em).BuildingCount < f.BuildingCount
                || TheWaningBorder.World.Regions.TerritoryOwnership.Version != f.TerritoryVersion)
            {
                _siteFails.Remove(SiteKey(faction, buildingId, anchor));
                return false;
            }
            reason = f.Reason;
            return true;
        }

        private void RememberFailedSiteSearch(EntityManager em, Faction faction, string buildingId,
            float3 anchor, string reason)
        {
            if (Cfg.failedSiteSearchCooldown <= 0f) return;
            _siteFails[SiteKey(faction, buildingId, anchor)] = new SiteFail
            {
                Until = _thinkNow + Cfg.failedSiteSearchCooldown,
                BuildingCount = BuildSiteSnapshot.Current(em).BuildingCount,
                TerritoryVersion = TheWaningBorder.World.Regions.TerritoryOwnership.Version,
                Reason = reason,
            };
        }




        /// <summary>
        /// Ring search for a legal site. <paramref name="inconclusive"/> is
        /// true when the search stopped on this think's candidate budget
        /// rather than exhausting its rings — not a proof that no site exists.
        ///
        /// SNAPSHOT-BACKED (2026-09-25 AI perf pass): every per-candidate world
        /// read (the building list for spacing and overlap, the obstacle list,
        /// the node / extractor / Hall lists for the node gate and hall cap)
        /// now comes from ONE BuildSiteSnapshot per tick instead of being
        /// copied out of the world per candidate. The rules are unchanged —
        /// the snapshot runs the router's own validator stages.
        /// </summary>
        private bool TryFindBuildPosition(EntityManager em, float3 anchor, int2 size, string buildingId,
            Faction faction, out float3 pos, out bool inconclusive)
        {
            inconclusive = false;
            var snap = BuildSiteSnapshot.Current(em);

            bool placingGHut = buildingId == "GatherersHut";

            // THE DRAWN BASE FIRST (2026-10-06, Game_AI.md § 6g): a building
            // the territory's layout has a slot for takes its next free slot
            // (moved to the nearest legal spot if blocked as drawn). The ring
            // search below runs only when every slot of its kind is used up.
            if (buildingId != "Fortress"
                && !TheWaningBorder.World.Regions.TerritoryOwnership.IsExtractor(buildingId)
                && !TheWaningBorder.World.Regions.TerritoryOwnership.IsClaimStructure(buildingId))
            {
                int slot = TryTemplateSlot(em, faction, buildingId, size, anchor, out pos);
                if (slot == SlotFound) return true;
                if (slot == SlotBudgetSpent)
                {
                    inconclusive = true;
                    _siteRefusalTally = "validation budget spent (base layout)";
                    pos = default;
                    return false;
                }
            }

            // THE HOUSE QUARTER (2026-10-02, operator: "AI should clump all
            // the houses together"). Once a faction has one House, every
            // later one is searched outward from the middle of the ones it
            // has, packed wall to wall with no centre spacing —
            // a single residential block instead of huts dotted through the
            // base. The first House still goes in the normal base ring.
            bool houseQuarter = buildingId == "Hut"
                && AICommon.TryHouseQuarterAnchor(em, faction, out anchor, anchor);

            // Resource keep-out: never wall off a patch's approach ring.
            // (Two copies per SEARCH, not per candidate.)
            var veilNodeQuery = QC_VeilstoneOutcroppingTagLocalTransform.Get(em, QT_VeilstoneOutcroppingTagLocalTransform);
            var ironNodeQuery = QC_IronMineTagLocalTransform.Get(em, QT_IronMineTagLocalTransform);
            using var veilNodeXfs = veilNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ironNodeXfs = ironNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float nodeClearSq = Cfg.minResourceNodeClearance * Cfg.minResourceNodeClearance;

            // Sample a ring of angles around the anchor at increasing radii.
            // GHs naturally need a wider ring to satisfy the 30 m spacing —
            // and their reach GROWS with every hut standing (2026-08-04): the
            // economy marches outward across the map instead of saturating
            // one ring around the Hall and stalling.
            float maxRadius = Cfg.buildRingDistanceMax;
            if (placingGHut)
                maxRadius = math.min(160f, Cfg.buildRingDistanceMax + 30f + snap.CountGathererHuts() * 12f);

            // COVERED-GROUND PREFERENCE for huts (2026-08-04, log-proven
            // churn: 16 huts built, 9 standing — the frontier ones died to
            // the curse). Pass 1 only accepts ground the faction already
            // holds (own influence at/over threshold, or inside the Hall
            // hearth ring); pass 2 falls back to any valid spot so the
            // spread never deadlocks. Hut expansion now FOLLOWS the
            // influence war instead of feeding it.
            // A CLAIM STARTS AT THE SEED. Every other building is extending a
            // base, so it wants a ring clear of the anchor; a Hall taking a
            // region wants the middle of that region, and starting 16 m out
            // biases it toward the border — or straight over it into the next
            // territory, which claims the wrong ground.
            // An extractor is sited the same way a claim is: ON the thing it
            // is for. Starting 16 m out would step off the node it has to stand
            // on, and every candidate would then fail the node gate above.
            bool isExtractor = TheWaningBorder.World.Regions.TerritoryOwnership
                                   .IsExtractor(buildingId);
            bool isClaim = TheWaningBorder.World.Regions.TerritoryOwnership
                               .IsClaimStructure(buildingId);
            bool onTarget = isClaim || isExtractor;
            // A Fortress is searched from its territory's seed / reserved
            // spot outward, not from a ring 16 m clear of it.
            // A COVERAGE TOWER (Game_AI.md 5g) is searched from its chosen
            // site outward, and only within towerSiteSearchRadius of it: a
            // tower pushed off its site no longer covers what it was for.
            bool towerSite = _towerSiteExact && buildingId == TowerId;
            float ringMin = onTarget || houseQuarter || towerSite || buildingId == "Fortress"
                ? 0f : Cfg.buildRingDistanceMin;

            // THE RESERVED FORTRESS SPOT FIRST (Game_AI.md 5g): it was chosen
            // legal and kept clear by every other placer; take it as it is
            // when the hard rules still pass there.
            if (_fortressSpotExact && buildingId == "Fortress"
                && _siteRegionLock != TheWaningBorder.World.Regions.RegionMap.None)
            {
                var spot = BuildGrid.Snap(anchor, size);
                spot.y = TerrainUtility.GetHeight(spot.x, spot.z);
                if (FortressSpotGood(em, faction, _siteRegionLock, spot, size,
                        nodeClear: false, sealCheck: false, _thinkNow)
                    && snap.OnFreeNodeFor(em, buildingId, spot.x, spot.z))
                {
                    pos = spot;
                    return true;
                }
            }

            // AN EXTRACTOR IS SITED BY THE MAP, NOT BY LAYOUT PREFERENCE. It
            // must stand within 4 m of its node (OnFreeNodeFor below), and its
            // node is map data — so the layout keep-outs this loop enforces
            // cannot apply to it or they contradict the node gate outright:
            //   * the 14 m node clearance, applied to the extractor's OWN node
            //     kind, excludes every candidate the node gate would accept.
            //     Measured across six 30-minute batch matches: 58 huts (supply
            //     nodes are not in the keep-out lists) and NOT ONE Mine or
            //     Veilstone Mine, while the factions aged up and held free
            //     iron from the first minute.
            //   * the 20/30 m building spacing walls off a node whenever any
            //     building — another extractor on the neighbouring node
            //     included — stands near it, and "how many extractors a
            //     territory supports" is the node count's decision, not a
            //     spacing constant's (Regions.md §4).
            // Real overlap is still refused, and the clearance still applies
            // to the node kinds the building does NOT stand on.
            // The clearance is exempted for the WHOLE extractor class, not
            // just for the kind of node the building stands on (2026-09-08).
            // Naming the two ore tags left the Gatherer's Hut — whose own node
            // is a supply spot — still required to stand 14 m clear of every
            // veilstone and iron node, so any supply node near ore was
            // permanently unbuildable. In the 22:36 match that refused 424 of
            // 1968 candidates and left seven free supply nodes unused.
            //
            // …and an extractor's search stops where its node gate does
            // (2026-09-25). The anchor IS the node site and the gate accepts
            // only candidates within 4 m of a free node, so rings out to the
            // base ring's 48 m were 200+ candidates that could never pass.
            if (isExtractor) maxRadius = math.min(maxRadius, Cfg.extractorSearchRadius);
            if (towerSite) maxRadius = math.min(maxRadius, math.max(BuildGrid.CellSize, Cfg.towerSiteSearchRadius));

            // INSIDE THE WALLS (2026-09-25). When this faction has planned a
            // perimeter wall around the base, a base building must fit inside
            // it with room to walk — wider spacing must never push the layout
            // out through the wall line. Claims and extractors are sited by
            // the map, not by the base layout, and are exempt.
            float2 wallMin = default, wallMax = default;
            bool walled = !onTarget
                && TryGetWallInterior(em, faction, anchor, out wallMin, out wallMax);

            // THE RESERVED RING (2026-10-04, docs/Design/Game_AI.md § Walls).
            // The home wall ring is planned from the first minute
            // (AIWallCorridor) and its corridor is refused to every footprint
            // but an extractor's, whose node decides where it stands (the
            // ring routes round it instead). A base building whose anchor is
            // inside the ring stands inside it too.
            bool ringed = !onTarget && AIWallCorridor.InsideRing(em, faction, anchor);

            // Rejection tally, written into the refusal reason when the whole
            // search fails. "No legal spot" with no evidence is the diagnostic
            // hole that hid the extractor contradiction for a full batch.
            int nCorridor = 0, nSpot = 0, nSeal = 0;
            string firstSeal = null;
            float3 firstSealAt = default;
            int nCand = 0, nCover = 0, nSpacing = 0, nGap = 0, nNodeClear = 0, nWall = 0,
                nCurse = 0, nTerritory = 0, nNodeGate = 0, nHallCap = 0, nInvalid = 0,
                nOverlap = 0;

            // SPACING IS A PREFERENCE; HAVING A BARRACKS IS NOT (2026-09-12).
            // The 20 m building spacing is a layout nicety -- it keeps a base
            // walkable and stops huts strangling each other. It is not a rule
            // of the game, and nothing downstream depends on it. Yet it was
            // absolute, so a base that filled its own ground simply stopped
            // being able to build.
            //
            // Measured, Hollow Table 1v1, 2026-09-12: at minute 16 Red held 24
            // buildings, 13,166 iron, 11,315 veilstone -- and ONE UNIT, with no
            // Barracks and no Archery Range. Every one of the 216 candidates its
            // placement scan tried was refused, 201 of them on spacing alone.
            //
            // So the scan gets LAST-RESORT passes with the centre spacing
            // dropped. They are reached only when every normal pass has
            // already failed, and they relax nothing else.
            //
            // BUT NEVER FLUSH (2026-09-25, operator: "AI building placement is
            // too cramped. AI should space buildings more"). The old last
            // resort dropped spacing to ZERO, so a full base packed its later
            // buildings wall to wall — no lane for an army to walk through.
            // Every pass now also keeps a clear EDGE-TO-EDGE gap between
            // footprints, measured in build cells:
            //   normal passes : 20 m centre spacing AND buildingGapCells (2 = 4 m)
            //   loose pass    : no centre spacing,   buildingGapCells (4 m)
            //   last resort   : no centre spacing,   relaxedBuildingGapCells (1 = 2 m),
            //                   ring reach out to relaxedBuildRingDistanceMax
            // Extractors are exempt from all of it (their node decides), which
            // is why they run only the normal passes.
            // docs/Design/Game_AI.md §6b, GAME_MANUAL.md
            int normalPasses = placingGHut ? 2 : 1;
            int passes = isExtractor ? normalPasses : normalPasses + 2;
            float gapNormal = math.max(0, Cfg.buildingGapCells) * BuildGrid.CellSize;
            float gapRelaxed = math.max(0, Cfg.relaxedBuildingGapCells) * BuildGrid.CellSize;

            // INWARD FIRST (2026-10-04, Game_AI.md § Walls). The rings already
            // grow outward from the anchor; with the anchor inside the home
            // ring, a bearing whose candidate has reached the wall corridor
            // or left the ring can only meet the corridor, the ring outside
            // or foreign ground further out — so that bearing is dropped for
            // the rest of the pass instead of being proposed again at every
            // larger radius (1,913 "on wall corridor" lines in Headless28,
            // a median 39% of every failed search's candidates). A concave
            // ring could in principle re-enter along a ray; the next pass
            // starts every bearing afresh.
            var rayPastRing = _rayPastRing;
            int nPastRing = 0;
            bool lostTrainerCore = _rebuildingLostTrainer;

            for (int pass = 0; pass < passes; pass++)
            {
                bool requireCover = placingGHut && pass == 0;
                bool centreSpacing = pass < normalPasses && !houseQuarter && Cfg.minBuildingSpacing > 0f;
                bool lastResort = pass == normalPasses + 1;
                // The LOOSE pass only drops the centre spacing; with none to
                // drop (minBuildingSpacing 0 since buildings may sit flush,
                // 2026-10-04) it would re-run the normal pass candidate for
                // candidate, so it is skipped.
                if (pass == normalPasses && !isExtractor
                    && (houseQuarter || Cfg.minBuildingSpacing <= 0f)) continue;
                if (ringed) System.Array.Clear(rayPastRing, 0, rayPastRing.Length);
                // Houses in their quarter may touch: the lane is only a look.
                float gap = houseQuarter ? 0f : lastResort ? gapRelaxed : gapNormal;
                float passMax = lastResort && !towerSite
                    ? math.max(maxRadius, Cfg.relaxedBuildRingDistanceMax) : maxRadius;

                for (float r = ringMin; r <= passMax; r += 4f)
                {
                    int angleStart = (int)(NextRandFloat01() * BuildAngleSamples);
                    for (int i = 0; i < BuildAngleSamples; i++)
                    {
                        // Radius 0 is one point: a coverage tower tries it once.
                        if (towerSite && r <= 0f && i > 0) break;
                        int idx = (angleStart + i) % BuildAngleSamples;
                        // A bearing past the ring costs nothing (no budget).
                        if (ringed && rayPastRing[idx]) { nPastRing++; continue; }

                        // Per-think candidate budget: a think that has already
                        // scanned its share stops here, and says so.
                        if (_siteCandidatesLeft <= 0)
                        {
                            inconclusive = true;
                            _siteRefusalTally = "candidate budget spent";
                            pos = default;
                            return false;
                        }
                        _siteCandidatesLeft--;

                        float angle = (idx / (float)BuildAngleSamples) * math.PI * 2f;
                        float3 candidate = new float3(
                            anchor.x + math.cos(angle) * r,
                            0f,
                            anchor.z + math.sin(angle) * r);
                        // SNAP FIRST (2026-08-18). Every check below — spacing,
                        // node clearance, crust, validity — must see the
                        // position the building will ACTUALLY occupy, because
                        // BuildingFactory snaps on the way in. docs/Design/Build_Grid.md
                        candidate = BuildGrid.Snap(candidate, size);
                        candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                        nCand++;
                        // A Fortress for a chosen territory stands IN it
                        // (EnsureFortressExpansion sets the lock): a ring that
                        // reaches over the border into other held ground would
                        // otherwise place it where a Fortress already stands.
                        if (_siteRegionLock != TheWaningBorder.World.Regions.RegionMap.None
                            && TheWaningBorder.World.Regions.RegionMap.RegionAt(candidate.x, candidate.z)
                               != _siteRegionLock)
                        { nTerritory++; continue; }
                        if (requireCover && !IsCoveredGround(faction, candidate, anchor))
                        { nCover++; continue; }

                        if (!isExtractor)
                        {
                            // THE BORDER BAND: keep the strip the border wall
                            // will run along clear (AIWallPlanner). Claims
                            // are sited by the map and exempt.
                            if (!onTarget && !AIWallPlanner.FootprintClearOfBorder(em, candidate, size))
                            { nWall++; continue; }

                            // ON THE WALL CORRIDOR: the ring could not be
                            // closed through this footprint. The lost-trainer
                            // rebuild is held off the wall's own cells only
                            // (the walkway yields to a production line the
                            // army has none of); a small footprint likewise
                            // (AIWallCorridor.FootprintClear).
                            if (!AIWallCorridor.FootprintClear(em, faction, candidate, size, lostTrainerCore))
                            {
                                nCorridor++;
                                if (ringed) rayPastRing[idx] = true;
                                continue;
                            }
                            if (ringed && !AIWallCorridor.FootprintInsideRing(em, faction, candidate, size))
                            {
                                nWall++;
                                if (ringed) rayPastRing[idx] = true;
                                continue;
                            }

                            if (centreSpacing && snap.AnyCentreWithin(candidate,
                                    Cfg.minBuildingSpacing, placingGHut ? Cfg.minGHutToGHutSpacing : 0f))
                            { nSpacing++; continue; }

                            // Edge-to-edge lane. Wall pieces are skipped here
                            // (a diagonal curtain's AABB is mostly open ground)
                            // — the wall-interior test below keeps the lane
                            // along a perimeter, and the overlap test still
                            // refuses standing ON a wall.
                            if (gap > 0f && snap.Overlaps(candidate, size, gap, ignoreWalls: true))
                            { nGap++; continue; }

                            if (TooCloseToAny(candidate, veilNodeXfs, nodeClearSq)
                                || TooCloseToAny(candidate, ironNodeXfs, nodeClearSq))
                            { nNodeClear++; continue; }

                            if (walled && !FootprintInside(candidate, size,
                                    wallMin + Cfg.wallInteriorClearance,
                                    wallMax - Cfg.wallInteriorClearance))
                            { nWall++; continue; }

                            // THE RESERVED FORTRESS SPOTS stay clear, like
                            // the wall corridor (AIBaseLayout, Game_AI.md 5g).
                            if (!AIBaseLayout.FootprintClearOfFortressSpots(candidate, size, buildingId))
                            { nSpot++; continue; }
                            // …and so do the drawn layout's free slots (§ 6g).
                            if (!AIBaseTemplate.FootprintClearOfFreeSlots(em, faction, candidate, size))
                            { nSpot++; continue; }
                        }

                        // Never place on crusted ground (2026-08-04): the
                        // curse crumbles the foundation before workers
                        // arrive — money in, nothing out, forever.
                        if (IsCursedGround(em, candidate))
                        { nCurse++; continue; }

                        // TERRITORY GATE — the same rule the player's placement
                        // obeys (docs/Design/Regions.md §2). A HARD constraint.
                        if (!TheWaningBorder.World.Regions.TerritoryOwnership.CanBuildAt(
                                em, faction, buildingId, candidate.x, candidate.z))
                        { nTerritory++; continue; }

                        // …and the placement rules the router will apply:
                        // EVERY EXTRACTOR NEEDS ITS OWN FREE NODE, and a
                        // territory takes only one Hall.
                        if (!snap.OnFreeNodeFor(em, buildingId, candidate.x, candidate.z))
                        { nNodeGate++; continue; }
                        if (isClaim && snap.HallCapReached(em, candidate.x, candidate.z))
                        { nHallCap++; continue; }

                        // FOOTPRINT OVERLAP, the router's own last-line
                        // invariant (2026-09-12) — walls included. One headless
                        // match logged 204 IDENTICAL refusals for Yellow at
                        // (10,-46) before this test existed here.
                        if (snap.Overlaps(candidate, size, 0f, ignoreWalls: false))
                        { nOverlap++; continue; }
                        // …and the faction's OWN plans, which the router
                        // refuses (Planned_Buildings.md) but the building list
                        // above never sees (a plan has no BuildingTag).
                        if (snap.OverlapsOwnPlan(faction, candidate, size))
                        { nOverlap++; continue; }
                        // A spot where this faction's plan was cancelled for a
                        // persistent refusal (Planned_Buildings.md, the grace)
                        // is not tried again while the memory lasts.
                        if (PlannedBuildings.IsRecentlyRefused(faction, candidate, Cfg.refusedSpotRadius, _thinkNow))
                        { nOverlap++; continue; }

                        // The expensive stage (terrain slope/water samples):
                        // its own, smaller budget per think.
                        if (_siteValidationsLeft <= 0)
                        {
                            inconclusive = true;
                            _siteRefusalTally = "validation budget spent";
                            pos = default;
                            return false;
                        }
                        _siteValidationsLeft--;

                        // The id goes in so the validator can make the
                        // extractor-on-node exemption (and the Veilworks
                        // crust exception) — the id-less overload is the
                        // strict rule and refuses every on-node candidate.
                        if (!snap.IsValidBuildPosition(em, candidate, size, buildingId))
                        {
                            nInvalid++;
                            continue;
                        }

                        // FLUSH IS ALLOWED; SEALING THE BASE IS NOT
                        // (2026-10-04, Game_AI.md 6b). One bounded flood per
                        // accepted candidate (AIBaseLayout). Extractors are
                        // sited by the map and exempt.
                        if (!isExtractor && Cfg.sealCheckEnabled)
                        {
                            if (_sealChecksLeft <= 0)
                            {
                                inconclusive = true;
                                _siteRefusalTally = "seal-check budget spent";
                                pos = default;
                                return false;
                            }
                            _sealChecksLeft--;
                            string seal = AIBaseLayout.WouldSeal(em, faction, candidate, size,
                                buildingId, _thinkNow);
                            if (seal != null)
                            {
                                nSeal++;
                                if (firstSeal == null) { firstSeal = seal; firstSealAt = candidate; }
                                continue;
                            }
                        }
                        if (firstSeal != null)
                            AIBaseLayout.LogSeal(faction, buildingId, firstSealAt,
                                firstSeal + $" — sited elsewhere after {nSeal} such candidate(s)", _thinkNow);
                        pos = candidate;
                        return true;
                    }
                }
            }
            // The relaxed passes ALWAYS run before this line is reached, so a
            // spacing count here describes the normal passes only -- say so,
            // or the next reader concludes spacing is still the blocker.
            // Built only when a log can read it.
            _siteRefusalTally = AILogger.Enabled
                ? $"{nCand} cand (incl. relaxed passes): " +
                  $"cover {nCover}, spacing {nSpacing}, gap {nGap}, " +
                  $"nodeclear {nNodeClear}, wall {nWall}, corridor {nCorridor}, past-ring {nPastRing}, " +
                  $"curse {nCurse}, territory {nTerritory}, " +
                  $"nodegate {nNodeGate}, hallcap {nHallCap}, overlap {nOverlap}, " +
                  $"invalid {nInvalid}, fortress-spot {nSpot}, seal {nSeal}"
                : "";
            if (firstSeal != null)
                AIBaseLayout.LogSeal(faction, buildingId, firstSealAt, firstSeal, _thinkNow);
            AIWallCorridor.NoteRejected(faction, buildingId, nCorridor);
            pos = default;
            return false;
        }

        /// <summary>Per-bearing "this ray has reached the wall corridor"
        /// scratch for TryFindBuildPosition (single-threaded think loop).</summary>
        private readonly bool[] _rayPastRing = new bool[BuildAngleSamples];

        // Per-think site-search budgets, reset at the start of every think
        // (SimpleAISystem.OnUpdate).
        private int _siteCandidatesLeft, _siteValidationsLeft, _sealChecksLeft;

        /// <summary>Footprint of a candidate entirely inside [mn, mx].</summary>
        private static bool FootprintInside(float3 c, int2 size, float2 mn, float2 mx)
        {
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            return c.x - hx >= mn.x && c.x + hx <= mx.x
                && c.z - hz >= mn.y && c.z + hz <= mx.y;
        }

        /// <summary>
        /// The rectangle a planned PERIMETER wall encloses (bounding box of
        /// its hub slots), when the anchor lies inside it. Chokepoint plans
        /// seal corridors rather than enclose a box, so they bound nothing
        /// here. Reads the brain's AIWallPlan / AIWallPlanSlot, written by
        /// the Alanthor endgame wall doctrine.
        /// </summary>
        private static bool TryGetWallInterior(EntityManager em, Faction faction, float3 anchor,
            out float2 mn, out float2 mx)
        {
            mn = default; mx = default;
            Entity brain = FindBrainEntity(em, faction);
            if (brain == Entity.Null || !em.HasComponent<AIWallPlan>(brain)
                || !em.HasBuffer<AIWallPlanSlot>(brain)) return false;
            byte wallMode = em.GetComponentData<AIWallPlan>(brain).Mode;
            if (wallMode != AIWallPlanner.ModePerimeter && wallMode != AIWallPlanner.ModeBorder) return false;
            var slots = em.GetBuffer<AIWallPlanSlot>(brain, true);
            if (slots.Length < 3) return false;
            // One ring per walled territory (2026-10-03: the home, plus any
            // territory with its own Fortress), each its own chain and
            // contiguous in the buffer — the box is the ring the anchor is in,
            // never the box round every ring at once.
            int start = 0;
            while (start < slots.Length)
            {
                byte chain = slots[start].Chain;
                int end = start;
                mn = new float2(float.MaxValue);
                mx = new float2(float.MinValue);
                while (end < slots.Length && slots[end].Chain == chain)
                {
                    var p = new float2(slots[end].Position.x, slots[end].Position.z);
                    mn = math.min(mn, p);
                    mx = math.max(mx, p);
                    end++;
                }
                if (end - start >= 3
                    && anchor.x > mn.x && anchor.x < mx.x && anchor.z > mn.y && anchor.z < mx.y)
                    return true;
                start = end;
            }
            return false;
        }

        /// <summary>Why the last failed TryFindBuildPosition refused each
        /// candidate — appended to the "no legal spot" reason so a silent
        /// search failure names its gate. Single-threaded think loop, so a
        /// field is safe.</summary>
        private string _siteRefusalTally = "";
        /// <summary>Ground this faction already HOLDS: own influence at/over
        /// the threshold, or inside the anchor Hall's hearth ring (the Age 0
        /// case, when no influence exists yet).</summary>
        private static bool IsCoveredGround(Faction faction, float3 p, float3 hallAnchor)
        {
            float hr = TheWaningBorder.Core.Config.VeilCrustConstants.HallHearthRadius;
            float dx = p.x - hallAnchor.x, dz = p.z - hallAnchor.z;
            if (dx * dx + dz * dz <= hr * hr) return true;

            int f = (int)faction;
            if (f < 0 || f >= TheWaningBorder.Influence.PlayerInfluenceMap.PlayerChannels)
                return false;
            return TheWaningBorder.Influence.PlayerInfluenceMap.Ready
                && TheWaningBorder.Influence.PlayerInfluenceMap.ChannelStrengthWorld(f, p.x, p.z)
                    >= TheWaningBorder.Core.Config.VeilCrustConstants.InfluenceThreshold;
        }

        /// <summary>Plain XZ proximity check against a position set — used
        /// for the resource-node keep-out.</summary>
        private static bool TooCloseToAny(
            float3 candidate,
            NativeArray<LocalTransform> positions,
            float minDistSq)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                float dx = candidate.x - positions[i].Position.x;
                float dz = candidate.z - positions[i].Position.z;
                if (dx * dx + dz * dz < minDistSq) return true;
            }
            return false;
        }

        /// <summary>True when the crust at this position is at/over the crust
        /// threshold. Building here burns money — the curse crumbles the
        /// foundation within seconds (the log-proven hut-pipeline loop:
        /// "started (total 14)" every 4 s while totals fell).</summary>
        private static bool IsCursedGround(EntityManager em, float3 p)
        {
            if (!TryGetVeilField(em, out var field)) return false;
            int cx = (int)math.floor((p.x - field.Origin.x) / field.CellSize);
            int cz = (int)math.floor((p.z - field.Origin.y) / field.CellSize);
            if (cx < 0 || cx >= field.Width || cz < 0 || cz >= field.Height) return false;
            return field.Saturation[field.Index(cx, cz)] >= VeilField.CrustThreshold;
        }



        /// <summary>
        /// ANTI-STAGNATION: keep building Huts while population headroom is
        /// tight (and the absolute cap isn't reached). Runs every think tick —
        /// both during the build order and in maintenance — because the train
        /// pop-gate in TryTrainUnit depends on headroom eventually appearing.
        /// TryBuildBuilding's own pre-flights (cost, idle worker, valid spot)
        /// make the retry safe.
        /// </summary>
        private void EnsurePopulationHeadroom(EntityManager em, Faction faction)
        {
            if (!PopulationHelper.TryGetFactionPopulation(faction, out int current, out int max)) return;

            // BUILD OUT TO THE CEILING, don't chase demand up to it.
            //
            // This used to wait until spare population fell under a floor, so
            // housing was always a reaction to being nearly capped and the cap
            // only ever crept up behind an army that was already blocked.
            // Across 26 measured matches the median cap reached 52 of 200 and
            // the median army 13 — the AI never had room it had not already
            // filled, so it never behaved like a player who houses first and
            // trains into the space.
            //
            // 200 is the ceiling every faction should reach, so the Huts for it
            // are simply part of the build: ~18 of them at 80 supplies is about
            // 1,440 against the ~12,000 earned in twenty minutes. One per call
            // keeps it paced and lets the budget refuse when the money is
            // genuinely needed elsewhere.
            if (max >= FactionPopulation.AbsoluteMax) return;

            // BUILD AHEAD ONLY OUT OF SURPLUS.
            //
            // Unconditional building-out hit the ceiling — caps reached 190 of
            // 200, which the reactive version never came close to — but it
            // took a Hut every think tick out of the EconomyExpansion wallet
            // and starved everything else drawing on it. Measured: 25 "wallet
            // short" refusals in seven minutes, 29 of them the build order's
            // own TrainUnit:Worker step, which then burned its 92-second
            // timeout and was skipped.
            //
            // So: always build when population is ACTUALLY about to block, and
            // otherwise only when the wallet still covers a worker and a
            // gatherer's hut afterwards. The ceiling is still the target; it is
            // just no longer paid for out of the build order's pocket.
            if (TheWaningBorder.Entities.BuildingFactory.AtFactionCap(em, faction, "Hut")) return;
            bool blocking = max - current <= HousingHeadroomFloor(faction);
            if (!blocking && OpeningHutsPending(faction)) return;   // the huts come first
            if (!blocking)
            {
                int spare = AIBudget.WalletSupplies(faction, AIBudgetCategory.EconomyExpansion);
                if (spare < Cfg.hutCostSupplies + Cfg.economyWorkingFloor) return;
            }
            TryBuildBuildingBudgeted(em, faction, "Hut", AIBudgetCategory.EconomyExpansion);
        }
    }
}
