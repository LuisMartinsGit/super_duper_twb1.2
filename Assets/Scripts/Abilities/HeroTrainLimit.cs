// HeroTrainLimit.cs
// One-per-player hero gate + escalating respawn cost for King Lexor.
//   - Only one live/queued King Lexor per faction (checked authoritatively at
//     the training-command gate).
//   - Each time he dies, his next training takes +15% longer (RespawnTrainMult).
//
// NOTE: the respawn counter is a static dictionary (simple, single-player
// correct). For lockstep multiplayer this should live on a per-faction ECS
// singleton so all clients agree — flagged for the netcode pass.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Abilities
{
    public static class HeroTrainLimit
    {
        private const float RespawnTaxPerDeath = 0.15f; // +15% training time per respawn
        private static readonly Dictionary<int, int> _kingLexorRespawns = new Dictionary<int, int>();

        public static int RespawnCount(Faction f) => _kingLexorRespawns.TryGetValue((int)f, out var n) ? n : 0;

        public static float RespawnTrainMult(Faction f) => 1f + RespawnTaxPerDeath * RespawnCount(f);

        public static void RecordRespawn(Faction f)
        {
            int k = (int)f;
            _kingLexorRespawns[k] = (_kingLexorRespawns.TryGetValue(k, out var n) ? n : 0) + 1;
        }

        public static void ResetAll() => _kingLexorRespawns.Clear();

        public static bool IsKingLexorId(string unitId) => unitId == "King Lexor" || unitId == "KingLexor";

        public static bool IsLedgerId(string unitId) => unitId == "Ledger";

        /// <summary>True if the faction already has a live Ledger or one queued
        /// for training anywhere. Each player fields at most ONE Ledger
        /// (design 2026-08-02).</summary>
        public static bool HasLiveOrQueuedLedger(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<LedgerTag>(), ComponentType.ReadOnly<FactionTag>());
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < facs.Length; i++)
                    if (facs[i].Value == faction) return true;
            }

            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<TrainQueueItem>(), ComponentType.ReadOnly<FactionTag>());
            using (var bents = bq.ToEntityArray(Allocator.Temp))
            using (var bfacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < bents.Length; i++)
                {
                    if (bfacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<TrainQueueItem>(bents[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (IsLedgerId(buf[j].UnitId.ToString())) return true;
                }
            }
            return false;
        }

        /// <summary>True if the faction already has a live King Lexor or one queued
        /// for training anywhere.</summary>
        public static bool HasLiveOrQueuedKingLexor(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<UniqueUnitTag>(), ComponentType.ReadOnly<FactionTag>());
            using (var tags = q.ToComponentDataArray<UniqueUnitTag>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tags.Length; i++)
                    if (tags[i].Kind == UniqueUnitKind.KingLexor && facs[i].Value == faction) return true;
            }

            var bq = em.CreateEntityQuery(ComponentType.ReadOnly<TrainQueueItem>(), ComponentType.ReadOnly<FactionTag>());
            using (var bents = bq.ToEntityArray(Allocator.Temp))
            using (var bfacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < bents.Length; i++)
                {
                    if (bfacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<TrainQueueItem>(bents[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (IsKingLexorId(buf[j].UnitId.ToString())) return true;
                }
            }
            return false;
        }
    }
}
