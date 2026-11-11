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

        // ── Decision 53: a building's level carries its tier techs ────────

        /// <summary>
        /// A BUILDING LEVEL GRANTS ITS TIER TECHS (decision 53,
        /// docs/Design/Age_1_Alanthor.md § Building levels). Every
        /// BuildingLevelDefSO.grantsTechs entry of a level the faction holds
        /// becomes researched for it, through FactionResearchState.CompleteResearch
        /// — the very call a finished research makes — so OnTechCompleted
        /// (TechEffectSystem) applies every effect exactly as if researched.
        /// Prerequisites are not checked.
        ///
        /// THE RULE, per faction with a COMPLETED culture and per granting
        /// level of that culture: level N of type T is granted when the
        /// faction's level of T (<see cref="Get"/>, faction-wide) is at least
        /// N AND either that level is 2+ (a finished level-up, which needed a
        /// finished building of T) or the faction owns a finished building of
        /// T right now. So L1 grants arrive with the faction's first finished
        /// building of the type after its age-up; "has owned" needs no record
        /// of its own, because a granted tech stays in the researched set —
        /// losing every building never takes it back.
        ///
        /// Lockstep and saves: run from a tick-stepped sim system, on its
        /// cadence, walking factions 0..7 and the levels in catalog order — no
        /// clock, no random. Nothing here keeps state: the inputs are the
        /// FactionBuildingLevel rows, the buildings and the researched set,
        /// all of which ride the snapshot, so a loaded game re-derives the same
        /// grants. Idempotent: a tech already researched is skipped (and
        /// CompleteResearch ignores a repeat).
        /// </summary>
        public static void GrantLevelTechs(EntityManager em)
        {
            var research = FactionResearchState.Instance;
            if (research == null || !TechCatalog.IsReady) return;
            var levels = TechCatalog.GrantingLevels;
            if (levels.Count == 0) return;

            System.Collections.Generic.List<(Faction faction, string techId)> grants = null;
            for (int f = 0; f <= (int)Faction.White; f++)
            {
                var faction = (Faction)f;
                byte culture = CultureConfig.GetCompletedCulture(em, faction);
                if (culture == Cultures.None || culture >= CultureConfig.Names.Length) continue;
                string cultureName = CultureConfig.Names[culture];
                int owned = -1;            // per ladder, computed only when needed
                string ownedLadder = null;

                for (int i = 0; i < levels.Count; i++)
                {
                    var lvl = levels[i];
                    if (lvl == null || lvl.grantsTechs == null || lvl.culture != cultureName) continue;
                    if (AllResearched(research, faction, lvl.grantsTechs)) continue;

                    byte have = Get(em, faction, lvl.buildingId);
                    if (lvl.level > have) continue;
                    if (have < 2)
                    {
                        if (ownedLadder != lvl.buildingId)
                        {
                            ownedLadder = lvl.buildingId;
                            owned = CountOwned(em, faction, lvl.buildingId);
                        }
                        if (owned <= 0) continue;
                    }

                    foreach (var techId in lvl.grantsTechs)
                    {
                        if (string.IsNullOrEmpty(techId) || research.HasResearched(faction, techId)) continue;
                        (grants ??= new System.Collections.Generic.List<(Faction, string)>()).Add((faction, techId));
                    }
                }
            }

            // Completed after the walk: OnTechCompleted's handlers make
            // structural changes of their own (as ProductionQueueSystem defers).
            if (grants == null) return;
            foreach (var (faction, techId) in grants)
            {
                research.CompleteResearch(faction, techId);
                TWBLog.Log($"[Upgrade] {faction} granted {techId} by its building level (decision 53)");
            }
        }

        private static bool AllResearched(FactionResearchState research, Faction faction, string[] techs)
        {
            for (int i = 0; i < techs.Length; i++)
                if (!string.IsNullOrEmpty(techs[i]) && !research.HasResearched(faction, techs[i])) return false;
            return true;
        }
    }
}
