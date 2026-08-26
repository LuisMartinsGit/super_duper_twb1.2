// RitualApproach.cs
// Where a ritualist should actually WALK to when ordered onto a well.
//
// THE BUG THIS EXISTS TO FIX
//   All three verb systems used to set DesiredDestination to the node's own
//   transform position — the centre of the well. Every BuildingTag entity
//   stamps a 3x3 cell block of NavCostField.CostImpassable centred on its
//   transform (StampBuildingFootprintJob), so that destination is ALWAYS
//   inside an impassable footprint.
//
//   IntegrationDijkstraJob bails the moment the goal cell is impassable:
//
//       if (Cost[goalIdx] == NavCostField.CostImpassable) return;
//
//   ...leaving the whole integration field at UnreachableIntegration. The
//   unit therefore has a live destination (so it plays its walk animation and
//   reads as "moving") and no gradient anywhere to follow, so it never
//   changes position. Observed 2026-08-07 as an Alanthor Holy Scholar frozen
//   on the spot with its destination set to the node, and it is the reason
//   Feraldis Corruptors only ever channelled when something else happened to
//   shove them inside range first.
//
//   The range checks are NOT the problem and are unchanged: "am I close
//   enough to channel" is still measured to the node centre. Only the
//   pathing target moves.

using Unity.Mathematics;

namespace TheWaningBorder.Systems.Border
{
    public static class RitualApproach
    {
        /// <summary>
        /// How far from the node centre to actually stand, in metres.
        ///
        /// Must clear the building footprint (3 cells across, so ~1.5 cells
        /// from centre plus grid snapping) and still sit inside every ritual's
        /// start range — RitualRange and CorruptRange are both 6 m, and the
        /// cancel ranges are 10 m, so 4 m leaves margin on both sides. Scales
        /// with the nav cell size so a coarser grid cannot swallow the
        /// stand-off.
        /// </summary>
        public static float StandOffDistance =>
            math.max(4f, GameSettings.PathfindingCellSize * 3f);

        /// <summary>
        /// A reachable point to path to when approaching <paramref name="nodePos"/>.
        /// Sits StandOffDistance out from the node on the side the ritualist is
        /// already coming from, so the approach is the short way round rather
        /// than a walk to some fixed bearing.
        /// </summary>
        public static float3 StandPoint(float3 nodePos, float3 ritualistPos)
        {
            float dx = ritualistPos.x - nodePos.x;
            float dz = ritualistPos.z - nodePos.z;
            float d = math.sqrt(dx * dx + dz * dz);

            // Degenerate case: the ritualist is standing exactly on the node
            // centre (extruded into the footprint by separation, say). Any
            // bearing is as good as another — pick one rather than divide by
            // zero and hand the mover a NaN destination.
            if (d < 0.01f) { dx = 1f; dz = 0f; d = 1f; }

            float s = StandOffDistance / d;
            return new float3(nodePos.x + dx * s, nodePos.y, nodePos.z + dz * s);
        }
    }
}
