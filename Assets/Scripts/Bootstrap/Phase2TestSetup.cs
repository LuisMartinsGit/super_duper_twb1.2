// Phase2TestSetup.cs
// task-112 M2: spawns the Phase2Test scenario -- 300 Blue Swordsmen in
// a packed 10x30 formation south-west of the centre, all commanded to a
// single point east of the centre. The packed formation forces the
// SteeringSystem to resolve crowd separation as units push past each
// other on the way to the target.
//
// Layout fits the M3 512x512 nav grid bootstrapped by NavGridBootstrapSystem
// (world cells centred on origin). Formation centre is at (-30, _, 0),
// 1.5 m spacing -> 13.5 m wide x 43.5 m deep block. Goal at (30, _, 0).
// Each unit gets the same world-space MoveCommand; MoveCommandHelper.Execute
// attaches a NavPathRequest per unit which AbstractPathfinderSystem
// solves into a NavPathResult + NavPathPortal buffer; FlowSegmentSystem
// caches per-tile flow slabs keyed by (tile, nextPortal). For 300 units
// heading at the same goal there's only one slab per traversed tile so
// the cache stays small.
//
// Mirrors Phase1TestSetup.cs in shape: static class with a single
// SpawnScenarioEntities entry that ScenarioSetup dispatches to.
//
// Location: Assets/Scripts/Bootstrap/Phase2TestSetup.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// 300-unit Phase 2 nav-stack test. Spawns a 10x30 packed formation of
    /// Blue Swordsmen centred at (-30, _, 0) and queues each unit to move
    /// to (30, _, 0). The flow field carries everyone east; the steering
    /// blend keeps them from stacking on top of each other.
    /// </summary>
    public static class Phase2TestSetup
    {
        public const int FormationCols = 10;
        public const int FormationRows = 30;
        public const float UnitSpacing = 1.5f;
        public const float FormationCentreX = -30f;
        public const float FormationCentreZ = 0f;
        public const float GoalX = 30f;
        public const float GoalZ = 0f;

        public static void SpawnScenarioEntities(EntityManager em)
        {
            float halfW = (FormationCols - 1) * 0.5f * UnitSpacing;
            float halfH = (FormationRows - 1) * 0.5f * UnitSpacing;

            float gy = TerrainUtility.GetHeight(GoalX, GoalZ);
            var goal = new float3(GoalX, gy, GoalZ);

            for (int row = 0; row < FormationRows; row++)
            {
                float z = FormationCentreZ - halfH + row * UnitSpacing;
                for (int col = 0; col < FormationCols; col++)
                {
                    float x = FormationCentreX - halfW + col * UnitSpacing;
                    float y = TerrainUtility.GetHeight(x, z);
                    var spawn = new float3(x, y, z);

                    var entity = UnitFactory.Create(em, "Swordsman", spawn, Faction.Blue);
                    if (entity == Entity.Null) continue;

                    // Same destination for every unit. MoveCommandHelper
                    // attaches a per-unit NavPathRequest -- the path is
                    // solved once per unit, but every unit reaching the
                    // same tile hits the same NavFlowCache slab so the
                    // segmented flow build amortises across the crowd.
                    MoveCommandHelper.Execute(em, entity, goal);
                }
            }
        }
    }
}
