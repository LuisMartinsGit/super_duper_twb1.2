// ResourcePatchFill.cs
// Shared patch geometry for every resource that spawns in clusters (iron,
// veilstone). Set-level code, so it sits at the ResourceNodes branch root
// rather than inside one node's folder.
//
// A patch is a SOLID BLOCK of build cells — no gaps between nodes.
//
// Every patch spawner used to do the same wrong thing: scatter positions on a
// continuous lattice (hex slots + jitter, or a random disc), and only then let
// the node factory snap each position to its build-cell centre. Nothing tied
// the scatter to the 2 m grid, so the snap simultaneously
//   * tore holes in the patch — two picks landing either side of an empty
//     cell, leaving bald ground between ore, and
//   * collapsed nodes — two picks snapping onto the SAME cell, where the
//     second silently stacked inside the first (or, for veilstone's
//     CreateOrMerge path, merged away entirely)
// which meant a marker's authored node count was never the number of nodes the
// map actually got.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static class ResourcePatchFill
    {
        /// <summary>
        /// Collect up to <paramref name="nodeCount"/> contiguous build-cell
        /// centres around <paramref name="center"/>, terrain height applied,
        /// into <paramref name="outPositions"/>.
        ///
        /// Cells are taken in order of true distance from the patch centre.
        /// That ordering is what guarantees contiguity: every cell except the
        /// centre has a 4-neighbour strictly closer to the centre, so that
        /// neighbour was already taken. The filled set can therefore never be
        /// disconnected or hollow.
        ///
        /// Cells whose ground is impassable TERRAIN (cliff, water, NoWalk
        /// paint) are skipped — a node there would be unreachable. Obstacle
        /// blocking is deliberately NOT consulted: forest/rock obstacles are
        /// cleared around patch centres by the callers, and treating them as
        /// occupied here would punch holes in the block for props that are
        /// about to be deleted.
        /// </summary>
        /// <param name="raggedEdge">
        /// Shuffle cells that are EQUIDISTANT from the centre, so the outer
        /// edge of a partially-filled ring varies with the seed. Safe by
        /// construction: reordering within one distance tier cannot break the
        /// containment argument above, because every cell of a strictly
        /// smaller distance is already placed either way.
        /// </param>
        public static void CollectCells(EntityManager em, float3 center, int nodeCount,
            bool raggedEdge, ref Random random, NativeList<float3> outPositions)
        {
            outPositions.Clear();
            if (nodeCount <= 0) return;

            int2 centreCell = BuildGrid.WorldToCell(center);

            // Search square big enough to hold nodeCount cells with headroom
            // for any the terrain rejects: (2r+1)^2 >= nodeCount * 2.
            int radius = math.max(1, (int)math.ceil(math.sqrt(nodeCount * 2f) * 0.5f) + 1);
            int side = radius * 2 + 1;

            var cells = new NativeList<int2>(side * side, Allocator.Temp);
            var keys = new NativeList<float>(side * side, Allocator.Temp);
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    cells.Add(new int2(centreCell.x + dx, centreCell.y + dz));
                    keys.Add(dx * dx + dz * dz);
                }
            }

            // Insertion sort by distance, tie-broken by (x, z) so every
            // lockstep client builds the identical patch.
            for (int i = 1; i < cells.Length; i++)
            {
                var c = cells[i];
                float k = keys[i];
                int j = i - 1;
                while (j >= 0 && IsAfter(keys[j], cells[j], k, c))
                {
                    cells[j + 1] = cells[j];
                    keys[j + 1] = keys[j];
                    j--;
                }
                cells[j + 1] = c;
                keys[j + 1] = k;
            }

            if (raggedEdge) ShuffleWithinDistanceTiers(cells, keys, ref random);

            var taken = CollectOccupiedCells(em, Allocator.Temp);
            var grid = PassabilityGrid.Instance;
            for (int i = 0; i < cells.Length && outPositions.Length < nodeCount; i++)
            {
                // Another resource node already owns this cell — a neighbouring
                // patch, or two markers authored too close together. Stacking
                // is silent and looks like ONE node carrying double the ore,
                // with only the top one clickable, so step over it.
                if (taken.Contains(cells[i])) continue;

                float2 c2 = BuildGrid.CellCentre(cells[i]);
                float y = TerrainUtility.GetHeight(c2.x, c2.y);
                var pos = new float3(c2.x, y, c2.y);

                if (grid != null)
                {
                    var pc = grid.WorldToCell(pos);
                    if (pc.x >= 0 && pc.x < grid.Width && pc.y >= 0 && pc.y < grid.Height
                        && grid.GetCell(pc) == PassabilityGrid.TerrainBlocked)
                        continue;
                }

                outPositions.Add(pos);
                taken.Add(cells[i]);
            }

            taken.Dispose();
            keys.Dispose();
            cells.Dispose();
        }

        /// <summary>Build cells already occupied by a resource node of any
        /// kind. Cheap: node counts are in the hundreds and this runs once per
        /// patch at bootstrap.</summary>
        private static NativeHashSet<int2> CollectOccupiedCells(EntityManager em, Allocator allocator)
        {
            var set = new NativeHashSet<int2>(256, allocator);
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<IronMineTag>(),
                    ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                    ComponentType.ReadOnly<VeilsteelDepositTag>(),
                },
                All = new[] { ComponentType.ReadOnly<LocalTransform>() },
            });
            using (var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < xfs.Length; i++)
                    set.Add(BuildGrid.WorldToCell(xfs[i].Position));
            }
            query.Dispose();
            return set;
        }

        /// <summary>Radius of the disc that holds <paramref name="nodeCount"/>
        /// build cells. Used for patch-ground painting and for reporting when a
        /// block overruns a marker's authored Spread.</summary>
        public static float BlockRadius(int nodeCount)
            => math.sqrt(math.max(0, nodeCount) / math.PI) * BuildGrid.CellSize;

        /// <summary>
        /// Warn when a patch could not be laid out as its marker asked.
        ///
        /// `Spread` is no longer a scatter radius — a gapless block is as tight
        /// as the node count allows — so it survives on the marker as authoring
        /// intent, and this reports when the block clearly overruns it.
        /// </summary>
        public static void ReportFit(string tag, float3 center,
            int placed, int nodeCount, float spread)
        {
            if (placed < nodeCount)
            {
                UnityEngine.Debug.LogWarning(
                    $"[{tag}] patch at ({center.x:0},{center.z:0}) placed " +
                    $"{placed}/{nodeCount} nodes — not enough buildable cells nearby.");
            }

            float blockRadius = BlockRadius(placed);
            if (spread > 0f && blockRadius > spread * 1.5f)
            {
                UnityEngine.Debug.LogWarning(
                    $"[{tag}] patch at ({center.x:0},{center.z:0}): {placed} gapless nodes " +
                    $"span ~{blockRadius:0.0} m, well past the marker's {spread:0.0} m Spread. " +
                    "Lower the node count to honour it.");
            }
        }

        /// <summary>Sort predicate: distance first, then x, then z.</summary>
        private static bool IsAfter(float ka, int2 ca, float kb, int2 cb)
        {
            if (ka != kb) return ka > kb;
            if (ca.x != cb.x) return ca.x > cb.x;
            return ca.y > cb.y;
        }

        /// <summary>Fisher-Yates within each run of equal distance keys.</summary>
        private static void ShuffleWithinDistanceTiers(
            NativeList<int2> cells, NativeList<float> keys, ref Random random)
        {
            int start = 0;
            while (start < cells.Length)
            {
                int end = start + 1;
                while (end < cells.Length && keys[end] == keys[start]) end++;
                for (int i = end - 1; i > start; i--)
                {
                    int j = random.NextInt(start, i + 1);
                    int2 tmp = cells[i];
                    cells[i] = cells[j];
                    cells[j] = tmp;
                }
                start = end;
            }
        }
    }
}
