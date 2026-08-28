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
// any other test can.

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

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _t += dt;

            if (!_headerWritten)
            {
                _buf.AppendLine("t,group,unit,leaderX,leaderZ,leaderSpeed,groupSpeed,"
                              + "posX,posZ,spotX,spotZ,offset,alongOffset,lateralOffset,"
                              + "nominalSpeed,commandedSpeed,actualSpeed,tier");
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
                    float commanded = em.HasComponent<FormationSpeedOverride>(u)
                        ? em.GetComponentData<FormationSpeedOverride>(u).Value
                        : nominal;
                    float actual = MeasureSpeed(u, pos, dt);

                    string tier = members[i].CatchingUp != 0 ? "catchup"
                        : along < -0.15f ? "ahead"
                        : offset <= 0.15f ? "inplace" : "closing";

                    Row(_t, ge.Index, u.Index, g.LeaderPos, leaderSpeed, g.GroupSpeed,
                        pos, spot, offset, along, lateral,
                        nominal, commanded, actual, tier);
                }
            }

            if (_pending >= FlushEveryRows) Flush();
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
