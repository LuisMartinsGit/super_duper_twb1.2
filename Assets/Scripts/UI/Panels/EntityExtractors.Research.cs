// EntityExtractors.Research.cs
// Research actions and state: per-building tech buttons (prerequisite and
// affordability checks), research progress/queue info, and the chapel
// research stub.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI
{
    public static partial class EntityActionExtractor
    {
        /// <summary>
        /// Get research action buttons for a building.
        /// Returns buttons for techs this building can research, with affordability and prerequisite checks.
        /// </summary>
        /// <summary>
        /// Techs that belong to exactly one culture. Data-driven first: techs
        /// carrying a "culture" field in TechTree.json (the Wave 2 Alanthor
        /// military tree) gate on it directly. Legacy techs fall through to
        /// the hardcoded id switch: the Gatherer's Hut carries both the
        /// Alanthor Surveys and the Feraldis Raiding line, and each is inert
        /// for the other culture — a Feraldis hut is a Raider Camp that
        /// gathers nothing, and nobody but Feraldis fields Plunderers.
        /// Returns true when the tech is universal.
        /// </summary>
        private static bool TechAvailableToCulture(TechnologyDef tech, byte culture)
        {
            if (!string.IsNullOrEmpty(tech.culture))
            {
                switch (tech.culture)
                {
                    case "Runai":    return culture == Cultures.Runai;
                    case "Alanthor": return culture == Cultures.Alanthor;
                    case "Feraldis": return culture == Cultures.Feraldis;
                    // Unknown culture name: fall through to the id switch.
                }
            }

            switch (tech.id)
            {
                // Feraldis Raider Camp ladder.
                case "Raiding1":
                case "Raiding2":
                case "Raiding3":
                case "IronPlunder":
                case "VeilstonePlunder":
                case "VeilsteelPlunder":
                    return culture == Cultures.Feraldis;

                // Alanthor Guild gather drips — dead weight on a Raider Camp.
                case "IronSurveying1":
                case "IronSurveying2":
                case "IronSurveying3":
                case "VeilstoneSurvey1":
                case "VeilstoneSurvey2":
                case "VeilsteelSurvey":
                    return culture != Cultures.Feraldis;

                default:
                    return true;
            }
        }

        public static List<ActionButton> GetResearchActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            // Chapel special case: research action derived from SectConfig, not TechTreeDB
            if (em.HasComponent<ChapelTag>(entity))
            {
                return GetChapelResearchActions(entity, em, faction);
            }

            string buildingId = GetBuildingId(entity, em);
            if (buildingId == null || !TechCatalog.IsReady) return actions;

            if (!TechCatalog.TryGetBuilding(buildingId, out var buildingDef)) return actions;

            // Fiendstone Keep: wing construction buttons ride the research
            // grid (Ids "KeepWing_*" — EntityActionPanel intercepts the click
            // and starts a KeepWingConstruction instead of queueing research).
            bool isKeep = em.HasComponent<FiendstoneKeepTag>(entity) && em.HasComponent<KeepWings>(entity);
            if (isKeep)
                actions.AddRange(GetKeepWingActions(entity, em, faction));

            // Librarians' wing: Hall economy techs become researchable at the
            // Keep as the "additional researches".
            var researchIds = new List<string>();
            if (buildingDef.research != null) researchIds.AddRange(buildingDef.research);
            if (isKeep
                && em.GetComponentData<KeepWings>(entity).Has(KeepWingType.Librarians)
                && TechCatalog.TryGetBuilding("Hall", out var hallDef)
                && hallDef.research != null)
            {
                foreach (var id in hallDef.research)
                    if (!researchIds.Contains(id)) researchIds.Add(id);
            }

            if (researchIds.Count == 0) return actions;

            var researchState = TheWaningBorder.Economy.FactionResearchState.Instance;
            Cost available = GetFactionResourcesAsCost(em, faction);

            byte factionCulture = CultureConfig.GetCompletedCulture(em, faction);

            // Host building level for level-gated techs (minBuildingLevel).
            // Default L1 for buildings that haven't been stamped with
            // BuildingUpgradeState yet (mirrors the training extractor).
            int buildingLevel = 1;
            if (em.HasComponent<BuildingUpgradeState>(entity))
            {
                int lv = em.GetComponentData<BuildingUpgradeState>(entity).Level;
                if (lv > buildingLevel) buildingLevel = lv;
            }

            foreach (var techId in researchIds)
            {
                if (!TechCatalog.TryGetTechnology(techId, out var tech)) continue;

                // Skip Research_Era2 — age-up is handled by DrawAgeUpSection + CultureChoicePopup
                if (techId == "Research_Era2") continue;

                // Culture gating. The Gatherer's Hut hosts two mutually
                // exclusive economy ladders on the same building: the
                // Alanthor Guild "Surveys" (gather drips) and the Feraldis
                // "Raiding" line (what Plunderers steal). A Feraldis hut is a
                // Raider Camp and gathers nothing, so showing it Surveys
                // would sell a tech that does nothing.
                if (!TechAvailableToCulture(tech, factionCulture)) continue;

                // Technologies are one-shot: drop any tech the faction has
                // already researched, OR that is currently queued / in
                // progress on any of the faction's research buildings.
                // Queueing a tech therefore removes it from the grid;
                // cancelling it (which empties the queue) brings it back.
                bool alreadyResearched = researchState != null && researchState.HasResearched(faction, techId);
                if (alreadyResearched) continue;
                if (IsTechQueued(em, faction, techId)) continue;

                var cost = tech.cost != null ? new Cost
                {
                    Supplies = tech.cost.Supplies,
                    Iron = tech.cost.Iron,
                    Veilstone = tech.cost.Veilstone,
                    Veilsteel = tech.cost.Veilsteel,
                } : default;

                bool canAfford = FactionEconomy.CanAfford(em, faction, cost);
                bool meetsPrereqs = researchState == null || researchState.MeetsPrerequisites(faction, tech.prerequisites);

                // Building-level gating: advanced techs (minBuildingLevel >= 2)
                // stay locked until the host building reaches the level.
                int minLv = tech.minBuildingLevel < 1 ? 1 : tech.minBuildingLevel;
                bool levelLocked = buildingLevel < minLv;

                string requirement = null;
                if (!meetsPrereqs && tech.prerequisites != null)
                    requirement = $"Requires: {string.Join(", ", tech.prerequisites)}";
                if (levelLocked)
                {
                    string levelReq = $"Requires Lv {minLv} {buildingDef.name ?? buildingId}";
                    requirement = requirement == null ? levelReq : levelReq + "\n" + requirement;
                }

                string tooltip = BuildTooltip(
                    tech.name,
                    tech.desc ?? tech.effect,
                    cost,
                    available,
                    trainingTime: tech.researchTime,
                    requirement: requirement
                );

                actions.Add(new ActionButton
                {
                    Id = tech.id,
                    Label = levelLocked ? $"{tech.name}  (Lv {minLv})" : tech.name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = meetsPrereqs && !levelLocked,
                    CanAfford = canAfford && meetsPrereqs && !levelLocked,
                    Icon = null
                });
            }

            return actions;
        }

        /// <summary>
        /// Fiendstone Keep wing construction buttons (choice-building
        /// leveling, design 2026-07-04). Up to three wings, each type once,
        /// one under construction at a time. Ids are "KeepWing_&lt;type&gt;" —
        /// EntityActionPanel routes these to KeepWingConstruction.
        /// </summary>
        private static List<ActionButton> GetKeepWingActions(Entity entity, EntityManager em, Faction faction)
        {
            var actions = new List<ActionButton>();
            var wings = em.GetComponentData<KeepWings>(entity);
            bool slotsFull = wings.Count >= TheWaningBorder.Core.Settings.KeepWingConfig.MaxWings;
            bool building = em.HasComponent<KeepWingConstruction>(entity);
            Cost available = GetFactionResourcesAsCost(em, faction);

            foreach (var wing in TheWaningBorder.Core.Settings.KeepWingConfig.AllWings)
            {
                if (wings.Has(wing)) continue; // each wing type at most once

                var cost = TheWaningBorder.Core.Settings.KeepWingConfig.CostOf(wing);
                string name = TheWaningBorder.Core.Settings.KeepWingConfig.NameOf(wing);
                bool enabled = !slotsFull && !building;

                string requirement = null;
                if (slotsFull) requirement = "All three wing slots are used";
                else if (building) requirement = "A wing is already under construction";

                string tooltip = BuildTooltip(
                    name,
                    TheWaningBorder.Core.Settings.KeepWingConfig.DescriptionOf(wing),
                    cost,
                    available,
                    trainingTime: TheWaningBorder.Core.Settings.KeepWingConfig.BuildDuration,
                    requirement: requirement);

                actions.Add(new ActionButton
                {
                    Id = "KeepWing_" + wing,
                    Label = "Build " + name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = enabled,
                    CanAfford = enabled && FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }

            return actions;
        }

        /// <summary>
        /// True when <paramref name="techId"/> sits in ANY of the faction's
        /// research-building queues (including the in-progress slot). Techs are
        /// one-shot, so a queued tech must not reappear as a buildable action
        /// on any building until it is cancelled or completed.
        /// </summary>
        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly Unity.Entities.ComponentType[] ResearchQueueQueryTypes =
        {
            Unity.Entities.ComponentType.ReadOnly<ResearchState>(),
            Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery _researchQueueQuery;

        public static bool IsTechQueued(EntityManager em, Faction faction, string techId)
        {
            if (em.Equals(default(EntityManager)) || string.IsNullOrEmpty(techId)) return false;

            var q = _researchQueueQuery.Get(em, ResearchQueueQueryTypes);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!em.HasBuffer<ResearchQueueItem>(ents[i])) continue;
                var buf = em.GetBuffer<ResearchQueueItem>(ents[i]);
                for (int b = 0; b < buf.Length; b++)
                    if (buf[b].TechId.ToString() == techId) return true;
            }
            return false;
        }

        /// <summary>
        /// Tech's base research cost from the catalog (for refund on cancel).
        /// Mirrors <see cref="GetUnitCost"/>; zero when the tech is unknown.
        /// </summary>
        public static TheWaningBorder.Core.Cost GetTechCost(string techId)
        {
            if (TechCatalog.TryGetTechnology(techId, out var tech) && tech.cost != null)
            {
                return new TheWaningBorder.Core.Cost
                {
                    Supplies = tech.cost.Supplies,
                    Iron = tech.cost.Iron,
                    Veilstone = tech.cost.Veilstone,
                    Veilsteel = tech.cost.Veilsteel,
                };
            }
            return default;
        }

        /// <summary>
        /// Extract current research state from a building for the progress bar.
        /// </summary>
        private static ResearchInfo GetResearchInfo(Entity entity, EntityManager em)
        {
            var rInfo = new ResearchInfo();

            if (!em.HasComponent<ResearchState>(entity)) return rInfo;

            var rs = em.GetComponentData<ResearchState>(entity);
            var queue = em.GetBuffer<ResearchQueueItem>(entity);

            if (rs.Busy != 0 && queue.Length > 0)
            {
                string techId = queue[0].TechId.ToString();
                rInfo.IsResearching = true;
                rInfo.CurrentTechId = techId;

                // Get total research time from TechTreeDB to compute progress
                float totalTime = 30f;
                if (TechCatalog.TryGetTechnology(techId, out var techDef))
                {
                    totalTime = techDef.researchTime > 0 ? techDef.researchTime : 30f;
                    rInfo.CurrentTechName = techDef.name;
                }
                else
                {
                    rInfo.CurrentTechName = techId;
                }

                rInfo.Total = totalTime;
                rInfo.TimeRemaining = rs.Remaining > 0 ? rs.Remaining : 0f;
                rInfo.Progress = totalTime > 0 ? 1f - (rInfo.TimeRemaining / totalTime) : 1f;
            }

            // Build queue display
            if (queue.Length > 0)
            {
                int startIndex = rs.Busy != 0 ? 1 : 0;
                var queueList = new List<string>();
                for (int i = startIndex; i < queue.Length; i++)
                    queueList.Add(queue[i].TechId.ToString());
                rInfo.Queue = queueList.ToArray();
            }
            else
            {
                rInfo.Queue = System.Array.Empty<string>();
            }

            return rInfo;
        }

        /// <summary>
        /// Get research actions for a chapel entity.
        /// task-063 phase 1: stub. Sect tech research is gone in the redesign —
        /// each chapel exposes 4 lever-upgrade buttons (Passive / Building /
        /// Unit / Active Power) instead. Phase 2 reintroduces upgrade
        /// actions backed by SectAdoption.TryUpgradeLever; not Tech research.
        /// </summary>
        private static List<ActionButton> GetChapelResearchActions(Entity entity, EntityManager em, Faction faction)
        {
            _ = entity; _ = em; _ = faction;
            return new List<ActionButton>();
        }
    }
}
