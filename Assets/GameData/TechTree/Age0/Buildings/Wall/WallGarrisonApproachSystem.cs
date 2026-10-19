// WallGarrisonApproachSystem.cs
// The second half of a garrison order (docs/Design/Age_1_Alanthor.md § Watch
// Tower, 2026-10-20). The order is issued at once — WallGarrison.BeginApproach
// sends the unit walking and tags it GarrisonApproach — but the unit goes in
// only when it ARRIVES: within the building's footprint plus
// WallGarrisonConfig.entryRange. It used to be absorbed from anywhere on the
// map the instant the order landed.
//
// Each tick, for every unit still walking:
//   * the building is gone, unfinished, at 0 HP or not the unit's side's
//     -> the walk is dropped (the unit stops where it is);
//   * the building is full -> dropped (the unit stands beside it);
//   * in range -> WallGarrison.Enter;
//   * the walk ended out of range (path blocked, unit idle) -> dropped.
// A NEW order replaces the walk on its own: every order's cleanup removes
// GarrisonApproach (CommandCleanup.ClearWorkOrders, CommandHelper.ClearAllCommands).
//
// Deterministic: units are processed in entity-index order, so when more men
// arrive than there are slots the same ones get in on every peer.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Navigation.UnitIntegratorSystem))]
    public partial class WallGarrisonApproachSystem : SystemBase
    {
        private readonly List<Entity> _walkers = new List<Entity>();

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<GarrisonApproach>()));
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            _walkers.Clear();
            foreach (var (_, entity) in SystemAPI.Query<RefRO<GarrisonApproach>>().WithEntityAccess())
                _walkers.Add(entity);
            if (_walkers.Count == 0) return;
            _walkers.Sort((a, b) => a.Index.CompareTo(b.Index));

            for (int i = 0; i < _walkers.Count; i++)
            {
                var u = _walkers[i];
                if (!em.Exists(u) || !WallGarrison.IsApproaching(em, u)) continue;
                var building = em.GetComponentData<GarrisonApproach>(u).Building;

                if (!Usable(em, u, building)) { WallGarrison.CancelApproach(em, u); continue; }
                if (!WallGarrison.HasFreeSlot(em, building)) { WallGarrison.CancelApproach(em, u); continue; }

                if (WallGarrison.InEntryRange(em, u, building))
                {
                    WallGarrison.Enter(em, u, building);
                    // Enter refuses (and leaves the tag) only on a rule the
                    // checks above already passed; never leave a stale walk.
                    WallGarrison.CancelApproach(em, u);
                    continue;
                }

                if (Idle(em, u)) WallGarrison.CancelApproach(em, u);
            }
        }

        /// <summary>The building still takes this unit: it stands, is
        /// finished and alive, and is the unit's side's.</summary>
        private static bool Usable(EntityManager em, Entity unit, Entity building)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
            if (!em.HasBuffer<WallGarrisonSlot>(building)) return false;
            if (!em.HasComponent<LocalTransform>(building)) return false;
            if (em.HasComponent<UnderConstruction>(building)) return false;
            if (em.HasComponent<Health>(building) && em.GetComponentData<Health>(building).Value <= 0) return false;
            if (em.HasComponent<Health>(unit) && em.GetComponentData<Health>(unit).Value <= 0) return false;
            if (!em.HasComponent<FactionTag>(unit) || !em.HasComponent<FactionTag>(building)) return false;
            return em.GetComponentData<FactionTag>(unit).Value == em.GetComponentData<FactionTag>(building).Value;
        }

        /// <summary>The walk has ended: no live destination and no climb or
        /// layered move still under way.</summary>
        private static bool Idle(EntityManager em, Entity unit)
        {
            if (em.HasComponent<LayeredMoveOrder>(unit) || em.HasComponent<LayerTraversalState>(unit)) return false;
            return !em.HasComponent<DesiredDestination>(unit)
                   || em.GetComponentData<DesiredDestination>(unit).Has == 0;
        }
    }
}
