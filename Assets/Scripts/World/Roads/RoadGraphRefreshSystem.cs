// RoadGraphRefreshSystem.cs
// Rebuilds the simulation road graph (docs/Design/Roads.md §0) on the
// lockstep tick when its sites change. A system of its own, not a step of
// the Runai trade, because the graph is the one map network every culture's
// roads are drawn from — and editions A/B strip the Runai trade's config.

using TheWaningBorder.Core;
using Unity.Entities;

namespace TheWaningBorder.World.Roads
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RoadGraphRefreshSystem : SystemBase
    {
        // Engine cadence, not balance: how often the site set is re-hashed.
        private const float RefreshSeconds = 2f;
        private SimCadence.Periodic _tick;

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (_tick.Due(dt, RefreshSeconds) || !RoadGraphSystem.Built)
                RoadGraphSystem.Refresh(EntityManager);
        }
    }
}
