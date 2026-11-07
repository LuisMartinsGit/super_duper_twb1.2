// SectHeroes.cs
// A SECT'S HERO (docs/Design/Religion.md §4, docs/Design/Heroes.md).
//
//   * one per sect per faction — live or queued;
//   * the first recruit costs 1 RP on top of its resources; a hero recruited
//     again after dying pays only its resources (revival never costs RP);
//   * it levels 1-10 from kills like any hero (Heroes.md) — the components
//     are stamped when it spawns (TrainingSystem).
//
// The RP is taken when the recruit is QUEUED (CommandRouter.TrainCommandDirect)
// and refunded if that queue item is cancelled before the hero has ever
// spawned; PerSectState.HeroRecruited latches when it first spawns.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public static class SectHeroes
    {
        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTypeId>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        static readonly ComponentType[] QT_Queues =
        {
            ComponentType.ReadOnly<ProductionQueueItem>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Queues;

        /// <summary>
        /// The sect whose HERO this unit id is, or null. Only authored heroes
        /// count (SectConfig.HeroIdFor): a sect's line unit is never a hero,
        /// in any cluster (Heroes.md §1, 2026-11-07).
        /// </summary>
        public static string SectIdForUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            for (int i = 0; i < SectConfig.AllSectIds.Length; i++)
            {
                string sect = SectConfig.AllSectIds[i];
                if (SectConfig.HeroIdFor(sect) == unitId) return sect;
            }
            return null;
        }

        /// <summary>The sect whose LINE unit (Religion.md §4: limited per
        /// faction, RP each) this unit id is, or null.</summary>
        public static string SectIdForLimitedUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            for (int i = 0; i < SectConfig.AllSectIds.Length; i++)
            {
                string sect = SectConfig.AllSectIds[i];
                if (SectConfig.UnitIdFor(sect) == unitId) return sect;
            }
            return null;
        }

        /// <summary>One per sect: a live one, or one already in a queue.</summary>
        public static bool HasLiveOrQueued(EntityManager em, Faction faction, string unitId)
            => CountLiveOrQueued(em, faction, unitId, 1) > 0;

        /// <summary>Live plus queued units of <paramref name="unitId"/>, counting
        /// no further than <paramref name="stopAt"/>.</summary>
        public static int CountLiveOrQueued(EntityManager em, Faction faction, string unitId, int stopAt)
        {
            int n = 0;
            var q = QC_Units.Get(em, QT_Units);
            using (var ids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var hps = q.ToComponentDataArray<Health>(Allocator.Temp))
            {
                var id = new FixedString64Bytes(unitId);
                for (int i = 0; i < ids.Length; i++)
                    if (facs[i].Value == faction && hps[i].Value > 0 && ids[i].Value == id
                        && ++n >= stopAt) return n;
            }

            var bq = QC_Queues.Get(em, QT_Queues);
            using (var bents = bq.ToEntityArray(Allocator.Temp))
            using (var bfacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < bents.Length; i++)
                {
                    if (bfacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<ProductionQueueItem>(bents[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].Kind == ProductionKind.Train && buf[j].Id.ToString() == unitId
                            && ++n >= stopAt)
                            return n;
                }
            }
            return n;
        }

        /// <summary>RP this recruit costs: the hero price until the sect's
        /// hero has spawned once, 0 after (revival never costs RP).</summary>
        public static int RpDue(EntityManager em, Faction faction, string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0 || !FactionEconomy.TryGetBank(em, faction, out var bank)
                || !em.HasComponent<SectAdoptionState>(bank)) return 0;
            var sect = em.GetComponentData<SectAdoptionState>(bank).Get(idx);
            return sect.HeroRecruited != 0 ? 0 : FactionReligionPointsHelper.Cfg.heroRp;
        }

        /// <summary>The RP a recruit of this unit costs right now: each sect
        /// unit, and a sect hero's first recruit. 0 for everything else.</summary>
        public static int RpFor(EntityManager em, Faction faction, string unitId)
        {
            if (SectIdForLimitedUnit(unitId) != null) return FactionReligionPointsHelper.Cfg.sectUnitRp;
            string hero = SectIdForUnit(unitId);
            return hero != null ? RpDue(em, faction, hero) : 0;
        }

        /// <summary>
        /// A sect hero just spawned: give it hero levels (Heroes.md) and latch
        /// the sect's recruit so later recruits pay no RP.
        /// </summary>
        public static void OnSpawned(EntityManager em, Entity unit, Faction faction, string unitId)
        {
            string sectId = SectIdForUnit(unitId);
            if (sectId == null) return;

            if (!em.HasComponent<HeroLevel>(unit)) em.AddComponentData(unit, new HeroLevel { Value = 1 });
            if (!em.HasComponent<HeroExperience>(unit)) em.AddComponentData(unit, new HeroExperience { Xp = 0 });

            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0 || !FactionEconomy.TryGetBank(em, faction, out var bank)
                || !em.HasComponent<SectAdoptionState>(bank)) return;
            var state = em.GetComponentData<SectAdoptionState>(bank);
            var sect = state.Get(idx);
            if (sect.HeroRecruited != 0) return;
            sect.HeroRecruited = 1;
            state.Set(idx, sect);
            em.SetComponentData(bank, state);
        }
    }
}
