// FarmStoneWall.cs
// THE FARM'S LEVEL-3 LOW STONE WALL (docs/Design/Age_1_Alanthor.md § The
// level-3 wall; decision 57 (f)): the deterministic layout and the piece
// factory. FarmStoneWallSystem decides WHEN (level 3, re-layout on a changed
// signature, rebuilds); this file decides WHERE and WHAT.
//
// WHERE — FarmBoundaryLayout.Compute (the same pure rules the fence is drawn
// with), fed only simulation data:
//   * blocked: FarmRing.Field — the yield's own obstacle test (buildings, nodes,
//     impassable ground, stone walls, other Farms' rings). Wall pieces are not in
//     it (no BuildingTag, no wall flag), so they never cut a ring's yield;
//   * neighbours: another Farm's ring splits the overlap down the middle; a
//     friendly Farm's field merges with this one (no wall between), a hostile
//     one is walled off along the split (FarmBoundaryLayout, NEIGHBOUR LINE);
//   * roads: the SIMULATION road graph (RoadGraphSystem, Roads.md §0), rebuilt
//     on the lockstep tick from a sorted site set. Each graph edge counts as the
//     straight line between its two nodes; a point within roadOpeningHalfWidth
//     of one is "road" (coverage 1, else 0). The drawn road (RoadNetwork's A*
//     trace and road mask) is presentation, routed on frame time, and is never
//     read here. With coverage 0/1 and RoadEdgeCoverage above 1 the layout lays
//     no road-edge pieces: the sim wall keeps to the rim and neighbour rims, with
//     an opening wherever a road crosses them.
//   * the door: the Farm's sim rotation turned by doorYawOffsetDegrees.
// Endpoints are snapped to a 1/16 m lattice, so a slot's identity (and the
// diff between two layouts) is exact. Lists are visited in entity-index order;
// set hashes are order-independent sums — chunk order differs between peers.
//
// WHAT — one entity per piece: the owner's FactionTag, Health and Defense from
// FarmStoneWallSystem.asset, a Radius of half its length (the combat range
// model), NodeNoAutoAcquire (never auto-acquired, no splash), NetworkedEntity
// (orders aimed at it survive the lockstep guard). No BuildingTag, no WallTag.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Roads;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Entities
{
    public static class FarmStoneWall
    {
        /// <summary>The piece's view (PresentationSpawnSystem.FarmStoneWall.cs).
        /// Next to the Farm (512) and the Market (513).</summary>
        public const int PresentationID = 514;

        /// <summary>Endpoint lattice, 1/16 m (engine-side, not balance).</summary>
        private const float Snap = 16f;
        /// <summary>A nav cell's half diagonal and a little: the thinnest strip
        /// that leaves no diagonal gap between two stamped cells.</summary>
        public const float MinHalfThickness = 0.75f;

        // ── The door ──────────────────────────────────────────────────

        /// <summary>World XZ the door of a Farm with <paramref name="rotation"/>
        /// faces. The fence (presentation) and the wall read this one rule.</summary>
        public static float2 DoorDirection(quaternion rotation)
        {
            float yaw = math.radians(FarmStoneWallSystemConfig.I.doorYawOffsetDegrees);
            float3 fwd = math.mul(math.mul(rotation, quaternion.RotateY(yaw)), new float3(0, 0, 1));
            return math.normalizesafe(fwd.xz, new float2(0, -1));
        }

        public static float2 SnapPoint(float2 p) => math.round(p * Snap) / Snap;

        // ── Roads, once per layout tick ──────────────────────────────

        /// <summary>Every edge of the simulation road graph, (a.xy, b.xy), lower
        /// node first, in node order. Filled by <see cref="GatherRoads"/>.</summary>
        private static readonly List<float4> _roads = new List<float4>();

        /// <summary>Copy the road graph's edges. Call once per layout tick, from
        /// the lockstep system, before <see cref="Signature"/> / <see cref="Layout"/>.</summary>
        public static void GatherRoads()
        {
            _roads.Clear();
            if (!RoadGraphSystem.Built) return;
            int n = RoadGraphSystem.NodeCount;
            for (int i = 0; i < n; i++)
            {
                var nb = RoadGraphSystem.Neighbours(i);
                for (int k = 0; k < nb.Count; k++)
                {
                    int j = nb[k];
                    if (j <= i) continue;
                    var a = RoadGraphSystem.NodePos(i);
                    var b = RoadGraphSystem.NodePos(j);
                    _roads.Add(new float4(a, b));
                }
            }
        }

        private static float SegDistSq(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float l2 = math.lengthsq(ab);
            float t = l2 > 1e-6f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distancesq(p, a + ab * t);
        }

        // ── The signature ─────────────────────────────────────────────

        private static readonly ComponentType[] QT_Farms =
        {
            ComponentType.ReadOnly<FarmTag>(), ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static CachedEntityQuery QC_Farms;

        private static uint Mix(uint h, int v) => (h ^ (uint)v) * 16777619u;
        private static int Q(float v) => (int)math.round(v * 4f);

        /// <summary>
        /// A hash of everything that shapes <paramref name="farm"/>'s wall: its
        /// position and door, the Farms and buildings whose footprint or ring
        /// reaches it, the roads near it, and whether the terrain mask is ready.
        /// The wall is laid out again only when this changes. Order-independent.
        /// </summary>
        public static uint Signature(EntityManager em, Entity farm)
        {
            var xf = em.GetComponentData<LocalTransform>(farm);
            float2 c = xf.Position.xz;
            float R = FarmRing.OuterRadius;
            float2 door = DoorDirection(xf.Rotation);
            uint h = 2166136261u;
            h = Mix(h, Q(c.x)); h = Mix(h, Q(c.y));
            h = Mix(h, (int)math.round(door.x * 64f)); h = Mix(h, (int)math.round(door.y * 64f));

            uint sum = 0;
            // Buildings whose footprint reaches the ring's box (other Farms included).
            float2 lo = c - (R + 1f), hi = c + (R + 1f);
            var bq = BuildCommandHelper.BuildingQuery(em);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    if (e == farm) continue;
                    var p = xfs[i].Position;
                    if (math.abs(p.x - c.x) > R + 40f || math.abs(p.z - c.y) > R + 40f) continue;
                    BuildCommandHelper.BuildingAabb(em, e, p, out float2 mn, out float2 mx);
                    bool farmNear = em.HasComponent<FarmTag>(e) && math.distancesq(p.xz, c) < 4f * R * R;
                    if (!farmNear && (mx.x <= lo.x || mn.x >= hi.x || mx.y <= lo.y || mn.y >= hi.y)) continue;
                    uint eh = 2166136261u;
                    eh = Mix(eh, e.Index); eh = Mix(eh, e.Version);
                    eh = Mix(eh, Q(mn.x)); eh = Mix(eh, Q(mn.y)); eh = Mix(eh, Q(mx.x)); eh = Mix(eh, Q(mx.y));
                    if (farmNear && em.HasComponent<FactionTag>(e))
                        eh = Mix(eh, (int)em.GetComponentData<FactionTag>(e).Value);   // merge or wall off
                    sum += eh;
                }
            }
            h = Mix(h, (int)sum);

            // Roads near the ring.
            float reach = R + RoadHalf + 1f;
            uint rsum = 0;
            for (int i = 0; i < _roads.Count; i++)
            {
                var r = _roads[i];
                if (SegDistSq(c, r.xy, r.zw) > reach * reach) continue;
                uint eh = 2166136261u;
                eh = Mix(eh, Q(r.x)); eh = Mix(eh, Q(r.y)); eh = Mix(eh, Q(r.z)); eh = Mix(eh, Q(r.w));
                rsum += eh;
            }
            h = Mix(h, (int)rsum);

            var g = PassabilityGrid.Instance;
            h = Mix(h, g != null && g.IsMaskReady ? 1 : 0);
            return h | 1u;
        }

        private static float RoadHalf => math.max(0f, FarmStoneWallSystemConfig.I.roadOpeningHalfWidth);

        // ── The layout ────────────────────────────────────────────────

        private sealed class SimWorld : FarmBoundaryLayout.IWorld
        {
            public readonly FarmRing.Field Field = new FarmRing.Field();
            public readonly List<float4> Roads = new List<float4>();
            public float HalfSq;
            public bool IsBlocked(float2 p) => Field.IsBlocked(p);
            public float RoadCoverage(float2 p)
            {
                for (int i = 0; i < Roads.Count; i++)
                    if (SegDistSq(p, Roads[i].xy, Roads[i].zw) <= HalfSq) return 1f;
                return 0f;
            }
        }

        private static readonly SimWorld _world = new SimWorld();
        private static readonly List<FarmBoundaryLayout.Neighbour> _others = new List<FarmBoundaryLayout.Neighbour>();
        private static readonly List<Entity> _otherEnts = new List<Entity>();

        /// <summary>
        /// The wall's pieces round <paramref name="farm"/>, openings dropped,
        /// endpoints snapped: appended to <paramref name="pieces"/> as (A.xy, B.xy).
        /// Deterministic: simulation data only, visited in a fixed order.
        /// </summary>
        public static void Layout(EntityManager em, Entity farm, List<float4> pieces, List<FarmBoundaryLayout.Segment> scratch)
        {
            var cfg = FarmStoneWallSystemConfig.I;
            var xf = em.GetComponentData<LocalTransform>(farm);
            float2 c = xf.Position.xz;
            float R = FarmRing.OuterRadius;

            // Other Farms whose ring reaches this one, in entity-index order.
            _otherEnts.Clear();
            var fq = QC_Farms.Get(em, QT_Farms);
            using (var fents = fq.ToEntityArray(Allocator.Temp))
            using (var fxfs = fq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < fents.Length; i++)
                {
                    if (fents[i] == farm) continue;
                    if (math.distancesq(fxfs[i].Position.xz, c) >= 4f * R * R) continue;
                    _otherEnts.Add(fents[i]);
                }
            }
            _otherEnts.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Version.CompareTo(b.Version));
            // The same owner or an ally: the fields merge. Hostile: walled off
            // along the split down the middle (FarmBoundaryLayout).
            var mine = em.GetComponentData<FactionTag>(farm).Value;
            _others.Clear();
            for (int i = 0; i < _otherEnts.Count; i++)
                _others.Add(new FarmBoundaryLayout.Neighbour
                {
                    Centre = em.GetComponentData<LocalTransform>(_otherEnts[i]).Position.xz,
                    Fence = !em.HasComponent<FactionTag>(_otherEnts[i])
                            || Alliances.AreHostile(mine, em.GetComponentData<FactionTag>(_otherEnts[i]).Value),
                });

            // The roads near the ring.
            float half = RoadHalf;
            float reach = R + half + 1f;
            _world.Roads.Clear();
            for (int i = 0; i < _roads.Count; i++)
                if (SegDistSq(c, _roads[i].xy, _roads[i].zw) <= reach * reach) _world.Roads.Add(_roads[i]);
            _world.HalfSq = half * half;
            _world.Field.Gather(em, c, farm, FarmRing.Ghost.None);

            var settings = new FarmBoundaryLayout.Settings
            {
                PieceLength = math.max(0.5f, cfg.pieceLength),
                Inset = cfg.inset,
                CropProbe = cfg.cropProbe,
                RoadEdgeCoverage = 2f,     // coverage is 0/1: no road-edge pieces, roads never "not crop"
                RoadBlockCoverage = 0.5f,  // a piece touching a road is an opening
                GridMeters = 1f,           // unused: no road edges are traced
                DoorOpeningMeters = cfg.doorOpeningMeters,
            };
            scratch.Clear();
            FarmBoundaryLayout.Compute(c, R, FarmRing.FootprintHalfMeters, DoorDirection(xf.Rotation),
                _others, _world, settings, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                var s = scratch[i];
                if (s.Opening) continue;
                pieces.Add(new float4(SnapPoint(s.A), SnapPoint(s.B)));
            }
        }

        // ── The clear-spot test (rebuilds) ───────────────────────────

        /// <summary>True when the strip A-B (half-thickness <paramref name="halfT"/>)
        /// touches a unit (with its radius) in <paramref name="units"/> (x, z, radius)
        /// or a building footprint in <paramref name="rects"/> (min.xy, max.xy).</summary>
        public static bool Occupied(float2 a, float2 b, float halfT, List<float3> units, List<float4> rects)
        {
            float2 d = b - a;
            float len = math.length(d);
            if (len < 1e-4f) return false;
            float2 dir = d / len;
            float2 m = (a + b) * 0.5f;
            float halfL = len * 0.5f;
            for (int i = 0; i < units.Count; i++)
            {
                float2 v = units[i].xy - m;
                float r = units[i].z;
                float along = math.abs(math.dot(v, dir));
                float across = math.abs(v.x * dir.y - v.y * dir.x);
                if (along <= halfL + r && across <= halfT + r) return true;
            }
            for (int k = 0; k <= 4; k++)
            {
                float2 p = math.lerp(a, b, k * 0.25f);
                for (int i = 0; i < rects.Count; i++)
                {
                    var q = rects[i];
                    if (p.x > q.x - halfT && p.x < q.z + halfT && p.y > q.y - halfT && p.y < q.w + halfT) return true;
                }
            }
            return false;
        }

        // ── The piece ─────────────────────────────────────────────────

        /// <summary>A new piece A-B of <paramref name="farm"/>'s wall, at full HP.
        /// Main thread, from the lockstep system: the order of these calls is the
        /// same on every peer, so the network ids are too.</summary>
        public static Entity CreatePiece(EntityManager em, Entity farm, Faction faction, float2 a, float2 b)
        {
            var cfg = FarmStoneWallSystemConfig.I;
            int hp = math.max(1, cfg.maxHealth);
            float2 m = (a + b) * 0.5f;
            float2 d = math.normalizesafe(b - a, new float2(1, 0));
            var pos = new float3(m.x, TerrainUtility.GetHeight(m.x, m.y), m.y);
            var rot = quaternion.LookRotationSafe(new float3(d.x, 0f, d.y), math.up());

            Entity e = em.CreateEntity();
            em.AddComponentData(e, new PresentationId { Id = PresentationID });
            em.AddComponentData(e, LocalTransform.FromPositionRotationScale(pos, rot, 1f));
            em.AddComponentData(e, new FactionTag { Value = faction });
            em.AddComponentData(e, new FarmWallPiece
            {
                Farm = farm, A = a, B = b,
                HalfThickness = math.max(MinHalfThickness, cfg.thicknessMeters * 0.5f),
            });
            em.AddComponent<NodeNoAutoAcquire>(e);
            em.AddComponentData(e, new Health { Value = hp, Max = hp });
            em.AddComponentData(e, new Radius { Value = math.distance(a, b) * 0.5f });
            em.AddComponentData(e, new ArmorTypeData { Value = ArmorType.StructureHuman });
            em.AddComponentData(e, new Defense
            {
                Melee = cfg.defenseMelee, Ranged = cfg.defenseRanged,
                Siege = cfg.defenseSiege, Magic = cfg.defenseMagic,
            });
            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick,
            });
            em.AddComponentData(e, new DisplayName { Value = new FixedString64Bytes("Low stone wall") });
            return e;
        }
    }
}
