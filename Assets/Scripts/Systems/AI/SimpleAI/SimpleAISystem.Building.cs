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
        {
            if (!TechCatalog.IsReady) return false;
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) return false;

            // Era gate (2026-08-11): minEra was UI-only, so the AI happily
            // built era-locked buildings — the Age-0 Archery Range this rule
            // now delays (ranged units are an Age-1 unlock, Combat_Pacing.md).
            if (def.minEra > 1)
            {
                int era = 1;
                if (FactionEconomy.TryGetBank(em, faction, out var eraBank)
                    && em.HasComponent<FactionEra>(eraBank))
                    era = em.GetComponentData<FactionEra>(eraBank).Value;
                if (era < def.minEra) return false;
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
                return false;

            // Choice-buildings are limited to one per faction.
            if (BuildingFactory.IsChoiceBuilding(buildingId))
            {
                var existing = BuildingFactory.GetFactionChoiceBuilding(em, faction);
                if (existing != null) return false;
            }

            // Need a Hall to anchor placement around.
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null) return false;
            if (!em.HasComponent<LocalTransform>(hall)) return false;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;
            float3 anchor = anchorOverride ?? hallPos;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;

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
                return false;

            // Pre-flight: at least one idle builder must be available BEFORE we
            // spend the cost and place the foundation. Without this gate the
            // build-order step advanced on a successful placement even when zero
            // builders were dispatched, leaving an orphan UnderConstruction site
            // that never gained HP and a permanently stalled build queue (the
            // build order would never re-attempt the same step). (task-062 G-2)
            if (CountIdleBuilders(em, faction) == 0) return false;

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
            bool onTarget = TheWaningBorder.World.Regions.TerritoryOwnership
                                .IsClaimStructure(buildingId)
                          || TheWaningBorder.World.Regions.TerritoryOwnership
                                .IsExtractor(buildingId);
            float ringMin = onTarget ? 0f : BuildRingDistanceMin;

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

                        if (requireCover && !IsCoveredGround(faction, candidate, anchor))
                            continue;

                        if (TooCloseToExistingBuilding(
                                candidate, bldgTransforms, bldgIsGHut,
                                minSpacingSq, minGHutSpacingSq, placingGHut))
                            continue;

                        if (TooCloseToAny(candidate, veilNodeXfs, nodeClearSq)
                            || TooCloseToAny(candidate, ironNodeXfs, nodeClearSq))
                            continue;

                        // Never place on crusted ground (2026-08-04): the
                        // curse crumbles the foundation before builders
                        // arrive — money in, nothing out, forever.
                        if (IsCursedGround(em, candidate))
                            continue;

                        // TERRITORY GATE — the same rule the player's placement
                        // obeys (docs/Design/Regions.md §2). A HARD constraint,
                        // not the covered-ground PREFERENCE above: without it
                        // the AI keeps proposing sites outside its holdings and
                        // CommandRouter.IssuePlaceBuilding keeps refusing them,
                        // which reads as an AI that has stopped building.
                        if (!TheWaningBorder.World.Regions.TerritoryOwnership.CanBuildAt(
                                em, faction, buildingId, candidate.x, candidate.z))
                            continue;

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
                            continue;
                        if (buildingId == "Hall"
                            && TheWaningBorder.World.Regions.TerritoryOwnership.HallCapReached(
                                   em, candidate.x, candidate.z))
                            continue;

                        if (BuildCommandHelper.IsValidBuildPosition(em, candidate, size))
                        {
                            pos = candidate;
                            return true;
                        }
                    }
                }
            }
            pos = default;
            return false;
        }
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
        private const int PopulationHeadroomFloor = 2;

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
            if (max >= FactionPopulation.AbsoluteMax) return;
            if (max - current > PopulationHeadroomFloor) return;
            TryBuildBuildingBudgeted(em, faction, "Hut", AIBudgetCategory.EconomyExpansion);
        }
    }
}
