// File: Assets/Scripts/Systems/Movement/WallDoorAccessSystem.cs
// Walkable-rampart access via doors (replaces the navmesh ramp, which couldn't
// reliably connect the ground and deck navmesh islands). Hubs / towers / gates
// have a ground-level door and a deck-level door. A unit ordered onto a wall
// walks (ground navmesh) to the nearest structure's GROUND door, then emerges at
// that structure's DECK door and continues to its deck destination. Ordered back
// to the ground, it reverses. The teleport bridges the two disconnected navmesh
// islands deterministically. See docs/Design/Age_1_Alanthor.md § Walkable Ramparts.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using TheWaningBorder.World.Terrain;

/// <summary>Tracks a unit mid-transition between the ground and a wall deck via a
/// structure's doors. Phase 1 = ascending (heading to a ground door); Phase 2 =
/// descending (heading to a deck door). FinalDest is the original order target,
/// restored once the unit emerges from the far door.</summary>
public struct WallAccessState : IComponentData
{
    public byte Phase;        // 1 = ascending, 2 = descending
    public Entity Structure;  // the hub/tower/gate whose doors are being used
    public float3 FinalDest;
}

namespace TheWaningBorder.Systems.Movement
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial class WallDoorAccessSystem : SystemBase
    {
        private const float Elevated     = 2.0f;  // (y - terrainY) above this ⇒ on a wall deck
        private const float DeckY        = 4.0f;
        private const float WallW        = 9.0f;
        private const float DeckWalkHalf = 4.0f;
        private const float ArriveDoor   = 2.5f;   // within this of a door ⇒ pass through it

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<WallTag>()));
        }

        // Door positions in the structure's local frame (inner side = local -X).
        // Ground door sits just outside the inner face on the ground; deck door
        // sits on the deck near the inner edge.
        private static float3 GroundDoorLocal => new float3(-(WallW * 0.5f + 0.5f), 0f, 0f);
        private static float3 DeckDoorLocal   => new float3(-(DeckWalkHalf - 1.0f), DeckY, 0f);

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // --- Access structures: hubs / towers / gates (not plain instances). ---
            var sQ = GetEntityQuery(ComponentType.ReadOnly<WallTag>(),
                                    ComponentType.ReadOnly<LocalTransform>());
            using var sEnts = sQ.ToEntityArray(Allocator.Temp);
            var accEnts = new List<Entity>(sEnts.Length);
            var accPos  = new List<float3>(sEnts.Length);
            var accRot  = new List<quaternion>(sEnts.Length);
            for (int i = 0; i < sEnts.Length; i++)
            {
                var e = sEnts[i];
                bool isAccess = em.HasComponent<WallHubTag>(e)
                    || em.HasComponent<WallTowerTag>(e) || em.HasComponent<WallGateTag>(e);
                if (!isAccess) continue;
                var lt = em.GetComponentData<LocalTransform>(e);
                accEnts.Add(e); accPos.Add(lt.Position); accRot.Add(lt.Rotation);
            }
            if (accEnts.Count == 0) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            var uQ = GetEntityQuery(ComponentType.ReadOnly<UnitTag>(),
                                    ComponentType.ReadWrite<LocalTransform>(),
                                    ComponentType.ReadWrite<DesiredDestination>());
            using var uEnts = uQ.ToEntityArray(Allocator.Temp);
            var order = new int[uEnts.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => uEnts[a].Index.CompareTo(uEnts[b].Index));

            for (int oi = 0; oi < order.Length; oi++)
            {
                var e = uEnts[order[oi]];
                var lt = em.GetComponentData<LocalTransform>(e);
                float3 pos = lt.Position;
                float terrainY = TerrainUtility.GetHeight(pos.x, pos.z);
                bool unitElevated = (pos.y - terrainY) > Elevated;

                // --- Mid-transition: walk to a door, then teleport through it. ---
                if (em.HasComponent<WallAccessState>(e))
                {
                    var st = em.GetComponentData<WallAccessState>(e);
                    if (!em.Exists(st.Structure))
                    {
                        ecb.RemoveComponent<WallAccessState>(e);
                        continue;
                    }
                    var sLt = em.GetComponentData<LocalTransform>(st.Structure);
                    float3 ground = sLt.Position + math.mul(sLt.Rotation, GroundDoorLocal);
                    float3 deck   = sLt.Position + math.mul(sLt.Rotation, DeckDoorLocal);

                    float3 nearDoor = (st.Phase == 1) ? ground : deck;
                    float ddx = pos.x - nearDoor.x, ddz = pos.z - nearDoor.z;
                    if (ddx * ddx + ddz * ddz <= ArriveDoor * ArriveDoor)
                    {
                        // Emerge from the far door (teleport bridges the islands).
                        float3 farDoor = (st.Phase == 1) ? deck : ground;
                        if (st.Phase == 2) farDoor.y = TerrainUtility.GetHeight(farDoor.x, farDoor.z);
                        lt.Position = farDoor;
                        em.SetComponentData(e, lt);
                        em.SetComponentData(e, new DesiredDestination { Position = st.FinalDest, Has = 1 });
                        ecb.RemoveComponent<WallAccessState>(e);
                    }
                    continue;
                }

                // --- Not transitioning: detect an order that crosses ground↔deck. ---
                if (!em.HasComponent<DesiredDestination>(e)) continue;
                var dd = em.GetComponentData<DesiredDestination>(e);
                if (dd.Has == 0) continue;

                float destTerrainY = TerrainUtility.GetHeight(dd.Position.x, dd.Position.z);
                bool destElevated = (dd.Position.y - destTerrainY) > Elevated;

                if (destElevated == unitElevated) continue; // same level — normal movement

                // Find the nearest access structure and route through its near door.
                int best = -1; float bestSq = float.MaxValue;
                for (int s = 0; s < accPos.Count; s++)
                {
                    float dx = pos.x - accPos[s].x, dz = pos.z - accPos[s].z;
                    float d = dx * dx + dz * dz;
                    if (d < bestSq) { bestSq = d; best = s; }
                }
                if (best < 0) continue;

                byte phase = (byte)(destElevated && !unitElevated ? 1 : 2); // 1 ascend, 2 descend
                float3 doorTarget = (phase == 1)
                    ? accPos[best] + math.mul(accRot[best], GroundDoorLocal)
                    : accPos[best] + math.mul(accRot[best], DeckDoorLocal);

                ecb.AddComponent(e, new WallAccessState
                {
                    Phase = phase,
                    Structure = accEnts[best],
                    FinalDest = dd.Position,
                });
                em.SetComponentData(e, new DesiredDestination { Position = doorTarget, Has = 1 });
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
