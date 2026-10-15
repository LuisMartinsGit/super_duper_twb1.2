// FeraldisErrandSystem.cs
// Finishes walk-to-and-act Feraldis orders: a slave reaching its extractor
// becomes crew, a soldier reaching the Great Forge is melted, a unit reaching
// the Blood Altar is sacrificed. An errand whose unit stopped short (it was
// re-ordered, or got stuck) is dropped. docs/Design/Age_1_Feraldis.md §3-5.

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisErrandSystem : SystemBase
    {
        private const float Interval = 0.25f;
        /// <summary>A unit idle this long short of its building has been re-ordered or is stuck.</summary>
        private const float GiveUpSeconds = 3f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Errands =
        {
            ComponentType.ReadOnly<FeraldisErrand>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Errands;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Interval)) return;
            var em = EntityManager;
            float reach = FeraldisEconomy.Cfg.errandReach;

            var q = QC_Errands.Get(em, QT_Errands);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var unit = ents[i];
                if (!em.Exists(unit)) continue;
                var errand = em.GetComponentData<FeraldisErrand>(unit);
                var b = errand.Building;
                if (b == Entity.Null || !em.Exists(b)) { em.RemoveComponent<FeraldisErrand>(unit); continue; }

                var pos = em.GetComponentData<LocalTransform>(unit).Position;
                if (TargetGeometry.SurfaceDistXZ(em, pos, b) <= reach)
                {
                    em.RemoveComponent<FeraldisErrand>(unit);
                    switch (errand.Kind)
                    {
                        case FeraldisErrandKind.Staff:
                            var asg = new StaffAssignment { Building = b };
                            if (em.HasComponent<StaffAssignment>(unit)) em.SetComponentData(unit, asg);
                            else em.AddComponentData(unit, asg);
                            break;
                        case FeraldisErrandKind.MeltDown:  FeraldisSacrifice.MeltDown(em, unit, b); break;
                        case FeraldisErrandKind.Sacrifice: FeraldisSacrifice.AltarSacrifice(em, unit, b); break;
                        case FeraldisErrandKind.Board:     WarRig.Board(em, unit, b); break;
                    }
                    continue;
                }

                // Still walking? A unit with no destination left has stopped.
                bool moving = em.HasComponent<DesiredDestination>(unit)
                              && em.GetComponentData<DesiredDestination>(unit).Has != 0;
                errand.StuckSeconds = moving ? 0f : errand.StuckSeconds + Interval;
                if (errand.StuckSeconds >= GiveUpSeconds) em.RemoveComponent<FeraldisErrand>(unit);
                else em.SetComponentData(unit, errand);
            }
        }
    }
}
