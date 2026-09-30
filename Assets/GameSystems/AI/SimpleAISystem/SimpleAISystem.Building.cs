// SimpleAISystem.Building.cs
// Building placement: siting rules, spacing, builder dispatch, pop headroom.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

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
        /// Place + dispatch builders for <paramref name="buildingId"/>. The
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
        /// idle-builder gate among them — and one of those inferences was
        /// wrong. A build path this load-bearing states its own cause.
        /// </summary>
        private bool TryBuildBuildingWithReason(EntityManager em, Faction faction,
            string buildingId, out string reason, float3? anchorOverride = null)
        {
            reason = null;
            // A build order naming a choice building (or the retired Shrine)
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
            if (buildingId == "Alanthor_Wall"
                || buildingId == "Alanthor_WallTower"
                || buildingId == "Alanthor_WallGate")
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

            int2 size = BuildingSizeConfig.GetSize(buildingId);

            // CHEAP GATES FIRST (2026-09-25 AI perf pass). The pivotal hold,
            // the crew and the open-site cap used to be tested AFTER the site
            // search — so every think that was going to be refused anyway for
            // "saving" or "5 sites open" first paid for a full ring scan of up
            // to ~3,000 candidates. Same gates, same verdicts; only the order
            // (and therefore which reason a double refusal reports) changed.
            if (!PassesBuildPreflight(em, faction, buildingId, out reason)) return false;

            // A search that just failed here, for this building, on this
            // ground, is not re-run until something that could change the
            // answer has changed — see SiteSearchRemembered.
            if (buildingId != "FiendstoneKeep"
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
            // is created on every peer two ticks later, so builders are
            // dispatched at the POSITION with a null target and auto-find the
            // site on arrival (same pattern as the human MP flow in
            // BuildCommandPannel).
            // A HALL NEEDS ITS BUILDER ON SITE (Regions.md §2): walk a worker
            // there first and place on a later think, once it has arrived.
            // The worker then rides the command so the executor can re-check.
            // (No builder rides the command any more: the Hall — the only
            // building that needed one on site — is removed.)
            Entity claimBuilder = Entity.Null;

            bool queued = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                claimBuilder, out Entity building, CommandSource.AI);
            // Whatever happened, buildings / the bank may have changed: every
            // memoised count is stale, and the site is taken for the rest of
            // this tick (in lockstep the foundation appears two ticks later).
            InvalidateThinkMemo();
            if (queued || building != Entity.Null)
                BuildSiteSnapshot.Current(em).NotePlaced(em, buildingId, pos, size);
            if (queued)
            {
                AICommon.DispatchBuildersTo(em, faction, Entity.Null, buildingId, pos, maxBuilders: 2);
                // No rollback path here: the placement command is already
                // queued on every peer. Past the idle-builder pre-flight a
                // zero dispatch is a rare race; builders auto-chain to nearby
                // unfinished structures, so the site still gets picked up.
                return true;
            }
            if (building == Entity.Null) return false;

            // Dispatch idle builders to actually construct the thing — without
            // this the building is created with HP=1 and UnderConstruction but
            // never gains progress. The human player flow does the same step
            // explicitly via BuildCommandPanel.AssignBuildersToConstruction.
            int dispatched = AICommon.DispatchBuildersTo(em, faction, building, buildingId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                // Race: a builder went busy between the pre-flight check and
                // dispatch. Refund + destroy the orphan foundation rather
                // than advancing the step on a stalled site. Refund the
                // amount PlaceBuildingDirect actually charged (stamped on the
                // foundation) — an escalated Hall refunds its own price.
                TheWaningBorder.Data.BuildCosts.TryGetPaid(em, building, buildingId, out var paid);
                FactionEconomy.Add(em, faction, paid);
                em.DestroyEntity(building);
                InvalidateThinkMemo();
                BuildSiteSnapshot.Invalidate();
                return false;
            }
            return true;
        }

        /// <summary>
        /// The build pre-flight that does not depend on WHERE: the savings
        /// hold, a build crew, and the open-site cap. Run before the site
        /// search so a refusal costs nothing.
        /// </summary>
        private bool PassesBuildPreflight(EntityManager em, Faction faction,
            string buildingId, out string reason)
        {
            reason = null;

            // Pre-flight: the faction must have a build crew, and not already
            // have more sites open than that crew can work.
            //
            // THIS USED TO DEMAND AN *IDLE* BUILDER, and that was fatal once the
            // crew shrank. Workers only build now (Regions.md §4), so the target
            // dropped from 14-45 to 3-5 — and since two builders are dispatched
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
            // is handled without the idle test: builders auto-chain to nearby
            // unfinished structures within line of sight, so a queued site gets
            // picked up as soon as a builder frees. What actually has to be
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
            // VeilstoneMine and Alanthor_Smelter, so a faction saving for a
            // Hall claim refused to build the ore income for as long as the
            // save ran — and the save runs until supplies accumulate, which
            // is what the ore income is for. Log-proven in the 22:36 Hard-AI
            // match: six straight minutes of
            //     "VeilstoneMine: 5 node(s), last refusal: pivotal hold"
            //     "Mine: 3 node(s), last refusal: pivotal hold"
            // with the nodes free inside its own territory, ending the match
            // on 396 supplies and zero ore extractors while the human it was
            // playing had twelve. IsExtractor is the whole class, so a future
            // extractor cannot fall through the same hole.
            if (TheWaningBorder.AI.AIPivotalReserve.ShouldHold(em, faction)
                && buildingId != "Hall"
                && buildingId != "ShrineOfRidan"
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
                && !ProductionLineSaturated(em, faction, buildingId))
            { reason = "pivotal hold (saving)"; return false; }

            int crew = CountAliveMiners(em, faction);
            if (crew == 0) { reason = "no build crew"; return false; }
            int openSites = CountFactionBuildingsUnderConstruction(em, faction);
            if (openSites >= math.max(2, crew))
            {
                reason = AILogger.Enabled ? $"{openSites} sites open, crew {crew}" : "sites open";
                return false;
            }
            return true;
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
            float ringMin = onTarget ? 0f : Cfg.buildRingDistanceMin;

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

            // INSIDE THE WALLS (2026-09-25). When this faction has planned a
            // perimeter wall around the base, a base building must fit inside
            // it with room to walk — wider spacing must never push the layout
            // out through the wall line. Claims and extractors are sited by
            // the map, not by the base layout, and are exempt.
            float2 wallMin = default, wallMax = default;
            bool walled = !onTarget
                && TryGetWallInterior(em, faction, anchor, out wallMin, out wallMax);

            // Rejection tally, written into the refusal reason when the whole
            // search fails. "No legal spot" with no evidence is the diagnostic
            // hole that hid the extractor contradiction for a full batch.
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

            for (int pass = 0; pass < passes; pass++)
            {
                bool requireCover = placingGHut && pass == 0;
                bool centreSpacing = pass < normalPasses;
                bool lastResort = pass == normalPasses + 1;
                float gap = lastResort ? gapRelaxed : gapNormal;
                float passMax = lastResort
                    ? math.max(maxRadius, Cfg.relaxedBuildRingDistanceMax) : maxRadius;

                for (float r = ringMin; r <= passMax; r += 4f)
                {
                    int angleStart = (int)(NextRandFloat01() * BuildAngleSamples);
                    for (int i = 0; i < BuildAngleSamples; i++)
                    {
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

                        int idx = (angleStart + i) % BuildAngleSamples;
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
                        if (requireCover && !IsCoveredGround(faction, candidate, anchor))
                        { nCover++; continue; }

                        if (!isExtractor)
                        {
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
                        }

                        // Never place on crusted ground (2026-08-04): the
                        // curse crumbles the foundation before builders
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
                        if (snap.IsValidBuildPosition(em, candidate, size, buildingId))
                        {
                            pos = candidate;
                            return true;
                        }
                        nInvalid++;
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
                  $"nodeclear {nNodeClear}, wall {nWall}, curse {nCurse}, territory {nTerritory}, " +
                  $"nodegate {nNodeGate}, hallcap {nHallCap}, overlap {nOverlap}, " +
                  $"invalid {nInvalid}"
                : "";
            pos = default;
            return false;
        }

        // Per-think site-search budgets, reset at the start of every think
        // (SimpleAISystem.OnUpdate).
        private int _siteCandidatesLeft, _siteValidationsLeft;

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
            mn = new float2(float.MaxValue);
            mx = new float2(float.MinValue);
            for (int i = 0; i < slots.Length; i++)
            {
                var p = new float2(slots[i].Position.x, slots[i].Position.z);
                mn = math.min(mn, p);
                mx = math.max(mx, p);
            }
            return anchor.x > mn.x && anchor.x < mx.x && anchor.z > mn.y && anchor.z < mx.y;
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
        /// TryBuildBuilding's own pre-flights (cost, idle builder, valid spot)
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
            bool blocking = max - current <= Cfg.populationHeadroomFloor;
            if (!blocking)
            {
                int spare = AIBudget.WalletSupplies(faction, AIBudgetCategory.EconomyExpansion);
                if (spare < Cfg.hutCostSupplies + Cfg.economyWorkingFloor) return;
            }
            TryBuildBuildingBudgeted(em, faction, "Hut", AIBudgetCategory.EconomyExpansion);
        }
    }
}
