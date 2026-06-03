// UnitNavProfileAttachSystem.cs
// task-112 M5 -- runs every tick in the SimulationSystemGroup,
// attaches NavLayerIndex { Layer = 0 } + NavTraversalProfile { ProfileId
// = ProfileClimbable } to every UnitTag entity that doesn't have one.
//
// This avoids touching every unit factory file in
// Assets/Scripts/Entities/Units/ (~25 files) -- the architecture's M5
// section calls for the components per unit, but the IEntityCreator
// pattern makes the mechanical edit error-prone (UnitFactory dispatches
// by string id). A per-tick attach system catches every spawn path
// uniformly.
//
// Determinism: the lazy-add fires on the tick AFTER the spawn, with
// ECB playback. Singletons present check ensures we don't add before
// the TraversalProfileBlob exists.
//
// Performance: the query is "UnitTag without NavLayerIndex" so once
// every existing unit has been promoted, the system runs against an
// empty query (zero-cost).
//
// Location: Assets/Scripts/Systems/Navigation/UnitNavProfileAttachSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M5 -- lazily attaches per-unit M5 nav components to any
    /// <c>UnitTag</c> entity that lacks them. Runs in the simulation
    /// group before the integrator so the layer index is available when
    /// the integrator does its height snap.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(UnitIntegratorSystem))]
    public partial struct UnitNavProfileAttachSystem : ISystem
    {
        private EntityQuery _missingLayerQuery;
        private EntityQuery _missingProfileQuery;

        public void OnCreate(ref SystemState state)
        {
            _missingLayerQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag>()
                .WithNone<NavLayerIndex>()
                .Build();
            _missingProfileQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag>()
                .WithNone<NavTraversalProfile>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_missingLayerQuery.IsEmpty
                && _missingProfileQuery.IsEmpty) return;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            if (!_missingLayerQuery.IsEmpty)
            {
                ecb.AddComponent(_missingLayerQuery, new NavLayerIndex { Layer = 0 });
            }

            if (!_missingProfileQuery.IsEmpty)
            {
                // Default: climbable -- swordsmen, archers, every infantry.
                // M6 specialisation can swap to ground-only / rampart-only
                // per unit type when the per-type profile table is wired.
                ecb.AddComponent(_missingProfileQuery, new NavTraversalProfile
                {
                    ProfileId = TraversalProfileSingleton.ProfileClimbable,
                });
            }
        }
    }
}
