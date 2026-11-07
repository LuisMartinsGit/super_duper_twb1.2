// FactionHeroRecords.cs
// A HERO'S LEVEL IS NEVER RESET (docs/Design/Heroes.md §5, 2026-11-07). When a
// hero falls its level and XP are written here, per faction and per hero, and
// the next one trained comes back with both; the price of dying is money and
// time, scaled with the level it fell at.
//
// Storage: a FactionHeroRecord buffer on the faction's bank entity, as
// FactionBuildingLevels does. It is sim state on an ordinary entity, so it
// rides the save snapshot, and it is written only from the tick-stepped
// HeroLevelSystem, so every lockstep peer holds the same rows.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

public struct FactionHeroRecord : IBufferElementData
{
    public FixedString64Bytes UnitId;
    public byte Level;
    public int Xp;
}

namespace TheWaningBorder.Abilities
{
    public static class FactionHeroRecords
    {
        /// <summary>King Lexor answers to two ids; every other unit to one.</summary>
        private static FixedString64Bytes Key(string unitId)
            => new FixedString64Bytes(HeroTrainLimit.IsKingLexorId(unitId) ? "King Lexor" : unitId);

        /// <summary>Remember what fell. Structural on the bank the first time:
        /// call it outside any query iteration, from the simulation only.</summary>
        public static void Record(EntityManager em, Faction faction, string unitId, byte level, int xp)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;
            if (!em.HasBuffer<FactionHeroRecord>(bank)) em.AddBuffer<FactionHeroRecord>(bank);
            var rows = em.GetBuffer<FactionHeroRecord>(bank);
            var key = Key(unitId);
            var row = new FactionHeroRecord { UnitId = key, Level = HeroProgressionConfig.Clamp(level), Xp = xp };
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].UnitId == key) { rows[i] = row; return; }
            rows.Add(row);
        }

        /// <summary>The level and XP this faction's hero fell with, if it has fallen.</summary>
        public static bool TryGet(EntityManager em, Faction faction, string unitId, out byte level, out int xp)
        {
            level = 0; xp = 0;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasBuffer<FactionHeroRecord>(bank)) return false;
            var rows = em.GetBuffer<FactionHeroRecord>(bank, true);
            var key = Key(unitId);
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].UnitId == key) { level = rows[i].Level; xp = rows[i].Xp; return true; }
            return false;
        }

        /// <summary>Multiplier on cost and training time for bringing this
        /// hero back; 1 when it has never fallen.</summary>
        public static float PriceMultiplier(EntityManager em, Faction faction, string unitId)
            => TryGet(em, faction, unitId, out byte level, out _)
                ? HeroProgressionConfig.RevivalMultiplier(level) : 1f;
    }
}
