// SimClockSystem.cs
// Advances TheWaningBorder.Core.SimClock once per simulation update.
//
// It lives in SimulationSystemGroup deliberately: under lockstep that group runs
// exactly once per tick at a fixed delta, so the clock counts TICKS rather than
// frames and reads identically on every peer. In single-player the group runs
// per frame and SimClock tracks frame time, which is what the callers used to
// read from Time.time anyway — so nothing about single-player changes.
//
// Ordered first in the group so everything else in the same update sees a clock
// that has already advanced for this step.
//
// Location: Assets/Scripts/Systems/Core/SimClockSystem.cs

using Unity.Burst;
using Unity.Entities;

namespace TheWaningBorder.Systems.Core
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct SimClockSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            TheWaningBorder.Core.SimClock.Advance(SystemAPI.Time.DeltaTime);
        }
    }
}
