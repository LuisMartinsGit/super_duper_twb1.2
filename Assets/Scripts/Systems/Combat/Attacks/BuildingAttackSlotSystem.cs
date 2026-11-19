// BuildingAttackSlotSystem.cs
// Where each melee attacker of a building stands (2026-11-18).
//
// THE BUG. A melee unit attacking a building walked to
// TargetExtent.ApproachPoint(myPos, ...) — the point of the footprint nearest
// to ITSELF (MeleeCombatSystem.Jobs.cs, MeleePlanJob chase branch). An army
// arriving from one side therefore aimed every man at nearly the same spot:
// they rushed it in a pile, and the separation force could only shove the
// pile sideways. The ones that did not fit kept chasing a spot they could
// never reach — a chaser is exempt from the integrator's crowded-arrival rule
// (UnitIntegratorSystem: `chasing`) — so they slid round the crowd, and the
// stuck redirect (StuckRedirectSystem.Redirect, combat-chaser branch) cleared
// their ChaseAnchor, which re-derived the nearest point from wherever the
// slide had left them: a new spot a little further round, again occupied.
// That is the circling.
//
// THE RULE (docs/Design/Navigation_And_Formations.md § 4.1). Every few
// tenths of a second this system hands out, per attacked building, SLOTS on
// two rings round its footprint:
//   * the FRONT ring, inside melee reach, slotSpacing apart along the edge;
//   * the WAITING ring, waitRingOffset behind it.
// A unit that holds a slot keeps it (stickiness) while it is free of wider
// neighbours. The rest, nearest the building first, take the nearest free
// front slot — walking round the building to it if this side is full — then
// the nearest free waiting slot. A unit with neither stops in the band behind
// the waiting ring and stands there. When a front-liner dies or leaves, the
// next allocation gives its slot to the nearest unit, which is the waiting
// unit behind it.
//
// MeleeCombatSystem reads the slot (BuildingAttackSlot) in its plan job: a
// FRONT unit walks to its slot and swings once it is there, a WAITING or
// OVERFLOW unit walks to its spot and holds (no destination — nothing left to
// chase, so nothing to circle).
//
// Scope: melee units (no ArcherTag) on the ground attacking a building with a
// BuildingSize footprint, EXCEPT torch throwers (2026-11-19, decision 82: a
// melee unit with a torch attacks a building from MeleeCombatSystem.asset's
// torch reach, a short ranged attack, and stands wherever it reaches it —
// CombatDamageHelper.ThrowsTorch). What is left here is the ram and any
// melee unit with no torch. Walls are left to the old nearest-point chase (only
// siege hits them, and a segment is not an axis-aligned box). Ranged units
// already halt the moment they are in range (UnitIntegratorSystem's firing
// band), so their stand is wherever they reach the range ring.
//
// Determinism: main thread, fixed cadence (SimCadence.Periodic on the sim
// step), all ordering by NETWORK id (entity index as fallback for unnetworked
// entities only), ties broken by index — no wall clock, no randomness.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Systems.Navigation;

/// <summary>
/// The spot a melee unit was given round the building it attacks (global
/// namespace per the project's ECS component convention). Written by
/// <see cref="TheWaningBorder.Systems.Combat.BuildingAttackSlotSystem"/>,
/// read by MeleeCombatSystem's plan job. Valid only while
/// <see cref="Target"/> is still the unit's Target.
/// </summary>
public struct BuildingAttackSlot : IComponentData
{
    public const byte None = 0;
    /// <summary>A front slot: walk to <see cref="Point"/>, swing there.</summary>
    public const byte Front = 1;
    /// <summary>A waiting slot: walk to <see cref="Point"/>, hold there.</summary>
    public const byte Wait = 2;
    /// <summary>No slot on either ring: hold anywhere in the band
    /// [<see cref="HoldBandMin"/>, <see cref="HoldBandMax"/>] of surface
    /// distance; walk toward <see cref="Point"/> while outside it.</summary>
    public const byte Hold = 3;

    public Entity Target;
    public float3 Point;
    public byte Kind;
    /// <summary>Slot index on its ring (Front / Wait).</summary>
    public short Index;
    /// <summary>Slots on that ring when this one was given — the index is
    /// only kept while the ring still has this many.</summary>
    public short RingCount;
    public float HoldBandMin;
    public float HoldBandMax;
}

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    [UpdateBefore(typeof(MeleeCombatSystem))]
    public partial class BuildingAttackSlotSystem : SystemBase
    {
        private SimCadence.Periodic _acc;
        private EntityQuery _attackers;

        private struct Entry
        {
            public Entity Unit;
            public Entity Target;
            public int TargetKey;
            public int UnitKey;
            public float3 Pos;
            public float Radius;
            public float SurfaceDist;
        }

        /// <summary>Groups attackers by target, then a fixed unit order.</summary>
        private struct ByTargetThenUnit : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b)
            {
                int c = a.TargetKey.CompareTo(b.TargetKey);
                if (c != 0) return c;
                c = a.Target.Index.CompareTo(b.Target.Index);
                if (c != 0) return c;
                c = a.UnitKey.CompareTo(b.UnitKey);
                if (c != 0) return c;
                return a.Unit.Index.CompareTo(b.Unit.Index);
            }
        }

        /// <summary>Nearest the building first, then the fixed unit order.</summary>
        private struct ByDistThenUnit : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b)
            {
                int c = a.SurfaceDist.CompareTo(b.SurfaceDist);
                if (c != 0) return c;
                c = a.UnitKey.CompareTo(b.UnitKey);
                if (c != 0) return c;
                return a.Unit.Index.CompareTo(b.Unit.Index);
            }
        }

        protected override void OnCreate()
        {
            _attackers = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, Target, Damage>()
                .WithNone<ArcherTag, DeathAnimationState>()
                .Build();
        }

        protected override void OnUpdate()
        {
            var cfg = BuildingAttackSlotSystemConfig.I;
            if (cfg == null) return;   // reported loudly by ComponentConfig.Require
            if (!_acc.Due(SystemAPI.Time.DeltaTime, cfg.reslotIntervalSeconds)) return;
            if (_attackers.IsEmpty) return;

            var em = EntityManager;

            using var units = _attackers.ToEntityArray(Allocator.Temp);
            using var targets = _attackers.ToComponentDataArray<Target>(Allocator.Temp);
            using var xfs = _attackers.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var entries = new NativeList<Entry>(units.Length, Allocator.Temp);
            var needAdd = new NativeList<Entity>(16, Allocator.Temp);

            for (int i = 0; i < units.Length; i++)
            {
                Entity u = units[i];
                Entity t = targets[i].Value;
                bool has = em.HasComponent<BuildingAttackSlot>(u);

                // A torch thrower attacks a building from range and takes no
                // slot (decision 82, MeleeCombatSystem plan job).
                if (!IsSlotTarget(em, t) || !OnGround(em, u) || Dead(em, u)
                    || CombatDamageHelper.ThrowsTorch(em, u))
                {
                    if (has && em.GetComponentData<BuildingAttackSlot>(u).Kind != BuildingAttackSlot.None)
                        em.SetComponentData(u, default(BuildingAttackSlot));
                    continue;
                }

                if (!has) needAdd.Add(u);
                float3 tp = em.GetComponentData<LocalTransform>(t).Position;
                entries.Add(new Entry
                {
                    Unit = u,
                    Target = t,
                    TargetKey = StableKey(em, t),
                    UnitKey = StableKey(em, u),
                    Pos = xfs[i].Position,
                    Radius = em.HasComponent<Radius>(u) ? em.GetComponentData<Radius>(u).Value : 0f,
                    SurfaceDist = TargetGeometry.SurfaceDistXZ(em, xfs[i].Position, tp, t),
                });
            }

            // Structural change once per unit lifetime (the component is never
            // removed; Kind None is "no slot").
            if (needAdd.Length > 0)
                em.AddComponent<BuildingAttackSlot>(needAdd.AsArray());

            if (entries.Length > 0)
            {
                entries.Sort(new ByTargetThenUnit());

                var front = new NativeList<float3>(32, Allocator.Temp);
                var frontOk = new NativeList<byte>(32, Allocator.Temp);
                var wait = new NativeList<float3>(32, Allocator.Temp);
                var waitOk = new NativeList<byte>(32, Allocator.Temp);
                var frontClaims = new NativeList<float4>(32, Allocator.Temp);
                var waitClaims = new NativeList<float4>(32, Allocator.Temp);
                var frontTaken = new NativeList<byte>(32, Allocator.Temp);
                var waitTaken = new NativeList<byte>(32, Allocator.Temp);
                var group = new NativeList<Entry>(32, Allocator.Temp);
                var results = new NativeList<BuildingAttackSlot>(32, Allocator.Temp);
                var done = new NativeList<byte>(32, Allocator.Temp);

                int a = 0;
                while (a < entries.Length)
                {
                    int b = a + 1;
                    while (b < entries.Length && entries[b].Target == entries[a].Target) b++;

                    group.Clear();
                    for (int k = a; k < b; k++) group.Add(entries[k]);
                    Allocate(em, cfg, group, front, frontOk, wait, waitOk, frontClaims, waitClaims,
                        frontTaken, waitTaken, results, done);

                    a = b;
                }

                front.Dispose(); frontOk.Dispose(); wait.Dispose(); waitOk.Dispose();
                frontClaims.Dispose(); waitClaims.Dispose(); frontTaken.Dispose(); waitTaken.Dispose();
                group.Dispose(); results.Dispose(); done.Dispose();
            }

            entries.Dispose();
            needAdd.Dispose();
        }

        /// <summary>The slots of one attacked building, handed out.</summary>
        private static void Allocate(EntityManager em, BuildingAttackSlotSystemConfig cfg,
            NativeList<Entry> group,
            NativeList<float3> front, NativeList<byte> frontOk,
            NativeList<float3> wait, NativeList<byte> waitOk,
            NativeList<float4> frontClaims, NativeList<float4> waitClaims,
            NativeList<byte> frontTaken, NativeList<byte> waitTaken,
            NativeList<BuildingAttackSlot> results, NativeList<byte> done)
        {
            Entity target = group[0].Target;
            var ext = TargetGeometry.Extent(em, target);

            float waitStand = cfg.frontStandOff + cfg.waitRingOffset;
            BuildRing(in ext, cfg.frontStandOff, cfg, front, frontOk);
            BuildRing(in ext, waitStand, cfg, wait, waitOk);

            int n = group.Length;
            results.Clear();
            done.Clear();
            for (int k = 0; k < n; k++)
            {
                results.Add(default);
                done.Add(0);
            }

            bool anyFront = false;
            for (int j = 0; j < frontOk.Length; j++) anyFront |= frontOk[j] != 0;
            if (!anyFront)
            {
                // Nowhere legal round it: leave these units to the plain
                // nearest-point chase rather than parking them all.
                for (int k = 0; k < n; k++) Write(em, group[k].Unit, default);
                return;
            }

            frontClaims.Clear(); waitClaims.Clear();
            frontTaken.Clear(); waitTaken.Clear();
            for (int j = 0; j < front.Length; j++) frontTaken.Add(0);
            for (int j = 0; j < wait.Length; j++) waitTaken.Add(0);

            float factor = cfg.bodyClearanceFactor;

            // A. Front-liners keep their slots.
            for (int k = 0; k < n; k++)
            {
                var e = group[k];
                var old = em.GetComponentData<BuildingAttackSlot>(e.Unit);
                if (old.Kind != BuildingAttackSlot.Front || old.Target != target
                    || old.RingCount != front.Length || old.Index < 0 || old.Index >= front.Length)
                    continue;
                if (!Free(old.Index, e.Radius, factor, front, frontOk, frontTaken, frontClaims)) continue;
                Claim(old.Index, e.Radius, front, frontTaken, frontClaims);
                results[k] = Slot(target, BuildingAttackSlot.Front, old.Index, front.Length, front[old.Index]);
                done[k] = 1;
            }

            // The rest, nearest the building first.
            var order = new NativeList<Entry>(n, Allocator.Temp);
            var orderIdx = new NativeList<int>(n, Allocator.Temp);
            for (int k = 0; k < n; k++)
            {
                if (done[k] != 0) continue;
                var e = group[k];
                e.TargetKey = k;   // carry the group index through the sort
                order.Add(e);
            }
            order.Sort(new ByDistThenUnit());
            for (int q = 0; q < order.Length; q++) orderIdx.Add(order[q].TargetKey);

            // B. The nearest free front slot — round the building if need be.
            for (int q = 0; q < orderIdx.Length; q++)
            {
                int k = orderIdx[q];
                var e = group[k];
                int j = Nearest(e.Pos, e.Radius, factor, front, frontOk, frontTaken, frontClaims);
                if (j < 0) continue;
                Claim(j, e.Radius, front, frontTaken, frontClaims);
                results[k] = Slot(target, BuildingAttackSlot.Front, j, front.Length, front[j]);
                done[k] = 1;
            }

            // C. Waiting units keep their waiting slots.
            for (int k = 0; k < n; k++)
            {
                if (done[k] != 0) continue;
                var e = group[k];
                var old = em.GetComponentData<BuildingAttackSlot>(e.Unit);
                if (old.Kind != BuildingAttackSlot.Wait || old.Target != target
                    || old.RingCount != wait.Length || old.Index < 0 || old.Index >= wait.Length)
                    continue;
                if (!Free(old.Index, e.Radius, factor, wait, waitOk, waitTaken, waitClaims)) continue;
                Claim(old.Index, e.Radius, wait, waitTaken, waitClaims);
                results[k] = Slot(target, BuildingAttackSlot.Wait, old.Index, wait.Length, wait[old.Index]);
                done[k] = 1;
            }

            // D. The nearest free waiting slot; E. else hold behind the ring.
            for (int q = 0; q < orderIdx.Length; q++)
            {
                int k = orderIdx[q];
                if (done[k] != 0) continue;
                var e = group[k];
                int j = Nearest(e.Pos, e.Radius, factor, wait, waitOk, waitTaken, waitClaims);
                if (j >= 0)
                {
                    Claim(j, e.Radius, wait, waitTaken, waitClaims);
                    results[k] = Slot(target, BuildingAttackSlot.Wait, j, wait.Length, wait[j]);
                }
                else
                {
                    float3 p = ext.ApproachPoint(e.Pos, waitStand + cfg.overflowBandDepth * 0.5f);
                    p.y = ext.Center.y;
                    results[k] = new BuildingAttackSlot
                    {
                        Target = target,
                        Point = p,
                        Kind = BuildingAttackSlot.Hold,
                        Index = -1,
                        RingCount = 0,
                        HoldBandMin = waitStand,
                        HoldBandMax = waitStand + cfg.overflowBandDepth,
                    };
                }
                done[k] = 1;
            }

            order.Dispose();
            orderIdx.Dispose();

            for (int k = 0; k < n; k++) Write(em, group[k].Unit, results[k]);
        }

        private static BuildingAttackSlot Slot(Entity target, byte kind, int index, int count, float3 point)
            => new BuildingAttackSlot
            {
                Target = target,
                Point = point,
                Kind = kind,
                Index = (short)index,
                RingCount = (short)count,
            };

        private static void Write(EntityManager em, Entity unit, BuildingAttackSlot slot)
            => em.SetComponentData(unit, slot);

        /// <summary>
        /// Slot points on the footprint rect grown by <paramref name="standOff"/>,
        /// evenly spaced by arc length (never fewer than four). A point on
        /// impassable ground is walked outward until it stands; one that never
        /// does is marked unusable.
        /// </summary>
        private static void BuildRing(in TargetExtent ext, float standOff,
            BuildingAttackSlotSystemConfig cfg, NativeList<float3> pts, NativeList<byte> ok)
        {
            pts.Clear();
            ok.Clear();

            float hx = ext.HalfW + standOff;
            float hz = ext.HalfH + standOff;
            float perim = 4f * (hx + hz);
            int count = (int)math.floor(perim / math.max(cfg.slotSpacing, 1e-3f));
            count = math.clamp(count, 4, math.max(4, cfg.maxSlotsPerRing));
            float stepLen = perim / count;

            for (int i = 0; i < count; i++)
            {
                float3 p = PerimeterPoint(ext.Center, hx, hz, (i + 0.5f) * stepLen);
                p.y = ext.Center.y;

                bool standable = NavGridQuery.IsWorldStandable(p);
                if (!standable)
                {
                    // Outward = away from the nearest point of the footprint.
                    float3 surf = new float3(
                        math.clamp(p.x, ext.Center.x - ext.HalfW, ext.Center.x + ext.HalfW),
                        p.y,
                        math.clamp(p.z, ext.Center.z - ext.HalfH, ext.Center.z + ext.HalfH));
                    float3 outward = p - surf;
                    outward.y = 0f;
                    float len = math.length(outward);
                    if (len > 1e-4f && cfg.standableSearchStep > 0f)
                    {
                        outward /= len;
                        for (float extra = cfg.standableSearchStep; extra <= cfg.standableSearchMax;
                             extra += cfg.standableSearchStep)
                        {
                            float3 probe = p + outward * extra;
                            if (!NavGridQuery.IsWorldStandable(probe)) continue;
                            p = probe;
                            standable = true;
                            break;
                        }
                    }
                }

                pts.Add(p);
                ok.Add((byte)(standable ? 1 : 0));
            }
        }

        /// <summary>The point <paramref name="s"/> metres round the rect of
        /// half-extents (hx, hz), starting at its (-x, -z) corner.</summary>
        private static float3 PerimeterPoint(float3 c, float hx, float hz, float s)
        {
            float w = 2f * hx, h = 2f * hz;
            if (s < w) return new float3(c.x - hx + s, c.y, c.z - hz);
            s -= w;
            if (s < h) return new float3(c.x + hx, c.y, c.z - hz + s);
            s -= h;
            if (s < w) return new float3(c.x + hx - s, c.y, c.z + hz);
            s -= w;
            return new float3(c.x - hx, c.y, c.z + hz - math.min(s, h));
        }

        private static bool Free(int j, float radius, float factor,
            NativeList<float3> pts, NativeList<byte> ok, NativeList<byte> taken, NativeList<float4> claims)
        {
            if (ok[j] == 0 || taken[j] != 0) return false;
            float3 p = pts[j];
            for (int c = 0; c < claims.Length; c++)
            {
                float4 cl = claims[c];
                float dx = p.x - cl.x, dz = p.z - cl.z;
                float min = (radius + cl.w) * factor;
                if (dx * dx + dz * dz < min * min) return false;
            }
            return true;
        }

        private static void Claim(int j, float radius,
            NativeList<float3> pts, NativeList<byte> taken, NativeList<float4> claims)
        {
            taken[j] = 1;
            claims.Add(new float4(pts[j], radius));
        }

        /// <summary>The free slot nearest <paramref name="pos"/> (lowest index
        /// on a tie), or -1.</summary>
        private static int Nearest(float3 pos, float radius, float factor,
            NativeList<float3> pts, NativeList<byte> ok, NativeList<byte> taken, NativeList<float4> claims)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int j = 0; j < pts.Length; j++)
            {
                if (!Free(j, radius, factor, pts, ok, taken, claims)) continue;
                float dx = pts[j].x - pos.x, dz = pts[j].z - pos.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = j; }
            }
            return best;
        }

        private static bool IsSlotTarget(EntityManager em, Entity t)
        {
            if (t == Entity.Null || !em.Exists(t)) return false;
            if (!em.HasComponent<BuildingTag>(t) || !em.HasComponent<BuildingSize>(t)) return false;
            if (!em.HasComponent<LocalTransform>(t)) return false;
            if (em.HasComponent<WallTag>(t)) return false;
            if (em.HasComponent<Health>(t) && em.GetComponentData<Health>(t).Value <= 0) return false;
            return true;
        }

        private static bool OnGround(EntityManager em, Entity u)
        {
            if (em.HasComponent<FlyingTag>(u)) return false;
            return !em.HasComponent<NavLayerIndex>(u)
                || em.GetComponentData<NavLayerIndex>(u).Layer != NavLayerIndex.LayerRampart;
        }

        private static bool Dead(EntityManager em, Entity u)
            => em.HasComponent<Health>(u) && em.GetComponentData<Health>(u).Value <= 0;

        /// <summary>Lockstep-stable ordering key: the network id (entity
        /// indices may differ between peers), else the index.</summary>
        private static int StableKey(EntityManager em, Entity e)
            => em.HasComponent<NetworkedEntity>(e) ? em.GetComponentData<NetworkedEntity>(e).NetworkId : e.Index;
    }
}
