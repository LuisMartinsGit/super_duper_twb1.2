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
        // 10 s (was 2 s, 2026-10-15 perf): in a late 8-player match a
        // building finishes every few seconds, so 2 s meant a full graph
        // rebuild — and every caravan re-entering the network — almost every
        // poll. A new site waiting up to 10 s for its road is invisible in
        // play. Lockstep-safe: the cadence counts sim time, the same on
        // every peer.
        private const float RefreshSeconds = 10f;
        private SimCadence.Periodic _tick;

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (_tick.Due(dt, RefreshSeconds) || !RoadGraphSystem.Built)
                RoadGraphSystem.Refresh(EntityManager);
        }
    }
}
