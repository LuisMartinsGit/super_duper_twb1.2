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

using System;
using System.Collections.Generic;
using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ScoutDirectorSystem : SystemBase
    {
        private double _simClockLast = -1d;

        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransformDesiredDestinationHealth =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<DesiredDestination>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransformDesiredDestinationHealth;

        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        #endregion

        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in ScoutDirectorSystem.asset now.</summary>
        static ScoutDirectorSystemConfig Cfg => ScoutDirectorSystemConfig.I;

        #endregion

        private const float ZoneVisitRadiusSq = 12f * 12f;

        private SimCadence.Periodic _acc;

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
            /// <summary>Legs in the chained route (1 for a single move);
            /// the timeout scales with it.</summary>
            public int Legs = 1;
        }

        /// <summary>Zones planned per decision and handed over as ONE chained
        /// order (AICommon.IssueChain): the scout runs them back to back with
        /// no idle tick between legs.</summary>
        private const int ScoutChainLegs = 3;

        /// <summary>
        /// A SCOUT NEVER STOPS (2026-09-29). Its next target is handed over
        /// this far BEFORE it reaches the current one, so the move order is
        /// replaced while it is still travelling — with the director thinking
        /// every tickInterval, an arrival radius of a few metres left scouts
        /// standing idle for a whole tick at every waypoint. (The perch-and-
        /// bloom dwell went with the changing Scout Sight vision it served.)
        /// </summary>
        private const float PlanArrivalRadiusSq = 18f * 18f;

        // Keyed by faction index / scout entity. AI runs host-only; this
        // state never replicates — everything flows out as movement orders.
        private readonly Dictionary<int, ZoneState> _zones = new Dictionary<int, ZoneState>();
        private readonly Dictionary<Entity, ScoutPlan> _plans = new Dictionary<Entity, ScoutPlan>();

        /// <summary>Units doing a scout's job because no scout is left.
        /// Rebuilt every pass on purpose: the moment a real Scout exists the
        /// set empties and the cavalry goes back to the army.</summary>
        private readonly HashSet<Entity> _standIns = new HashSet<Entity>();

        /// <summary>How many stand-ins one faction may run at once. Two is
        /// the scout target; more would be an army detachment, not a
        /// scouting patrol.</summary>
        private const int MaxStandIns = 2;

        /// <summary>
        /// Draft light cavalry to scout. Prefers the Outrider by id, then any
        /// cavalry class, and refuses anything the army cannot spare: no
        /// workers, no wounded, nothing already marching with a wave.
        /// </summary>
        private static void DraftStandIns(EntityManager em, Faction owner,
            NativeArray<Entity> ents, NativeArray<UnitTag> tags,
            NativeArray<FactionTag> facs, NativeArray<Health> hps,
            HashSet<Entity> into)
        {
            // Two passes so a named Outrider always beats a generic horseman,
            // whatever order the chunks happen to be in.
            for (int pass = 0; pass < 2 && into.Count < MaxStandIns; pass++)
                for (int i = 0; i < ents.Length && into.Count < MaxStandIns; i++)
                {
                    if (facs[i].Value != owner || !em.Exists(ents[i])) continue;
                    var cls = tags[i].Class;
                    if (cls == UnitClass.Economy || cls == UnitClass.Worker
                        || cls == UnitClass.Scout) continue;
                    if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                    // Hurt units are no use out alone.
                    if (hps[i].Max > 0 && hps[i].Value < hps[i].Max * 0.6f) continue;
                    // Never strip a wave of a body it is counting on.
                    if (TransientState.Active<FormationMemberState>(em, ents[i])) continue;

                    bool isOutrider = em.HasComponent<UnitTypeId>(ents[i])
                        && em.GetComponentData<UnitTypeId>(ents[i]).Value
                             .ToString().EndsWith("Outrider");
                    if (pass == 0 && !isOutrider) continue;
                    if (pass == 1 && !IsCavalry(em, ents[i])) continue;
                    into.Add(ents[i]);
                }
        }

        /// <summary>Cavalry by the SO's own class string, so a culture that
        /// names its horsemen something else still qualifies.</summary>
        private static bool IsCavalry(EntityManager em, Entity e)
        {
            if (!em.HasComponent<UnitTypeId>(e)) return false;
            var def = TechCatalog.Unit(
                em.GetComponentData<UnitTypeId>(e).Value.ToString());
            return def != null && def.unitClass != null
                && def.unitClass.IndexOf("cavalry", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected override void OnCreate()
        {
            RequireForUpdate<AIBrain>();
            _zones.Clear();
            _plans.Clear();
            _watchers.Clear();
        }

        protected override void OnUpdate()
        {
            // Host-only: scout tasking is an AI brain decision, and those run on
            // the host alone in multiplayer. docs/Multiplayer_LAN_Readiness.md
            if (!GameSettings.ShouldRunAIBrains()) return;

            if (!_acc.Due(AIClock.Delta(ref _simClockLast), Cfg.tickInterval)) return;

            var em = EntityManager;
            float now = TheWaningBorder.Core.SimClock.Now;
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
            var scoutQuery = QC_UnitTagFactionTagLocalTransformDesiredDestinationHealth.Get(em, QT_UnitTagFactionTagLocalTransformDesiredDestinationHealth);
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
                // The snapshot cadence above is shared by every faction, so
                // difficulty lands on the per-brain lever instead: how long a
                // zone stays claimed before it may be re-tasked.
                float assignHold = Cfg.assignmentHoldSeconds
                    * AISimpleDifficulty.GetProfile(brain.Difficulty).SupportThinkScale;
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

                // ── THE OUTRIDER STANDS IN (2026-09-12, Game_AI.md §7) ──
                //
                // Scouting must not stop because the scouts died. Observed on
                // Veilmarch: Yellow's last scout died around minute 26 and the
                // director went silent for the remaining NINETY MINUTES. Its
                // wave target sat on ground nobody had revealed, so the
                // no-blind-dispatch gate turned every attack into a recon
                // request, and nothing was alive to answer one. 137 units
                // stood still for an hour and a half because one tile was
                // dark.
                //
                // Light cavalry is the stand-in: the Outrider on Alanthor,
                // the fastest human_cavalry on any culture. It is the only
                // thing that crosses the map at scouting speed, and a horseman
                // sent to go and look is ordinary RTS vocabulary.
                //
                // A real Scout takes the job back the moment one exists, so
                // this set is recomputed every pass and never persisted.
                _standIns.Clear();
                bool anyScout = false;
                for (int i = 0; i < sEnts.Length; i++)
                    if (sFacs[i].Value == owner && sTags[i].Class == UnitClass.Scout
                        && em.Exists(sEnts[i]))
                    { anyScout = true; break; }
                if (!anyScout)
                    DraftStandIns(em, owner, sEnts, sTags, sFacs, sHps, _standIns);

                // ── THE CELESTAR WATCH (2026-10-19, Age_1_Alanthor.md §
                // Scouting Celestarii) ── the Celestar passive fires only on an
                // IDLE scout, and "a scout never stops" would starve it. Once
                // the faction owns the research and has scouts to spare, a
                // few (celestarWatchScouts, never the last one) are parked
                // beside the Fortress and left alone; the rest keep exploring.
                var watchers = PickCelestarWatchers(em, owner, hasHall, sEnts, sTags, sFacs, sXfs, hallPos);
                float3 parkPos = float3.zero;
                bool parkOk = false;
                if (watchers.Count > 0)
                    parkOk = TryParkPoint(hallPos, worldMin, worldSize, out parkPos);

                for (int i = 0; i < sEnts.Length; i++)
                {
                    if (sFacs[i].Value != owner) continue;
                    if (sTags[i].Class != UnitClass.Scout
                        && !_standIns.Contains(sEnts[i])) continue;
                    if (!em.Exists(sEnts[i])) continue;
                    if (em.HasComponent<UnderConstruction>(sEnts[i])) continue;

                    Entity scout = sEnts[i];
                    float3 pos = sXfs[i].Position;

                    if (parkOk && watchers.Contains(scout))
                    {
                        _plans.TryGetValue(scout, out var wplan);
                        float dxw = parkPos.x - pos.x, dzw = parkPos.z - pos.z;
                        float arrive = Cfg.celestarWatchArriveRadius;
                        if (dxw * dxw + dzw * dzw > arrive * arrive)
                        {
                            bool heading = wplan != null && !wplan.Fleeing
                                && math.distancesq(wplan.Target, parkPos) < 1f;
                            if (!heading || (sDds[i].Has == 0
                                && !TransientState.Active<CommandQueueActive>(em, scout)))
                            {
                                _plans[scout] = new ScoutPlan { Target = parkPos, Since = now };
                                CommandRouter.IssueMove(em, scout, parkPos, CommandSource.AI);
                                if (!heading)
                                    TheWaningBorder.AI.AILogger.Log(owner, "SCOUT",
                                        $"celestar watch -> park at ({parkPos.x:0},{parkPos.z:0})");
                            }
                        }
                        else if (wplan != null) _plans.Remove(scout);   // parked: issue nothing, stay idle
                        continue;
                    }

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
                                CommandRouter.IssueMove(em, scout, hallPos, CommandSource.AI);
                            }
                            else if (sDds[i].Has == 0)
                            {
                                // Movement consumed the destination — re-issue
                                // toward the SAME target (no re-decision).
                                CommandRouter.IssueMove(em, scout, plan.Target, CommandSource.AI);
                            }
                            continue;
                        }
                        if (plan != null && plan.Fleeing) { _plans.Remove(scout); plan = null; }
                    }

                    // Honor the existing commitment until it is nearly reached
                    // (or times out), then re-task at once — no perch.
                    if (plan != null)
                    {
                        float dxp = plan.Target.x - pos.x, dzp = plan.Target.z - pos.z;
                        bool arrived = dxp * dxp + dzp * dzp <= PlanArrivalRadiusSq;
                        bool timedOut = now - plan.Since > Cfg.planTimeoutSeconds * math.max(1, plan.Legs);
                        if (!arrived && !timedOut)
                        {
                            // Between legs of a chain the destination reads
                            // empty for a frame while the queue hands over the
                            // next one; re-ordering then would wipe the chain.
                            if (sDds[i].Has == 0 && !TransientState.Active<CommandQueueActive>(em, scout))
                                CommandRouter.IssueMove(em, scout, plan.Target, CommandSource.AI);
                            continue;
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
                            CommandRouter.IssueMove(em, scout, reconPos, CommandSource.AI);
                            TheWaningBorder.AI.AILogger.Log(owner, "SCOUT",
                                $"recon -> ({reconPos.x:0},{reconPos.z:0}) (wave held for intel)");
                            continue;
                        }
                    }

                    // Plan a ROUTE: the best zone from here, then the best from
                    // there, up to ScoutChainLegs, handed over as one chained
                    // order. The director only re-plans near the route's end.
                    var steps = new List<(QueuedCommandType, float3)>(ScoutChainLegs);
                    float3 from = pos;
                    for (int leg = 0; leg < ScoutChainLegs; leg++)
                    {
                        int bestZone = -1;
                        float bestScore = float.MinValue;
                        for (int z = 0; z < zs.LastVisit.Length; z++)
                        {
                            if (now - zs.LastAssigned[z] < assignHold) continue;
                            float3 center = ZoneCenter(zs, worldMin, worldSize, z);
                            float staleness = zs.LastVisit[z] < 0f ? Cfg.neverVisitedBonus : (now - zs.LastVisit[z]);
                            float score = staleness
                                + (zs.EnemyBase[z] ? Cfg.enemyBaseBonus : 0f)
                                - math.distance(new float2(center.x, center.z), new float2(from.x, from.z)) * Cfg.distancePenaltyPerMeter
                                - ThreatMaps.Sample(owner, center) * Cfg.threatPenaltyFactor;
                            if (score > bestScore) { bestScore = score; bestZone = z; }
                        }
                        // Every zone recently assigned (small map, several
                        // scouts): the first leg takes the stalest anyway
                        // rather than let the scout idle.
                        if (bestZone < 0 && leg == 0)
                        {
                            for (int z = 0; z < zs.LastVisit.Length; z++)
                            {
                                float3 c = ZoneCenter(zs, worldMin, worldSize, z);
                                float dxz = c.x - from.x, dzz = c.z - from.z;
                                if (dxz * dxz + dzz * dzz <= PlanArrivalRadiusSq) continue;   // not where it stands
                                float stale = zs.LastVisit[z] < 0f ? float.MaxValue : now - zs.LastVisit[z];
                                if (stale > bestScore) { bestScore = stale; bestZone = z; }
                            }
                        }
                        if (bestZone < 0) break;

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
                        steps.Add((QueuedCommandType.Move, snapped));
                        from = snapped;
                    }
                    if (steps.Count == 0) continue;

                    var last = steps[steps.Count - 1].Item2;
                    _plans[scout] = new ScoutPlan { Target = last, Since = now, Legs = steps.Count };
                    AICommon.IssueChain(em, scout, steps);
                    TheWaningBorder.AI.AILogger.Log(owner, "SCOUT",
                        $"explore route of {steps.Count} zone(s), ending ({last.x:0},{last.z:0})");
                }

                if (stateChanged)
                    em.SetComponentData(brainEntity, aiState);
            }
        }

        private ZoneState GetZones(Faction f, float2 worldSize)
        {
            int key = (int)f;
            int n = math.clamp((int)math.ceil(math.max(worldSize.x, worldSize.y) / Cfg.zoneSize), 2, 10);
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

        /// <summary>Watchers kept from pass to pass (per faction), so the
        /// parked scout is not swapped for another each think.</summary>
        private readonly Dictionary<int, HashSet<Entity>> _watchers = new Dictionary<int, HashSet<Entity>>();

        private const string CelestarTech = "ScoutingCelestarii";

        /// <summary>The scouts that hold the Celestar watch for
        /// <paramref name="owner"/> this pass: empty until the research is
        /// owned; otherwise up to celestarWatchScouts real Scouts, never all
        /// of them (one always explores). Sticky: a live watcher stays one;
        /// a gap is filled by the Scout nearest the Fortress.</summary>
        private HashSet<Entity> PickCelestarWatchers(EntityManager em, Faction owner, bool hasHall,
            NativeArray<Entity> ents, NativeArray<UnitTag> tags, NativeArray<FactionTag> facs,
            NativeArray<LocalTransform> xfs, float3 hallPos)
        {
            if (!_watchers.TryGetValue((int)owner, out var set))
                _watchers[(int)owner] = set = new HashSet<Entity>();

            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            bool owned = hasHall && Cfg.celestarWatchScouts > 0 && research != null
                         && research.HasResearched(owner, CelestarTech);

            int scouts = 0;
            if (owned)
                for (int i = 0; i < ents.Length; i++)
                    if (facs[i].Value == owner && tags[i].Class == UnitClass.Scout && em.Exists(ents[i])
                        && !em.HasComponent<UnderConstruction>(ents[i])) scouts++;
            int want = owned ? math.min(Cfg.celestarWatchScouts, scouts - 1) : 0;
            if (want <= 0) { set.Clear(); return set; }

            // Drop the dead and the no-longer-scouts, keep the rest.
            var keep = new HashSet<Entity>();
            for (int i = 0; i < ents.Length; i++)
                if (set.Contains(ents[i]) && facs[i].Value == owner && tags[i].Class == UnitClass.Scout
                    && em.Exists(ents[i]) && keep.Count < want) keep.Add(ents[i]);
            set.Clear();
            foreach (var e in keep) set.Add(e);

            while (set.Count < want)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != owner || tags[i].Class != UnitClass.Scout) continue;
                    if (set.Contains(ents[i]) || !em.Exists(ents[i])) continue;
                    if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                    float d = math.distancesq(xfs[i].Position, hallPos);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) break;
                set.Add(ents[best]);
            }
            return set;
        }

        /// <summary>The watch post: celestarWatchOffset metres from the
        /// Fortress on its far side from the map centre (the safe side),
        /// snapped onto the walkable field.</summary>
        private static bool TryParkPoint(float3 hallPos, float2 worldMin, float2 worldSize, out float3 park)
        {
            float2 centre = worldMin + worldSize * 0.5f;
            float2 away = new float2(hallPos.x, hallPos.z) - centre;
            float len = math.length(away);
            float2 dir = len > 0.01f ? away / len : new float2(1f, 0f);
            float3 want = new float3(hallPos.x + dir.x * Cfg.celestarWatchOffset, hallPos.y,
                                     hallPos.z + dir.y * Cfg.celestarWatchOffset);
            NavGridQuery.SnapToWalkable(want, out park, out bool ok);
            return ok;
        }

        private static bool TryGetHallPos(EntityManager em, Faction faction, out float3 pos)
        {
            var q = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
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
