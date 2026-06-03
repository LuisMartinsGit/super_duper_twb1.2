// Phase5TestSetup.cs
// task-112 M5 -- spawns the Phase5Test scenario: an Alanthor wall ring
// (4 hubs, 4 segments, 1 stair = an extra hub, 1 gatehouse =
// 5-instance gate region) at the world origin with 10 Blue (friendly)
// + 10 Red (enemy) swordsmen south of the wall. Both squads issued an
// attack-move to a point INSIDE the ring.
//
// Expected behaviour:
//   * Blue units path through the south gate (auto-opens for friendly
//     proximity per GateStateSystem) AND/OR climb the stair hub up to
//     the rampart and patrol it.
//   * Red units are REJECTED at the south gate (owner mismatch -> R4
//     backstop in LayerTransitionSystem). The A* should re-route them
//     around the ring; if no route exists they stop short.
//
// Layout (origin centred):
//
//             +Z (north)
//                 |
//          [Hub N]----[Hub NE]
//             |          |
//          (seg)      (seg)
//             |          |
//          [Hub W]    [Hub E]
//             |          |
//          (seg)      (seg)
//             |          |
//          [Hub SW]   [Hub SE]
//                \  /
//             [Gate Hub S]
//                 |
//             [10 Blue, 10 Red south of here]
//                 v
//             -Z (south)
//
// The stair = the Hub-W (left side of the ring); the gate = a
// 5-instance gate region centred on the south segment.
//
// Location: Assets/Scripts/Bootstrap/Phase5TestSetup.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// 10v10 Phase 5 wall-and-gate scenario. Builds an Alanthor wall
    /// ring with a stair + south gate; spawns friendly/enemy squads
    /// outside the ring and orders both inside.
    /// </summary>
    public static class Phase5TestSetup
    {
        // Ring layout (cubes, world units).
        //
        //   Width  = 5 cubes (RingCols), CubeSpacing apart -> ~16 m wide
        //   Height = 6 cubes (RingRows), CubeSpacing apart -> ~20 m tall
        //   Gate sits at the bottom row centre column (Z = -10).
        //
        //   ASCII:   R R R R R    (top, z = +10)
        //            R       R
        //            R       R
        //            R       R
        //            R       R
        //            R R G R R    (bottom, z = -10)
        public const int RingCols = 5;
        public const int RingRows = 6;
        public const float CubeSpacing = 4f;   // 5x5 cost-field stamp overlaps neighbours -> zero gaps

        // Squad spawn south of the ring.
        public const float SquadSpawnZ = -30f;
        public const float UnitSpacing = 1.6f;
        public const int SquadSize = 10;
        // Inside-the-ring goal.
        public const float GoalX = 0f;
        public const float GoalZ = 0f;

        public static void SpawnScenarioEntities(EntityManager em)
        {
            // ── Procedural cube ring. One entity per cube, tagged either
            // as a wall (Red) or a gate (Green = bottom row centre). The
            // cost-field stamp + portal detector pick these up the same
            // way they would AlanthorWall instances.
            float ringMinX = -((RingCols - 1) * 0.5f) * CubeSpacing;
            float ringMinZ = -((RingRows - 1) * 0.5f) * CubeSpacing;

            int gateCol = RingCols / 2;   // middle column on the bottom row
            int gateRow = 0;              // bottom row (low z)

            for (int row = 0; row < RingRows; row++)
            for (int col = 0; col < RingCols; col++)
            {
                // Perimeter only -- no cubes in the interior.
                bool onBorder = row == 0 || row == RingRows - 1
                             || col == 0 || col == RingCols - 1;
                if (!onBorder) continue;

                float x = ringMinX + col * CubeSpacing;
                float z = ringMinZ + row * CubeSpacing;
                float3 pos = WorldHeight(new float3(x, 0f, z));

                bool isGate = (row == gateRow && col == gateCol);
                SpawnRingCube(em, pos, isGate, Faction.Blue);
            }

            // ── Debug cube visuals (red walls, green gates). These also
            // visually replace any prefab presentation -- the cube
            // entities deliberately don't carry a PresentationId so
            // PresentationSpawnSystem skips them.
            SpawnWallAndGateDebugVisuals(em);

            // ── 10 Blue + 10 Red south of the ring, ordered to (0,_,0). ─
            float halfRow = (SquadSize - 1) * 0.5f * UnitSpacing;
            float3 goal = WorldHeight(new float3(GoalX, 0, GoalZ));

            for (int i = 0; i < SquadSize; i++)
            {
                float x = -halfRow + i * UnitSpacing;
                float3 spawn = WorldHeight(new float3(x - 12f, 0, SquadSpawnZ));
                var e = UnitFactory.Create(em, "Swordsman", spawn, Faction.Blue);
                if (e != Entity.Null) MoveCommandHelper.Execute(em, e, goal);
            }

            for (int i = 0; i < SquadSize; i++)
            {
                float x = -halfRow + i * UnitSpacing;
                float3 spawn = WorldHeight(new float3(x + 12f, 0, SquadSpawnZ));
                var e = UnitFactory.Create(em, "Swordsman", spawn, Faction.Red);
                if (e != Entity.Null) MoveCommandHelper.Execute(em, e, goal);
            }
        }

        // Spawn a single wall/gate cube entity. Carries the minimum
        // component set the cost-field stamp + gate machinery need:
        //   WallTag + BuildingTag + LocalTransform + BuildingSize 5x5
        //   FactionTag (owner)
        // Gate cubes additionally carry WallGateTag + WallGateRegionTag
        // so WallPortalDetectionSystem emits a conditional gate portal
        // for them and AbstractPathfinder.SolveGated honours the owner.
        //
        // PresentationId is deliberately NOT added so the prefab
        // visualiser never instantiates a model -- the cubes spawned
        // by SpawnWallAndGateDebugVisuals are the only visual.
        private static void SpawnRingCube(EntityManager em, float3 position,
            bool isGate, Faction owner)
        {
            var e = em.CreateEntity(
                typeof(LocalTransform),
                typeof(FactionTag),
                typeof(BuildingTag),
                typeof(BuildingSize),
                typeof(WallTag));

            em.SetComponentData(e, LocalTransform.FromPosition(position));
            em.SetComponentData(e, new FactionTag { Value = owner });
            em.SetComponentData(e, new BuildingTag { IsBase = 0 });
            em.SetComponentData(e, new BuildingSize
            {
                Width = 5,
                Height = 5,
            });

            if (isGate)
            {
                em.AddComponentData(e, new WallGateTag());
                em.AddComponentData(e, new WallGateRegionTag());
                em.AddComponentData(e, new WallGateGroup { Leader = e });
                em.AddComponentData(e, new WallGateState
                {
                    IsOpen = 0,
                    RecheckTimer = 0f,
                });
            }
        }

        // Snap a world position's y to the terrain height (matches every
        // other Phase scenario's setup convention).
        private static float3 WorldHeight(float3 p)
        {
            p.y = TerrainUtility.GetHeight(p.x, p.z);
            return p;
        }

        // Replace every wall + gate prefab visual with a colour-coded
        // cube. Red = wall (impassable). Green = gate (conditional,
        // owner-gated). The original prefab models are visually too
        // large to coexist with debug cubes; we strip the PresentationId
        // component BEFORE the PresentationSpawnSystem gets a chance to
        // instantiate them, so the cubes are the only thing on screen.
        //
        // Cubes are sized to match the 5x5 cost-field stamp (~5 m square)
        // and stand 4 m tall, oriented to the wall's local rotation. No
        // collider -- nav stack reads the cost field, not Unity physics.
        // Parented under a single Phase5WallDebugVisuals GameObject so
        // they all clear when the scene unloads.
        private static void SpawnWallAndGateDebugVisuals(EntityManager em)
        {
            var wallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<WallTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            int wallCount = wallQuery.CalculateEntityCount();
            if (wallCount == 0) { wallQuery.Dispose(); return; }

            var wallEntities = wallQuery.ToEntityArray(Allocator.Temp);
            var root = new GameObject("Phase5WallDebugVisuals");

            for (int i = 0; i < wallEntities.Length; i++)
            {
                var e = wallEntities[i];
                var xf = em.GetComponentData<LocalTransform>(e);

                // Hide the prefab visual: stripping PresentationId before
                // the spawn system polls means no GameObject is ever
                // instantiated for this entity. Safe because the nav
                // stack reads BuildingTag / WallTag / cost field, not
                // PresentationId, so wall blocking still works.
                if (em.HasComponent<PresentationId>(e))
                    em.RemoveComponent<PresentationId>(e);

                bool isGate = em.HasComponent<WallGateTag>(e);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = isGate ? $"GateCube_{i}" : $"WallCube_{i}";
                cube.transform.SetParent(root.transform, worldPositionStays: false);
                cube.transform.position = new Vector3(
                    xf.Position.x,
                    xf.Position.y + 2.0f,
                    xf.Position.z);
                cube.transform.rotation = xf.Rotation;
                // Visual cube size == CubeSpacing -> adjacent cubes share
                // edges (touch, no gap, no overlap). The 7x7 cost-field
                // stamp is intentionally wider than this so navigation
                // sees an overlapping wall even when the visuals touch
                // perfectly.
                cube.transform.localScale = new Vector3(CubeSpacing, 4.0f, CubeSpacing);

                var rend = cube.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material.color = isGate
                        ? new Color(0.2f, 1.0f, 0.3f)   // bright green = gate
                        : new Color(0.85f, 0.15f, 0.15f); // bright red = wall
                }
                var col = cube.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }

            wallEntities.Dispose();
            wallQuery.Dispose();
        }

    }
}
