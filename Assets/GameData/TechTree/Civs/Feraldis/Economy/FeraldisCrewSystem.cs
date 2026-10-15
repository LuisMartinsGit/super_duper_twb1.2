// FeraldisCrewSystem.cs
// Counts the slaves crewing every staffable Feraldis building, once a second,
// and drops assignments for slaves that were ordered away.
// Canon: docs/Design/Age_1_Feraldis.md §3.1, §3.3, §4.1.
//
// A crew is visible and killable on purpose: a slave works ON SITE (it stands
// within crewRadius of the building), so raiding a Feraldis economy means
// killing slaves, not only burning buildings.

using System.Collections.Generic;
using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisCrewSystem : SystemBase
    {
        private const float Interval = 1f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Staff =
        {
            ComponentType.ReadOnly<StaffAssignment>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Staff;

        static readonly ComponentType[] QT_Overseers =
        {
            ComponentType.ReadOnly<OverseerTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Overseers;

        static readonly ComponentType[] QT_Staffable =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Staffable;

        private readonly Dictionary<Entity, int> _counts = new Dictionary<Entity, int>();

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Interval)) return;
            var em = EntityManager;
            var cfg = FeraldisEconomy.Cfg;
            _counts.Clear();

            // 1. Every staffing slave: still where it should be?
            var drop = new NativeList<Entity>(Allocator.Temp);
            {
                var q = QC_Staff.Get(em, QT_Staff);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var asg = q.ToComponentDataArray<StaffAssignment>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    var b = asg[i].Building;
                    if (b == Entity.Null || !em.Exists(b) || !em.HasComponent<FactionTag>(b)
                        || em.GetComponentData<FactionTag>(b).Value != facs[i].Value
                        || FeraldisEconomy.CrewSlots(em, b) <= 0)
                    { drop.Add(ents[i]); continue; }

                    float d = TargetGeometry.SurfaceDistXZ(em, xfs[i].Position, b);
                    if (d > cfg.crewRadius)
                    {
                        // Walking there still (an errand) is fine; anywhere else
                        // means it was ordered away — the assignment ends.
                        if (!em.HasComponent<FeraldisErrand>(ents[i])) drop.Add(ents[i]);
                        continue;
                    }
                    _counts.TryGetValue(b, out int n);
                    _counts[b] = n + 1;
                }
            }
            for (int i = 0; i < drop.Length; i++) em.RemoveComponent<StaffAssignment>(drop[i]);
            drop.Dispose();

            // 2. Overseers (positions per faction).
            var ovPos = new NativeList<float3>(Allocator.Temp);
            var ovFac = new NativeList<byte>(Allocator.Temp);
            {
                var q = QC_Overseers.Get(em, QT_Overseers);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < xfs.Length; i++) { ovPos.Add(xfs[i].Position); ovFac.Add((byte)facs[i].Value); }
            }

            // 3. Write the crew onto every staffable building of a Feraldis faction.
            {
                var q = QC_Staffable.Get(em, QT_Staffable);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                float r2 = cfg.overseerRadius * cfg.overseerRadius;
                var writes = new List<(Entity, ExtractorCrew)>();
                for (int i = 0; i < ents.Length; i++)
                {
                    int slots = FeraldisEconomy.CrewSlots(em, ents[i]);
                    if (slots <= 0) continue;
                    if (!FeraldisEconomy.IsFeraldis(em, facs[i].Value)) continue;

                    _counts.TryGetValue(ents[i], out int count);
                    float work = FeraldisEconomy.ChainsMultiplier(facs[i].Value);
                    for (int o = 0; o < ovPos.Length; o++)
                        if (ovFac[o] == (byte)facs[i].Value && math.distancesq(ovPos[o].xz, xfs[i].Position.xz) <= r2)
                        { work *= math.max(1f, cfg.overseerWorkMultiplier); break; }

                    writes.Add((ents[i], new ExtractorCrew
                    {
                        Count = math.min(count, slots), Slots = slots, WorkMultiplier = work,
                    }));
                }
                foreach (var (e, crew) in writes)
                {
                    if (em.HasComponent<ExtractorCrew>(e)) em.SetComponentData(e, crew);
                    else em.AddComponentData(e, crew);
                }
            }
            ovPos.Dispose();
            ovFac.Dispose();
        }
    }
}
