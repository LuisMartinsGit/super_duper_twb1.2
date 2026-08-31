// AIAlanthorEndgameSystem.Towers.cs
// Tower doctrine: budget, threat hints, chokepoint and coverage siting.
// Partial of AIAlanthorEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        // ──────────────────────────────────────────────────────────────────
        // 6. TOWER DOCTRINE (chokepoints + territory claims, anti-clump)
        // ──────────────────────────────────────────────────────────────────

        // Own towers may never stand closer than this — 1.6× the 15 m
        // influence radius, so their build-space circles TILE new ground
        // instead of stacking (the old ring placement produced 4-in-a-row).
        private const float MinTowerSpacing = 24f;
        // A corridor narrower than this along the enemy approach counts as
        // a chokepoint worth fortifying.
        private const float ChokeWidthThreshold = 26f;

        /// <summary>Nearest resource node (veilstone or iron) within
        /// <see cref="UnprotectedNodeRadius"/> of the hall whose ground this
        /// faction's influence does NOT yet cover — the next tower anchor.</summary>
        private const float UnprotectedNodeRadius = 130f;

        private static bool TryFindUnprotectedResourceNode(
            EntityManager em, Faction faction, float3 hallPos, out float3 nodePos)
        {
            nodePos = default;
            if (!TheWaningBorder.Influence.PlayerInfluenceMap.Ready) return false;

            float bestD2 = UnprotectedNodeRadius * UnprotectedNodeRadius;
            bool found = false;

            var veilQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ironQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            for (int q = 0; q < 2; q++)
            {
                using var xfs = (q == 0 ? veilQ : ironQ)
                    .ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < xfs.Length; i++)
                {
                    float dx = xfs[i].Position.x - hallPos.x;
                    float dz = xfs[i].Position.z - hallPos.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= bestD2) continue;
                    // Already covered by our own influence → protected.
                    if (TheWaningBorder.Influence.PlayerInfluenceMap.ChannelStrengthWorld(
                            (int)faction, xfs[i].Position.x, xfs[i].Position.z)
                        >= TheWaningBorder.Core.Config.VeilCrustConstants.InfluenceThreshold)
                        continue;
                    bestD2 = d2;
                    nodePos = xfs[i].Position;
                    found = true;
                }
            }
            return found;
        }

        // Raised 2026-08-04 ("AI must build more towers outside influence"):
        // towers are Alanthor's long territorial arm (45 m influence claim) —
        // they extend curse suppression, corruption immunity, and the
        // Gatherer's Huts' influence-border income bonus across the map.
        private static int TowerBudget(AIDifficulty d) => d switch
        {
            AIDifficulty.Easy => 3,
            AIDifficulty.Normal => 5,
            AIDifficulty.Hard => 8,
            AIDifficulty.Expert => 10,
            _ => 5,
        };

        private static void TryBuildDefensiveTower(Faction faction, EntityManager em,
            Entity brainEntity, AIDifficulty difficulty, float3 hallPos)
        {
            const string towerId = "Alanthor_Tower";
            int existing = CountFactionBuildings(em, faction, towerId);
            if (existing >= TowerBudget(difficulty)) return;

            // NOT BEFORE THERE IS AN ARMY TO DEFEND WITH. This path spends
            // straight from the bank, outside the budget wallets, so every
            // tower silently shrinks all three of them (the allocator
            // reconciles wallets down to the real balance). Measured: factions
            // put up 4-7 towers and never afforded a single Barracks. Towers
            // are the long territorial arm of a faction that HAS a military,
            // not a substitute for having one.
            if (AIEndgameCommon.FindFactionBuilding<BarracksTag>(em, faction) == Entity.Null) return;

            if (!BuildCosts.TryGet(towerId, out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (CountIdleBuilders(em, faction) == 0) return;

            // Own tower positions — the anti-clump constraint.
            var ownTowers = new NativeList<float3>(Allocator.Temp);
            CollectOwnTowerPositions(em, faction, ownTowers);

            // Threat bearing (fog-honest): freshest remembered enemy sighting,
            // base sightings preferred; pre-contact, claim toward map center.
            GetThreatHint(em, brainEntity, out float3 threatHint);

            // RESOURCE ANCHORING (2026-08-04, design: "the most effective way
            // of defeating the curse is to build influence"): an UNPROTECTED
            // resource node — one this faction's influence does not yet cover
            // — outranks the threat bearing. The tower's 45 m influence claim
            // shields the patch from curse growth, mining corruption and the
            // slow curse-influence escalation, and feeds the huts' border
            // bonus. Nearest unprotected node within tower reach wins.
            if (TryFindUnprotectedResourceNode(em, faction, hallPos, out float3 nodePos))
                threatHint = nodePos;

            int2 towerSize = BuildingSizeConfig.GetSize(towerId);
            bool found = TryFindTowerSpot(em, hallPos, threatHint, ownTowers, towerSize, out float3 pos);
            // HUT COVERAGE (endgame completeness): when the chokepoint /
            // directed-ring passes come up empty (anti-clump spacing
            // saturates the threat arc over a long match), spend the
            // remaining budget covering Gatherer's Huts — every hut farther
            // than HutCoverageRadius from a friendly Watch Tower gets one.
            if (!found)
                found = TryFindHutCoverageSpot(em, faction, hallPos, ownTowers, towerSize, out pos);
            ownTowers.Dispose();
            if (!found) return;

            // No AI-side Spend: PlaceBuildingDirect charges the cost on
            // every peer (docs/Multiplayer_LAN_Readiness.md).

            // Replicating entry point (audit F4) — PlaceBuildingDirect was
            // host-only. Queued case: dispatch at the position, null target.
            bool queuedPlacement = CommandRouter.IssuePlaceBuilding(em, towerId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queuedPlacement)
            {
                DispatchBuildersTo(em, faction, Entity.Null, towerId, pos, maxBuilders: 1);
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor towers: {existing + 1}/{TowerBudget(difficulty)} toward " +
                    $"({threatHint.x:F0},{threatHint.z:F0})");
                return;
            }
            // Null = the executor rejected — nothing spent, nothing to refund.
            if (building == Entity.Null) return;

            int dispatched = DispatchBuildersTo(em, faction, building, towerId, pos, maxBuilders: 1);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return;
            }
            AILogger.Log(faction, "BUILDING",
                $"Alanthor towers: {existing + 1}/{TowerBudget(difficulty)} toward " +
                $"({threatHint.x:F0},{threatHint.z:F0})");
        }

        private static void CollectOwnTowerPositions(EntityManager em, Faction faction,
            NativeList<float3> into)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<WatchTowerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) into.Add(xfs[i].Position);
        }

        // Freshest remembered enemy sighting (base categories strongly
        // preferred). Fog-honest — the buffer only holds what this faction
        // has actually seen. Pre-contact fallback: map center (forward
        // territory claim, no intel assumed).
        private static void GetThreatHint(EntityManager em, Entity brainEntity, out float3 hint)
        {
            if (em.HasBuffer<EnemySightingRecord>(brainEntity))
            {
                var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
                float best = float.MinValue;
                bool found = false;
                float3 bestPos = default;
                for (int i = 0; i < buf.Length; i++)
                {
                    var rec = buf[i];
                    bool baseCat = rec.Category == IntelCategory.Hall
                        || rec.Category == IntelCategory.MilitaryBuilding;
                    float score = rec.LastSeenTime + (baseCat ? 100000f : 0f);
                    if (score > best) { best = score; bestPos = rec.Position; found = true; }
                }
                if (found) { hint = bestPos; return; }
            }
            TheWaningBorder.World.Terrain.TerrainUtility.GetPlayableBounds(out var mn, out var mx);
            hint = new float3((mn.x + mx.x) * 0.5f, 0f, (mn.y + mx.y) * 0.5f);
        }

        /// <summary>
        /// Tower spot selection, in preference order:
        ///   1. CHOKEPOINT — walk the straight approach line from the Hall
        ///      toward the threat; measure corridor width at each step by
        ///      perpendicular passability probes on the nav grid; flank the
        ///      narrowest sub-threshold corridor on its clearer side.
        ///   2. DIRECTED RING — deterministic angles within ±60° of the
        ///      threat bearing at 25–40 m ("facing the enemy").
        /// All candidates respect MinTowerSpacing + placement validity.
        /// </summary>
        private static bool TryFindTowerSpot(EntityManager em, float3 hallPos, float3 threatHint,
            NativeList<float3> ownTowers, int2 towerSize, out float3 spot)
        {
            spot = default;
            float3 dir = threatHint - hallPos;
            dir.y = 0f;
            float len = math.length(dir);
            if (len < 20f) return false; // threat on top of us — no bearing
            dir /= len;
            float3 perp = new float3(-dir.z, 0f, dir.x);

            // ── 1. Chokepoint scan along the approach (shared with the
            //       wall doctrine — TryFindApproachChokepoint). ──
            float maxWalk = math.min(len - 10f, 90f);
            bool choke = TryFindApproachChokepoint(hallPos, dir, perp, maxWalk,
                out float3 chokePos, out float3 chokeSide, out _);
            if (choke)
            {
                for (float off = 4f; off <= 10f; off += 3f)
                {
                    float3 c = chokePos + chokeSide * off;
                    if (IsTowerSpotOk(em, ref c, towerSize, ownTowers)) { spot = c; return true; }
                }
            }

            // ── 2. Directed ring toward the threat. ──
            // Angle order: straight at it, then ±30°, then ±60°.
            float[] angles = { 0f, 0.5236f, -0.5236f, 1.0472f, -1.0472f };
            for (float r = 25f; r <= 40f; r += 5f)
            {
                for (int a = 0; a < angles.Length; a++)
                {
                    float cos = math.cos(angles[a]);
                    float sin = math.sin(angles[a]);
                    float3 rd = dir * cos + perp * sin;
                    float3 c = hallPos + rd * r;
                    if (IsTowerSpotOk(em, ref c, towerSize, ownTowers)) { spot = c; return true; }
                }
            }
            return false;
        }

        // Walkable meters from `from` along `stepDir` before hitting an
        // impassable nav cell (max capped). Integer-grid deterministic.
        private static float ClearanceAlong(float3 from, float3 stepDir, float max)
        {
            for (float s = 1f; s <= max; s += 1f)
            {
                float3 p = from + stepDir * s;
                var cell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(p);
                if (cell.x == int.MinValue) return s - 1f;
                if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell)) return s - 1f;
            }
            return max;
        }

        /// <summary>
        /// Anti-clump + placement test for one candidate. SNAPS the candidate
        /// to the build grid FIRST and reports the snapped position back, so
        /// what gets validated is exactly what gets built.
        ///
        /// Every candidate here comes off a ring or a chokepoint walk and is
        /// therefore at an arbitrary fractional position, while
        /// BuildingFactory.Create snaps before it places. Validating the raw
        /// candidate and then building somewhere else — up to a full cell
        /// away — is how a tower could land inside a footprint the check had
        /// just cleared (2026-08-18: "towers on top of the Hall").
        /// </summary>
        private static bool IsTowerSpotOk(EntityManager em, ref float3 pos, int2 size,
            NativeList<float3> ownTowers)
        {
            pos = BuildGrid.Snap(pos, size);
            pos.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos.x, pos.z);

            for (int i = 0; i < ownTowers.Length; i++)
            {
                float dx = pos.x - ownTowers[i].x;
                float dz = pos.z - ownTowers[i].z;
                if (dx * dx + dz * dz < MinTowerSpacing * MinTowerSpacing) return false;
            }
            return BuildCommandHelper.IsValidBuildPosition(em, pos, size);
        }

        /// <summary>Chokepoint scan shared by the tower and wall doctrines:
        /// walk the straight approach line from the Hall toward the threat,
        /// measure corridor width at each step by perpendicular passability
        /// probes on the nav grid, and report the narrowest sub-threshold
        /// corridor. Integer-grid deterministic.</summary>
        private static bool TryFindApproachChokepoint(float3 hallPos, float3 dir, float3 perp,
            float maxWalk, out float3 chokePos, out float3 chokeSide, out float chokeWidth)
        {
            chokePos = default;
            chokeSide = default;
            chokeWidth = ChokeWidthThreshold;
            bool choke = false;
            for (float d = 14f; d <= maxWalk; d += 4f)
            {
                float3 p = hallPos + dir * d;
                float left = ClearanceAlong(p, perp, 14f);
                float right = ClearanceAlong(p, -perp, 14f);
                if (left + right <= 2f) continue; // solid wall, not a corridor
                float width = left + right;
                if (width < chokeWidth)
                {
                    chokeWidth = width;
                    chokePos = p;
                    chokeSide = left >= right ? perp : -perp;
                    choke = true;
                }
            }
            return choke;
        }

        /// <summary>A Gatherer's Hut counts as tower-covered when a friendly
        /// Watch Tower stands within this range.</summary>
        private const float HutCoverageRadius = 28f;

        /// <summary>Coverage fallback for the tower doctrine: find the
        /// uncovered Gatherer's Hut nearest the Hall (deterministic — the
        /// coverage grows outward from the core) and pick a tower spot in a
        /// ring around it, inside coverage range, with the doctrine's
        /// anti-clump spacing enforced by <see cref="IsTowerSpotOk"/>.</summary>
        private static bool TryFindHutCoverageSpot(EntityManager em, Faction faction,
            float3 hallPos, NativeList<float3> ownTowers, int2 towerSize, out float3 spot)
        {
            spot = default;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float covSq = HutCoverageRadius * HutCoverageRadius;
            float3 hut = default;
            float bestDistSq = float.MaxValue;
            bool foundHut = false;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                float3 p = xfs[i].Position;
                bool covered = false;
                for (int t = 0; t < ownTowers.Length; t++)
                {
                    float dx = p.x - ownTowers[t].x, dz = p.z - ownTowers[t].z;
                    if (dx * dx + dz * dz <= covSq) { covered = true; break; }
                }
                if (covered) continue;
                float hx = p.x - hallPos.x, hz = p.z - hallPos.z;
                float d2 = hx * hx + hz * hz;
                if (d2 < bestDistSq) { bestDistSq = d2; hut = p; foundHut = true; }
            }
            if (!foundHut) return false;

            // Ring around the hut — every radius stays inside coverage range.
            for (float r = 6f; r <= 18f; r += 4f)
            {
                for (int a = 0; a < 12; a++)
                {
                    float ang = (a / 12f) * 2f * math.PI;
                    float3 c = hut + new float3(math.cos(ang) * r, 0f, math.sin(ang) * r);
                    if (IsTowerSpotOk(em, ref c, towerSize, ownTowers)) { spot = c; return true; }
                }
            }
            return false;
        }
    }
}
