// CurseUnitCap.cs
// THE CURSE IS CAPPED (docs/Design/Territory_Claims.md §6.8, 2026-10-04).
//
// One shared gate for every path that raises a curse unit: the living
// curse's garrisons, claim / fill parties, the Shardroot hunt and the attack
// waves (CurseTerritorySystem), and the event spawners that still run beside
// it (BloodCurseSpawnSystem, RitualBacklashSystem, CorruptionDefenseSystem,
// VeilFieldSystem's infection eruption, NodeStateDeathInterceptSystem's
// Violent Extraction final wave). Each asks Headroom ONCE per spawn batch and
// raises at most that many, decrementing its local copy as it goes.
//
// The count is every BorderUnitTag entity alive in the world — one cached
// query, CalculateEntityCount (chunk counts, no iteration). Spawns go through
// the EntityManager directly, so a unit raised earlier in the same tick is
// already in the count the next caller reads. A dying unit (Health 0, not
// yet destroyed by DeathSystem) still counts until it is gone: the gate errs
// toward fewer, never more.
//
// Determinism: the count is pure sim state and every caller runs in-sim on
// every peer in system order, so every peer reads the same headroom.
//
// THE CAP FOLLOWS THE GROUND (§6.8, 2026-10-07): the limit is
// curseUnitsBase + curseUnitsPerTerritory x the territories the curse holds
// (TerritoryOwnership.CurseHeldCount), never above maxCurseUnits — all three
// on BorderSettings.asset. The curse that conquers fields more; the curse
// that is driven back fields less, and its surplus is simply not replaced
// (nothing is culled). The held count is the ownership array the lockstep
// claim tick writes, so every peer reads the same cap.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data.Border;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Border
{
    public static class CurseUnitCap
    {
        private static readonly ComponentType[] QT_CurseUnits =
            { ComponentType.ReadOnly<BorderUnitTag>() };
        private static CachedEntityQuery QC_CurseUnits;

        /// <summary>The cap NOW: min(maxCurseUnits, curseUnitsBase +
        /// curseUnitsPerTerritory x territories the curse holds), from
        /// BorderSettings.asset (Territory_Claims.md §6.8).</summary>
        public static int Max
        {
            get
            {
                var s = BorderSettings.Get();
                int ceiling = math.max(0, s.maxCurseUnits);
                long scaled = (long)math.max(0, s.curseUnitsBase)
                            + (long)math.max(0, s.curseUnitsPerTerritory) * TerritoryOwnership.CurseHeldCount;
                return (int)math.min((long)ceiling, scaled);
            }
        }

        /// <summary>Curse units alive right now.</summary>
        public static int Live(EntityManager em)
            => QC_CurseUnits.Get(em, QT_CurseUnits).CalculateEntityCount();

        /// <summary>How many more curse units may be raised right now
        /// (0 when the curse is at or over the cap).</summary>
        public static int Headroom(EntityManager em)
            => math.max(0, Max - Live(em));
    }
}
