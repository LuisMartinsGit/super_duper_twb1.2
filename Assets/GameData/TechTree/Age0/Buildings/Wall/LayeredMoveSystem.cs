// LayeredMoveSystem.cs
// Two-layer (ground / wall-top) movement, modelled on Age of Empires IV.
//
// The wall upper deck (every wall instance + corner) is a navigable layer
// that any FOOT unit can walk on. Units change layers only at ACCESS POINTS:
//
//   * Towers and Gates  -- friendly-gated: only the owner's units may climb
//     them (the tower's inner door / the gatehouse).
//   * Breach ramps      -- ungated: when a wall instance is destroyed, the
//     instances immediately left/right of the gap become ramps any unit
//     (friend or foe) can climb.
//
// A LayeredMoveOrder asks to move a unit to a world position on a target
// layer. If that differs from the unit's current layer it walks to the
// nearest usable access point, LERPs up/down there, then moves freely on the
// target layer to the destination. Same-layer orders are a plain move.
//
// During the LERP DesiredDestination.Has is cleared so UnitIntegratorSystem
// skips the unit and this system owns its position. The integrator is
// layer-aware so deck units walk only on wall-top cells.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Navigation.UnitIntegratorSystem))]
    public partial class LayeredMoveSystem : SystemBase
    {
        // Wall footprints are ~7 cells, so a unit approaching a tower/gate
        // from outside stops a few metres from its centre — give a generous
        // "reached the access point" radius.
        private const float AccessEntryDist = 6.0f;
        private const float TransitionRate  = 1.6666666f; // ~0.6 s LERP

        // Reused scratch (cleared each tick) so a normal garrison op doesn't
        // allocate. owner == -1 means an UNGATED access (breach ramp).
        private readonly List<float3> _apPos = new List<float3>();
        private readonly List<int> _apOwner = new List<int>();

        private EntityQuery _segQ;

        /// <summary>A deck corner counts as reached within this (XZ).</summary>
        private const float WaypointReach = 0.9f;
        /// <summary>Cells a deck path search may visit before giving up —
        /// loop resolution, not a tunable: a wall-walk is a strip a few cells
        /// wide, so even a long perimeter is a few thousand cells.</summary>
        private const int DeckSearchBudget = 12000;

        protected override void OnCreate()
        {
            RequireAnyForUpdate(
                GetEntityQuery(ComponentType.ReadOnly<LayeredMoveOrder>()),
                GetEntityQuery(ComponentType.ReadOnly<DeckWaypoint>()));
            _segQ = GetEntityQuery(ComponentType.ReadOnly<WallSegmentTag>(),
                                   ComponentType.ReadOnly<WallInstanceRef>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            FollowDeckPaths(em);

            _apPos.Clear();
            _apOwner.Clear();
            GatherGatedAccess(em);   // friendly towers + gates
            GatherBreachRamps(em);   // ungated instances beside a destroyed one
            GatherOverpassRamps();   // ungated overpass-bridge ramps (any faction)

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (orderRO, entity) in
                     SystemAPI.Query<RefRW<LayeredMoveOrder>>().WithEntityAccess())
            {
                if (!em.HasComponent<LocalTransform>(entity)) { ecb.RemoveComponent<LayeredMoveOrder>(entity); continue; }
                var ord = orderRO.ValueRW;
                var xf = em.GetComponentData<LocalTransform>(entity);
                byte layer = em.HasComponent<NavLayerIndex>(entity)
                    ? em.GetComponentData<NavLayerIndex>(entity).Layer : (byte)0;

                // ── Phase 1: LERP between layers. ──
                if (ord.Phase == 1)
                {
                    ord.Progress = math.min(1f, ord.Progress + TransitionRate * dt);
                    if (ord.Progress >= 0.5f && layer != ord.TargetLayer && em.HasComponent<NavLayerIndex>(entity))
                    {
                        var nli = em.GetComponentData<NavLayerIndex>(entity);
                        nli.Layer = ord.TargetLayer;
                        em.SetComponentData(entity, nli);
                        layer = ord.TargetLayer;
                    }
                    xf.Position = math.lerp(ord.TransStart, ord.TransEnd, ord.Progress);
                    em.SetComponentData(entity, xf);

                    if (ord.Progress >= 1f)
                    {
                        Route(em, ecb, entity, ord.TransEnd, ord.FinalDest, ord.TargetLayer);
                        ecb.RemoveComponent<LayeredMoveOrder>(entity);
                    }
                    else
                    {
                        SetDestHasZero(em, entity);
                        orderRO.ValueRW = ord;
                    }
                    continue;
                }

                // ── Already on the target layer: plain move, drop order. ──
                if (layer == ord.TargetLayer)
                {
                    Route(em, ecb, entity, xf.Position, ord.FinalDest, ord.TargetLayer);
                    ecb.RemoveComponent<LayeredMoveOrder>(entity);
                    continue;
                }

                // ── Phase 0: route to the nearest USABLE access point on the
                //    current layer, then start the LERP. ──
                int unitFaction = em.HasComponent<FactionTag>(entity)
                    ? (int)em.GetComponentData<FactionTag>(entity).Value : -1;

                int best = -1; float bestSq = float.MaxValue;
                for (int i = 0; i < _apPos.Count; i++)
                {
                    // Gated access (owner >= 0) is friendly-only; breach ramps
                    // (owner == -1) are usable by anyone.
                    // Owner OR ally — matches the gate rule: a wall that stops
                    // your ally is worse than no wall. docs/Design/Teams.md
                    if (_apOwner[i] >= 0
                        && !Alliances.AreAllied((Faction)_apOwner[i], (Faction)unitFaction)) continue;
                    float dx = _apPos[i].x - xf.Position.x;
                    float dz = _apPos[i].z - xf.Position.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestSq) { bestSq = d; best = i; }
                }

                if (best < 0)
                {
                    // No usable access (no friendly tower/gate, no breach) ->
                    // can't change layers; best-effort plain move + drop.
                    Route(em, ecb, entity, xf.Position, ord.FinalDest, layer);
                    ecb.RemoveComponent<LayeredMoveOrder>(entity);
                    continue;
                }

                float3 ap = _apPos[best];
                float ux = xf.Position.x - ap.x, uz = xf.Position.z - ap.z;
                if (ux * ux + uz * uz <= AccessEntryDist * AccessEntryDist)
                {
                    ord.Phase = 1;
                    ord.Progress = 0f;
                    ord.TransStart = xf.Position;
                    float endY = ord.TargetLayer == NavLayerIndex.LayerRampart
                        ? RampartSurfaceY(ap.x, ap.z)
                        : TerrainUtility.GetHeight(ap.x, ap.z);
                    ord.TransEnd = new float3(ap.x, endY, ap.z);
                    SetDestHasZero(em, entity);
                    orderRO.ValueRW = ord;
                }
                else if (layer == NavLayerIndex.LayerRampart)
                {
                    // On the deck the way to the access point follows the
                    // wall-walk. Planned once per access point (Phase 2
                    // remembers which, in TransStart) — not every tick.
                    if (ord.Phase != 2 || math.distancesq(ord.TransStart.xz, ap.xz) > 1f)
                    {
                        Route(em, ecb, entity, xf.Position, ap, layer);
                        ord.Phase = 2;
                        ord.TransStart = ap;
                    }
                    orderRO.ValueRW = ord;
                }
                else
                {
                    SetDest(em, ecb, entity, new float3(ap.x, xf.Position.y, ap.z));
                    orderRO.ValueRW = ord;
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        // Friendly-gated access: every stone hub, tower and gate, tagged with
        // its owner faction. Climbing one is only allowed for that owner's
        // (or an ally's) units. A palisade has no wall-walk, so none of its
        // pieces is a way up (docs/Design/Age_1_Alanthor.md § The stone wall).
        private void GatherGatedAccess(EntityManager em)
        {
            foreach (var (xf, fac) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<WallHubTag>().WithNone<PalisadeTag, UnderConstruction>())
            { _apPos.Add(xf.ValueRO.Position); _apOwner.Add((int)fac.ValueRO.Value); }

            foreach (var (xf, fac) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<WallTowerTag>().WithNone<PalisadeTag>())
            { _apPos.Add(xf.ValueRO.Position); _apOwner.Add((int)fac.ValueRO.Value); }

            foreach (var (xf, fac) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<WallGateTag>().WithNone<PalisadeTag>())
            { _apPos.Add(xf.ValueRO.Position); _apOwner.Add((int)fac.ValueRO.Value); }
        }

        // Ungated breach ramps: walk each segment's ordered instance buffer; a
        // gap (an instance entity that no longer exists) turns the instances
        // immediately left and right of it into ramps usable by any unit.
        private void GatherBreachRamps(EntityManager em)
        {
            using var segs = _segQ.ToEntityArray(Allocator.Temp);
            for (int s = 0; s < segs.Length; s++)
            {
                // A palisade has no deck to climb onto through its breach.
                if (em.HasComponent<PalisadeTag>(segs[s])) continue;
                var buf = em.GetBuffer<WallInstanceRef>(segs[s], true);
                for (int i = 0; i < buf.Length; i++)
                {
                    if (em.Exists(buf[i].Instance)) continue; // intact
                    // Gap at i -> neighbours i-1 / i+1 become ramps.
                    AddRamp(em, buf, i - 1);
                    AddRamp(em, buf, i + 1);
                }
            }
        }

        private void AddRamp(EntityManager em, DynamicBuffer<WallInstanceRef> buf, int j)
        {
            if (j < 0 || j >= buf.Length) return;
            var inst = buf[j].Instance;
            if (!em.Exists(inst) || !em.HasComponent<LocalTransform>(inst)) return;
            _apPos.Add(em.GetComponentData<LocalTransform>(inst).Position);
            _apOwner.Add(-1); // ungated
        }

        // Overpass bridges are roads, not fortifications: their ramps are
        // usable by every faction (owner == -1, same as breach ramps).
        private void GatherOverpassRamps()
        {
            foreach (var xf in SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<OverpassRampTag>())
            { _apPos.Add(xf.ValueRO.Position); _apOwner.Add(-1); }
        }

        /// <summary>Deck-layer surface height at (x, z): the actual bridge
        /// deck when a BridgeSurface covers the point (overpass meshes),
        /// else the stone wall-walk over the ground there.</summary>
        internal static float RampartSurfaceY(float x, float z)
        {
            if (BridgeSurface.TryGetDeckHeight(x, z, out float deckY))
                return deckY;
            return LayerTransitionSystem.DeckYAt(x, z);
        }

        /// <summary>
        /// Send <paramref name="e"/> to <paramref name="dest"/> on
        /// <paramref name="layer"/>. On the ground that is a plain destination
        /// (the flow fields route it). On the rampart it is a path along the
        /// wall-walk's own cells: the first corner becomes the destination and
        /// the rest wait in the unit's <see cref="DeckWaypoint"/> buffer.
        /// </summary>
        private static void Route(EntityManager em, EntityCommandBuffer ecb, Entity e,
            float3 from, float3 dest, byte layer)
        {
            if (layer != NavLayerIndex.LayerRampart
                || !TryPlanDeckPath(from, dest, out var corners) || corners.Count == 0)
            {
                if (em.HasBuffer<DeckWaypoint>(e)) em.GetBuffer<DeckWaypoint>(e).Clear();
                SetDest(em, ecb, e, DestOnLayer(dest, layer));
                return;
            }

            DynamicBuffer<DeckWaypoint> buf = em.HasBuffer<DeckWaypoint>(e)
                ? ecb.SetBuffer<DeckWaypoint>(e)
                : ecb.AddBuffer<DeckWaypoint>(e);
            for (int i = 0; i < corners.Count; i++)
                buf.Add(new DeckWaypoint { Position = DestOnLayer(corners[i], layer) });
            SetDest(em, ecb, e, DestOnLayer(corners[0], layer));
        }

        /// <summary>
        /// Advance every deck walker to its next corner once it reaches the
        /// current one. A unit whose destination was changed by anything else
        /// (a new order, a chase) or which left the deck drops the rest of
        /// its path — the buffer only ever steers a walk it planned itself.
        /// </summary>
        private void FollowDeckPaths(EntityManager em)
        {
            foreach (var (buf, xf, entity) in SystemAPI
                         .Query<DynamicBuffer<DeckWaypoint>, RefRO<LocalTransform>>()
                         .WithEntityAccess())
            {
                if (buf.Length == 0) continue;
                bool onDeck = em.HasComponent<NavLayerIndex>(entity)
                    && em.GetComponentData<NavLayerIndex>(entity).Layer == NavLayerIndex.LayerRampart;
                if (!onDeck || !em.HasComponent<DesiredDestination>(entity)) { buf.Clear(); continue; }
                if (em.HasComponent<LayeredMoveOrder>(entity)
                    && em.GetComponentData<LayeredMoveOrder>(entity).Phase == 1) continue;

                var dd = em.GetComponentData<DesiredDestination>(entity);
                float3 cur = buf[0].Position;
                if (dd.Has != 0 && math.distancesq(dd.Position.xz, cur.xz) > 0.01f) { buf.Clear(); continue; }

                float3 p = xf.ValueRO.Position;
                bool reached = dd.Has == 0
                    || math.distancesq(p.xz, cur.xz) <= WaypointReach * WaypointReach;
                if (!reached) continue;

                buf.RemoveAt(0);
                if (buf.Length == 0) continue;
                em.SetComponentData(entity, new DesiredDestination { Position = buf[0].Position, Has = 1 });
            }
        }

        static readonly int2[] DeckSteps =
        {
            new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1),
            new int2(1, 1), new int2(-1, 1), new int2(1, -1), new int2(-1, -1),
        };

        /// <summary>
        /// A path over the rampart layer from <paramref name="from"/> to
        /// <paramref name="to"/>, as the corners a unit walks between: a
        /// breadth-first search over walkable deck cells (fixed neighbour
        /// order, no corner cutting — identical on every peer), then pulled
        /// tight so only the bends of the wall remain. False when either end
        /// is off the deck or the two are not joined by it.
        /// </summary>
        private static bool TryPlanDeckPath(float3 from, float3 to, out List<float3> corners)
        {
            corners = null;
            int2 start = NavGridQuery.WorldToCellInt2(from);
            int2 goal = NavGridQuery.WorldToCellInt2(to);
            if (start.x == int.MinValue || goal.x == int.MinValue) return false;
            const byte deck = NavLayerIndex.LayerRampart;
            if (!NavGridQuery.IsCellPassable(goal, deck)) return false;
            if (!NavGridQuery.IsCellPassable(start, deck))
            {
                // Just landed from a climb onto a cell at the deck's edge:
                // start from the nearest walkable neighbour.
                bool found = false;
                for (int i = 0; i < DeckSteps.Length && !found; i++)
                    if (NavGridQuery.IsCellPassable(start + DeckSteps[i], deck)) { start += DeckSteps[i]; found = true; }
                if (!found) return false;
            }

            var parent = new Dictionary<int2, int2>();
            var frontier = new Queue<int2>();
            parent[start] = start;
            frontier.Enqueue(start);
            bool reachedGoal = start.Equals(goal);
            while (frontier.Count > 0 && !reachedGoal && parent.Count < DeckSearchBudget)
            {
                int2 c = frontier.Dequeue();
                for (int i = 0; i < DeckSteps.Length; i++)
                {
                    int2 s = DeckSteps[i];
                    int2 n = c + s;
                    if (parent.ContainsKey(n)) continue;
                    if (!NavGridQuery.IsCellPassable(n, deck)) continue;
                    // A diagonal needs both of its sides open.
                    if (s.x != 0 && s.y != 0
                        && (!NavGridQuery.IsCellPassable(new int2(c.x + s.x, c.y), deck)
                            || !NavGridQuery.IsCellPassable(new int2(c.x, c.y + s.y), deck)))
                        continue;
                    parent[n] = c;
                    if (n.Equals(goal)) { reachedGoal = true; break; }
                    frontier.Enqueue(n);
                }
            }
            if (!reachedGoal) return false;

            var cells = new List<int2>();
            for (int2 c = goal; ; c = parent[c])
            {
                cells.Add(c);
                if (c.Equals(start)) break;
            }
            cells.Reverse();

            // Pull the cell chain tight: from each corner, the furthest cell
            // still in a clear straight line along the deck is the next one.
            corners = new List<float3>();
            float3 at = from;
            int k = 0;
            while (k < cells.Count - 1)
            {
                int far = k + 1;
                for (int j = cells.Count - 1; j > k + 1; j--)
                    if (DeckLineClear(at, NavGridQuery.CellCenter(cells[j]))) { far = j; break; }
                at = far == cells.Count - 1 ? to : NavGridQuery.CellCenter(cells[far]);
                corners.Add(at);
                k = far;
            }
            if (corners.Count == 0) corners.Add(to);
            return true;
        }

        /// <summary>True when the straight line a→b stays on deck cells,
        /// sampled every half cell.</summary>
        private static bool DeckLineClear(float3 a, float3 b)
        {
            float len = math.distance(a.xz, b.xz);
            int steps = math.max(1, (int)math.ceil(len / 0.5f));
            for (int i = 1; i <= steps; i++)
            {
                float3 p = math.lerp(a, b, i / (float)steps);
                int2 c = NavGridQuery.WorldToCellInt2(p);
                if (c.x == int.MinValue || !NavGridQuery.IsCellPassable(c, NavLayerIndex.LayerRampart))
                    return false;
            }
            return true;
        }

        private static float3 DestOnLayer(float3 dest, byte layer)
        {
            float y = layer == NavLayerIndex.LayerRampart
                ? RampartSurfaceY(dest.x, dest.z)
                : TerrainUtility.GetHeight(dest.x, dest.z);
            return new float3(dest.x, y, dest.z);
        }

        // Add is structural -> ECB (we're inside a SystemAPI.Query foreach);
        // SetComponentData on an existing component is non-structural.
        private static void SetDest(EntityManager em, EntityCommandBuffer ecb, Entity e, float3 dest)
        {
            if (em.HasComponent<DesiredDestination>(e))
                em.SetComponentData(e, new DesiredDestination { Position = dest, Has = 1 });
            else
                ecb.AddComponent(e, new DesiredDestination { Position = dest, Has = 1 });
        }

        private static void SetDestHasZero(EntityManager em, Entity e)
        {
            if (em.HasComponent<DesiredDestination>(e))
            {
                var d = em.GetComponentData<DesiredDestination>(e);
                d.Has = 0;
                em.SetComponentData(e, d);
            }
        }
    }
}
