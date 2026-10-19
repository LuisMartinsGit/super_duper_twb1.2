// FactionBuildingLevels.cs
// A BUILDING LEVEL IS A FACTION FACT, PER BUILDING TYPE (2026-10-19,
// docs/Design/Overview.md § Building levels). Levelling one Barracks levels
// every Barracks the faction owns, and every Barracks it builds later is
// raised straight to that level when it finishes. Every culture.
//
// Storage: a FactionBuildingLevel buffer on the faction's bank entity, one
// row per ladder id (UpgradeBuildingCommandHelper.ResolveBuildingId) that has
// been levelled PAST the free age-up level. No row = level 1 once the
// faction's culture is complete, 0 before — so the age-up L1 needs no write
// and cannot disagree with the buildings it is applied to. The buffer is sim
// state on an ordinary entity: it rides the save snapshot (SerializeUtility)
// and is written only from the tick-stepped production queue, so every
// lockstep peer holds the same rows in the same order.
//
// The rule that keeps it one decision: only ONE level-up of a type may be
// queued or running per faction (UpgradeInFlight). The building it runs on is
// just where the clock lives — it pays its BuildingUpgrading downtime alone.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Buildings
{
    public static class FactionBuildingLevels
    {
        #region Cached queries

        static readonly ComponentType[] QT_Upgradeable =
        {
            ComponentType.ReadOnly<BuildingUpgradeable>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Upgradeable;

        static readonly ComponentType[] QT_Levelled =
        {
            ComponentType.ReadOnly<BuildingUpgradeState>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Levelled;

        #endregion

        /// <summary>
        /// The level <paramref name="faction"/>'s buildings of ladder
        /// <paramref name="ladderId"/> stand at: its row, else 1 with a
        /// completed culture (the free age-up level), else 0.
        /// </summary>
        public static byte Get(EntityManager em, Faction faction, string ladderId)
        {
            byte floor = CultureConfig.GetCompletedCulture(em, faction) != Cultures.None ? (byte)1 : (byte)0;
            if (string.IsNullOrEmpty(ladderId)) return floor;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return floor;
            if (!em.HasBuffer<FactionBuildingLevel>(bank)) return floor;
            var rows = em.GetBuffer<FactionBuildingLevel>(bank, true);
            var key = new FixedString64Bytes(ladderId);
            for (int i = 0; i < rows.Length; i++)
                if (rows[i].BuildingId == key)
                    return rows[i].Level > floor ? rows[i].Level : floor;
            return floor;
        }

        /// <summary>The faction level of <paramref name="building"/>'s own
        /// type (its owner, its ladder).</summary>
        public static byte LevelFor(EntityManager em, Entity building)
        {
            if (!em.HasComponent<FactionTag>(building)) return 0;
            return Get(em, em.GetComponentData<FactionTag>(building).Value,
                UpgradeBuildingCommandHelper.ResolveBuildingId(em, building));
        }

        /// <summary>
        /// Raise the faction's level of a type (never lowers it). Structural
        /// on the bank the first time (adds the buffer) — call from the
        /// simulation only.
        /// </summary>
        public static void Raise(EntityManager em, Faction faction, string ladderId, byte level)
        {
            if (string.IsNullOrEmpty(ladderId)) return;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;
            if (!em.HasBuffer<FactionBuildingLevel>(bank)) em.AddBuffer<FactionBuildingLevel>(bank);
            var rows = em.GetBuffer<FactionBuildingLevel>(bank);
            var key = new FixedString64Bytes(ladderId);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].BuildingId != key) continue;
                if (rows[i].Level >= level) return;
                var r = rows[i];
                r.Level = level;
                rows[i] = r;
                return;
            }
            rows.Add(new FactionBuildingLevel { BuildingId = key, Level = level });
        }

        /// <summary>
        /// Is a level-up of this type queued or running on ANY building of the
        /// faction? <paramref name="where"/> is that building.
        /// </summary>
        public static bool UpgradeInFlight(EntityManager em, Faction faction, string ladderId, out Entity where)
        {
            where = Entity.Null;
            if (string.IsNullOrEmpty(ladderId)) return false;
            var q = QC_Upgradeable.Get(em, QT_Upgradeable);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                // The cheap test first: almost no building has one queued.
                if (!UpgradeBuildingCommandHelper.IsUpgradeQueued(em, ents[i])) continue;
                if (UpgradeBuildingCommandHelper.ResolveBuildingId(em, ents[i]) != ladderId) continue;
                where = ents[i];
                return true;
            }
            return false;
        }

        /// <summary>Finished buildings of this type the faction owns — how
        /// many one level-up raises (UI readout, AI valuation).</summary>
        public static int CountOwned(EntityManager em, Faction faction, string ladderId)
        {
            if (string.IsNullOrEmpty(ladderId)) return 0;
            var q = QC_Upgradeable.Get(em, QT_Upgradeable);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (UpgradeBuildingCommandHelper.ResolveBuildingId(em, ents[i]) != ladderId) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// A level-up of this type finished: record it and apply it to EVERY
        /// finished building of the type the faction owns. Buildings still
        /// under construction take it when they finish (BuildingLevelOneSeed
        /// / BuildingCultureAutoLevel read <see cref="Get"/>).
        /// </summary>
        public static void ApplyToAll(EntityManager em, Faction faction, string ladderId, byte level)
        {
            Raise(em, faction, ladderId, level);
            if (string.IsNullOrEmpty(ladderId)) return;

            // Snapshot: ApplyLevel may add an attack (structural).
            var q = QC_Levelled.Get(em, QT_Levelled);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var e = ents[i];
                if (!em.Exists(e) || em.HasComponent<UnderConstruction>(e)) continue;
                if (UpgradeBuildingCommandHelper.ResolveBuildingId(em, e) != ladderId) continue;
                if (em.GetComponentData<BuildingUpgradeState>(e).Level >= level) continue;
                BuildingUpgradeSystem.ApplyLevel(em, e, level);
            }
        }
    }
}
