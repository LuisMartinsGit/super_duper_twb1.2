// WarRig.cs
// The War Rig carries a squad of infantry (docs/Design/Age_1_Feraldis.md
// §7.5): infantry ordered onto it walk to it and board (hidden, disabled —
// the wall-garrison pattern); Unload stands them around the rig. If the rig
// dies they climb out where it fell.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Feraldis
{
    public static class WarRig
    {
        public static bool CanLoad(EntityManager em, Entity unit, Entity rig)
        {
            if (rig == Entity.Null || !em.Exists(rig) || !em.HasComponent<WarRigTag>(rig)) return false;
            if (!em.HasBuffer<WarRigPassenger>(rig)) return false;
            if (!em.HasComponent<UnitTag>(unit) || em.HasComponent<RidingWarRig>(unit)) return false;
            if (em.HasComponent<SiegeTag>(unit) || em.HasComponent<CanBuild>(unit)) return false;
            if (em.HasComponent<UnitTagsData>(unit)
                && (em.GetComponentData<UnitTagsData>(unit).Mask & UnitTagParse.Tag("infantry")) == 0) return false;
            return Count(em, rig) < FeraldisCombat.Cfg.warRigCapacity;
        }

        public static int Count(EntityManager em, Entity rig)
        {
            int n = 0;
            var buf = em.GetBuffer<WarRigPassenger>(rig, true);
            for (int i = 0; i < buf.Length; i++) if (buf[i].Unit != Entity.Null && em.Exists(buf[i].Unit)) n++;
            return n;
        }

        /// <summary>The order: walk to the rig; FeraldisErrandSystem boards it on arrival.</summary>
        public static void Load(EntityManager em, Entity unit, Entity rig)
        {
            if (!CanLoad(em, unit, rig)) return;
            var errand = new FeraldisErrand { Building = rig, Kind = FeraldisErrandKind.Board };
            if (em.HasComponent<FeraldisErrand>(unit)) em.SetComponentData(unit, errand);
            else em.AddComponentData(unit, errand);
            MoveCommandHelper.Execute(em, unit, em.GetComponentData<LocalTransform>(rig).Position);
        }

        /// <summary>Board now (the unit has reached the rig). Structural.</summary>
        public static void Board(EntityManager em, Entity unit, Entity rig)
        {
            if (!CanLoad(em, unit, rig)) return;
            em.GetBuffer<WarRigPassenger>(rig).Add(new WarRigPassenger { Unit = unit });
            em.AddComponentData(unit, new RidingWarRig
            {
                Rig = rig, LastRigPosition = em.GetComponentData<LocalTransform>(rig).Position,
            });
            var spawn = PresentationSpawnSystem.Instance;
            if (spawn != null) spawn.ForceRespawn(unit);
            em.AddComponent<Disabled>(unit);
        }

        public static void Unload(EntityManager em, Entity rig)
        {
            if (!em.Exists(rig) || !em.HasBuffer<WarRigPassenger>(rig)) return;
            var units = new NativeList<Entity>(Allocator.Temp);
            var buf = em.GetBuffer<WarRigPassenger>(rig);
            for (int i = 0; i < buf.Length; i++) units.Add(buf[i].Unit);
            buf.Clear();
            float3 at = em.GetComponentData<LocalTransform>(rig).Position;
            for (int i = 0; i < units.Length; i++) StepOut(em, units[i], at, i, units.Length);
            units.Dispose();
        }

        /// <summary>Stand one passenger on the ground in a ring around <paramref name="at"/>.</summary>
        public static void StepOut(EntityManager em, Entity u, float3 at, int index, int count)
        {
            if (u == Entity.Null || !em.Exists(u)) return;
            float a = index * (2f * math.PI / math.max(1, count));
            float3 spot = at + new float3(math.cos(a), 0f, math.sin(a)) * 3f;
            spot.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(spot.x, spot.z);
            if (em.HasComponent<Disabled>(u)) em.RemoveComponent<Disabled>(u);
            if (em.HasComponent<RidingWarRig>(u)) em.RemoveComponent<RidingWarRig>(u);
            if (em.HasComponent<LocalTransform>(u))
            {
                var t = em.GetComponentData<LocalTransform>(u);
                em.SetComponentData(u, LocalTransform.FromPositionRotationScale(spot, t.Rotation, t.Scale));
            }
        }
    }

    /// <summary>Keeps each passenger's idea of where its rig is, and lets them out when it dies.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WarRigSystem : SystemBase
    {
        private SimCadence.Periodic _cadence;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, 0.5f)) return;
            var em = EntityManager;
            var riders = new NativeList<Entity>(Allocator.Temp);
            foreach (var (r, e) in SystemAPI.Query<RefRO<RidingWarRig>>()
                         .WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
                riders.Add(e);

            int stepped = 0;
            for (int i = 0; i < riders.Length; i++)
            {
                var u = riders[i];
                var ride = em.GetComponentData<RidingWarRig>(u);
                bool rigAlive = ride.Rig != Entity.Null && em.Exists(ride.Rig)
                                && em.HasComponent<Health>(ride.Rig) && em.GetComponentData<Health>(ride.Rig).Value > 0;
                if (rigAlive)
                {
                    ride.LastRigPosition = em.GetComponentData<LocalTransform>(ride.Rig).Position;
                    em.SetComponentData(u, ride);
                    // Carried units ride along (fog, selection boxes, the ring on unload).
                    if (em.HasComponent<LocalTransform>(u))
                    {
                        var t = em.GetComponentData<LocalTransform>(u);
                        t.Position = ride.LastRigPosition;
                        em.SetComponentData(u, t);
                    }
                    continue;
                }
                WarRig.StepOut(em, u, ride.LastRigPosition, stepped++, 8);
            }
            riders.Dispose();
        }
    }
}
