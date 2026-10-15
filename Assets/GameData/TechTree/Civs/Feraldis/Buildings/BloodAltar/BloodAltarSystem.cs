// BloodAltarSystem.cs
// The Blood Altar (the Feraldis Temple of Ridan) stands on a PERMANENT blood
// pool: the ground under and around it is re-saturated on a fixed interval,
// so it never fades and a home totem can always be planted beside it.
// docs/Design/Age_1_Feraldis.md §5; Religion.md §7 (2026-10-12).
//
// Sacrifice itself is an errand (FeraldisErrandSystem → FeraldisSacrifice).

using TheWaningBorder.Core;
using TheWaningBorder.Influence;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BloodAltarSystem : SystemBase
    {
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Temples =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Temples;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            var cfg = FeraldisEconomy.Cfg;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Mathf.Max(0.5f, cfg.altarBloodInterval))) return;
            if (!BloodMap.Ready) return;
            var em = EntityManager;

            var q = QC_Temples.Get(em, QT_Temples);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (!FeraldisEconomy.IsFeraldis(em, facs[i].Value)) continue;
                Vector3 c = xfs[i].Position;
                float r = cfg.altarBloodRadius;
                BloodMap.AddBlood(c, cfg.altarBloodAmount);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f;
                    BloodMap.AddBlood(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * 0.6f, cfg.altarBloodAmount);
                }
            }
        }
    }
}
