// ScoutDirectorSystem.cs
// Information-driven scouting (AI plan M3). Replaces SimpleAISystem's random
// scout wandering with zone-based exploration plus a recon channel:
//
//   * The map is divided into a coarse zone grid. Each zone scores by
//     staleness (time since a scout stood in it), a never-visited bonus, and
//     a known-enemy-base bonus (perimeter re-scouting), minus distance.
//   * Idle scouts are assigned the best zone; arrival stamps the zone fresh.
//   * Scout-then-strike: when SimpleAISystem wants to assault a target whose
//     intel is stale, it raises SimpleAIState.HasReconRequest — the director
//     diverts the nearest scout there before the attack re-evaluates.
//   * Survival: a scout under half health flees to the Hall, and scouts route
//     around high-threat zones (ThreatMap sample added as a penalty).
//
// IntelSystem does the actual "remembering" — anything a scout reveals lands
// in the brain's EnemySightingRecord buffer automatically.
//
// Location: Assets/Scripts/Systems/AI/ScoutDirectorSystem.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ScoutDirectorSystem : SystemBase
    {
        private const float TickInterval = 2f;
        private const float ZoneSize = 64f;
        private const float ZoneVisitRadiusSq = 12f * 12f;
        private const float NeverVisitedBonus = 120f;
        private const float EnemyBaseBonus = 60f;
        private const float DistancePenaltyPerMeter = 0.15f;
        private const float ThreatPenaltyFactor = 0.2f;
        private const float AssignmentHoldSeconds = 12f; // don't re-assign a zone someone is heading to

        private float _acc;

        private class ZoneState
        {
            public int N;                 // grid is N x N
            public float[] LastVisit;     // ElapsedTime of last scout presence (-1 = never)
            public float[] LastAssigned;  // ElapsedTime of last assignment
            public bool[] EnemyBase;      // a known enemy Hall sits in this zone
        }

        /// <summary>
        /// COMMAND FOLLOW-THROUGH: the movement stack consumes
        /// DesiredDestination into its own path state, so a traveling scout
        /// often reads as dd.Has == 0. The director therefore keeps its OWN
        /// per-scout commitment and only re-tasks when the scout actually
        /// arrives at its target, the assignment times out (stuck failsafe),
        /// or a flee/recon priority overrides it. Without this, scouts were
        /// re-aimed at a different zone every tick and went nowhere.
        /// </summary>
        private class ScoutPlan
        {
            public float3 Target;
            public float Since;
            public bool Fleeing;
            /// <summary>Perch-and-bloom dwell: once arrived, the scout HOLDS
            /// until this time so its Scout Sight vision ramp
            /// (AbilityAuraSystem.TickScoutSight) builds up before the next
            /// hop. 0 = not yet arrived.</summary>
            public float DwellUntil;
        }

        private const float PlanArrivalRadiusSq = 6f * 6f;
        private const float PlanTimeoutSeconds = 60f;
        // The Scout Sight ability ramps vision linearly over 25 s of standing
        // still (AbilityAuraSystem.ScoutRampSeconds); dwell covers the full
        // ramp plus the IntelSystem 1 s tick that records what it reveals.
        private const float ScoutDwellSeconds = 26f;

        // Keyed by faction index / scout entity. AI runs host-only; this
        // state never replicates — everything flows out as movement orders.
        private readonly Dictionary<int, ZoneState> _zones = new Dictionary<int, ZoneState>();
        private readonly Dictionary<Entity, ScoutPlan> _plans = new Dictionary<Entity, ScoutPlan>();

        protected override void OnCreate()
        {
            RequireForUpdate<AIBrain>();
            _zones.Clear();
            _plans.Clear();
        }

        protected override void OnUpdate()
        {
            _acc += SystemAPI.Time.DeltaTime;
            if (_acc < TickInterval) return;
            _acc -= TickInterval;

            var em = EntityManager;
            float now = (float)SystemAPI.Time.ElapsedTime;
            var settings = AISettings.Get();

            // Zone grid covers the ACTUAL terrain rectangle (corner-anchored,
            // usually not origin-centred) — an origin-centred MapHalfSize box
            // would put most zone centers off the terrain and send scouts
            // outside the nav grid.
            TheWaningBorder.World.Terrain.TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
            float2 worldMin = new float2(bMin.x, bMin.y);
            float2 worldSize = new float2(bMax.x - bMin.x, bMax.y - bMin.y);

            var brainsQuery = SystemAPI.QueryBuilder().WithAll<AIBrain, SimpleAIState>().Build();
            using var brains = brainsQuery.ToEntityArray(Allocator.Temp);

            // One scout snapshot for all factions.
            var scoutQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<DesiredDestination>(),
                ComponentType.ReadOnly<Health>());
            using var sEnts = scoutQuery.ToEntityArray(Allocator.Temp);
            using var sTags = scoutQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var sFacs = scoutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var sXfs = scoutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sDds = scoutQuery.ToComponentDataArray<DesiredDestination>(Allocator.Temp);
            using var sHps = scoutQuery.ToComponentDataArray<Health>(Allocator.Temp);

            foreach (var brainEntity in brains)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;
                Faction owner = brain.Owner;
                var zs = GetZones(owner, worldSize);

                float3 hallPos = float3.zero;
                bool hasHall = TryGetHallPos(em, owner, out hallPos);

                // Mark zones containing known enemy Halls (perimeter re-scout targets).
                if (em.HasBuffer<EnemySightingRecord>(brainEntity))
                {
                    var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
                    for (int i = 0; i < buf.Length; i++)
                        if (buf[i].Category == IntelCategory.Hall && ZoneIndex(zs, worldMin, worldSize, buf[i].Position, out int zi))
                            zs.EnemyBase[zi] = true;
                }

                var aiState = em.GetComponentData<SimpleAIState>(brainEntity);
                bool stateChanged = false;

                for (int i = 0; i < sEnts.Length; i++)
                {
                    if (sFacs[i].Value != owner) continue;
                    if (sTags[i].Class != UnitClass.Scout) continue;
                    if (!em.Exists(sEnts[i])) continue;
                    if (em.HasComponent<UnderConstruction>(sEnts[i])) continue;

                    Entity scout = sEnts[i];
                    float3 pos = sXfs[i].Position;

                    // Stamp the zone the scout is standing in as freshly visited.
                    if (ZoneIndex(zs, worldMin, worldSize, pos, out int hereZone))
                    {
                        float3 center = ZoneCenter(zs, worldMin, worldSize, hereZone);
                        float dx0 = center.x - pos.x, dz0 = center.z - pos.z;
                        if (dx0 * dx0 + dz0 * dz0 <= ZoneVisitRadiusSq)
                            zs.LastVisit[hereZone] = now;
                    }

                    _plans.TryGetValue(scout, out var plan);

                    // Survival override: flee home when hurt (and not already
                    // near home). Issued once; the commitment keeps it stable.
                    if (hasHall && sHps[i].Max > 0
                        && sHps[i].Value < sHps[i].Max * settings.scoutFleeHealthFraction)
                    {
                        float dxh = hallPos.x - pos.x, dzh = hallPos.z - pos.z;
                        if (dxh * dxh + dzh * dzh > 25f * 25f)
                        {
                            if (plan == null || !plan.Fleeing)
                            {
                                _plans[scout] = new ScoutPlan { Target = hallPos, Since = now, Fleeing = true };
                                em.SetComponentData(scout, new DesiredDestination { Position = hallPos, Has = 1 });
                            }
                            else if (sDds[i].Has == 0)
                            {
                                // Movement consumed the destination — re-issue
                                // toward the SAME target (no re-decision).
                                em.SetComponentData(scout, new DesiredDestination { Position = plan.Target, Has = 1 });
                            }
                            continue;
                        }
                        if (plan != null && plan.Fleeing) { _plans.Remove(scout); plan = null; }
                    }

                    // Honor the existing commitment until arrival or timeout;
                    // then PERCH: hold at the vantage until the Scout Sight
                    // ramp has built vision up before the next hop.
                    if (plan != null)
                    {
                        float dxp = plan.Target.x - pos.x, dzp = plan.Target.z - pos.z;
                        bool arrived = dxp * dxp + dzp * dzp <= PlanArrivalRadiusSq;
                        bool timedOut = now - plan.Since > PlanTimeoutSeconds;
                        if (!arrived && !timedOut)
                        {
                            if (sDds[i].Has == 0)
                                em.SetComponentData(scout, new DesiredDestination { Position = plan.Target, Has = 1 });
                            continue;
                        }
                        if (arrived && !plan.Fleeing)
                        {
                            if (plan.DwellUntil <= 0f)
                            {
                                plan.DwellUntil = now + ScoutDwellSeconds;
                                continue; // start the perch
                            }
                            if (now < plan.DwellUntil) continue; // blooming
                        }
                        _plans.Remove(scout);
                    }

                    // New assignment. Recon request takes priority over exploration.
                    if (aiState.HasReconRequest != 0)
                    {
                        // Snap onto the cost field — unwalkable recon points
                        // (cliff edges, water) left scouts grinding against
                        // terrain forever. Unsnappable request: drop it.
                        NavGridQuery.SnapToWalkable(aiState.ReconTarget, out float3 reconPos, out bool reconOk);
                        aiState.HasReconRequest = 0;
                        stateChanged = true;
                        if (reconOk)
                        {
                            _plans[scout] = new ScoutPlan { Target = reconPos, Since = now };
                            em.SetComponentData(scout, new DesiredDestination
                            {
                                Position = reconPos,
                                Has = 1
                            });
                            continue;
                        }
                    }

                    // Pick the best exploration zone.
                    int bestZone = -1;
                    float bestScore = float.MinValue;
                    for (int z = 0; z < zs.LastVisit.Length; z++)
                    {
                        if (now - zs.LastAssigned[z] < AssignmentHoldSeconds) continue;
                        float3 center = ZoneCenter(zs, worldMin, worldSize, z);
                        float staleness = zs.LastVisit[z] < 0f ? NeverVisitedBonus : (now - zs.LastVisit[z]);
                        float score = staleness
                            + (zs.EnemyBase[z] ? EnemyBaseBonus : 0f)
                            - math.distance(new float2(center.x, center.z), new float2(pos.x, pos.z)) * DistancePenaltyPerMeter
                            - ThreatMaps.Sample(owner, center) * ThreatPenaltyFactor;
                        if (score > bestScore) { bestScore = score; bestZone = z; }
                    }
                    if (bestZone < 0) continue;

                    zs.LastAssigned[bestZone] = now;
                    float3 dest = ZoneCenter(zs, worldMin, worldSize, bestZone);

                    // Snap the zone center onto the cost field. A center over
                    // water/cliffs snaps to the nearest walkable cell (the
                    // scout still surveys the zone from its edge); a zone with
                    // NO walkable cell in snap range is stamped visited so it
                    // stops winning the priority race.
                    NavGridQuery.SnapToWalkable(dest, out float3 snapped, out bool ok);
                    if (!ok)
                    {
                        zs.LastVisit[bestZone] = now;
                        continue;
                    }
                    dest = snapped;

                    _plans[scout] = new ScoutPlan { Target = dest, Since = now };
                    em.SetComponentData(scout, new DesiredDestination
                    {
                        Position = dest,
                        Has = 1
                    });
                }

                if (stateChanged)
                    em.SetComponentData(brainEntity, aiState);
            }
        }

        private ZoneState GetZones(Faction f, float2 worldSize)
        {
            int key = (int)f;
            int n = math.clamp((int)math.ceil(math.max(worldSize.x, worldSize.y) / ZoneSize), 2, 10);
            if (_zones.TryGetValue(key, out var zs) && zs.N == n) return zs;
            zs = new ZoneState
            {
                N = n,
                LastVisit = new float[n * n],
                LastAssigned = new float[n * n],
                EnemyBase = new bool[n * n],
            };
            for (int i = 0; i < zs.LastVisit.Length; i++) { zs.LastVisit[i] = -1f; zs.LastAssigned[i] = -1000f; }
            _zones[key] = zs;
            return zs;
        }

        private static bool ZoneIndex(ZoneState zs, float2 worldMin, float2 worldSize, float3 pos, out int idx)
        {
            float cellX = worldSize.x / zs.N;
            float cellZ = worldSize.y / zs.N;
            int x = (int)math.floor((pos.x - worldMin.x) / cellX);
            int z = (int)math.floor((pos.z - worldMin.y) / cellZ);
            if (x < 0 || z < 0 || x >= zs.N || z >= zs.N) { idx = 0; return false; }
            idx = z * zs.N + x;
            return true;
        }

        private static float3 ZoneCenter(ZoneState zs, float2 worldMin, float2 worldSize, int idx)
        {
            float cellX = worldSize.x / zs.N;
            float cellZ = worldSize.y / zs.N;
            int x = idx % zs.N;
            int z = idx / zs.N;
            return new float3(
                worldMin.x + (x + 0.5f) * cellX,
                0f,
                worldMin.y + (z + 0.5f) * cellZ);
        }

        private static bool TryGetHallPos(EntityManager em, Faction faction, out float3 pos)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                pos = xfs[i].Position;
                return true;
            }
            pos = float3.zero;
            return false;
        }
    }
}
