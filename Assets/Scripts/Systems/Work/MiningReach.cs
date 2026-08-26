// MiningReach.cs
// Where a worker stands to mine, and how close it has to be. ONE definition,
// shared by GatheringSystem (the click path), MiningSystem (iron / veilsteel)
// and VeilstoneMiningSystem.
//
// Two bugs this exists to kill:
//
// 1. THE RANGES DISAGREED. GatheringSystem used 5 m measured CENTRE-to-centre;
//    the two mining systems used 2.5 m measured to the node SURFACE. Gathering
//    runs first and flips the miner straight to Gathering, so its 5 m won —
//    a worker already within 2.5 build cells of a node never took a step and
//    mined from there. That is the "they mine from about 2 squares away".
//
// 2. THE DESTINATION WAS THE NODE ITSELF. Every mining path set
//    DesiredDestination to the node's transform, which is the centre of a cell
//    the node has stamped IMPASSABLE. A worker can never arrive there, so it
//    ground against the node until the range check happened to catch it —
//    and FlowFollowSystem's line-of-sight test fails against an impassable
//    goal cell, so the approach fell back to the quantised flow field instead
//    of a straight line. That is the "get lost and circle around". Mining was
//    the last work system still doing this: combat, construction and repair
//    all aim at TargetGeometry.ApproachPoint.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Work
{
    public static class MiningReach
    {
        /// <summary>
        /// How far from the node's SURFACE a worker may stand and still mine.
        ///
        /// A node fills one 2 m build cell (radius 1 m). 1.5 m of surface reach
        /// puts the worker at most 2.5 m from the node centre — just over one
        /// cell — so it reads as standing against the node, not lobbing a pick
        /// from two squares off.
        ///
        /// Sized against the two things that have to fit inside it:
        ///   * the stand point below, 0.5 m off the face, and
        ///   * an orthogonally adjacent cell centre, exactly 1.0 m off the face
        /// plus one separation radius (1.5 m, SteeringSystem) of slack, so a
        /// second and third worker shoved off the ideal spot by the crowd still
        /// mine instead of jostling forever trying to close a gap physics
        /// forbids. Any tighter and only the first worker to arrive can work.
        /// </summary>
        public const float GatherRange = 1.5f;

        /// <summary>How far outside the node's footprint the walk destination
        /// sits. Small, because the worker should end up AT the node, not near
        /// it — the old behaviour of stopping wherever the range check first
        /// fired is what read as mining from two squares away.</summary>
        public const float StandOff = 0.5f;

        /// <summary>
        /// A stand slot with another unit this close counts as TAKEN.
        ///
        /// Units are never written into the pathing grids (that is the nav
        /// design's rule — crowds are resolved by steering), so static
        /// passability cannot see a worker parked on a slot. Without this test
        /// a second worker approaching from the same side aimed at the exact
        /// spot the first one occupies, pressed into its separation ring,
        /// walked on the spot, and was then declared arrived-but-out-of-range
        /// by the crowd rules — it stopped instead of stepping around.
        ///
        /// Just under SteeringSystem's 1.5 m separation radius: at that spacing
        /// the occupant physically cannot be displaced, so the slot is gone.
        /// </summary>
        private const float SlotTakenRadius = 1.2f;

        /// <summary>
        /// Where <paramref name="worker"/> should stand to mine
        /// <paramref name="node"/>, or false when there is nowhere legal.
        ///
        /// Preference order:
        ///   1. The straight-line approach point — the closest point on the
        ///      node's surface, pushed <see cref="StandOff"/> back out toward
        ///      the worker. This is what makes the walk a straight line.
        ///   2. The nearest ORTHOGONALLY adjacent build cell that is free.
        ///      Diagonal neighbours are deliberately not offered: their centres
        ///      sit ~1.83 m off the node face, outside <see cref="GatherRange"/>,
        ///      so a worker sent there would stand around never mining.
        ///
        /// False means the node is walled in — every approach is another node,
        /// a building or unwalkable ground. Callers must then divert to a node
        /// that IS minable rather than sending the worker to orbit this one.
        /// A solid ore patch is mined from the outside in: as perimeter nodes
        /// deplete, PassabilityBuildingSync unblocks their cells and the next
        /// ring becomes reachable.
        /// </summary>
        public static bool TryGetMiningStand(EntityManager em, Entity node, Entity worker,
            float3 fromPos, in NavSpatialHash hash, out float3 stand)
            => ResolveStand(em, node, worker, fromPos, in hash, requireFree: false, out stand);

        /// <summary>
        /// Like <see cref="TryGetMiningStand"/> but succeeds ONLY on a slot no
        /// other unit is standing on. Used when re-picking after a failed
        /// approach: retrying onto another occupied slot would just fail again,
        /// so "no free slot" has to be a definite answer the caller can act on
        /// rather than a walk that ends the same way.
        /// </summary>
        public static bool TryGetFreeMiningStand(EntityManager em, Entity node, Entity worker,
            float3 fromPos, in NavSpatialHash hash, out float3 stand)
            => ResolveStand(em, node, worker, fromPos, in hash, requireFree: true, out stand);

        /// <summary>Occupancy-blind overload for callers with no access to the
        /// spatial hash (the command helper runs outside a system). The mining
        /// systems refine the destination on the next tick.</summary>
        public static bool TryGetMiningStand(EntityManager em, Entity node, float3 fromPos,
            out float3 stand)
        {
            NavSpatialHash none = default;
            return ResolveStand(em, node, Entity.Null, fromPos, in none,
                requireFree: false, out stand);
        }

        private static bool ResolveStand(EntityManager em, Entity node, Entity worker,
            float3 fromPos, in NavSpatialHash hash, bool requireFree, out float3 stand)
        {
            stand = fromPos;
            if (node == Entity.Null || !em.Exists(node)) return false;
            if (!em.HasComponent<Unity.Transforms.LocalTransform>(node)) return false;

            var extent = TargetGeometry.Extent(em, node);
            float3 nodePos = em.GetComponentData<Unity.Transforms.LocalTransform>(node).Position;

            // 1. Straight-line approach — the best-looking walk when it's free.
            // Assigned to `stand` up front so the out value is usable even on
            // failure: a caller with nowhere better still gets a point OUTSIDE
            // the node's impassable cell, which is the part that mattered.
            float3 approach = extent.ApproachPoint(fromPos, StandOff);
            stand = approach;
            bool approachStandable = IsStandable(approach);
            if (approachStandable && !IsSlotTaken(em, in hash, approach, worker)) return true;

            // 2. Orthogonal neighbour cells, nearest first — the way AROUND a
            // worker already mining. Their centres are 2.83 m apart, well past
            // the separation radius, so all four can be worked at once.
            int2 nodeCell = BuildGrid.WorldToCell(nodePos);
            bool anyStandable = false, anyFree = false;
            float bestD2 = float.MaxValue, bestFreeD2 = float.MaxValue;
            float3 best = approach, bestFree = approach;

            for (int i = 0; i < 4; i++)
            {
                int2 n = nodeCell + Orthogonal[i];
                float2 c = BuildGrid.CellCentre(n);
                var candidate = new float3(c.x, nodePos.y, c.y);
                if (!IsStandable(candidate)) continue;

                float dx = candidate.x - fromPos.x;
                float dz = candidate.z - fromPos.z;
                float d2 = dx * dx + dz * dz;

                if (d2 < bestD2) { bestD2 = d2; best = candidate; anyStandable = true; }

                if (!IsSlotTaken(em, in hash, candidate, worker) && d2 < bestFreeD2)
                {
                    bestFreeD2 = d2;
                    bestFree = candidate;
                    anyFree = true;
                }
            }

            if (anyFree) { stand = bestFree; return true; }
            if (requireFree) return false;

            // Everything is taken. Fall back to a legal-but-crowded slot rather
            // than refusing — the crowd rules will settle the worker nearby.
            if (approachStandable) { stand = approach; return true; }
            if (anyStandable) { stand = best; return true; }
            return false;
        }

        /// <summary>True when this node can be mined from somewhere — the
        /// reachability test callers use to decide whether to divert.
        ///
        /// Deliberately occupancy-BLIND: a worker standing on a slot is
        /// transient, and diverting a whole assignment away from a good node
        /// because someone is briefly on it would be worse than waiting. Only
        /// permanent geometry (walled in by other nodes, buildings, unwalkable
        /// ground) makes a node unminable.</summary>
        public static bool IsMinable(EntityManager em, Entity node, float3 fromPos)
            => TryGetMiningStand(em, node, fromPos, out _);

        /// <summary>Is another unit parked on this slot? Probes the 3x3
        /// spatial-hash ring around it; a default/unbuilt hash reports free.</summary>
        private static bool IsSlotTaken(EntityManager em, in NavSpatialHash hash,
            float3 slot, Entity worker)
        {
            if (!hash.Map.IsCreated) return false;

            NavSpatialHash.WorldToCell(in slot, hash.CellSize, out int cx, out int cz);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = NavSpatialHash.PackKey(cx + dx, cz + dz);
                    if (!hash.Map.TryGetFirstValue(key, out Entity other, out var it)) continue;
                    do
                    {
                        if (other == worker) continue;
                        if (!em.HasComponent<Unity.Transforms.LocalTransform>(other)) continue;
                        if (em.HasComponent<DeathAnimationState>(other)) continue;

                        float3 p = em.GetComponentData<Unity.Transforms.LocalTransform>(other).Position;
                        float ox = p.x - slot.x, oz = p.z - slot.z;
                        if (ox * ox + oz * oz <= SlotTakenRadius * SlotTakenRadius) return true;
                    } while (hash.Map.TryGetNextValue(out other, ref it));
                }
            }
            return false;
        }

        private static readonly int2[] Orthogonal =
        {
            new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1),
        };

        /// <summary>Can a worker stand here? Shared with every other approach
        /// system — see NavGridQuery.IsWorldStandable.</summary>
        private static bool IsStandable(float3 world) => NavGridQuery.IsWorldStandable(world);
    }
}
