// Phase4TestSetup.cs
// task-112 M4 -- spawns the Phase4Test scenario: 50 Blue Swordsmen
// commanded across the cost field, with a scripted controller that
// places a wall mid-run + destroys it later. Validates:
//   * NavDirtyTiles populated when the wall stamp changes the cost field.
//   * IncrementalPortalRebuildSystem swaps the graph blob WITHOUT a full
//     restart (CCD-5 swap protocol).
//   * NavFlowCache slabs intersecting the dirty tiles are evicted.
//   * Units re-route via the M3 abstract-A* + segmented flow stack
//     without per-frame stalls.
//
// Auto-mode controller (Phase4ScriptedWallController) runs the place /
// destroy script in fixed-step sim time so the scenario is reproducible
// across runs. The architecture / task body call for an interactive
// "player places/destroys a wall" path; for autopilot we drive the same
// code path on a fixed schedule. A TODO comment is left here for the
// future manual-input polish (M7 hardening).
//
// Layout
//   * 50 swordsmen spawned in a 10-wide x 5-deep block centred at
//     world (-50, _, 0), commanded to (50, _, 0).
//   * Wall placement at the midpoint (0, _, 0) -- a 1x20 row of wall
//     instances perpendicular to the unit path. This is the building
//     stamp that dirties the central tiles.
//   * After tick 600 (~10 sim seconds at 60Hz), the wall entities are
//     destroyed -- the next BuildingCostStampSystem pass leaves their
//     cells clear, dirtying them again, and the pathing adapts.
//
// Location: Assets/Scripts/Bootstrap/Phase4TestSetup.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// 50-unit Phase 4 dynamic-world test. Spawns the swordsmen + the
    /// scripted wall controller singleton that drives the place/destroy
    /// timing.
    /// </summary>
    public static class Phase4TestSetup
    {
        public const int FormationCols = 10;
        public const int FormationRows = 5;
        public const float UnitSpacing = 1.5f;
        public const float FormationCentreX = -50f;
        public const float FormationCentreZ = 0f;
        public const float GoalX = 50f;
        public const float GoalZ = 0f;

        // Wall placement: 1 cell deep (X) x 20 cells wide (Z), centred at origin.
        // Z extent chosen so it actually crosses the unit path corridor.
        public const float WallCentreX = 0f;
        public const float WallCentreZ = 0f;
        public const int WallWidth = 1;
        public const int WallLength = 20;

        // Scripted timings (sim seconds, the controller ticks dt every OnUpdate).
        public const float PlaceWallAtSeconds = 5f;
        public const float DestroyWallAtSeconds = 10f;

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

                    MoveCommandHelper.Execute(em, entity, goal);
                }
            }

            // Create the scripted-wall controller singleton entity. The
            // Phase4ScriptedWallController ISystem checks for this and
            // drives the place/destroy timing.
            var controller = em.CreateEntity(typeof(Phase4ScriptedWallState));
            em.SetComponentData(controller, new Phase4ScriptedWallState
            {
                ElapsedSeconds = 0f,
                Phase = Phase4ScriptedWallState.PhaseWaitingToPlace,
                WallEntityCount = 0,
            });

            // Allocate the wall-entity buffer (separate from the state
            // because dynamic buffers can't sit on a singleton that's
            // accessed via SystemAPI.GetSingleton<T>() cleanly).
            em.AddBuffer<Phase4ScriptedWallEntity>(controller);
        }
    }

    /// <summary>
    /// Singleton state for the Phase4 scripted-wall controller. Lives
    /// on its own entity created by <see cref="Phase4TestSetup"/>.
    /// </summary>
    public struct Phase4ScriptedWallState : IComponentData
    {
        public float ElapsedSeconds;
        public byte Phase;
        public int WallEntityCount;

        public const byte PhaseWaitingToPlace = 0;
        public const byte PhasePlaced = 1;
        public const byte PhaseDestroyed = 2;
    }

    /// <summary>
    /// Buffer slot tracking one of the wall entities the controller
    /// spawned. Read on the destroy tick to remove them.
    /// </summary>
    public struct Phase4ScriptedWallEntity : IBufferElementData
    {
        public Entity Value;
    }
}
