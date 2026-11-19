// EntityExtractors.Training.cs
// Unit-training actions and state: per-building training buttons (culture and
// building-level gated), training progress/queue info, unit cost lookup, and
// the temple training specializations.

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
        public static List<ActionButton> GetTrainingActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            // Get faction for affordability checks
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            // Identify building type and look up its definition
            string buildingId = GetBuildingId(entity, em);
            if (buildingId == null || !TechCatalog.IsReady) return actions;

            if (!TechCatalog.TryGetBuilding(buildingId, out var buildingDef)) return actions;

            // Fiendstone Keep: the trainable roster comes from its built
            // WINGS (War = Barracks/Range/Stable units, Civic = Workers,
            // Temple = Litharchs), not a static trains list.
            string[] trainsList = buildingDef.trains;
            if (em.HasComponent<FiendstoneKeepTag>(entity) && em.HasComponent<KeepWings>(entity))
                trainsList = TheWaningBorder.Core.Settings.KeepWingConfig
                    .BuildTrainList(em.GetComponentData<KeepWings>(entity));

            if (trainsList == null || trainsList.Length == 0) return actions;

            // Determine faction culture from the building's faction -> Hall -> FactionProgress
            byte factionCulture = Cultures.None;
            if (em.HasComponent<FactionTag>(entity))
            {
                var buildingFaction = em.GetComponentData<FactionTag>(entity).Value;
                factionCulture = CultureConfig.GetCompletedCulture(em, buildingFaction);
            }

            // Get current resources for rich tooltip coloring
            Cost available = GetFactionResourcesAsCost(em, faction);

            // Building level for advanced-unit gating. Default L1 for buildings
            // that haven't been stamped with BuildingUpgradeState yet.
            int buildingLevel = 1;
            if (em.HasComponent<BuildingUpgradeState>(entity))
            {
                int lv = em.GetComponentData<BuildingUpgradeState>(entity).Level;
                if (lv > buildingLevel) buildingLevel = lv;
            }

            // Only show units this building can train (from its "trains" array)
            foreach (var unitId in trainsList)
            {
                if (!TechCatalog.TryGetUnit(unitId, out var unit)) continue;

                // Culture gating: skip units that require a different culture
                byte requiredCulture = GetRequiredCultureForUnit(unitId);
                if (requiredCulture != Cultures.None && requiredCulture != factionCulture)
                    continue;

                // ...and skip a universal unit that this culture replaces with
                // its own variant, so the roster doesn't show both.
                if (IsSupersededByCulture(unitId, factionCulture)) continue;

                // Building-level gating: advanced units (minBuildingLevel >= 2)
                // stay locked until the trainer reaches the required level.
                int minLv = unit.minBuildingLevel < 1 ? 1 : unit.minBuildingLevel;
                bool levelLocked = buildingLevel < minLv;

                var cost = unit.cost != null ? new Cost
                {
                    Supplies = unit.cost.Supplies,
                    Iron = unit.cost.Iron,
                    Veilstone = unit.cost.Veilstone,
                } : default;

                // The time this building will ACTUALLY take, not the SO's base
                // number. The tooltip used to quote unit.trainingTime raw, so
                // a Spearman read "22s" whatever the player had done to earn
                // otherwise — Conscription, the building's level, the Feraldis
                // 1.75x, the War sect ladder and Call to Arms were all
                // invisible, and researching a train-speed tech looked like it
                // had done nothing. TrainDuration is the same call the
                // simulation charges.
                float trainTime = TheWaningBorder.Systems.Training.TrainingSystem
                    .TrainDuration(em, entity, unitId, faction);

                string tooltip = BuildTooltip(
                    unit.name,
                    unit.unitClass,
                    cost,
                    available,
                    trainingTime: trainTime
                );
                // The Power number belongs HERE above all: the training button
                // is where a player is actually choosing between units, and
                // the whole point of the metric is that comparison
                // (docs/Design/Unit_Power.md).
                var power = TheWaningBorder.Data.UnitPower.Breakdown(unit);
                if (power.Measurable)
                    tooltip += "\n" + string.Format(Loc.T("Power: {0}"),
                        UnityEngine.Mathf.RoundToInt(power.Power));
                if (levelLocked)
                    tooltip = string.Format(Loc.T("Requires Lv {0} {1}"),
                        minLv, Loc.T(buildingDef.name ?? buildingId)) + "\n" + tooltip;

                // A FALLEN HERO comes back at its level, for more (Heroes.md §5).
                if (TheWaningBorder.Abilities.FactionHeroRecords.TryGet(em, faction, unit.id, out byte fellAt, out _))
                {
                    float mult = TheWaningBorder.Economy.HeroProgressionConfig.RevivalMultiplier(fellAt);
                    cost = new Cost
                    {
                        Supplies  = UnityEngine.Mathf.RoundToInt(cost.Supplies  * mult),
                        Iron      = UnityEngine.Mathf.RoundToInt(cost.Iron      * mult),
                        Veilstone = UnityEngine.Mathf.RoundToInt(cost.Veilstone * mult),
                    };
                    tooltip = string.Format(Loc.T("Returns at level {0}, with everything it had earned."), fellAt) + "\n" + tooltip;
                }

                // Sect units cost RP each; a sect hero's first recruit does too.
                int rpDue = TheWaningBorder.Systems.Sect.SectHeroes.RpFor(em, faction, unit.id);
                if (rpDue > 0) tooltip += "\n" + string.Format(Loc.T("Also costs {0} Religion Point(s)"), rpDue);

                actions.Add(new ActionButton
                {
                    Id = unit.id,
                    Label = levelLocked
                        ? string.Format(Loc.T("{0}  (Lv {1})"), Loc.T(unit.name), minLv)
                        : Loc.T(unit.name),
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = !levelLocked,
                    CanAfford = !levelLocked && FactionEconomy.CanAfford(em, faction, cost)
                        && FactionReligionPointsHelper.CanAfford(em, faction, rpDue),
                    Icon = null
                });
            }

            actions.RemoveAll(a => !TheWaningBorder.Tutorial.TutorialMissionState.AllowsUnit(a.Id));
            return actions;
        }

        /// <summary>
        /// Extract current training state from a building for the progress bar.
        /// </summary>

        /// <summary>
        /// Look up a unit's training cost from TechTreeDB for refund purposes.
        /// Returns a zero cost if the unit is not found.
        /// </summary>
        /// <summary>Training cost for a unit. The lookup itself lives in
        /// Data.UnitCosts -- it is tech-tree data, not presentation -- and this
        /// forwards so the panels keep their familiar entry point.</summary>
        public static TheWaningBorder.Core.Cost GetUnitCost(string unitId)
            => TheWaningBorder.Data.UnitCosts.Get(unitId);

        /// <summary>
        /// Determine the required culture for a unit based on its ID prefix.
        /// Units with "Alanthor_" prefix require Alanthor culture, etc.
        /// Returns Cultures.None for universal units (available to all cultures).
        /// </summary>
        private static byte GetRequiredCultureForUnit(string unitId)
            => TheWaningBorder.Data.CultureGate.RequiredCultureForUnit(unitId);

        /// <summary>
        /// True when a universal unit is REPLACED by a culture's own variant,
        /// so only the variant should appear on the roster.
        ///
        /// Feraldis fields its own Spearman (less HP, more attack — see
        /// docs/Design/Age_1_Feraldis.md), which supersedes the Age 0 one at
        /// the Hall of Warriors. Without this the panel would offer both,
        /// and the authored actions grid only has five train slots.
        /// </summary>
        private static bool IsSupersededByCulture(string unitId, byte factionCulture)
        {
            return TheWaningBorder.Data.CultureGate.IsSupersededByCulture(unitId, factionCulture);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECT BUILDINGS & THE TEMPLE (from SectConfig, not TechTreeDB)
        // ═══════════════════════════════════════════════════════════════════

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly Unity.Entities.ComponentType[] ReliquaryQueryTypes =
        {
            Unity.Entities.ComponentType.ReadOnly<ReliquaryTag>(),
            Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery _reliquaryQuery;

        private static bool FactionHasReliquary(EntityManager em, Faction faction)
        {
            var q = _reliquaryQuery.Get(em, ReliquaryQueryTypes);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction) return true;
            return false;
        }

        /// <summary>
        /// The Reliquary's three ability buttons (Antiquity building lever).
        /// Scry / Lockout are ground-targeted (BFME-style ring); Vision is an
        /// instant self-centered reveal aura.
        /// </summary>
        private static List<ActionButton> GetReliquaryActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();
            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            var s = em.GetComponentData<ReliquaryState>(entity);

            void Add(string id, string label, float cdRemaining, int abilityIdx, string desc)
            {
                bool unlocked = TheWaningBorder.Systems.Sect.ReliquaryHelper
                    .AbilityUnlocked(em, faction, abilityIdx);
                bool ready = cdRemaining <= 0f;
                string locLabel = Loc.T(label);
                actions.Add(new ActionButton
                {
                    Id = id,
                    Label = ready ? locLabel : $"{locLabel}\n{(int)cdRemaining}s",
                    Tooltip = desc + (unlocked ? ""
                        : "\n" + Loc.T("(Requires Reliquary lever Lv II)")),
                    Enabled = unlocked && ready,
                    CanAfford = true,
                    Icon = null
                });
            }

            Add("Reliquary_Scry", "Scry", s.ScryCooldown, 0,
                string.Format(Loc.T("Scry — reveal a distant area of the map ({0}m for {1}s)."),
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.ScryRadius.ToString("0"),
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.ScryDuration.ToString("0")));
            Add("Reliquary_Lockout", "Lockout", s.LockoutCooldown, 1,
                string.Format(Loc.T("Ability Lockout — enemy attack & ability cooldowns in the target circle stop recovering for {0}s."),
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.LockoutDuration.ToString("0")));
            Add("Reliquary_Vision", "Vision", s.VisionCooldown, 2,
                string.Format(Loc.T("Vision Aura — a wide reveal around the Reliquary ({0}m for {1}s)."),
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.VisionRadius.ToString("0"),
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.VisionDuration.ToString("0")));

            return actions;
        }

        /// <summary>
        /// Get training actions for the Temple of Ridan.
        /// Returns training buttons for ALL adopted sect units.
        /// Also includes Litharch as base temple unit.
        /// </summary>
        private static List<ActionButton> GetTempleTrainingActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            Cost available = GetFactionResourcesAsCost(em, faction);

            // Base temple unit: Litharch
            if (TechCatalog.TryGetUnit("Litharch", out var lithUnit))
            {
                var cost = lithUnit.cost != null ? new Cost
                {
                    Supplies = lithUnit.cost.Supplies,
                    Iron = lithUnit.cost.Iron,
                    Veilstone = lithUnit.cost.Veilstone
                } : default;

                actions.Add(new ActionButton
                {
                    Id = "Litharch",
                    Label = Loc.T(lithUnit.name),
                    Tooltip = BuildTooltip(lithUnit.name, lithUnit.unitClass, cost, available, trainingTime: lithUnit.trainingTime),
                    Cost = cost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }

            // (The Feraldis Corruptor is retired — wells are gone; docs/Design/
            // Age_1_Feraldis.md §7.7. It is no longer offered at the Temple.)

            // Adopted-sect unique units (new roster, rollout 2026-07-05):
            // once a sect is adopted its unit trains here at the Temple.
            // Sects without an implemented unit yet contribute nothing —
            // widen SectUnitFor as their kits land.
            // 2026-11-18 (docs/Design/Age_0.md § Temple of Ridan): the Temple
            // shows the Litharch and nothing else unless TempleOffer.asset
            // turns the sect units back on.
            if (TheWaningBorder.Entities.TempleOffer.OffersSectUnits(em, faction)
                && TheWaningBorder.Economy.FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<SectAdoptionState>(bank))
            {
                var adoption = em.GetComponentData<SectAdoptionState>(bank);
                for (int i = 0; i < TheWaningBorder.Economy.SectConfig.SectCount; i++)
                {
                    string sectId = TheWaningBorder.Economy.SectConfig.IdAt(i);
                    if (!adoption.Get(sectId).IsAdopted) continue;
                    // Sects with their own building train there instead.
                    if (TheWaningBorder.Economy.SectConfig.BuildingIdFor(sectId) != null) continue;
                    if (!SectUnitFor(sectId, out var unitId, out var label,
                        out var unitCost, out var tooltip)) continue;

                    actions.Add(new ActionButton
                    {
                        Id = unitId,
                        Label = Loc.T(label),
                        Tooltip = Loc.T(tooltip) + "\n" + Loc.T("Cost: ")
                            + string.Format(Loc.T("{0} Supplies, {1} Iron"),
                                unitCost.Supplies, unitCost.Iron),
                        Cost = unitCost,
                        Enabled = true,
                        CanAfford = FactionEconomy.CanAfford(em, faction, unitCost),
                        Icon = null
                    });
                }
            }

            return actions;
        }

        /// <summary>Completed-age-up culture of a faction, read from its
        /// Hall's FactionProgress (Cultures.None before/without age-up).</summary>
        private static byte GetFactionCulture(EntityManager em, Faction faction)
        {
            var hallQuery = _hallCultureQuery.Get(em, HallCultureQueryTypes);
            using var hallEntities = hallQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < hallEntities.Length; i++)
                if (em.GetComponentData<FactionTag>(hallEntities[i]).Value == faction)
                    return em.GetComponentData<FactionProgress>(hallEntities[i]).Culture;
            return Cultures.None;
        }

        /// <summary>
        /// Sect id → temple-trainable unique unit (task-063 unit lever).
        /// Returns false for sects whose unit isn't implemented yet.
        /// </summary>
        private static bool SectUnitFor(string sectId, out string unitId,
            out string label, out Cost cost, out string tooltip)
        {
            switch (sectId)
            {
                case TheWaningBorder.Economy.SectConfig.Justice:
                    unitId = "Sect_Inquisitor"; label = "Inquisitor";
                    cost = Cost.Of(supplies: 140, iron: 50);
                    tooltip = "Inquisitor — Justice support caster. Periodically cleanses "
                        + "a debuff (such as a Codex freeze) from a nearby ally.";
                    return true;
                case TheWaningBorder.Economy.SectConfig.War:
                    unitId = "Sect_Warbreaker"; label = "Warbreaker";
                    cost = Cost.Of(supplies: 180, iron: 80);
                    tooltip = "Warbreaker — War's heavy elite. A slow, hard-hitting "
                        + "frontline bruiser.";
                    return true;
                default:
                    unitId = null; label = null; cost = default; tooltip = null;
                    return false;
            }
        }
    }
}
