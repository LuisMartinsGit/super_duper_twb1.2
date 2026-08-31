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
        // Build placement scan ring: how far from the Hall and at how many
        // angles we try before giving up on this tick. The min was bumped to
        // 10 m and max to 30 m so buildings have room to fan out around the
        // Hall without crowding it (and around each other — see spacing below).
        // Doubled with the building footprints (2026-08-13): the Hall's
        // half-extent went 2 m -> 4 m and a typical building's 2 m -> 4 m, so a
        // 10 m ring start left only 2 m of gap and most candidates failed
        // validation against the Hall itself.
        private const float BuildRingDistanceMin = 16f;
        private const float BuildRingDistanceMax = 48f;
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
                if (era < def.minEra) { reason = $"era {era} < minEra {def.minEra}"; return false; }
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

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost))
            { reason = $"bank short ({cost.Supplies}s {cost.Iron}i {cost.Veilstone}v)"; return false; }

            int2 size = BuildingSizeConfig.GetSize(buildingId);

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
            else if (!TryFindBuildPosition(em, anchor, size, buildingId, faction, out pos))
            {
                reason = $"no legal {size.x}x{size.y} spot near ({anchor.x:F0},{anchor.z:F0}) " +
                         $"[{_siteRefusalTally}]";
                return false;
            }

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
            if (TheWaningBorder.AI.AIPivotalReserve.ShouldHold(em, faction)
                && buildingId != "Hall"
                && buildingId != "ShrineOfRidan"
                && buildingId != "VaultOfAlmierra"
                && buildingId != "FiendstoneKeep"
                && buildingId != "TempleOfRidan")
            { reason = "pivotal hold (saving)"; return false; }

            int crew = CountAliveMiners(em, faction);
            if (crew == 0) { reason = "no build crew"; return false; }
            int openSites = CountFactionBuildingsUnderConstruction(em, faction);
            if (openSites >= math.max(2, crew))
            { reason = $"{openSites} sites open, crew {crew}"; return false; }

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
            bool queued = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queued)
            {
                DispatchBuildersTo(em, faction, Entity.Null, buildingId, pos, maxBuilders: 2);
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
            int dispatched = DispatchBuildersTo(em, faction, building, buildingId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                // Race: a builder went busy between the pre-flight check and
                // dispatch. Refund + destroy the orphan foundation rather
                // than advancing the step on a stalled site. Refund the
                // BuildCosts price — the amount PlaceBuildingDirect actually
                // charged — not the catalog cost used for the decision gate.
                FactionEconomy.Add(em, faction,
                    TheWaningBorder.Data.BuildCosts.Get(buildingId));
                em.DestroyEntity(building);
                return false;
            }
            return true;
        }
        /// <summary>
        /// Count the faction's idle builders. Cheap O(N) snapshot used as a
        /// pre-flight gate so TryBuildBuilding doesn't spend resources on a
        /// foundation that no builder will ever pick up. (task-062 G-2)
        /// </summary>
        private static int CountIdleBuilders(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (IsCommittedWorker(em, ents[i])) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Find up to <paramref name="maxBuilders"/> idle builders of the given
        /// faction and issue BuildCommand on each, pointing at <paramref name="site"/>.
        /// Idle = has CanBuild but no current BuildOrder.
        /// </summary>
        /// <returns>Number of builders actually dispatched (0 = nobody available).</returns>
        private static int DispatchBuildersTo(
            EntityManager em, Faction faction, Entity site,
            string buildingId, float3 sitePos, int maxBuilders)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs  = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Collect available builders + their distance² to the site. Workers
            // already committed to a build/repair are never pulled. Truly idle
            // workers are preferred over mining ones (mining is interruptible —
            // construction is imperative — but only as a second choice).
            var idle = new System.Collections.Generic.List<BuilderCandidate>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var b = ents[i];
                if (IsCommittedWorker(em, b)) continue;           // already building/repairing
                // No worker gathers any more (Regions.md §4), so no candidate
                // is ever "busy mining" and every uncommitted worker is an
                // equally good pick. Kept as a named local so the sort below
                // still reads as a preference rather than a mystery false.
                const bool mining = false;
                float dx = xfs[i].Position.x - sitePos.x;
                float dz = xfs[i].Position.z - sitePos.z;
                idle.Add(new BuilderCandidate { Entity = b, DistSq = dx * dx + dz * dz, Mining = mining });
            }

            // Sort: idle workers first, then by distance ascending.
            idle.Sort((a, c) => a.Mining != c.Mining
                ? a.Mining.CompareTo(c.Mining)
                : a.DistSq.CompareTo(c.DistSq));

            int dispatched = 0;
            for (int i = 0; i < idle.Count && dispatched < maxBuilders; i++)
            {
                // CommandSource.AI, not the LocalPlayer default — mislabeled
                // AI orders ride the player's command stream. (audit F20)
                CommandRouter.IssueBuild(em, idle[i].Entity, site, buildingId, sitePos,
                    CommandSource.AI);
                dispatched++;
            }
            return dispatched;
        }

        private struct BuilderCandidate
        {
            public Entity Entity;
            public float DistSq;
            public bool Mining;
        }

        // Default: candidate must be ≥12 m from any existing building so the
        // AI leaves wide walkable corridors. Earlier 7 m was just enough that
        // unit pathing could squeeze through, but Gaussian-smoothed flow at
        // tight cell-corner thresholds would dither and units got stuck.
        // Doubled with the footprints (2026-08-13). At the old 12 m, two 8 m
        // buildings sat 4 m apart edge-to-edge and two 12 m ones OVERLAPPED —
        // every candidate then failed IsValidBuildPosition and the AI simply
        // stopped building. 20 m restores the ~6-12 m corridor the comment
        // below describes, still wider than any unit's collision radius.
        private const float MinBuildingSpacing = 20f;
        /// <summary>Placement keep-out around resource nodes (veilstone
        /// outcroppings + iron deposits). Structures parked against a patch
        /// blocked the workers' approach ring — they orbited the node
        /// forever (2026-08-03 playtest). Sized so a 4x4-cell footprint plus
        /// worker corridor always fits between building edge and node.
        /// Raised with the footprint doubling — the "4x4-cell footprint" this
        /// was sized for is now 8 m across, not 4.</summary>
        private const float MinResourceNodeClearance = 14f;

        // Kept as plain SPACING, not as an income rule. It was sized so two
        // huts' 15 m gather circles stayed disjoint, and huts no longer earn
        // from an area at all (docs/Design/Regions.md §4) — but three huts
        // stacked on top of each other in one territory is still a wall across
        // the AI's own base, so the distance earns its keep on layout alone.
        private const float MinGHutToGHutSpacing = 30f;

        private bool TryFindBuildPosition(EntityManager em, float3 anchor, int2 size, string buildingId, Faction faction, out float3 pos)
        {
            // Snapshot existing buildings once per call. We need both positions
            // and "is GathererHut?" so we can apply the GH-vs-GH spacing rule.
            var bldgQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var bldgEntities  = bldgQuery.ToEntityArray(Allocator.Temp);
            using var bldgTransforms = bldgQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Pre-mark which existing buildings are GathererHuts so we can do
            // the 30 m check only against them when placing another GHut.
            // Managed bool[] sidesteps NativeArray's `using var` write-access
            // restriction and SimpleAISystem isn't Bursted, so it costs nothing.
            var bldgIsGHut = new bool[bldgEntities.Length];
            for (int i = 0; i < bldgEntities.Length; i++)
                bldgIsGHut[i] = em.HasComponent<GathererHutTag>(bldgEntities[i]);

            bool placingGHut = buildingId == "GatherersHut";
            float minSpacingSq      = MinBuildingSpacing      * MinBuildingSpacing;
            float minGHutSpacingSq  = MinGHutToGHutSpacing    * MinGHutToGHutSpacing;

            // Resource keep-out: never wall off a patch's approach ring.
            var veilNodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ironNodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var veilNodeXfs = veilNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ironNodeXfs = ironNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float nodeClearSq = MinResourceNodeClearance * MinResourceNodeClearance;

            // Sample a ring of angles around the anchor at increasing radii.
            // GHs naturally need a wider ring to satisfy the 30 m spacing —
            // and their reach GROWS with every hut standing (2026-08-04): the
            // economy marches outward across the map instead of saturating
            // one ring around the Hall and stalling.
            float maxRadius = BuildRingDistanceMax;
            if (placingGHut)
            {
                int ghCount = 0;
                for (int i = 0; i < bldgIsGHut.Length; i++)
                    if (bldgIsGHut[i]) ghCount++;
                maxRadius = math.min(160f, BuildRingDistanceMax + 30f + ghCount * 12f);
            }
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
            bool onTarget = TheWaningBorder.World.Regions.TerritoryOwnership
                                .IsClaimStructure(buildingId)
                          || isExtractor;
            float ringMin = onTarget ? 0f : BuildRingDistanceMin;

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
            // Real overlap is still refused by IsValidBuildPosition and by the
            // router's own gates, and the clearance still applies to the node
            // kinds the building does NOT stand on.
            var ownNode = TheWaningBorder.World.Regions.TerritoryOwnership
                              .RequiredNodeFor(buildingId);
            bool onIronNode = ownNode.HasValue
                && ownNode.Value.TypeIndex == ComponentType.ReadOnly<IronMineTag>().TypeIndex;
            bool onVeilstoneNode = ownNode.HasValue
                && ownNode.Value.TypeIndex == ComponentType.ReadOnly<VeilstoneOutcroppingTag>().TypeIndex;

            // Rejection tally, written into the refusal reason when the whole
            // search fails. "No legal spot" with no evidence is the diagnostic
            // hole that hid the extractor contradiction for a full batch.
            int nCand = 0, nCover = 0, nSpacing = 0, nNodeClear = 0, nCurse = 0,
                nTerritory = 0, nNodeGate = 0, nHallCap = 0, nInvalid = 0;

            int passes = placingGHut ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                bool requireCover = placingGHut && pass == 0;
                for (float r = ringMin; r <= maxRadius; r += 4f)
                {
                    int angleStart = (int)(NextRandFloat01() * BuildAngleSamples);
                    for (int i = 0; i < BuildAngleSamples; i++)
                    {
                        int idx = (angleStart + i) % BuildAngleSamples;
                        float angle = (idx / (float)BuildAngleSamples) * math.PI * 2f;
                        float3 candidate = new float3(
                            anchor.x + math.cos(angle) * r,
                            0f,
                            anchor.z + math.sin(angle) * r);
                        // Snap the candidate BEFORE any of the checks below.
                        // BuildingFactory snaps every spawn, so validating an
                        // unsnapped point would approve a spot up to a metre
                        // from where the building actually lands — enough to
                        // overlap a neighbour or a node the clearance test
                        // just cleared. docs/Design/Build_Grid.md
                        candidate = BuildGrid.Snap(candidate, size);
                        // SNAP FIRST (2026-08-18). Every check below — spacing,
                        // node clearance, crust, validity — must see the
                        // position the building will ACTUALLY occupy, because
                        // BuildingFactory snaps on the way in. Validating the
                        // raw candidate and then placing up to a cell away is
                        // how a spot this loop had just cleared could be
                        // refused as overlapping by the placement executor,
                        // silently timing out hut / Barracks / Shrine steps
                        // while the bank sat full.
                        candidate = BuildGrid.Snap(candidate, size);
                        candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                        nCand++;
                        if (requireCover && !IsCoveredGround(faction, candidate, anchor))
                        { nCover++; continue; }

                        if (!isExtractor && TooCloseToExistingBuilding(
                                candidate, bldgTransforms, bldgIsGHut,
                                minSpacingSq, minGHutSpacingSq, placingGHut))
                        { nSpacing++; continue; }

                        if ((!onVeilstoneNode && TooCloseToAny(candidate, veilNodeXfs, nodeClearSq))
                            || (!onIronNode && TooCloseToAny(candidate, ironNodeXfs, nodeClearSq)))
                        { nNodeClear++; continue; }

                        // Never place on crusted ground (2026-08-04): the
                        // curse crumbles the foundation before builders
                        // arrive — money in, nothing out, forever.
                        if (IsCursedGround(em, candidate))
                        { nCurse++; continue; }

                        // TERRITORY GATE — the same rule the player's placement
                        // obeys (docs/Design/Regions.md §2). A HARD constraint,
                        // not the covered-ground PREFERENCE above: without it
                        // the AI keeps proposing sites outside its holdings and
                        // CommandRouter.IssuePlaceBuilding keeps refusing them,
                        // which reads as an AI that has stopped building.
                        if (!TheWaningBorder.World.Regions.TerritoryOwnership.CanBuildAt(
                                em, faction, buildingId, candidate.x, candidate.z))
                        { nTerritory++; continue; }

                        // …and the placement rules the router will apply:
                        // a hut has to land on a free supply node, and a
                        // territory takes only one Hall. Without these the AI
                        // proposes sites the router refuses and reads as an AI
                        // that has stopped building.
                        // EVERY EXTRACTOR NEEDS ITS OWN FREE NODE, not just the
                        // hut. The router refuses a Mine that is not on iron and
                        // a Smelter that is not on veilsteel, so proposing one
                        // anywhere else is a step that times out silently.
                        if (!TheWaningBorder.World.Regions.TerritoryOwnership.OnFreeNodeFor(
                                em, buildingId, candidate.x, candidate.z))
                        { nNodeGate++; continue; }
                        if (buildingId == "Hall"
                            && TheWaningBorder.World.Regions.TerritoryOwnership.HallCapReached(
                                   em, candidate.x, candidate.z))
                        { nHallCap++; continue; }

                        // The id goes in so the validator can make the
                        // extractor-on-node exemption (and the Veilworks
                        // crust exception) — the id-less overload is the
                        // strict rule and refuses every on-node candidate.
                        if (BuildCommandHelper.IsValidBuildPosition(em, candidate, size, buildingId))
                        {
                            pos = candidate;
                            return true;
                        }
                        nInvalid++;
                    }
                }
            }
            _siteRefusalTally = $"{nCand} cand: cover {nCover}, spacing {nSpacing}, " +
                $"nodeclear {nNodeClear}, curse {nCurse}, territory {nTerritory}, " +
                $"nodegate {nNodeGate}, hallcap {nHallCap}, invalid {nInvalid}";
            pos = default;
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

        private static bool TooCloseToExistingBuilding(
            float3 candidate,
            NativeArray<LocalTransform> existing,
            bool[] existingIsGHut,
            float minDistSq,
            float minGHutDistSq,
            bool placingGHut)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                float dx = candidate.x - existing[i].Position.x;
                float dz = candidate.z - existing[i].Position.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < minDistSq) return true;
                if (placingGHut && existingIsGHut[i] && d2 < minGHutDistSq) return true;
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
        // Build a Hut whenever population headroom drops to this or below.
        /// <summary>
        /// Spare population the AI keeps in hand. Below this it raises a Hut.
        ///
        /// Was 2, which meant housing always TRAILED production: the AI waited
        /// until it was within two units of the cap, then built one hut, and
        /// every trainer stalled for the build while the buffer refilled. With
        /// an army of five that never mattered, because the cap was never
        /// approached. The target is now a 200-pop ceiling inside twenty
        /// minutes, which is roughly ten units a minute sustained, and a
        /// two-unit buffer cannot absorb that for the fifteen seconds a hut
        /// takes to go up.
        ///
        /// 16 is a little over a minute of production at that rate, so housing
        /// leads demand instead of chasing it. Huts are 80 supplies and the
        /// full 200 pop of them is ~1,440 - affordable many times over against
        /// the ~12,000 supplies earned in twenty minutes, so building ahead
        /// costs nothing that matters.
        /// </summary>
        private const int PopulationHeadroomFloor = 16;

        /// <summary>A Hut's supply price, for the surplus test above.</summary>
        private const int HutCostSupplies = 80;

        /// <summary>Supplies the economy wallet keeps back from building ahead
        /// — a Worker (140) plus a Gatherer's Hut, so the build order can still
        /// take its turn.</summary>
        private const int EconomyWorkingFloor = 240;

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
            bool blocking = max - current <= PopulationHeadroomFloor;
            if (!blocking)
            {
                int spare = AIBudget.WalletSupplies(faction, AIBudgetCategory.EconomyExpansion);
                if (spare < HutCostSupplies + EconomyWorkingFloor) return;
            }
            TryBuildBuildingBudgeted(em, faction, "Hut", AIBudgetCategory.EconomyExpansion);
        }
    }
}
