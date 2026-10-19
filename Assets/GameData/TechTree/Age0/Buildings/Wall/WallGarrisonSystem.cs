// WallGarrisonSystem.cs
// Keeps a reinforced wall's garrison honest (docs/Design/Age_1_Alanthor.md
// § Garrison slots). Two jobs, both cheap and polled:
//
//   * a module whose occupants changed (one died, one was added) gets its
//     fire rebuilt from the men actually in it;
//   * a unit whose building is GONE is EJECTED alive around where it stood
//     (2026-10-20 — it used to die with it). The building's position is
//     remembered on the unit (WallGarrisonedIn.At), so this one sweep covers
//     every way a building can go: killed, demolished with Delete, collapsed
//     with its node (NodeCollapse), converted, rebuilt at age-up. A building
//     that changed hands, lost its slots or is at 0 HP ejects too, and so
//     does a unit no slot lists any more.
//
// The 2026-05-29 "spread elevated units along the parapet" body is gone:
// the compact curtain has no walkable deck to spread anyone along, and the
// garrison is absorbed rather than parked (see WallGarrison.cs).

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WallGarrisonSystem : SystemBase
    {
        /// <summary>Seconds between sweeps. The garrison changes on player
        /// orders and on deaths, neither of which needs a per-frame answer.</summary>
        private const float PollInterval = 0.5f;
        // SimCadence-phased, NOT a raw `_next -= dt` accumulator: a raw timer
        // carries a machine-dependent phase in from the pre-match frames, and
        // this sweep writes sim state (the Watch Tower's targets since
        // 2026-10-03), so it must land on the same tick on every peer.
        private SimCadence.Periodic _acc;

        private EntityQuery _orphanQuery;

        protected override void OnCreate()
        {
            // Disabled entities are invisible to SystemAPI.Query, so the men
            // inside are found off this explicit query. It also keeps the
            // system running after the LAST garrisonable building falls —
            // gating on the slot buffer alone left those men disabled for ever.
            _orphanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<WallGarrisonedIn>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
            RequireAnyForUpdate(
                GetEntityQuery(ComponentType.ReadOnly<WallGarrisonSlot>()),
                _orphanQuery);
        }

        protected override void OnUpdate()
        {
            if (!_acc.Due(SystemAPI.Time.DeltaTime, PollInterval)) return;

            var em = EntityManager;

            var modules = new List<Entity>();
            foreach (var (_, entity) in SystemAPI
                         .Query<DynamicBuffer<WallGarrisonSlot>>()
                         .WithEntityAccess())
                modules.Add(entity);

            for (int i = 0; i < modules.Count; i++)
                if (em.Exists(modules[i])) WallGarrison.RefreshFire(em, modules[i]);

            // Orphans: the building they were in is gone (or no longer
            // theirs).
            if (_orphanQuery.IsEmptyIgnoreFilter) return;

            var garrisoned = _orphanQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            // Ejected in entity-index order, grouped by the building they
            // were in, so every peer stands every man on the same spot.
            var orphans = new List<Entity>();
            for (int i = 0; i < garrisoned.Length; i++)
            {
                var u = garrisoned[i];
                if (!em.Exists(u)) continue;
                var gi = em.GetComponentData<WallGarrisonedIn>(u);
                if (!StillInside(em, u, gi.Module)) orphans.Add(u);
            }
            garrisoned.Dispose();
            if (orphans.Count == 0) return;
            orphans.Sort((a, b) => a.Index.CompareTo(b.Index));

            var group = new List<Entity>();
            var done = new HashSet<Entity>();
            for (int i = 0; i < orphans.Count; i++)
            {
                var u = orphans[i];
                if (done.Contains(u) || !em.Exists(u)) continue;
                var gi = em.GetComponentData<WallGarrisonedIn>(u);
                group.Clear();
                for (int j = i; j < orphans.Count; j++)
                {
                    var v = orphans[j];
                    if (done.Contains(v) || !em.Exists(v)) continue;
                    if (em.GetComponentData<WallGarrisonedIn>(v).Module != gi.Module) continue;
                    group.Add(v);
                    done.Add(v);
                }
                // A building that still stands (it changed hands, lost its
                // slots) drops them from its slots too.
                if (em.Exists(gi.Module) && em.HasBuffer<WallGarrisonSlot>(gi.Module))
                {
                    var slots = em.GetBuffer<WallGarrisonSlot>(gi.Module);
                    for (int s = 0; s < slots.Length; s++)
                        if (group.Contains(slots[s].Occupant))
                            slots[s] = new WallGarrisonSlot { Occupant = Entity.Null };
                }
                WallGarrison.Eject(em, group, gi.At, gi.Half);
                if (em.Exists(gi.Module)) WallGarrison.RefreshFire(em, gi.Module);
            }
        }

        /// <summary>True while <paramref name="unit"/> is legitimately inside
        /// <paramref name="building"/>: it stands, it is alive, it is still the
        /// unit's side's, and one of its slots names the unit.</summary>
        private static bool StillInside(EntityManager em, Entity unit, Entity building)
        {
            if (building == Entity.Null || !em.Exists(building)) return false;
            if (!em.HasBuffer<WallGarrisonSlot>(building)) return false;
            if (em.HasComponent<Health>(building) && em.GetComponentData<Health>(building).Value <= 0) return false;
            if (em.HasComponent<FactionTag>(building) && em.HasComponent<FactionTag>(unit)
                && em.GetComponentData<FactionTag>(building).Value != em.GetComponentData<FactionTag>(unit).Value)
                return false;
            var slots = em.GetBuffer<WallGarrisonSlot>(building);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Occupant == unit) return true;
            return false;
        }
    }
}
