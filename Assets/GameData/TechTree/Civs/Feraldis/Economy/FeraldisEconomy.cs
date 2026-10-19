// FeraldisEconomy.cs
// The Feraldis economy's shared rules: who is Feraldis, which buildings take a
// crew and how much a crew earns, and the slave cap.
// Canon: docs/Design/Age_1_Feraldis.md §3 (2026-10-12).

using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    public static class FeraldisEconomy
    {
        private static FeraldisEconomyConfig _cfg;
        public static FeraldisEconomyConfig Cfg =>
            _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<FeraldisEconomyConfig>());

        public const string SlaveId = "Feraldis_Slave";
        public const string OverseerId = "Feraldis_Overseer";

        /// <summary>True once <paramref name="faction"/> has aged up into Feraldis.</summary>
        public static bool IsFeraldis(EntityManager em, Faction faction)
            => CultureConfig.GetCompletedCulture(em, faction) == Cultures.Feraldis;

        public static bool IsFeraldisOwned(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e) && IsFeraldis(em, em.GetComponentData<FactionTag>(e).Value);

        // ── crews ────────────────────────────────────────────────────────

        /// <summary>Crew slots this building offers a Feraldis faction, 0 when it takes no crew.</summary>
        public static int CrewSlots(EntityManager em, Entity building)
        {
            if (building == Entity.Null || !em.Exists(building)) return 0;
            var c = Cfg;
            if (em.HasComponent<GreatForgeTag>(building)) return c.crewSlotsForge;
            if (em.HasComponent<VeilstoneMineTag>(building)) return c.crewSlotsVeilstoneMine;
            if (em.HasComponent<MineTag>(building)) return c.crewSlotsMine;
            // a depleted Work Camp works no node (Territory_Claims.md § 11.3,
            // 2026-10-19): no crew, so its slaves are released
            if (em.HasComponent<GathererHutTag>(building))
                return em.HasComponent<DepletedHutTag>(building) ? 0 : c.crewSlotsWorkCamp;
            return 0;
        }

        /// <summary>
        /// The share of its slot income an extractor pays: 1 for anyone but
        /// Feraldis; for Feraldis, from <c>crewIncomeAtZero</c> (no crew) up to
        /// <c>crewIncomeAtFull</c> (full crew), the crew's part scaled by its
        /// work multiplier (Overseer, Chains). §3.1.
        /// </summary>
        public static float IncomeMultiplier(EntityManager em, Entity extractor)
        {
            if (!IsFeraldisOwned(em, extractor)) return 1f;
            var c = Cfg;
            float fill = 0f, work = 1f;
            if (em.HasComponent<ExtractorCrew>(extractor))
            {
                var crew = em.GetComponentData<ExtractorCrew>(extractor);
                if (crew.Slots > 0) fill = Mathf.Clamp01(crew.Count / (float)crew.Slots);
                work = Mathf.Max(1f, crew.WorkMultiplier);
            }
            return c.crewIncomeAtZero + (c.crewIncomeAtFull - c.crewIncomeAtZero) * fill * work;
        }

        /// <summary>The Chains research's work multiplier for a faction (≥ 1).</summary>
        public static float ChainsMultiplier(Faction faction)
        {
            var research = FactionResearchState.Instance;
            var tiers = Cfg.chainsWorkMultipliers;
            if (research == null || tiers == null) return 1f;
            float m = 1f;
            for (int i = 0; i < tiers.Length && i < ChainsTechs.Length; i++)
                if (research.HasResearched(faction, ChainsTechs[i])) m *= Mathf.Max(1f, tiers[i]);
            return m;
        }

        public static readonly string[] ChainsTechs =
            { "Feraldis_IronChains", "Feraldis_VeilstoneChains", "Feraldis_VeilsteelChains" };

        // ── the slave cap (§3.2) ─────────────────────────────────────────

        static readonly ComponentType[] QT_Slaves =
            { ComponentType.ReadOnly<SlaveTag>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Slaves;

        static readonly ComponentType[] QT_Pens =
            { ComponentType.ReadOnly<HutTag>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Pens;

        static readonly ComponentType[] QT_Queues =
            { ComponentType.ReadOnly<ProductionQueueItem>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Queues;

        /// <summary>Slaves a faction may own: the Fortress's own allowance plus every finished Slave Pen's.</summary>
        public static int SlaveCap(EntityManager em, Faction faction)
        {
            var c = Cfg;
            int cap = c.slaveCapFortress;
            bool expansion = FactionResearchState.Instance != null
                && FactionResearchState.Instance.HasResearched(faction, "Feraldis_PenExpansion");

            var q = QC_Pens.Get(em, QT_Pens);
            using var pens = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < pens.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(pens[i])) continue;
                int level = em.HasComponent<BuildingUpgradeState>(pens[i])
                    ? Mathf.Max(1, em.GetComponentData<BuildingUpgradeState>(pens[i]).Level) : 1;
                var def = BuildingUpgradeConfig.LevelDef(em, faction, "Hut", (byte)level);
                cap += def != null ? def.populationProvided : 0;
                if (expansion) cap += c.penExpansionBonus;
            }
            return cap;
        }

        /// <summary>Live slaves plus every slave (or Worker) queued at any of the faction's buildings.</summary>
        public static int SlaveCount(EntityManager em, Faction faction)
        {
            int n = 0;
            var q = QC_Slaves.Get(em, QT_Slaves);
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++) if (facs[i].Value == faction) n++;

            var qq = QC_Queues.Get(em, QT_Queues);
            using var ents = qq.ToEntityArray(Allocator.Temp);
            using var owners = qq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (owners[i].Value != faction) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i], true);
                for (int j = 0; j < buf.Length; j++)
                {
                    if (buf[j].Kind != ProductionKind.Train) continue;
                    var id = buf[j].Id;
                    if (id == SlaveId || id == "Worker") n++;
                }
            }
            return n;
        }

        /// <summary>True when one more slave would exceed the cap (the training gate).</summary>
        public static bool SlaveCapReached(EntityManager em, Faction faction)
            => SlaveCount(em, faction) >= SlaveCap(em, faction);

        public const string HarkonId = "Feraldis_HarkonFiend";

        /// <summary>
        /// Why <paramref name="faction"/> may not train <paramref name="unitId"/>
        /// right now, or null. Runs in TrainCommandDirect on every peer.
        /// </summary>
        public static string TrainRefusal(EntityManager em, Faction faction, string unitId)
        {
            if (unitId == SlaveId || (unitId == "Worker" && IsFeraldis(em, faction)))
                return SlaveCapReached(em, faction) ? "Your Slave Pens are full - build more Slave Pens" : null;
            if (unitId == OverseerId && CountLiveOrQueued(em, faction, OverseerId) > 0)
                return "You already have an Overseer";
            if (unitId == HarkonId && CountLiveOrQueued(em, faction, HarkonId) > 0)
                return "Harkon Fiend already leads your warband";
            return null;
        }

        static readonly ComponentType[] QT_Typed =
            { ComponentType.ReadOnly<UnitTypeId>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Typed;

        /// <summary>Live units of <paramref name="unitId"/> plus queued ones, for a one-per-faction gate.</summary>
        public static int CountLiveOrQueued(EntityManager em, Faction faction, string unitId)
        {
            int n = 0;
            var q = QC_Typed.Get(em, QT_Typed);
            using (var ids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ids.Length; i++)
                    if (facs[i].Value == faction && ids[i].Value == unitId) n++;

            var qq = QC_Queues.Get(em, QT_Queues);
            using var ents = qq.ToEntityArray(Allocator.Temp);
            using var owners = qq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (owners[i].Value != faction) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i], true);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].Kind == ProductionKind.Train && buf[j].Id == unitId) n++;
            }
            return n;
        }
    }
}
