// WallGarrisonSystem.cs
// Keeps a reinforced wall's garrison honest (docs/Design/Age_1_Alanthor.md
// § Garrison slots). Two jobs, both cheap and polled:
//
//   * a module whose occupants changed (one died, one was added) gets its
//     fire rebuilt from the men actually in it;
//   * a unit whose module is GONE dies with it. A wall that falls takes the
//     men on it — an absorbed unit has no ground to come back to, and
//     leaving it disabled forever would strand it out of every query.
//
// The 2026-05-29 "spread elevated units along the parapet" body is gone:
// the compact curtain has no walkable deck to spread anyone along, and the
// garrison is absorbed rather than parked (see WallGarrison.cs).

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WallGarrisonSystem : SystemBase
    {
        /// <summary>Seconds between sweeps. The garrison changes on player
        /// orders and on deaths, neither of which needs a per-frame answer.</summary>
        private const float PollInterval = 0.5f;
        private float _next;

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<WallGarrisonSlot>()));
        }

        protected override void OnUpdate()
        {
            _next -= SystemAPI.Time.DeltaTime;
            if (_next > 0f) return;
            _next = PollInterval;

            var em = EntityManager;

            var modules = new List<Entity>();
            foreach (var (_, entity) in SystemAPI
                         .Query<DynamicBuffer<WallGarrisonSlot>>()
                         .WithEntityAccess())
                modules.Add(entity);

            for (int i = 0; i < modules.Count; i++)
                if (em.Exists(modules[i])) WallGarrison.RefreshFire(em, modules[i]);

            // Orphans: the module they were in is gone. Disabled entities are
            // invisible to SystemAPI.Query, so they are collected off the
            // explicit query below.
            var orphanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<WallGarrisonedIn>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
            if (orphanQuery.IsEmptyIgnoreFilter) return;

            var garrisoned = orphanQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            var doomed = new List<Entity>();
            for (int i = 0; i < garrisoned.Length; i++)
            {
                var u = garrisoned[i];
                if (!em.Exists(u)) continue;
                var module = em.GetComponentData<WallGarrisonedIn>(u).Module;
                if (module == Entity.Null || !em.Exists(module)) doomed.Add(u);
            }
            garrisoned.Dispose();

            for (int i = 0; i < doomed.Count; i++)
                if (em.Exists(doomed[i])) em.DestroyEntity(doomed[i]);
        }
    }
}
