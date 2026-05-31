// NavMeshConfineSystem.cs
// Confines units to the baked navmesh — the staying-on-the-surface guarantee
// that a Unity NavMeshAgent would normally provide. This game has no
// NavMeshAgent (units are ECS entities; movement writes LocalTransform
// directly), and the navmesh is only used to COMPUTE paths, not to constrain
// where units end up. Several systems move units without checking the navmesh
// (MovementSystem's corridor follow + fallback + stuck sidestep,
// UnitSeparationSystem spacing pushes, BattalionSyncSystem formation slots),
// so a unit can drift or be shoved off the navmesh with nothing to pull it back.
//
// This system runs at the END of the simulation tick (LateSimulationSystemGroup,
// after every mover in SimulationSystemGroup) and snaps any unit that ended up
// off the navmesh back onto the nearest navmesh point. Units already on the
// navmesh sample to themselves and are left untouched.
//
// Location: Assets/Scripts/Systems/Movement/NavMeshConfineSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial class NavMeshConfineSystem : SystemBase
    {
        // How far off the navmesh a unit may be and still get rescued. Units
        // further off than this are left alone (SamplePosition finds nothing) —
        // shouldn't happen in normal play since units start on the navmesh and
        // per-frame displacement is small.
        private const float ClampSearchRadius = 12f;
        // Ignore sub-0.2m differences so a unit sitting exactly on an edge
        // doesn't micro-jitter against the sampled point.
        private const float ClampEpsilonSq = 0.04f;

        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadWrite<LocalTransform>());
            RequireForUpdate(_query);
        }

        protected override void OnUpdate()
        {
            var nmm = NavMeshManager.Instance;
            if (nmm == null || !nmm.IsBaked) return;

            var em = EntityManager;
            using var entities = _query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var xf = em.GetComponentData<LocalTransform>(e);
                float3 p = xf.Position;

                // SamplePosition uses the default agent (id 0), which matches
                // the scene's baked surface. If the unit is already on the
                // navmesh the nearest point is itself (zero offset) → no change.
                if (!NavMesh.SamplePosition(p, out var hit, ClampSearchRadius, NavMesh.AllAreas))
                    continue;

                float dx = hit.position.x - p.x;
                float dz = hit.position.z - p.z;
                if (dx * dx + dz * dz <= ClampEpsilonSq) continue;

                // Off the navmesh — snap back onto the nearest navmesh point,
                // keeping terrain height (presentation re-snaps Y anyway).
                float y = TerrainUtility.GetHeight(hit.position.x, hit.position.z);
                xf.Position = new float3(hit.position.x, y, hit.position.z);
                em.SetComponentData(e, xf);
            }
        }
    }
}
