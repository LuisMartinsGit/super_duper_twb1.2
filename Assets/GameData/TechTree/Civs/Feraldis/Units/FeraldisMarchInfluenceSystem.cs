// RETIRED (docs/Design/Regions.md §3b, 2026-08-31): there are no influence
// maps any more, so there is no field for a marching army to leak into.
//
// This system was the Feraldis half of the influence model — every soldier
// deposited a little claim under its feet, so the border crept toward the
// army ("Feraldis claims ground by WALKING ON IT",
// docs/Design/Age_1_Feraldis.md). With fixed-shape territories the claim
// verb is the HALL for every culture (Regions.md §2), and ownership changes
// instantly; a creeping per-unit deposit has nothing to write to and would
// only thrash the version-gated renderers.
//
// The file stays (rather than being deleted) as the tombstone for the
// mechanic, because Age_1_Feraldis.md still describes it and the design
// folder has not had its Feraldis pass yet. When it does, either this file
// goes with it or the design brings the mechanic back in territory terms
// (e.g. an army presence requirement on the CLAIM, not a field).

using Unity.Entities;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisMarchInfluenceSystem : SystemBase
    {
        protected override void OnCreate()
        {
            Enabled = false;   // Regions.md §3b — no influence maps
        }

        protected override void OnUpdate() { }
    }
}
