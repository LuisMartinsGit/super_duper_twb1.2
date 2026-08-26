// CachedEntityQuery.cs
// Per-call-site EntityQuery cache for MANAGED (MonoBehaviour / static
// helper) code. EntityManager.CreateEntityQuery permanently registers a
// NEW query with the world on every call — calling it inside Update /
// LateUpdate / OnGUI leaks hundreds of queries per second, bloating the
// world's query registry so every query call AND every structural change
// gets progressively slower (the "skirmish starts smooth, sinks to 15 FPS"
// curve). ECS systems have state.GetEntityQuery for this; managed code
// gets this struct instead:
//
//   private static readonly ComponentType[] DustTypes =
//       { ComponentType.ReadOnly<UnderConstruction>(), typeof(BuildingTag) };
//   private CachedEntityQuery _dustQuery;                  // field, no init
//   ...
//   var query = _dustQuery.Get(em, DustTypes);             // per frame: free
//
// The query is created once per world; when the world is rebuilt (back to
// menu, new match) the stale handle is dropped and a fresh query is created
// against the new world. Never Dispose these — world teardown owns them.

using Unity.Entities;

namespace TheWaningBorder.Core
{
    public struct CachedEntityQuery
    {
        private EntityQuery _query;
        private Unity.Entities.World _world;

        /// <summary>Cached query for <paramref name="types"/> in the
        /// manager's world. Hoist the types array into a static readonly
        /// field at the call site so the hot path allocates nothing.</summary>
        public EntityQuery Get(EntityManager em, ComponentType[] types)
        {
            var world = em.World;
            if (!ReferenceEquals(_world, world) || world == null || !world.IsCreated)
            {
                _query = em.CreateEntityQuery(types);
                _world = world;
            }
            return _query;
        }
    }
}
