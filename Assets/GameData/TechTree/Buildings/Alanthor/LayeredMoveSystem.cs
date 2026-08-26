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

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<LayeredMoveOrder>()));
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

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
                        SetDest(em, ecb, entity, DestOnLayer(ord.FinalDest, ord.TargetLayer));
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
                    SetDest(em, ecb, entity, DestOnLayer(ord.FinalDest, ord.TargetLayer));
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
                    SetDest(em, ecb, entity, DestOnLayer(ord.FinalDest, layer));
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
                else
                {
                    SetDest(em, ecb, entity, new float3(ap.x, xf.Position.y, ap.z));
                    orderRO.ValueRW = ord;
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        // Friendly-gated access: every tower and gate, tagged with its owner
        // faction. Climbing one is only allowed for that owner's units.
        private void GatherGatedAccess(EntityManager em)
        {
            foreach (var (xf, fac) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<WallTowerTag>())
            { _apPos.Add(xf.ValueRO.Position); _apOwner.Add((int)fac.ValueRO.Value); }

            foreach (var (xf, fac) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithAll<WallGateTag>())
            { _apPos.Add(xf.ValueRO.Position); _apOwner.Add((int)fac.ValueRO.Value); }
        }

        // Ungated breach ramps: walk each segment's ordered instance buffer; a
        // gap (an instance entity that no longer exists) turns the instances
        // immediately left and right of it into ramps usable by any unit.
        private void GatherBreachRamps(EntityManager em)
        {
            var segQ = GetEntityQuery(ComponentType.ReadOnly<WallSegmentTag>(),
                                      ComponentType.ReadOnly<WallInstanceRef>());
            using var segs = segQ.ToEntityArray(Allocator.Temp);
            for (int s = 0; s < segs.Length; s++)
            {
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
        /// else the uniform wall-deck constant.</summary>
        internal static float RampartSurfaceY(float x, float z)
        {
            if (BridgeSurface.TryGetDeckHeight(x, z, out float deckY))
                return deckY;
            return LayerTransitionSystem.DeckY;
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
