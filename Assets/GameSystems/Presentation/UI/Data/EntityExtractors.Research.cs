// EntityExtractors.Research.cs
// Research actions and state: per-building tech buttons (prerequisite and
// affordability checks), research progress/queue info, and the chapel
// research stub.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Data
{
    public static partial class EntityActionExtractor
    {

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

            // Alanthor building-fired actives ride the research grid the same way
            // Keep wings do (the panel intercepts the click; nothing is queued).
            // Choreographed Volleys is NOT here any more: it is cast by a
            // ranged unit, not fired from the building, so it lives on the
            // unit's spells bar (AbilityCatalog "Choreographed Volleys").
            AddAlanthorActiveButton(actions, em, faction, buildingId,
                "Alanthor_SiegeYard", "RangingShot", "Alanthor_RangingShot", "Ranging Shot",
                "Planted siege engines load an aimed shot: +100% damage on their next shot.",
                TheWaningBorder.Abilities.AlanthorActiveHelper.RangingShotCooldownRemaining(faction));

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
                if (!TechCatalog.CultureAllows(tech, factionCulture)) continue;

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
                    requirement = string.Format(Loc.T("Requires: {0}"),
                        string.Join(", ", tech.prerequisites));
                if (levelLocked)
                {
                    string levelReq = string.Format(Loc.T("Requires Lv {0} {1}"),
                        minLv, Loc.T(buildingDef.name ?? buildingId));
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
                    Label = levelLocked
                        ? string.Format(Loc.T("{0}  (Lv {1})"), Loc.T(tech.name), minLv)
                        : Loc.T(tech.name),
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
                if (slotsFull) requirement = Loc.T("All three wing slots are used");
                else if (building) requirement = Loc.T("A wing is already under construction");

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
                    Label = string.Format(Loc.T("Build {0}"), Loc.T(name)),
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
        /// One button for a researched Alanthor building-fired active
        /// (Choreographed Volleys / Ranging Shot). Free to press; disabled while
        /// the faction-wide cooldown runs.
        /// </summary>
        private static void AddAlanthorActiveButton(List<ActionButton> actions, EntityManager em,
            Faction faction, string buildingId, string hostBuildingId, string techId,
            string actionId, string label, string desc, float cooldownRemaining)
        {
            if (buildingId != hostBuildingId) return;
            if (FactionResearchState.Instance == null
                || !FactionResearchState.Instance.HasResearched(faction, techId)) return;

            bool ready = cooldownRemaining <= 0f;
            string locLabel = Loc.T(label);
            string locDesc = Loc.T(desc);
            string tooltip = ready
                ? locLabel + "\n" + locDesc
                : locLabel + "\n" + locDesc + "\n"
                    + string.Format(Loc.T("Recharging: {0}s"), (int)cooldownRemaining);

            actions.Add(new ActionButton
            {
                Id = actionId,
                Label = ready ? locLabel : locLabel + " (" + ((int)cooldownRemaining) + "s)",
                Tooltip = tooltip,
                Cost = default,
                Enabled = ready,
                CanAfford = ready,
                Icon = null
            });
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
            Unity.Entities.ComponentType.ReadOnly<ProductionState>(),
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
                if (!em.HasBuffer<ProductionQueueItem>(ents[i])) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i]);
                for (int b = 0; b < buf.Length; b++)
                {
                    if (buf[b].Kind != ProductionKind.Research) continue;
                    if (buf[b].Id.ToString() == techId) return true;
                }
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
        /// Describe a building's production queue — research AND level-ups —
        /// for the HUD.
        ///
        /// Progress comes from ProductionState.Total, captured when the item
        /// started. It used to be recomputed here from the catalog every
        /// frame, which reported the wrong fraction for anything whose
        /// duration a sect multiplier had changed after it began, and could
        /// not describe a level-up at all.
        /// </summary>
        private static ProductionInfo GetProductionInfo(Entity entity, EntityManager em)
        {
            var info = new ProductionInfo { Entries = System.Array.Empty<ProductionQueueEntry>() };

            if (!em.HasComponent<ProductionState>(entity)) return info;
            if (!em.HasBuffer<ProductionQueueItem>(entity)) return info;

            var ps = em.GetComponentData<ProductionState>(entity);
            var queue = em.GetBuffer<ProductionQueueItem>(entity);
            if (queue.Length == 0) return info;

            var entries = new ProductionQueueEntry[queue.Length];
            for (int i = 0; i < queue.Length; i++)
            {
                var item = queue[i];
                entries[i] = new ProductionQueueEntry
                {
                    Kind = item.Kind,
                    // The tech id or the unit id — what the actions grid
                    // matches its button sweep against. Empty for a level-up.
                    Id   = item.Kind == ProductionKind.BuildingUpgrade ? string.Empty : item.Id.ToString(),
                    Name = DescribeProductionItem(item),
                };
            }
            info.Entries = entries;

            if (ps.Busy != 0)
            {
                info.IsBusy = true;
                info.CurrentKind = entries[0].Kind;
                info.CurrentId = entries[0].Id;
                info.CurrentName = entries[0].Name;
                info.Total = ps.Total;
                info.TimeRemaining = ps.Remaining > 0f ? ps.Remaining : 0f;
                info.Progress = ps.Total > 0f ? 1f - (info.TimeRemaining / ps.Total) : 1f;
            }

            return info;
        }

        /// <summary>The label a queued item shows on its slot. Public: the
        /// roster panel renders the same queue and must name its entries the
        /// same way this describer does.</summary>
        public static string DescribeProductionItem(in ProductionQueueItem item)
        {
            if (item.Kind == ProductionKind.BuildingUpgrade)
                return string.Format(Loc.T("Upgrade to Level {0}"), item.Level);

            if (item.Kind == ProductionKind.Train)
            {
                string unitId = item.Id.ToString();
                string unitName = EntityInfoExtractor.GetUnitDisplayName(unitId);
                return string.IsNullOrEmpty(unitName) ? unitId : unitName;
            }

            string techId = item.Id.ToString();
            return TechCatalog.TryGetTechnology(techId, out var def) && def != null
                ? Loc.T(def.name) : techId;
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
