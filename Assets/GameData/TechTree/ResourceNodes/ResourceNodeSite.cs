// ResourceNodeSite.cs
// Where a resource node may stand, and what may stand on one.
// docs/Design/Build_Grid.md §3 (2026-09-29).
//
// Two rules, one footprint. Every node — supply, iron, veilstone, veilsteel —
// is a 2 x 2-cell (4 x 4 m) square on the build grid:
//
//   1. A NODE SITE is legal only when that whole square is buildable ground
//      (inside the map, no water, no slope over 15°, every passability cell
//      open, no building or obstacle on it) and it keeps one clear cell from
//      every other node. Every node factory resolves its position through
//      TryResolve, so the authored markers, the fallback scatters, the
//      coverage passes and the curse's precipitation all obey it. An illegal
//      authored spot is moved to the NEAREST legal grid site (a fixed search
//      order, so every lockstep peer lands on the same one); with none inside
//      SearchRings the node is not spawned at all.
//
//   2. NOTHING IS BUILT ON A NODE except the extractor made for it
//      (TerritoryOwnership.RequiredNodeFor). The placement validators ask
//      OverlapsNode with that extractor's node kind exempt. The supply node
//      matters most here: it carries no ObstacleTag and blocks no grid cell
//      (it is ground you are meant to build on), so neither the obstacle nor
//      the passability test ever saw it.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static class ResourceNodeSite
    {
        /// <summary>Clear ground kept between two node footprints: one cell.</summary>
        public const float Gap = BuildGrid.CellSize;

        /// <summary>How far (in cells) a node may be moved off an illegal
        /// authored spot before it is dropped — 12 cells, 24 m.</summary>
        public const int SearchRings = 12;

        static readonly ComponentType[] QT_Supply =
            { ComponentType.ReadOnly<SupplyNodeTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Iron =
            { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Veilstone =
            { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static readonly ComponentType[] QT_Veilsteel =
            { ComponentType.ReadOnly<VeilsteelDepositTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static CachedEntityQuery QC_Supply, QC_Iron, QC_Veilstone, QC_Veilsteel;

        /// <summary>Grid offsets (in cells) nearest-first, ties broken by z
        /// then x — the one search order every peer walks.</summary>
        static int2[] _offsets;

        // ─────────────────────────────────────────────────────────────
        // Rule 2 — nothing on a node but its own extractor
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Does the footprint [min, max] overlap any resource node's 4 x 4 m
        /// square? Nodes of the <paramref name="exempt"/> kind are ignored —
        /// that is the extractor standing on its own node.
        /// </summary>
        public static bool OverlapsNode(EntityManager em, float2 min, float2 max, ComponentType? exempt)
        {
            return OverlapsKind(em, ref QC_Supply,    QT_Supply,    min, max, exempt)
                || OverlapsKind(em, ref QC_Iron,      QT_Iron,      min, max, exempt)
                || OverlapsKind(em, ref QC_Veilstone, QT_Veilstone, min, max, exempt)
                || OverlapsKind(em, ref QC_Veilsteel, QT_Veilsteel, min, max, exempt);
        }

        static bool OverlapsKind(EntityManager em, ref CachedEntityQuery qc, ComponentType[] qt,
                                 float2 min, float2 max, ComponentType? exempt)
        {
            if (exempt != null && exempt.Value.TypeIndex == qt[0].TypeIndex) return false;
            var q = qc.Get(em, qt);
            if (q.IsEmpty) return false;
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            const float h = BuildGrid.ResourceNodeHalf;
            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                if (min.x < p.x + h && max.x > p.x - h && min.y < p.z + h && max.y > p.z - h)
                    return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────
        // Rule 1 — where a node may stand
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The legal node site nearest <paramref name="desired"/>: the snapped
        /// spot itself when it is legal, else the first legal grid site in
        /// the fixed nearest-first order. False when none lies within
        /// <see cref="SearchRings"/> cells — the caller does not spawn.
        /// </summary>
        public static bool TryResolve(EntityManager em, float3 desired, out float3 site)
        {
            float3 start = BuildGrid.SnapResourceNode(desired);
            site = start;

            // Everything a candidate is tested against, read once.
            var nodes = new NativeList<float2>(64, Allocator.Temp);
            Collect(em, ref QC_Supply,    QT_Supply,    nodes);
            Collect(em, ref QC_Iron,      QT_Iron,      nodes);
            Collect(em, ref QC_Veilstone, QT_Veilstone, nodes);
            Collect(em, ref QC_Veilsteel, QT_Veilsteel, nodes);

            var oq = BuildCommandHelper.ObstacleQuery(em);
            var obsPos = oq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var obsRad = oq.ToComponentDataArray<Radius>(Allocator.Temp);

            try
            {
                var offsets = Offsets();
                for (int k = 0; k < offsets.Length; k++)
                {
                    float3 c = start;
                    if (k > 0)
                    {
                        c.x += offsets[k].x * BuildGrid.CellSize;
                        c.z += offsets[k].y * BuildGrid.CellSize;
                        c.y = TerrainUtility.GetHeight(c.x, c.z);
                    }
                    if (IsLegal(em, c, nodes, obsPos, obsRad)) { site = c; return true; }
                }
                return false;
            }
            finally
            {
                nodes.Dispose();
                obsPos.Dispose();
                obsRad.Dispose();
            }
        }

        static bool IsLegal(EntityManager em, float3 c, NativeList<float2> nodes,
                            NativeArray<LocalTransform> obsPos, NativeArray<Radius> obsRad)
        {
            const float h = BuildGrid.ResourceNodeHalf;
            var min = new float2(c.x - h, c.z - h);
            var max = new float2(c.x + h, c.z + h);

            if (!BuildCommandHelper.InsideMapBounds(min, max)) return false;

            // Other nodes: footprints may not overlap, and keep one cell clear.
            const float reach = BuildGrid.ResourceNodeMeters + Gap;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (math.abs(nodes[i].x - c.x) < reach && math.abs(nodes[i].y - c.z) < reach)
                    return false;
            }

            for (int i = 0; i < obsPos.Length; i++)
                if (BuildCommandHelper.CircleHitsAabb(obsPos[i].Position, obsRad[i].Value, min, max))
                    return false;

            int m = (int)BuildGrid.ResourceNodeMeters;
            var size = new int2(m, m);
            if (!BuildCommandHelper.PassesTerrainAndGrid(c, size, min, max, isExtractor: false))
                return false;

            return !BuildCommandHelper.OverlapsExistingBuilding(em, c, size);
        }

        static void Collect(EntityManager em, ref CachedEntityQuery qc, ComponentType[] qt,
                            NativeList<float2> into)
        {
            var q = qc.Get(em, qt);
            if (q.IsEmpty) return;
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
                into.Add(new float2(xfs[i].Position.x, xfs[i].Position.z));
        }

        static int2[] Offsets()
        {
            if (_offsets != null) return _offsets;
            int n = SearchRings, side = 2 * n + 1;
            var list = new int2[side * side];
            int k = 0;
            for (int z = -n; z <= n; z++)
                for (int x = -n; x <= n; x++)
                    list[k++] = new int2(x, z);
            System.Array.Sort(list, (a, b) =>
            {
                int da = a.x * a.x + a.y * a.y, db = b.x * b.x + b.y * b.y;
                if (da != db) return da.CompareTo(db);
                if (a.y != b.y) return a.y.CompareTo(b.y);
                return a.x.CompareTo(b.x);
            });
            _offsets = list;
            return list;
        }
    }
}
