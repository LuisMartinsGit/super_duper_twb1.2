// BloodsightSystem.cs
// Bloodsight (Feraldis Fortress research, docs/Design/Age_1_Feraldis.md §7.6):
// a faction's Scouts smell blood — every BLEEDING enemy within
// BloodsightRange of one of its Scouts is revealed through the fog for a
// moment, refreshed every second while it keeps bleeding. Uses the same
// reveal circle as the sect scrying powers (SectActivePowerHelper.SpawnReveal).

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BloodsightSystem : SystemBase
    {
        private const float Interval = 1f;
        /// <summary>Scout-to-victim distance at which a bleeding enemy is smelt. Engine-side: a reveal radius, not a balance value.</summary>
        private const float BloodsightRange = 60f;
        private const float RevealRadius = 4f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Scouts =
        {
            ComponentType.ReadOnly<FeraldisScoutTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Scouts;

        static readonly ComponentType[] QT_Bleeding =
        {
            ComponentType.ReadOnly<Bleeding>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Bleeding;

        protected override void OnUpdate()
        {
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Interval)) return;
            var em = EntityManager;

            var sq = QC_Scouts.Get(em, QT_Scouts);
            if (sq.IsEmpty) return;
            var bq = QC_Bleeding.Get(em, QT_Bleeding);
            if (bq.IsEmpty) return;

            using var sx = sq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sf = sq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bx = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var bf = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            float r2 = BloodsightRange * BloodsightRange;

            for (int s = 0; s < sx.Length; s++)
            {
                var fac = sf[s].Value;
                if (!FeraldisCombat.Researched(fac, "Feraldis_Bloodsight")) continue;
                for (int b = 0; b < bx.Length; b++)
                {
                    if (!Alliances.AreHostile(fac, bf[b].Value)) continue;
                    if (math.distancesq(sx[s].Position.xz, bx[b].Position.xz) > r2) continue;
                    TheWaningBorder.Systems.Sect.SectActivePowerHelper.SpawnReveal(em, fac, bx[b].Position, RevealRadius, Interval * 1.5f);
                }
            }
        }
    }
}
