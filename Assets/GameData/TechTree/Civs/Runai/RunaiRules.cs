// RunaiRules.cs
// The Runai rules the shared command code asks about
// (docs/Design/Age_1_Runai.md): the one-per-faction units.

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Runai
{
    public static class RunaiRules
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTypeId>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Queues =
        {
            ComponentType.ReadOnly<ProductionQueueItem>(), ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Queues;

        /// <summary>The Caravan Master and the Wayfinder: one per faction.</summary>
        public static bool IsOnePerFaction(string unitId)
            => unitId == "Runai_CaravanMaster" || unitId == "Runai_Wayfinder";

        /// <summary>Why <paramref name="unitId"/> may not be trained now, or null.</summary>
        public static string TrainRefusal(EntityManager em, Faction faction, string unitId)
        {
            if (!IsOnePerFaction(unitId)) return null;
            if (HasLiveOrQueued(em, faction, unitId))
                return unitId == "Runai_Wayfinder" ? "The Wayfinder already rides for you"
                                                   : "Your caravans already have a Caravan Master";
            return null;
        }

        private static bool HasLiveOrQueued(EntityManager em, Faction faction, string unitId)
        {
            var q = QC_Units.Get(em, QT_Units);
            using (var ids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ids.Length; i++)
                    if (facs[i].Value == faction && ids[i].Value.ToString() == unitId) return true;

            var bq = QC_Queues.Get(em, QT_Queues);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var buf = em.GetBuffer<ProductionQueueItem>(ents[i], true);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].Kind == ProductionKind.Train && buf[j].Id.ToString() == unitId) return true;
                }
            return false;
        }
    }
}
