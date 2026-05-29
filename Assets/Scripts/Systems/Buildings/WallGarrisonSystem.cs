// File: Assets/Scripts/Systems/Buildings/WallGarrisonSystem.cs
// Walkable-rampart garrison (W4). When units are ordered onto a wall and end up
// standing on a deck (elevated well above the terrain), spread them into ranks
// along the OUTER parapet edge of the nearest wall module so they read as manning
// the wall. Ordered back to the ground (no longer elevated), the garrison state
// clears and they move normally. See docs/Design/Age_1_Alanthor.md § Walkable
// Ramparts, Stairs & Garrison.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using TheWaningBorder.World.Terrain;

/// <summary>Marks a unit currently garrisoning a wall deck; holds its assigned
/// outer-edge slot so it holds position there.</summary>
public struct WallGarrisonState : IComponentData
{
    public float3 Slot;
}

namespace TheWaningBorder.Systems.Buildings
{
    /// <summary>
    /// Stations on-wall units in ranks along the outer parapet. Runs after
    /// movement so it sees up-to-date positions. Deterministic: wall modules and
    /// units are visited in a stable entity-id order, so the same lockstep inputs
    /// produce the same slots on every peer.
    ///
    /// v1 scope / known tuning points (editor-verify):
    /// - O(units × wall-modules) per tick — gate to elevated units only if it
    ///   shows up in profiling.
    /// - Re-derives lanes each tick; a unit leaving mid-rank can reshuffle others.
    /// - May contend with battalion-follow formation while a unit is en route;
    ///   garrison takes over only once the unit is actually on a deck.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Movement.MovementSystem))]
    public partial class WallGarrisonSystem : SystemBase
    {
        private const float ElevatedThreshold = 2.0f; // (pos.y - terrainY) above this ⇒ on a wall deck
        private const float DeckY        = 4f;
        private const float DeckWalkHalf = 4f;
        private const float EdgeInset    = 0.7f;       // stand this far inside the outer parapet
        private const float FileSpacing  = 1.2f;
        private const float RankSpacing  = 1.2f;
        private const float ModuleLen    = 4f;
        private const float ArriveDist   = 1.2f;

        protected override void OnCreate()
        {
            // Only run when at least one wall exists in the world.
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<WallTag>()));
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // --- Garrisonable wall modules (instances / towers / gates). Hubs are
            //     stair cores and segments are data-only, so skip both. ---
            var modQ = GetEntityQuery(ComponentType.ReadOnly<WallTag>(),
                                      ComponentType.ReadOnly<LocalTransform>());
            using var modEnts = modQ.ToEntityArray(Allocator.Temp);
            var modPos = new List<float3>(modEnts.Length);
            var modRot = new List<quaternion>(modEnts.Length);
            for (int i = 0; i < modEnts.Length; i++)
            {
                var e = modEnts[i];
                if (em.HasComponent<WallHubTag>(e) || em.HasComponent<WallSegmentTag>(e)) continue;
                bool garrisonable = em.HasComponent<WallInstanceTag>(e)
                    || em.HasComponent<WallTowerTag>(e) || em.HasComponent<WallGateTag>(e);
                if (!garrisonable) continue;
                var lt = em.GetComponentData<LocalTransform>(e);
                modPos.Add(lt.Position);
                modRot.Add(lt.Rotation);
            }
            if (modPos.Count == 0) return;

            // --- Units in a deterministic (entity-id) order ---
            var unitQ = GetEntityQuery(ComponentType.ReadOnly<UnitTag>(),
                                       ComponentType.ReadOnly<LocalTransform>());
            using var unitEnts = unitQ.ToEntityArray(Allocator.Temp);
            var order = new int[unitEnts.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => unitEnts[a].Index.CompareTo(unitEnts[b].Index));

            var laneCount = new int[modPos.Count];
            int perRank = math.max(1, (int)(ModuleLen / FileSpacing)); // ~3 files per rank

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int oi = 0; oi < order.Length; oi++)
            {
                var e = unitEnts[order[oi]];
                var lt = em.GetComponentData<LocalTransform>(e);
                float3 pos = lt.Position;
                float terrainY = TerrainUtility.GetHeight(pos.x, pos.z);
                bool elevated = (pos.y - terrainY) > ElevatedThreshold;

                if (!elevated)
                {
                    // Descended to the ground (or never on a wall) → dissolve garrison.
                    if (em.HasComponent<WallGarrisonState>(e))
                        ecb.RemoveComponent<WallGarrisonState>(e);
                    continue;
                }

                // Nearest garrisonable module.
                int best = -1; float bestSq = float.MaxValue;
                for (int m = 0; m < modPos.Count; m++)
                {
                    float dx = pos.x - modPos[m].x, dz = pos.z - modPos[m].z;
                    float d = dx * dx + dz * dz;
                    if (d < bestSq) { bestSq = d; best = m; }
                }
                if (best < 0) continue;

                int lane = laneCount[best]++;
                int rank = lane / perRank;
                int file = lane % perRank;

                float3 outward = math.mul(modRot[best], new float3(1f, 0f, 0f)); // +X = outer face
                float3 along   = math.mul(modRot[best], new float3(0f, 0f, 1f)); // +Z = along the wall

                float3 slot = modPos[best]
                    + outward * (DeckWalkHalf - EdgeInset - rank * RankSpacing)
                    + along   * ((file - (perRank - 1) * 0.5f) * FileSpacing);
                slot.y = DeckY;

                if (!em.HasComponent<WallGarrisonState>(e))
                    ecb.AddComponent(e, new WallGarrisonState { Slot = slot });
                else
                    em.SetComponentData(e, new WallGarrisonState { Slot = slot });

                if (em.HasComponent<DesiredDestination>(e))
                {
                    float sdx = pos.x - slot.x, sdz = pos.z - slot.z;
                    bool arrived = (sdx * sdx + sdz * sdz) <= ArriveDist * ArriveDist;
                    em.SetComponentData(e, new DesiredDestination
                    {
                        Position = slot,
                        Has = (byte)(arrived ? 0 : 1),
                    });

                    if (arrived)
                    {
                        // Hold position facing outward over the parapet.
                        var face = outward; face.y = 0f;
                        if (math.lengthsq(face) > 1e-4f)
                        {
                            lt.Rotation = quaternion.LookRotationSafe(math.normalize(face), math.up());
                            em.SetComponentData(e, lt);
                        }
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
