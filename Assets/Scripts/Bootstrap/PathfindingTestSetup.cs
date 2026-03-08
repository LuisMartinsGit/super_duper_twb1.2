// PathfindingTestSetup.cs
// Spawns 400 units and obstacle buildings for pathfinding stress testing
// Location: Assets/Scripts/Bootstrap/PathfindingTestSetup.cs

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns a pathfinding test scenario: 200 Swordsmen per faction
    /// with Barracks obstacles in the center creating chokepoints.
    /// </summary>
    public static class PathfindingTestSetup
    {
        public static void SpawnTestScenario()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Spawn 200 Swordsmen for Blue (left side) in a 20x10 grid
            SpawnUnitGrid(em, "Swordsman", Faction.Blue, new float3(-40f, 0f, 0f), 20, 10, 2f);

            // Spawn 200 Swordsmen for Red (right side) in a 20x10 grid
            SpawnUnitGrid(em, "Swordsman", Faction.Red, new float3(40f, 0f, 0f), 20, 10, 2f);

            // Place obstacle walls in the center with gaps for chokepoints
            SpawnObstacleWall(em, 0f);
            SpawnObstacleWall(em, 4f);

            Debug.Log("[PathfindingTestSetup] Spawned 400 units + obstacle walls");
        }

        private static void SpawnUnitGrid(EntityManager em, string unitId, Faction faction,
            float3 center, int cols, int rows, float spacing)
        {
            float startX = center.x - (cols - 1) * spacing * 0.5f;
            float startZ = center.z - (rows - 1) * spacing * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var pos = new float3(startX + c * spacing, 0f, startZ + r * spacing);
                    UnitFactory.Create(em, unitId, pos, faction);
                }
            }
        }

        /// <summary>
        /// Spawn a row of Barracks along the X axis with gaps for chokepoints.
        /// Row spans z=-18 to z=+18 with a gap every 8 buildings.
        /// </summary>
        private static void SpawnObstacleWall(EntityManager em, float xPos)
        {
            float spacing = 2.5f;
            int buildingsPerSegment = 6;
            float gapSize = 4f;

            // 3 segments with 2 gaps between them
            float[] segmentStarts = { -18f, -4f, 10f };

            foreach (float segStart in segmentStarts)
            {
                for (int i = 0; i < buildingsPerSegment; i++)
                {
                    float z = segStart + i * spacing;
                    var pos = new float3(xPos, 0f, z);
                    // Alternate faction so both sides see them
                    Barracks.Create(em, pos, Faction.Blue);
                }
            }
        }
    }
}
