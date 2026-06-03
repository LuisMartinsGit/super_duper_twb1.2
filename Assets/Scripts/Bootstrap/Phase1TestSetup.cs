// Phase1TestSetup.cs
// Spawns the minimum scene for task-112 M1 verification: a single Blue
// Swordsman on the flat 64x64 grid the NavGridBootstrapSystem allocates.
// Issues a click-to-move command toward the opposite corner so the new
// flow pipeline drives the unit while the legacy NavMesh path stays in
// place as a fallback (OOS2).
//
// Mirrors the shape of every other Bootstrap/*TestSetup.cs in the repo:
// static class with a SpawnScenarioEntities(EntityManager) entry that
// ScenarioSetup.SpawnScenarioEntities() dispatches to.
//
// Location: Assets/Scripts/Bootstrap/Phase1TestSetup.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// One-unit Phase 1 nav-stack test. Spawns a Swordsman at world (4, _, 4)
    /// and queues a move to (60, _, 60). With the M1 plumbing in place, the
    /// unit should follow the flow-field direction across the grid and
    /// arrive within ~1.0 world unit of the goal within 200 sim ticks.
    /// </summary>
    public static class Phase1TestSetup
    {
        public const float SpawnX = 4f;
        public const float SpawnZ = 4f;
        public const float GoalX = 60f;
        public const float GoalZ = 60f;

        public static void SpawnScenarioEntities(EntityManager em)
        {
            float sy = TerrainUtility.GetHeight(SpawnX, SpawnZ);
            float gy = TerrainUtility.GetHeight(GoalX, GoalZ);

            var spawn = new float3(SpawnX, sy, SpawnZ);
            var entity = UnitFactory.Create(em, "Swordsman", spawn, Faction.Blue);
            if (entity == Entity.Null) return;

            // Issue the move toward the NE corner. M3 MoveCommandHelper
            // attaches a NavPathRequest, AbstractPathfinderSystem solves
            // it, FlowSegmentSystem caches per-tile flow slabs, and
            // FlowFollowSystem samples the slab at the unit's cell each
            // tick.
            var goal = new float3(GoalX, gy, GoalZ);
            MoveCommandHelper.Execute(em, entity, goal);
        }
    }
}
