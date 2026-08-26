// WallClimbTestSetup.cs
// Wall-climb / rampart-garrison test scenario.
//
// Builds a straight east-west wall whose centre piece is a TOWER (the
// friendly climb access), spawns a Blue squad south of it, and auto-issues a
// layered move onto the WALL TOP so the squad walks to the tower, climbs onto
// the deck, and spreads along the wall top. The same order is what a player
// triggers by right-clicking the wall with units selected
// (RTSInputManager -> CommandRouter.IssueLayeredMove).
//
// Chain under test (AoE4-style layered movement):
//   * CostFieldStampSystem stamps every wall piece ground=impassable /
//     deck(rampart)=walkable.
//   * LayeredMoveSystem walks each ordered unit to the nearest friendly
//     access point (tower / gate), LERPs it up to the deck, then moves it
//     freely on the wall top. Enemy units can only use a breach ramp.
//   * UnitIntegratorSystem (layer-aware) walks deck units along wall-top
//     cells only.
//   * Right-clicking the ground issues a layered move back down (route to
//     access -> LERP down -> ground move).
//
// Debug cubes: red = wall instance, cyan = tower (access). (No PresentationId,
// so the prefab visualiser is skipped — mirrors Phase5TestSetup.)

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Straight wall + south stair. Spawns a Blue squad and auto-garrisons it
    /// onto the wall so the climb / rampart / parapet chain runs on load.
    /// </summary>
    public static class WallClimbTestSetup
    {
        public const int WallCubes = 7;        // wall instances along +X
        public const float CubeSpacing = 4f;   // 7x7 stamp overlaps neighbours -> sealed
        public const float WallZ = 0f;

        public const int SquadSize = 10;
        public const float SquadSpawnZ = -22f;
        public const float UnitSpacing = 1.6f;

        public static void SpawnScenarioEntities(EntityManager em)
        {
            float wallMinX = -((WallCubes - 1) * 0.5f) * CubeSpacing;

            int centreCol = WallCubes / 2;

            // A straight wall of instances; the centre piece is a TOWER — the
            // friendly access point units climb (AoE4: towers + gates are the
            // only gated access). The rest are solid wall instances, so there
            // is no ground gap: units must use the tower to get atop.
            for (int col = 0; col < WallCubes; col++)
            {
                float x = wallMinX + col * CubeSpacing;
                float3 pos = WorldHeight(new float3(x, 0f, WallZ));
                SpawnWallCube(em, pos, isTower: col == centreCol, Faction.Blue);
            }

            SpawnDebugVisuals(em);

            // ── Blue squad south. Auto-issue a layered move onto the WALL TOP
            // so the climb -> rampart-walk chain self-demonstrates on load.
            // Each unit targets a distinct point along the wall top so they
            // spread out instead of stacking. (Right-click the ground to send
            // them back down; right-click the wall top to reposition.) ──
            float halfRow = (SquadSize - 1) * 0.5f * UnitSpacing;
            float wallSpan = (WallCubes - 1) * CubeSpacing;
            for (int i = 0; i < SquadSize; i++)
            {
                float x = -halfRow + i * UnitSpacing;
                float3 spawn = WorldHeight(new float3(x, 0, SquadSpawnZ));
                var u = UnitFactory.Create(em, "Swordsman", spawn, Faction.Blue);
                if (u == Entity.Null) continue;

                // Spread targets across the wall top (x along the wall, z = WallZ).
                float tx = wallMinX + (SquadSize <= 1 ? wallSpan * 0.5f
                    : wallSpan * (i / (float)(SquadSize - 1)));
                float3 wallTop = new float3(tx, LayerTransitionSystem.DeckY, WallZ);
                CommandRouter.IssueLayeredMove(em, u, wallTop,
                    NavLayerIndex.LayerRampart, CommandSource.LocalPlayer);
            }
        }

        // Plain cubes are wall instances (WallInstanceTag); the tower cube is
        // the friendly climb access (WallTowerTag). Both carry the 5x5
        // BuildingSize the wall cost-field stamp keys off + FactionTag owner.
        private static Entity SpawnWallCube(EntityManager em, float3 position,
            bool isTower, Faction owner)
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
            em.SetComponentData(e, new BuildingSize { Width = 5, Height = 5 });

            if (isTower)
                em.AddComponentData(e, new WallTowerTag());
            else
                em.AddComponentData(e, new WallInstanceTag());

            return e;
        }

        private static float3 WorldHeight(float3 p)
        {
            p.y = TerrainUtility.GetHeight(p.x, p.z);
            return p;
        }

        private static void SpawnDebugVisuals(EntityManager em)
        {
            var wallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<WallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            int wallCount = wallQuery.CalculateEntityCount();
            if (wallCount == 0) { wallQuery.Dispose(); return; }

            var wallEntities = wallQuery.ToEntityArray(Allocator.Temp);
            var root = new GameObject("WallClimbDebugVisuals");

            for (int i = 0; i < wallEntities.Length; i++)
            {
                var e = wallEntities[i];
                var xf = em.GetComponentData<LocalTransform>(e);

                if (em.HasComponent<PresentationId>(e))
                    em.RemoveComponent<PresentationId>(e);

                bool isTower = em.HasComponent<WallTowerTag>(e);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = isTower ? $"Tower_{i}" : $"WallCube_{i}";
                cube.transform.SetParent(root.transform, worldPositionStays: false);
                cube.transform.position = new Vector3(
                    xf.Position.x, xf.Position.y + 2.0f, xf.Position.z);
                cube.transform.rotation = xf.Rotation;
                cube.transform.localScale = new Vector3(CubeSpacing, 4.0f, CubeSpacing);

                var rend = cube.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material.color = isTower
                        ? new Color(0.2f, 0.8f, 1.0f)    // cyan = tower (access)
                        : new Color(0.85f, 0.15f, 0.15f); // red = wall
                }
                // Keep the box collider so the wall is raycast-clickable, and
                // link the cube to its wall entity so right-clicking it
                // resolves to the wall (the AoE4 "click the wall" garrison
                // path in RTSInputManager).
                cube.AddComponent<TheWaningBorder.Core.EntityReference>().Entity = e;
            }

            wallEntities.Dispose();
            wallQuery.Dispose();
        }
    }
}
