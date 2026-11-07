// HeroTrainLimit.cs
// One-per-player gate for King Lexor and the Ledger.

using TheWaningBorder.Core;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class HeroTrainLimit
    {
        static readonly ComponentType[] QT_UniqueUnitTagFactionTag =
        {
            ComponentType.ReadOnly<UniqueUnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_UniqueUnitTagFactionTag;
        static readonly ComponentType[] QT_ProductionQueueItemFactionTag =
        {
            ComponentType.ReadOnly<ProductionQueueItem>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_ProductionQueueItemFactionTag;

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_LedgerTagFactionTag =
        {
            ComponentType.ReadOnly<LedgerTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_LedgerTagFactionTag;

        #endregion
        public static bool IsKingLexorId(string unitId) => unitId == "King Lexor" || unitId == "KingLexor";

        public static bool IsLedgerId(string unitId) => unitId == "Ledger";

        /// <summary>True if the faction already has a live Ledger or one queued
        /// for training anywhere. Each player fields at most ONE Ledger
        /// (design 2026-08-02).</summary>
        public static bool HasLiveOrQueuedLedger(EntityManager em, Faction faction)
        {
            var q = QC_LedgerTagFactionTag.Get(em, QT_LedgerTagFactionTag);
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < facs.Length; i++)
                    if (facs[i].Value == faction) return true;
            }

            var bq = QC_ProductionQueueItemFactionTag.Get(em, QT_ProductionQueueItemFactionTag);
            using (var bents = bq.ToEntityArray(Allocator.Temp))
            using (var bfacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < bents.Length; i++)
                {
                    if (bfacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<ProductionQueueItem>(bents[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].Kind == ProductionKind.Train
                            && IsLedgerId(buf[j].Id.ToString())) return true;
                }
            }
            return false;
        }

        /// <summary>True if the faction already has a live King Lexor or one queued
        /// for training anywhere.</summary>
        public static bool HasLiveOrQueuedKingLexor(EntityManager em, Faction faction)
        {
            var q = QC_UniqueUnitTagFactionTag.Get(em, QT_UniqueUnitTagFactionTag);
            using (var tags = q.ToComponentDataArray<UniqueUnitTag>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tags.Length; i++)
                    if (tags[i].Kind == UniqueUnitKind.KingLexor && facs[i].Value == faction) return true;
            }

            var bq = QC_ProductionQueueItemFactionTag.Get(em, QT_ProductionQueueItemFactionTag);
            using (var bents = bq.ToEntityArray(Allocator.Temp))
            using (var bfacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < bents.Length; i++)
                {
                    if (bfacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<ProductionQueueItem>(bents[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].Kind == ProductionKind.Train
                            && IsKingLexorId(buf[j].Id.ToString())) return true;
                }
            }
            return false;
        }
    }
}
