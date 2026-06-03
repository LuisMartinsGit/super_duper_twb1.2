// Phase3TestSetup.cs
// task-112 M3 -- spawns the Phase3Test scenario: a single Blue
// Swordsman near the SW corner of the 512x512 cost field, commanded to
// the NE corner. The scenario validates the architecture's R10 scale
// target (a corner-to-corner move on a 512x512 grid without a
// whole-map integration field).
//
// Layout
//   * Cost field is 512x512 with CellSize = 1 and Origin centred on
//     world origin -- world cells [-256..+256] on both axes.
//   * SW corner cell ~(8, 8) maps to world ~(-248, _, -248).
//   * NE corner cell ~(504, 504) maps to world ~(+248, _, +248).
//
// On execution:
//   1. MoveCommandHelper attaches a NavPathRequest to the unit
//      carrying the start/goal cells + current graph generation.
//   2. PortalGraphBuildSystem (one-shot) detects inter-tile portals
//      across the 32x32 tile grid; ~32*31*2 = 1984 portal nodes
//      worst-case, ~3-4K edges including intra-tile manhattan stubs.
//   3. AbstractPathfinderSystem solves the chain of portals SW -> NE.
//   4. FlowSegmentSystem ensures each traversed tile has a cached
//      flow slab keyed by (tile, exitPortal).
//   5. FlowFollowSystem samples the slab at the unit's tile-local
//      cell each tick and writes FlowDesiredDir.
//   6. MovementSystem reads FlowDesiredDir and integrates motion.
//
// Location: Assets/Scripts/Bootstrap/Phase3TestSetup.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// One-unit Phase 3 scale test. Spawns a Swordsman at cell ~(8, 8)
    /// and commands a move to cell ~(504, 504). With the M3 stack in
    /// place the unit should follow per-tile cached flow slabs all the
    /// way across without a whole-map integration field allocation.
    /// </summary>
    public static class Phase3TestSetup
    {
        // World coordinates that map to cells ~(8, 8) and ~(504, 504) on
        // the 512x512 grid (origin at world centre, CellSize = 1).
        public const float SpawnX = -248f;
        public const float SpawnZ = -248f;
        public const float GoalX = 248f;
        public const float GoalZ = 248f;

        public static void SpawnScenarioEntities(EntityManager em)
        {
            float sy = TerrainUtility.GetHeight(SpawnX, SpawnZ);
            float gy = TerrainUtility.GetHeight(GoalX, GoalZ);

            var spawn = new float3(SpawnX, sy, SpawnZ);
            var entity = UnitFactory.Create(em, "Swordsman", spawn, Faction.Blue);
            if (entity == Entity.Null) return;

            var goal = new float3(GoalX, gy, GoalZ);
            MoveCommandHelper.Execute(em, entity, goal);
        }
    }
}
