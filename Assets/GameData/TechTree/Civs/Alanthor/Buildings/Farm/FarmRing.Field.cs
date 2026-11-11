// FarmRing.Field.cs
// WHAT IS FREE IN A FARM'S CROP RING (docs/Design/Age_1_Alanthor.md § The
// Farm, Unification decision 57). The ring's geometry is FarmRing.cs; this is
// the one answer to "is this point of a ring blocked?", shared by the
// simulation (FarmSystem pays with it), the placement preview
// (UI/World/FarmYieldPreview) and the crop visuals.
//
// A point is BLOCKED when it lies on:
//   * a building's footprint — any faction, finished or under construction
//     (BuildingTag; a PLAN has none, so plans never count). Wall curtains are
//     tested at their real stair-stepped cells (NavGridQuery), not their box;
//   * a resource node's 4 x 4 m square (supply, iron, veilstone — a curse node
//     stands on one);
//   * impassable ground: a PassabilityGrid cell that is TerrainBlocked (slope,
//     water, cliff, map edge) or ObstacleBlocked (trees, rocks, forest, nodes);
//   * land of ANOTHER Farm's ring that lies NEARER that Farm's centre
//     (2026-11-11, replaces "overlap counts against both"): where two rings
//     overlap the land is split straight down the middle — the perpendicular
//     bisector of the two centres — and each half is free crop land of the
//     nearer Farm. An exact tie goes to the lower entity index (a would-be
//     Farm, which has none yet, loses it).
// Roads are not obstacles; units are not obstacles.
//
// THE FREE SHARE is counted on a FIXED LATTICE: the 1 m points
// (i + 0.5, j + 0.5) round the Farm's centre that lie in the ring
// (FarmRing.InRing). free / total, in per-mille, integer — so every lockstep
// peer reaches the same number from the same world. The simulation recomputes
// it on a cadence and caches it on the Farm (FarmField); nothing recomputes it
// per frame per Farm.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static partial class FarmRing
    {
        // ─────────────────────────────────────────────────────────────
        // The would-be building (placement preview, AI site scoring)
        // ─────────────────────────────────────────────────────────────

        /// <summary>A building that is not there yet — the placement ghost.
        /// Its footprint blocks; when it is a Farm its ring blocks too.</summary>
        public struct Ghost
        {
            public bool Has;
            public float2 Centre;
            public int2 SizeMeters;
            public bool IsFarm;

            /// <summary>No hypothetical building.</summary>
            public static Ghost None => default;

            /// <summary>The building <paramref name="buildingId"/> standing
            /// at <paramref name="centre"/> (already snapped).</summary>
            public static Ghost Of(string buildingId, float2 centre) => new Ghost
            {
                Has = !string.IsNullOrEmpty(buildingId),
                Centre = centre,
                SizeMeters = string.IsNullOrEmpty(buildingId) ? int2.zero : BuildingSizeConfig.GetSize(buildingId),
                IsFarm = buildingId == Farm.BuildingId,
            };
        }

        // ─────────────────────────────────────────────────────────────
        // The sample lattice
        // ─────────────────────────────────────────────────────────────

        private static float2[] _lattice;
        private static float _latticeOuter = -1f, _latticeHalf = -1f;

        /// <summary>How many lattice points one ring holds (the per-mille's
        /// denominator).</summary>
        public static int SampleCount => Lattice().Length;

        /// <summary>Lattice point <paramref name="i"/>, as an offset from the
        /// Farm's centre (world XZ metres). Add the centre to get the point.</summary>
        public static float2 SampleOffset(int i) => Lattice()[i];

        private static float2[] Lattice()
        {
            float r = OuterRadius, h = FootprintHalfMeters;
            if (_lattice != null && r == _latticeOuter && h == _latticeHalf) return _lattice;
            int n = (int)math.ceil(r);
            var pts = new List<float2>(4 * n * n);
            for (int j = -n; j < n; j++)
                for (int i = -n; i < n; i++)
                {
                    var d = new float2(i + 0.5f, j + 0.5f);
                    if (InRing(float2.zero, d)) pts.Add(d);
                }
            _lattice = pts.ToArray();
            _latticeOuter = r;
            _latticeHalf = h;
            return _lattice;
        }

        // ─────────────────────────────────────────────────────────────
        // The obstacles near one ring
        // ─────────────────────────────────────────────────────────────

        /// <summary>Half the widest building footprint could reach, metres —
        /// the gather's cheap pre-filter (engine-side bound, not balance).</summary>
        private const float BuildingReachMeters = 32f;

        private static readonly ComponentType[] QT_Farms =
        {
            ComponentType.ReadOnly<FarmTag>(),
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Farms;

        private static readonly ComponentType[] QT_Supply =
            { ComponentType.ReadOnly<SupplyNodeTag>(), ComponentType.ReadOnly<LocalTransform>() };
        private static readonly ComponentType[] QT_Iron =
            { ComponentType.ReadOnly<IronMineTag>(), ComponentType.ReadOnly<LocalTransform>() };
        private static readonly ComponentType[] QT_Veilstone =
            { ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), ComponentType.ReadOnly<LocalTransform>() };
        private static CachedEntityQuery QC_Supply, QC_Iron, QC_Veilstone;

        /// <summary>
        /// Everything that can block one ring, gathered once: the building
        /// boxes, node squares and other Farms' centres that reach it. Fill it
        /// with <see cref="Gather"/>, then ask <see cref="IsBlocked"/> per
        /// point or <see cref="CountFreePermille"/> for the share. Reusable;
        /// main thread only.
        /// </summary>
        public sealed class Field
        {
            private readonly List<float4> _rects = new List<float4>(32);   // min.x, min.z, max.x, max.z
            private readonly List<float2> _nodes = new List<float2>(8);
            private readonly List<float2> _farms = new List<float2>(8);
            private readonly List<int> _farmIndex = new List<int>(8);   // tie-break, entity index
            private int _selfIndex;
            private float2 _centre;
            private float _outer, _half;   // the ring, read once per Gather
            private PassabilityGrid _grid;

            /// <summary>The ring's centre this field was gathered for.</summary>
            public float2 Centre => _centre;

            /// <summary>
            /// Gather the obstacles of a ring centred at <paramref name="centre"/>.
            /// <paramref name="self"/> is the Farm the ring belongs to (left out
            /// of the building and Farm lists; Entity.Null for a would-be Farm);
            /// <paramref name="ghost"/> adds a building that is not there yet.
            /// </summary>
            public void Gather(EntityManager em, float2 centre, Entity self, in Ghost ghost)
            {
                _rects.Clear();
                _nodes.Clear();
                _farms.Clear();
                _farmIndex.Clear();
                _selfIndex = self == Entity.Null ? int.MaxValue : self.Index;
                _centre = centre;
                float r = OuterRadius;
                _outer = r;
                _half = FootprintHalfMeters;
                float2 lo = centre - r, hi = centre + r;

                // Buildings: footprint boxes that reach the ring's bounds.
                var bq = BuildCommandHelper.BuildingQuery(em);
                using (var ents = bq.ToEntityArray(Allocator.Temp))
                using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                {
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var e = ents[i];
                        if (e == self) continue;
                        // Cheap reach test first: no footprint is anywhere near
                        // BuildingReachMeters wide, so a centre this far off
                        // cannot touch the ring.
                        var bp = xfs[i].Position;
                        if (math.abs(bp.x - centre.x) > r + BuildingReachMeters
                            || math.abs(bp.z - centre.y) > r + BuildingReachMeters) continue;
                        // Curtains are tested at their real cells below; their
                        // axis box is mostly open ground on a diagonal.
                        if (em.HasComponent<WallTag>(e) || em.HasComponent<WallSegmentTag>(e)) continue;
                        BuildCommandHelper.BuildingAabb(em, e, xfs[i].Position, out float2 mn, out float2 mx);
                        if (mx.x <= lo.x || mn.x >= hi.x || mx.y <= lo.y || mn.y >= hi.y) continue;
                        _rects.Add(new float4(mn.x, mn.y, mx.x, mx.y));
                    }
                }

                // Resource nodes (a curse node stands on one).
                const float nh = BuildGrid.ResourceNodeHalf;
                CollectNodes(em, ref QC_Supply, QT_Supply, lo - nh, hi + nh);
                CollectNodes(em, ref QC_Iron, QT_Iron, lo - nh, hi + nh);
                CollectNodes(em, ref QC_Veilstone, QT_Veilstone, lo - nh, hi + nh);

                // Other Farms whose rings can reach this one.
                var fq = QC_Farms.Get(em, QT_Farms);
                if (!fq.IsEmptyIgnoreFilter)
                {
                    using var fents = fq.ToEntityArray(Allocator.Temp);
                    using var fxfs = fq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                    float reach2 = (2f * r) * (2f * r);
                    for (int i = 0; i < fents.Length; i++)
                    {
                        if (fents[i] == self) continue;
                        var c = fxfs[i].Position.xz;
                        if (math.distancesq(c, centre) >= reach2) continue;
                        if (math.distancesq(c, centre) < 1e-4f) continue;   // the ring's own Farm
                        _farms.Add(c);
                        _farmIndex.Add(fents[i].Index);
                    }
                }

                // The building that is not there yet.
                if (ghost.Has)
                {
                    float2 half = new float2(ghost.SizeMeters.x, ghost.SizeMeters.y) * 0.5f;
                    _rects.Add(new float4(ghost.Centre - half, ghost.Centre + half));
                    if (ghost.IsFarm && math.distancesq(ghost.Centre, centre) < (2f * r) * (2f * r)
                        && math.distancesq(ghost.Centre, centre) >= 1e-4f)
                    {
                        _farms.Add(ghost.Centre);
                        _farmIndex.Add(int.MaxValue - 1);   // not built yet: a later index than any Farm
                    }
                }

                var g = PassabilityGrid.Instance;
                _grid = g != null && g.IsMaskReady ? g : null;
            }

            private void CollectNodes(EntityManager em, ref CachedEntityQuery qc, ComponentType[] qt,
                                      float2 lo, float2 hi)
            {
                var q = qc.Get(em, qt);
                if (q.IsEmptyIgnoreFilter) return;
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < xfs.Length; i++)
                {
                    var p = xfs[i].Position.xz;
                    if (p.x < lo.x || p.x > hi.x || p.y < lo.y || p.y > hi.y) continue;
                    _nodes.Add(p);
                }
            }

            /// <summary>True when world point <paramref name="p"/> (XZ) is
            /// covered by something the gathered ring counts as an obstacle.
            /// Meaningful for points of this ring (see the file header).</summary>
            public bool IsBlocked(float2 p)
            {
                for (int i = 0; i < _rects.Count; i++)
                {
                    var b = _rects[i];
                    if (p.x > b.x && p.x < b.z && p.y > b.y && p.y < b.w) return true;
                }
                const float nh = BuildGrid.ResourceNodeHalf;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var n = _nodes[i];
                    if (math.abs(p.x - n.x) < nh && math.abs(p.y - n.y) < nh) return true;
                }
                // Another Farm's ring, where it is the NEARER Farm (the overlap
                // split down the bisector). Its footprint is a building rect above.
                if (NearerOtherFarm(p)) return true;
                if (_grid != null)
                {
                    byte v = _grid.GetCell(_grid.WorldToCell(new float3(p.x, 0f, p.y)));
                    if (v == PassabilityGrid.TerrainBlocked || v == PassabilityGrid.ObstacleBlocked) return true;
                }
                // Wall curtains, at the nav cells they really cross.
                if (TheWaningBorder.Systems.Navigation.NavGridQuery.AnyWallCellIn(p - 0.01f, p + 0.01f))
                    return true;
                return false;
            }

            /// <summary>
            /// True when <paramref name="p"/> lies in another gathered Farm's
            /// ring AND nearer that Farm's centre than this ring's (an exact tie:
            /// the lower entity index). The overlap of two rings is split along
            /// the perpendicular bisector of their centres.
            /// </summary>
            public bool NearerOtherFarm(float2 p)
            {
                float dSelf = math.distancesq(p, _centre);
                for (int i = 0; i < _farms.Count; i++)
                {
                    float2 d = p - _farms[i];
                    float dOther = math.lengthsq(d);
                    if (dOther > _outer * _outer) continue;
                    if (math.abs(d.x) < _half && math.abs(d.y) < _half) continue;   // its footprint: a building
                    if (dOther < dSelf || (dOther == dSelf && _farmIndex[i] < _selfIndex)) return true;
                }
                return false;
            }

            /// <summary>The free share of the gathered ring, in per-mille
            /// (0..1000): free lattice points x 1000 / all lattice points.</summary>
            public int CountFreePermille()
            {
                var lattice = Lattice();
                if (lattice.Length == 0) return 0;
                int free = 0;
                for (int i = 0; i < lattice.Length; i++)
                    if (!IsBlocked(_centre + lattice[i])) free++;
                return free * 1000 / lattice.Length;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // The public answers
        // ─────────────────────────────────────────────────────────────

        // One scratch field for the computing calls below (simulation and UI
        // both run on the main thread and never nest these calls); the
        // per-point presentation query keeps its own so it is never clobbered.
        private static readonly Field _scratch = new Field();
        private static readonly Field _pointField = new Field();
        private static Entity _pointFarm = Entity.Null;
        private static int _pointFrame = -1;

        /// <summary>A Farm's centre, world XZ.</summary>
        public static float2 CentreOf(EntityManager em, Entity farm)
            => em.GetComponentData<LocalTransform>(farm).Position.xz;

        /// <summary>
        /// The free share of <paramref name="farm"/>'s ring right now, in
        /// per-mille, computed from the world (not the cached FarmField).
        /// The simulation's computation (FarmSystem).
        /// </summary>
        public static int FreePermille(EntityManager em, Entity farm)
            => FreePermilleAt(em, CentreOf(em, farm), farm, Ghost.None);

        /// <summary>
        /// The free share, in per-mille, of a ring centred at
        /// <paramref name="centre"/>: <paramref name="self"/> is the Farm it
        /// belongs to (Entity.Null for a would-be Farm), and
        /// <paramref name="ghost"/> a building that is not there yet. The
        /// placement preview and the AI site search call this with the
        /// would-be position, so they show exactly what the Farm would be paid.
        /// </summary>
        public static int FreePermilleAt(EntityManager em, float2 centre, Entity self, in Ghost ghost)
        {
            _scratch.Gather(em, centre, self, ghost);
            return _scratch.CountFreePermille();
        }

        /// <summary>
        /// The Farm's cached free share (FarmField, what FarmSystem pays
        /// with), or -1 before its first computation.
        /// </summary>
        public static int CachedFreePermille(EntityManager em, Entity farm)
        {
            if (!em.HasComponent<FarmField>(farm)) return -1;
            var f = em.GetComponentData<FarmField>(farm);
            return f.Computed != 0 ? f.FreePermille : -1;
        }

        /// <summary>
        /// PRESENTATION CONVENIENCE for the crop visuals: is world point
        /// <paramref name="p"/> (XZ) of <paramref name="farm"/>'s ring blocked?
        /// The same test the simulation counts. Gathers the Farm's obstacles
        /// once per rendered frame per Farm (keyed by UnityEngine frame count),
        /// so asking every lattice point of one Farm in a row is cheap. Never
        /// call from the simulation — use <see cref="Field"/> or
        /// <see cref="FreePermille"/> there.
        /// </summary>
        public static bool IsBlocked(EntityManager em, Entity farm, float2 p)
        {
            int frame = UnityEngine.Time.frameCount;
            if (farm != _pointFarm || frame != _pointFrame)
            {
                if (!em.Exists(farm) || !em.HasComponent<LocalTransform>(farm)) return false;
                _pointField.Gather(em, CentreOf(em, farm), farm, Ghost.None);
                _pointFarm = farm;
                _pointFrame = frame;
            }
            return _pointField.IsBlocked(p);
        }
    }
}
