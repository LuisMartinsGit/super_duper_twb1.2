// FormationTelemetry.cs
// Per-frame CSV of every formation member: where it is, where its spot is, what
// speed it was TOLD to move at, and what speed it ACTUALLY achieved.
//
// The last pair is the point. "The units feel slow" has two completely
// different causes and they need different fixes:
//
//   commanded < nominal   -> the FORMATION is holding them back. Some tier of
//                            the speed ladder, the leader's ease, or a stale
//                            GroupSpeed picked from the slowest member.
//   actual < commanded    -> the formation is asking for full speed and
//                            something downstream is eating it: separation and
//                            avoidance forces bending the step, the turn-rate
//                            clamp while a unit is still rotating, terrain
//                            cost, arrival braking, or a blocked cell.
//
// Guessing between those from watching the screen is how the last several
// rounds went. One row per member per frame answers it outright.
//
// Writes to the match log folder (logs/<session>/Formation.csv), so it lands
// beside Console.log and Timeline.csv for the same run.
//
// OFF by default. FormationOctagonDriver turns it on, and Enabled is public so
// any other test can; a headless match takes -twbFormationTelemetry (Install,
// one sample a second).
//
// WHERE THE SPEED GOES (2026-10-25). actual < commanded says something
// downstream eats the step but not WHAT, so each row also carries the
// candidates: bendDeg (the angle steering turned the formation's wanted
// direction through -- separation / avoidance), stuck (the integrator's
// blocked-step counter), aheadBlocked (a unit could not stand one metre along
// the steered direction) and foreign (units of another formation, or none,
// within ForeignRadius).

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Diagnostics
{
    [DefaultExecutionOrder(2100)] // after the sim has stepped this frame
    public class FormationTelemetry : MonoBehaviour
    {
        /// <summary>Master switch. Nothing is sampled or allocated while off.</summary>
        public static bool Enabled;

        /// <summary>Rows are buffered and flushed in batches — a file write per
        /// unit per frame would be measuring the profiler, not the game.</summary>
        private const int FlushEveryRows = 600;

        /// <summary>Seconds between samples; 0 = every frame.</summary>
        public static float Period;
        /// <summary>Radius (m) the foreign-neighbour count looks in.</summary>
        private const float ForeignRadius = 1.5f;

        /// <summary>A headless match's switch: one sample every
        /// <paramref name="period"/> seconds.</summary>
        public static void Install(float period)
        {
            Period = period;
            Enabled = true;
            var go = new GameObject("FormationTelemetry");
            DontDestroyOnLoad(go);
            go.AddComponent<FormationTelemetry>();
        }

        private float _next;
        private float _lastSim = -1f;
        private static readonly ComponentType[] UnitTypes =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private CachedEntityQuery _unitQuery;
        private readonly Dictionary<long, List<(Entity e, float3 p)>> _cells = new();

        private readonly StringBuilder _buf = new StringBuilder(1 << 16);
        private readonly Dictionary<Entity, float3> _lastPos = new();
        private int _pending;
        private bool _headerWritten;
        private float _t;

        private EntityWorld _world;

        private static readonly ComponentType[] GroupTypes =
        {
            ComponentType.ReadOnly<FormationGroup>(),
        };
        private CachedEntityQuery _groupQuery;

        private void LateUpdate()
        {
            if (!Enabled) return;

            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            var em = _world.EntityManager;

            // SIM time, not frame time: a headless frame at 3x steps the
            // simulation zero, one or several times, so a frame delta would
            // read false stops and spikes.
            float simNow = TheWaningBorder.Core.SimClock.Now;
            if (_lastSim < 0f || simNow < _lastSim) { _lastSim = simNow; return; }
            float dt = simNow - _lastSim;
            if (dt <= 0f) return;            // no sim step this frame
            _lastSim = simNow;
            _t += dt;
            if (Period > 0f)
            {
                // Sampled: speed is still over ONE frame (positions are kept
                // current every frame), but a row is written once a period.
                if (_t < _next) { RememberPositions(em); return; }
                _next = _t + Period;
            }
            BucketUnits(em);

            if (!_headerWritten)
            {
                _buf.AppendLine("t,group,unit,leaderX,leaderZ,leaderSpeed,groupSpeed,"
                              + "posX,posZ,spotX,spotZ,offset,alongOffset,lateralOffset,"
                              + "nominalSpeed,commandedSpeed,actualSpeed,tier,"
                              + "bendDeg,stuck,aheadBlocked,foreign");
                _headerWritten = true;
            }

            var q = _groupQuery.Get(em, GroupTypes);
            using var groups = q.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int gi = 0; gi < groups.Length; gi++)
            {
                var ge = groups[gi];
                if (!em.Exists(ge) || !em.HasBuffer<FormationMember>(ge)) continue;
                var g = em.GetComponentData<FormationGroup>(ge);
                var members = em.GetBuffer<FormationMember>(ge);

                float3 facing = math.normalizesafe(g.Facing, new float3(0f, 0f, 1f));
                float3 right = math.cross(new float3(0f, 1f, 0f), facing);

                // The leader's own achieved speed, from its position delta —
                // the same measure applied to the members, so the two columns
                // are comparable rather than one being a command and the other
                // an outcome.
                float leaderSpeed = MeasureSpeed(ge, g.LeaderPos, dt);

                for (int i = 0; i < members.Length; i++)
                {
                    var u = members[i].Unit;
                    if (u == Entity.Null || !em.Exists(u)) continue;
                    if (!em.HasComponent<LocalTransform>(u)) continue;

                    float3 pos = em.GetComponentData<LocalTransform>(u).Position;
                    float2 sl = members[i].Slot;
                    float3 spot = g.LeaderPos + right * sl.x + facing * sl.y;

                    float3 toSpot = spot - pos;
                    toSpot.y = 0f;
                    float offset = math.length(toSpot);
                    float along = math.dot(toSpot, facing);
                    float lateral = math.dot(toSpot, right);

                    float nominal = em.HasComponent<MoveSpeed>(u)
                        ? em.GetComponentData<MoveSpeed>(u).Value : 0f;
                    float commanded = TransientState.Active<FormationSpeedOverride>(em, u)
                        ? em.GetComponentData<FormationSpeedOverride>(u).Value
                        : nominal;
                    float actual = MeasureSpeed(u, pos, dt);

                    string tier = members[i].CatchingUp != 0 ? "catchup"
                        : along < -0.15f ? "ahead"
                        : offset <= 0.15f ? "inplace" : "closing";

                    float bend = 0f;
                    float3 steer = float3.zero;
                    if (TransientState.Active<SteeringDesiredDir>(em, u))
                        steer = em.GetComponentData<SteeringDesiredDir>(u).Value;
                    steer.y = 0f;
                    if (TransientState.Active<FlowDesiredDir>(em, u) && math.lengthsq(steer) > 1e-6f)
                    {
                        var fd = em.GetComponentData<FlowDesiredDir>(u);
                        float3 fv = new float3(fd.Value.x, 0f, fd.Value.z);
                        if (fd.HasValue != 0 && math.lengthsq(fv) > 1e-6f)
                            bend = math.degrees(math.acos(math.clamp(
                                math.dot(math.normalize(fv), math.normalize(steer)), -1f, 1f)));
                    }
                    int stuck = em.HasComponent<StuckState>(u) ? em.GetComponentData<StuckState>(u).Counter : 0;
                    bool aheadBlocked = math.lengthsq(steer) > 1e-6f
                        && !TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(
                               pos + math.normalize(steer));
                    int foreign = ForeignNear(em, u, pos, ge);

                    Row(_t, ge.Index, u.Index, g.LeaderPos, leaderSpeed, g.GroupSpeed,
                        pos, spot, offset, along, lateral,
                        nominal, commanded, actual, tier);
                    _buf.Length--;   // reopen the row for the cause columns
                    _buf.Append(',').Append(bend.ToString("0.0", CultureInfo.InvariantCulture))
                        .Append(',').Append(stuck)
                        .Append(',').Append(aheadBlocked ? 1 : 0)
                        .Append(',').Append(foreign).Append('\n');
                }
            }

            if (_pending >= FlushEveryRows) Flush();
        }

        /// <summary>Keep every member's last position current between
        /// samples, so a sample's speed is over ONE frame.</summary>
        private void RememberPositions(EntityManager em)
        {
            var q = _groupQuery.Get(em, GroupTypes);
            using var groups = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var ge = groups[gi];
                if (!em.Exists(ge) || !em.HasBuffer<FormationMember>(ge)) continue;
                _lastPos[ge] = em.GetComponentData<FormationGroup>(ge).LeaderPos;
                var members = em.GetBuffer<FormationMember>(ge);
                for (int i = 0; i < members.Length; i++)
                {
                    var u = members[i].Unit;
                    if (u != Entity.Null && em.Exists(u) && em.HasComponent<LocalTransform>(u))
                        _lastPos[u] = em.GetComponentData<LocalTransform>(u).Position;
                }
            }
        }

        private static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        private void BucketUnits(EntityManager em)
        {
            foreach (var l in _cells.Values) l.Clear();
            var q = _unitQuery.Get(em, UnitTypes);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var p = xfs[i].Position;
                long k = CellKey((int)math.floor(p.x / ForeignRadius), (int)math.floor(p.z / ForeignRadius));
                if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<(Entity, float3)>();
                l.Add((ents[i], p));
            }
        }

        /// <summary>Units within ForeignRadius that are NOT members of
        /// <paramref name="group"/> (another formation's, or loose).</summary>
        private int ForeignNear(EntityManager em, Entity self, float3 pos, Entity group)
        {
            int n = 0;
            int cx = (int)math.floor(pos.x / ForeignRadius), cz = (int)math.floor(pos.z / ForeignRadius);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!_cells.TryGetValue(CellKey(cx + dx, cz + dz), out var l)) continue;
                foreach (var (e, p) in l)
                {
                    if (e == self) continue;
                    float ddx = p.x - pos.x, ddz = p.z - pos.z;
                    if (ddx * ddx + ddz * ddz > ForeignRadius * ForeignRadius) continue;
                    bool mate = TransientState.Active<FormationMemberState>(em, e)
                        && em.GetComponentData<FormationMemberState>(e).Group == group;
                    if (!mate) n++;
                }
            }
            return n;
        }

        /// <summary>Achieved speed from the position delta. Falls back to 0 on
        /// the first frame an entity is seen, which is honest — there is no
        /// previous position to difference against.</summary>
        private float MeasureSpeed(Entity key, float3 pos, float dt)
        {
            float v = 0f;
            if (_lastPos.TryGetValue(key, out var prev))
            {
                float dx = pos.x - prev.x, dz = pos.z - prev.z;
                v = math.sqrt(dx * dx + dz * dz) / dt;
            }
            _lastPos[key] = pos;
            return v;
        }

        private void Row(float t, int group, int unit, float3 leader, float leaderSpeed,
            float groupSpeed, float3 pos, float3 spot, float offset, float along,
            float lateral, float nominal, float commanded, float actual, string tier)
        {
            var c = CultureInfo.InvariantCulture;
            _buf.Append(t.ToString("0.000", c)).Append(',')
                .Append(group).Append(',').Append(unit).Append(',')
                .Append(leader.x.ToString("0.00", c)).Append(',')
                .Append(leader.z.ToString("0.00", c)).Append(',')
                .Append(leaderSpeed.ToString("0.000", c)).Append(',')
                .Append(groupSpeed.ToString("0.000", c)).Append(',')
                .Append(pos.x.ToString("0.00", c)).Append(',')
                .Append(pos.z.ToString("0.00", c)).Append(',')
                .Append(spot.x.ToString("0.00", c)).Append(',')
                .Append(spot.z.ToString("0.00", c)).Append(',')
                .Append(offset.ToString("0.000", c)).Append(',')
                .Append(along.ToString("0.000", c)).Append(',')
                .Append(lateral.ToString("0.000", c)).Append(',')
                .Append(nominal.ToString("0.000", c)).Append(',')
                .Append(commanded.ToString("0.000", c)).Append(',')
                .Append(actual.ToString("0.000", c)).Append(',')
                .Append(tier).Append('\n');
            _pending++;
        }

        private void Flush()
        {
            if (_buf.Length == 0) return;
            try
            {
                System.IO.File.AppendAllText(MatchLogSession.File("Formation.csv"), _buf.ToString());
            }
            catch { /* diagnostics must never throw into the game */ }
            _buf.Length = 0;
            _pending = 0;
        }

        private void OnDisable() => Flush();
        private void OnDestroy() => Flush();
        private void OnApplicationQuit() => Flush();
    }
}
