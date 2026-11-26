// FarmStoneWallSystem.cs
// The Farm's level-3 low stone wall over time (docs/Design/Age_1_Alanthor.md
// § The level-3 wall; decision 57 (f)). Every TickSeconds, for each Farm in
// entity-index order:
//
//   * WANTS A WALL when it is finished, alive and its faction holds Farm level 3
//     (FactionBuildingLevels). A Farm that stops wanting one loses its pieces.
//   * THE OUT-OF-COMBAT CLOCK: QuietTicks counts ticks since the Farm or any
//     of its pieces lost HP (or a piece fell). Read off Health against the last
//     tick's value, so no damage path needs to know about the wall.
//   * LAYOUT: every LayoutEveryTicks the Farm's signature (FarmStoneWall.Signature
//     — neighbours, buildings, roads near it) is compared with the one it was laid
//     out for; only a change lays it out again. The new piece lines are diffed with
//     the slots: a line that stays keeps its piece (and HP) and its timers, a line
//     that goes takes its piece with it, a new line starts ready.
//   * REBUILD: a down slot gets a fresh piece when (it is new) or (it fell at least
//     rebuildSeconds ago and the Farm has been quiet for outOfCombatSeconds), and
//     its strip is clear of units and buildings — otherwise it waits.
//   * Orphans: a piece whose Farm is gone or no longer holds its slot dies.
//
// Removing a piece never destroys it here: its Health is zeroed and DeathSystem
// removes it (the unit-death contract — a synchronous destroy outside DeathSystem
// breaks the EndSimulation buffer). Any change to the set of standing pieces asks
// CostFieldStampSystem for a restamp, on this tick, on every peer.
//
// Lockstep: SimCadence clock (epoch-reset), integer tick counters, every piece of
// state on the Farm (FarmWallState, FarmWallSlot) and the piece (FarmWallPiece),
// so it rides saves; Farms visited in entity-index order; the road graph read only
// after RoadGraphRefreshSystem this tick, and never before it is built.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Roads;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RoadGraphRefreshSystem))]
    public partial class FarmStoneWallSystem : SystemBase
    {
        /// <summary>The wall clock, seconds (engine-side cadence, not balance).</summary>
        private const float TickSeconds = 0.5f;
        /// <summary>How often signatures are compared, in wall ticks (engine-side).</summary>
        private const int LayoutEveryTicks = 2;
        /// <summary>DownTicks of a slot the layout just added: ready at once.</summary>
        private const int FreshSlot = -1;
        /// <summary>A counter never needs to run past this (no overflow in a long match).</summary>
        private const int TickCap = 1 << 24;

        private SimCadence.Periodic _tick;
        private int _layoutTick;
        private int _epoch = -1;

        private readonly List<Entity> _farms = new List<Entity>();
        private readonly List<Entity> _pieces = new List<Entity>();
        private readonly List<float4> _lines = new List<float4>();
        private readonly List<FarmBoundaryLayout.Segment> _segScratch = new List<FarmBoundaryLayout.Segment>();
        private readonly List<FarmWallSlot> _newSlots = new List<FarmWallSlot>();
        private readonly List<float3> _units = new List<float3>();
        private readonly List<float4> _rects = new List<float4>();
        private bool _occupancyGathered;

        private static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<FlyingTag>(),
        };
        private static CachedEntityQuery QC_Units;

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<FarmTag>()));
        }

        protected override void OnUpdate()
        {
            if (!_tick.Due(SystemAPI.Time.DeltaTime, TickSeconds)) return;
            if (_epoch != SimCadence.Epoch) { _epoch = SimCadence.Epoch; _layoutTick = 0; }
            var em = EntityManager;
            var cfg = FarmStoneWallSystemConfig.I;
            if (cfg == null) return;
            bool layoutTick = (_layoutTick++ % LayoutEveryTicks) == 0;
            int rebuildTicks = (int)math.ceil(math.max(0f, cfg.rebuildSeconds) / TickSeconds);
            int quietNeeded = (int)math.ceil(math.max(0f, cfg.outOfCombatSeconds) / TickSeconds);
            bool restamp = false;
            _occupancyGathered = false;
            bool roadsGathered = false;

            _farms.Clear();
            foreach (var (_, e) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<FarmTag, BuildingTag>().WithEntityAccess())
                _farms.Add(e);
            _farms.Sort(ByIndex);

            for (int fi = 0; fi < _farms.Count; fi++)
            {
                var farm = _farms[fi];
                bool want = WantsWall(em, farm);
                bool has = em.HasComponent<FarmWallState>(farm);
                if (!want)
                {
                    if (has && em.HasBuffer<FarmWallSlot>(farm))
                    {
                        var old = em.GetBuffer<FarmWallSlot>(farm);
                        for (int i = 0; i < old.Length; i++) restamp |= Kill(em, old[i].Piece);
                        old.Clear();
                        var st0 = em.GetComponentData<FarmWallState>(farm);
                        st0.Laid = 0; st0.Signature = 0; st0.QuietTicks = 0;
                        em.SetComponentData(farm, st0);
                    }
                    continue;
                }
                if (!has)
                {
                    em.AddComponentData(farm, new FarmWallState { LastFarmHp = em.GetComponentData<Health>(farm).Value });
                    if (!em.HasBuffer<FarmWallSlot>(farm)) em.AddBuffer<FarmWallSlot>(farm);
                }

                var state = em.GetComponentData<FarmWallState>(farm);

                // ── The out-of-combat clock ──
                int farmHp = em.GetComponentData<Health>(farm).Value;
                bool hurt = farmHp < state.LastFarmHp;
                state.LastFarmHp = farmHp;
                var slots = em.GetBuffer<FarmWallSlot>(farm);
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s.Piece != Entity.Null)
                    {
                        if (Standing(em, s.Piece))
                        {
                            int hp = em.GetComponentData<Health>(s.Piece).Value;
                            if (hp < s.LastHp) hurt = true;
                            s.LastHp = hp;
                        }
                        else
                        {
                            // It fell since the last tick.
                            hurt = true;
                            s.Piece = Entity.Null;
                            s.DownTicks = 0;
                            restamp = true;
                        }
                    }
                    else if (s.DownTicks != FreshSlot && s.DownTicks < TickCap) s.DownTicks++;
                    slots[i] = s;
                }
                state.QuietTicks = hurt ? 0 : math.min(TickCap, state.QuietTicks + 1);

                // ── Layout, when what shapes it changed ──
                if ((layoutTick || state.Laid == 0) && RoadGraphSystem.Built)
                {
                    if (!roadsGathered) { FarmStoneWall.GatherRoads(); roadsGathered = true; }
                    uint sig = FarmStoneWall.Signature(em, farm);
                    if (state.Laid == 0 || sig != state.Signature)
                    {
                        state.Signature = sig;
                        state.Laid = 1;
                        _lines.Clear();
                        FarmStoneWall.Layout(em, farm, _lines, _segScratch);
                        slots = em.GetBuffer<FarmWallSlot>(farm);   // Layout made no structural change; re-read for safety
                        restamp |= Relay(em, slots);
                    }
                }

                // ── Rebuild the down slots that may come back ──
                slots = em.GetBuffer<FarmWallSlot>(farm);
                var faction = em.GetComponentData<FactionTag>(farm).Value;
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s.Piece != Entity.Null) continue;
                    bool ready = s.DownTicks == FreshSlot
                        || (s.DownTicks >= rebuildTicks && state.QuietTicks >= quietNeeded);
                    if (!ready) continue;
                    GatherOccupancy(em);
                    float halfT = math.max(FarmStoneWall.MinHalfThickness, cfg.thicknessMeters * 0.5f);
                    if (FarmStoneWall.Occupied(s.A, s.B, halfT, _units, _rects)) continue;

                    var piece = FarmStoneWall.CreatePiece(em, farm, faction, s.A, s.B);
                    slots = em.GetBuffer<FarmWallSlot>(farm);   // CreatePiece is a structural change
                    s.Piece = piece;
                    s.DownTicks = 0;
                    s.LastHp = em.GetComponentData<Health>(piece).Value;
                    slots[i] = s;
                    restamp = true;
                }
                em.SetComponentData(farm, state);
            }

            restamp |= SweepOrphans(em);
            if (restamp) CostFieldStampSystem.RequestRestamp();
        }

        private static int ByIndex(Entity a, Entity b)
            => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Version.CompareTo(b.Version);

        private static bool WantsWall(EntityManager em, Entity farm)
        {
            if (em.HasComponent<UnderConstruction>(farm)) return false;
            if (!em.HasComponent<Health>(farm) || em.GetComponentData<Health>(farm).Value <= 0) return false;
            if (!em.HasComponent<FactionTag>(farm)) return false;
            var f = em.GetComponentData<FactionTag>(farm).Value;
            return TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, f, Farm.BuildingId) >= 3;
        }

        private static bool Standing(EntityManager em, Entity piece)
            => piece != Entity.Null && em.Exists(piece) && em.HasComponent<FarmWallPiece>(piece)
               && em.GetComponentData<Health>(piece).Value > 0;

        /// <summary>Zero a piece's Health so DeathSystem removes it. True when
        /// it was standing (the stamp changes).</summary>
        private static bool Kill(EntityManager em, Entity piece)
        {
            if (!Standing(em, piece)) return false;
            var h = em.GetComponentData<Health>(piece);
            h.Value = 0;
            em.SetComponentData(piece, h);
            return true;
        }

        /// <summary>Replace the slots with the lines in _lines: a line already
        /// held keeps its slot as it is, a slot whose line went loses its piece,
        /// a new line becomes a fresh slot. Output in layout order.</summary>
        private bool Relay(EntityManager em, DynamicBuffer<FarmWallSlot> slots)
        {
            bool changed = false;
            _newSlots.Clear();
            var used = new NativeArray<bool>(slots.Length, Allocator.Temp);
            for (int l = 0; l < _lines.Count; l++)
            {
                float2 a = _lines[l].xy, b = _lines[l].zw;
                int found = -1;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (used[i]) continue;
                    var s = slots[i];
                    if ((s.A.Equals(a) && s.B.Equals(b)) || (s.A.Equals(b) && s.B.Equals(a))) { found = i; break; }
                }
                if (found >= 0)
                {
                    used[found] = true;
                    _newSlots.Add(slots[found]);
                }
                else
                {
                    _newSlots.Add(new FarmWallSlot { A = a, B = b, Piece = Entity.Null, DownTicks = FreshSlot });
                }
            }
            for (int i = 0; i < slots.Length; i++)
                if (!used[i]) changed |= Kill(em, slots[i].Piece);
            used.Dispose();
            slots.Clear();
            for (int i = 0; i < _newSlots.Count; i++) slots.Add(_newSlots[i]);
            return changed;
        }

        /// <summary>Units (x, z, radius) and building footprints, once per tick
        /// and only when some slot wants to come back.</summary>
        private void GatherOccupancy(EntityManager em)
        {
            if (_occupancyGathered) return;
            _occupancyGathered = true;
            _units.Clear();
            _rects.Clear();
            var uq = QC_Units.Get(em, QT_Units);
            using (var ents = uq.ToEntityArray(Allocator.Temp))
            using (var xfs = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                // A lookup, not em.HasComponent + GetComponentData per unit: those
                // walk the unit's ~100 component types each (2026-11-26).
                var radius = GetComponentLookup<Radius>(true);
                for (int i = 0; i < ents.Length; i++)
                {
                    float r = radius.TryGetComponent(ents[i], out var rad) ? rad.Value : 0.5f;
                    _units.Add(new float3(xfs[i].Position.x, xfs[i].Position.z, r));
                }
            }
            var bq = BuildCommandHelper.BuildingQuery(em);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    BuildCommandHelper.BuildingAabb(em, ents[i], xfs[i].Position, out float2 mn, out float2 mx);
                    _rects.Add(new float4(mn, mx));
                }
            }
        }

        /// <summary>Kill every standing piece whose Farm is gone or no longer
        /// holds it in a slot. True when any was standing.</summary>
        private bool SweepOrphans(EntityManager em)
        {
            _pieces.Clear();
            foreach (var (p, e) in SystemAPI.Query<RefRO<FarmWallPiece>>().WithEntityAccess())
                _pieces.Add(e);
            if (_pieces.Count == 0) return false;
            _pieces.Sort(ByIndex);
            bool changed = false;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var e = _pieces[i];
                if (!Standing(em, e)) continue;
                var farm = em.GetComponentData<FarmWallPiece>(e).Farm;
                bool held = false;
                if (farm != Entity.Null && em.Exists(farm) && em.HasBuffer<FarmWallSlot>(farm))
                {
                    var slots = em.GetBuffer<FarmWallSlot>(farm);
                    for (int k = 0; k < slots.Length && !held; k++) held = slots[k].Piece == e;
                }
                if (!held) changed |= Kill(em, e);
            }
            return changed;
        }
    }
}
