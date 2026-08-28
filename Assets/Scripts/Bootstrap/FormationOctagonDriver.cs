// FormationOctagonDriver.cs
// A formation test rig: walks one squad around an octagon, issuing a real
// FORMATION move order for each leg and nothing else.
//
// Deliberately NOT ScenarioPatrolController. That drives units by writing
// DesiredDestination per unit every frame, which is a nine-single-unit-moves
// test — it never builds a FormationGroup, so it cannot exercise slot
// assignment, the virtual leader, the tether, or the arrival settle, which is
// where every formation bug so far has lived.
//
// An octagon is the useful shape because each leg turns the formation 45
// degrees. The bugs that survived a straight-line test were all direction
// dependent:
//   * the slot lattice colliding with the axis-aligned build grid, which only
//     bites at off-axis headings;
//   * slot assignment scrambling when the group's current arrangement no
//     longer lines up with the new travel axis;
//   * the layout being re-derived per order, so every turn is a fresh chance
//     to shuffle who stands where.
// Eight headings, 45 degrees apart, hits all of them in one loop.
//
// Watch it with F2 (FormationDebugOverlay): the leader gimbal should turn on
// each leg, the nine spot rings should stay a rigid 3x3 about it, and the line
// from each unit to its spot should stay short. A unit whose line grows, or
// whose spot swaps sides with a neighbour's on a turn, is the bug reproducing.
//
// Editor / scenario use. Nothing mounts this automatically.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Bootstrap
{
    public class FormationOctagonDriver : MonoBehaviour
    {
        /// <summary>The squad. Order does not matter; the formation assigns
        /// slots from live positions.</summary>
        public readonly List<Entity> Units = new();

        [Tooltip("Centre of the octagon in world XZ.")]
        public Vector3 Centre = Vector3.zero;

        [Tooltip("Distance from the centre to each corner. Long enough that a " +
                 "leg is a real march rather than a shuffle.")]
        public float Radius = 40f;

        [Tooltip("Formation to hold. Box gives a 3x3 for nine units.")]
        public FormationShape Shape = FormationShape.Box;

        [Tooltip("How close the squad's centroid must get before the next leg " +
                 "is ordered.")]
        public float LegArrivalRadius = 3f;

        [Tooltip("Safety valve: order the next leg anyway after this long, so " +
                 "one wedged unit cannot park the test forever.")]
        public float LegTimeout = 30f;

        /// <summary>
        /// Idle units posted around the course, absorbed into the army as it
        /// marches up to them.
        ///
        /// This is the part of a formation the straight march never tests: the
        /// roster CHANGING mid-course. Every joiner invalidates the layout —
        /// new blocks, new slot count, a new layout key — so the slot memory
        /// must fall through to a clean rebuild and the whole army has to
        /// re-form around units that were standing still a moment ago, without
        /// losing the shape it already had.
        /// </summary>
        public readonly List<Entity> Reinforcements = new();

        [Tooltip("How close the army centroid must come before an idle " +
                 "reinforcement falls in.")]
        public float PickupRadius = 12f;

        private const int Corners = 8;

        [Tooltip("Write logs/<session>/Formation.csv: per-frame position, spot, " +
                 "commanded speed and ACHIEVED speed for every member.")]
        public bool LogTelemetry = true;

        private int _leg = -1;
        private float _legAge;
        private bool _issued;

        /// <summary>Corner i of the octagon, starting due +Z and turning 45
        /// degrees a leg.</summary>
        public Vector3 Corner(int i)
        {
            float a = (i % Corners) * (Mathf.PI * 2f / Corners);
            return Centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius;
        }

        private void Awake()
        {
            if (!LogTelemetry) return;
            TheWaningBorder.Core.Diagnostics.FormationTelemetry.Enabled = true;
            if (Object.FindFirstObjectByType<
                    TheWaningBorder.Core.Diagnostics.FormationTelemetry>() == null)
                gameObject.AddComponent<TheWaningBorder.Core.Diagnostics.FormationTelemetry>();
        }

        private void LateUpdate()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Drop anything that died so the squad does not shrink into a
            // stale entity list.
            for (int i = Units.Count - 1; i >= 0; i--)
                if (!em.Exists(Units[i]) || !em.HasComponent<LocalTransform>(Units[i]))
                    Units.RemoveAt(i);
            if (Units.Count == 0) return;

            if (!_issued)
            {
                _leg++;
                IssueLeg(em, _leg);
                _issued = true;
                _legAge = 0f;
                return;
            }

            _legAge += Time.deltaTime;

            // Absorb anyone the army has marched up to, and re-issue THIS leg
            // so the joiners are given slots. Re-issuing rather than waiting
            // for the corner is the point: a formation that can only accept
            // reinforcements at a waypoint is not accepting them, it is being
            // rebuilt from scratch between legs.
            if (AbsorbNearby(em))
            {
                IssueLeg(em, _leg);
                return;
            }

            // ONE order per leg. Re-issuing every frame would hide the very
            // thing being tested: a formation that only looks correct because
            // it is being re-planned constantly is not holding its shape, and
            // the kiting bug was exactly a group that never survived its own
            // stream of orders.
            if (CentroidDistanceTo(em, Corner(_leg + 1)) <= LegArrivalRadius
                || _legAge >= LegTimeout)
                _issued = false;
        }

        /// <summary>Pull any idle reinforcement within PickupRadius of the
        /// army into the army. True if the roster changed.</summary>
        private bool AbsorbNearby(EntityManager em)
        {
            if (Reinforcements.Count == 0) return false;

            float3 c = float3.zero;
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                if (!em.HasComponent<LocalTransform>(Units[i])) continue;
                c += em.GetComponentData<LocalTransform>(Units[i]).Position;
                n++;
            }
            if (n == 0) return false;
            c /= n;

            bool joined = false;
            for (int i = Reinforcements.Count - 1; i >= 0; i--)
            {
                var e = Reinforcements[i];
                if (!em.Exists(e) || !em.HasComponent<LocalTransform>(e))
                {
                    Reinforcements.RemoveAt(i);
                    continue;
                }
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz > PickupRadius * PickupRadius) continue;

                Reinforcements.RemoveAt(i);
                Units.Add(e);
                joined = true;
            }
            if (joined)
                TWBLog.Log($"[FormationOctagon] army is now {Units.Count} strong " +
                           $"({Reinforcements.Count} still posted).");
            return joined;
        }

        private void IssueLeg(EntityManager em, int leg)
        {
            // Target the NEXT corner: leg 0 walks corner 0 -> corner 1.
            float3 dest = Corner(leg + 1);
            FormationMoveCommandHelper.Execute(em, Units, dest, Shape, attackMove: false);
        }

        private float CentroidDistanceTo(EntityManager em, Vector3 p)
        {
            float3 c = float3.zero;
            int n = 0;
            for (int i = 0; i < Units.Count; i++)
            {
                if (!em.HasComponent<LocalTransform>(Units[i])) continue;
                c += em.GetComponentData<LocalTransform>(Units[i]).Position;
                n++;
            }
            if (n == 0) return float.MaxValue;
            c /= n;
            float dx = c.x - p.x, dz = c.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
            for (int i = 0; i < Corners; i++)
                Gizmos.DrawLine(Corner(i), Corner(i + 1));
        }
    }
}
