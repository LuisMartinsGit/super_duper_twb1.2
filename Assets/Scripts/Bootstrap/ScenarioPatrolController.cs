// ScenarioPatrolController.cs
// Per-frame movement driver for the PatrolDefense scenario. Pushes
// DesiredDestination on each registered Veilstinger every LateUpdate so the
// standard MovementSystem walks the unit around its waypoint loop.
//
// Targeting is handled by ScenarioPatrolTargetingSystem (an ECS system that
// runs immediately before VeilstingerCombatSystem). Writing Target from
// inside SimulationSystemGroup is necessary because em.SetComponentData
// writes from a MonoBehaviour phase get zeroed out before the next frame's
// VeilstingerCombatSystem read (confirmed via diagnostics — see the system
// header comment).

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Bootstrap
{
    public class ScenarioPatrolController : MonoBehaviour
    {
        public class PatrolUnit
        {
            public Entity Entity;
            public Vector3[] Waypoints;
            public int CurrentWaypoint;
            /// <summary>Distance at which this unit will consider an enemy a candidate. Kept on the data type for API compatibility — actual engagement targeting is done by ScenarioPatrolTargetingSystem.</summary>
            public float EngageRange;
        }

        public readonly List<PatrolUnit> Units = new();

        /// <summary>How close an entity must get (XZ) to count as "arrived".</summary>
        public float ArrivalRadius = 1.5f;

        void LateUpdate()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            for (int u = 0; u < Units.Count; u++)
            {
                var unit = Units[u];
                if (!em.Exists(unit.Entity)) continue;
                if (!em.HasComponent<LocalTransform>(unit.Entity)) continue;
                if (unit.Waypoints == null || unit.Waypoints.Length == 0) continue;
                if (!em.HasComponent<DesiredDestination>(unit.Entity)) continue;

                var myPos = em.GetComponentData<LocalTransform>(unit.Entity).Position;
                Vector3 target = unit.Waypoints[unit.CurrentWaypoint];

                float2 posXZ = new float2(myPos.x, myPos.z);
                float2 tgtXZ = new float2(target.x, target.z);
                float dist = math.distance(posXZ, tgtXZ);

                if (dist < ArrivalRadius)
                {
                    unit.CurrentWaypoint = (unit.CurrentWaypoint + 1) % unit.Waypoints.Length;
                    target = unit.Waypoints[unit.CurrentWaypoint];
                }

                em.SetComponentData(unit.Entity, new DesiredDestination
                {
                    Position = new float3(target.x, target.y, target.z),
                    Has = 1
                });
            }
        }
    }
}
