// EntityExtractors.Training.cs
// Unit-training actions and state: per-building training buttons (culture and
// building-level gated), training progress/queue info, unit cost lookup, and
// the chapel/temple training specializations.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI
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

            // Chapel special case: training action derived from SectConfig, not TechTreeDB
            if (em.HasComponent<ChapelTag>(entity))
            {
                return GetChapelTrainingActions(entity, em, faction);
            }

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

            // King's Court (Alanthor age-up of the Hall) also trains the Ledger
            // automaton and the King Lexor hero. Injected here rather than via the
            // building's trains[] because BuildingDefSO.ApplyTo overwrites trains
            // from the SO on every lookup. Culture gating below hides them for
            // non-Alanthor factions, so they only appear at a King's Court.
            if (em.HasComponent<HallTag>(entity))
                trainsList = AppendTrains(trainsList, "Ledger", "King Lexor");

            if (trainsList == null || trainsList.Length == 0) return actions;

            // Determine faction culture from the building's faction -> Hall -> FactionProgress
            byte factionCulture = Cultures.None;
            if (em.HasComponent<FactionTag>(entity))
            {
                var buildingFaction = em.GetComponentData<FactionTag>(entity).Value;
                // Shares _hallCultureQuery with the Buildings partial (same types).
                var hallQuery = _hallCultureQuery.Get(em, HallCultureQueryTypes);
                var hallEntities = hallQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < hallEntities.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(hallEntities[i]).Value == buildingFaction)
                    {
                        factionCulture = em.GetComponentData<FactionProgress>(hallEntities[i]).Culture;
                        break;
                    }
                }
                hallEntities.Dispose();
            }

            // Get current resources for rich tooltip coloring
            Cost available = GetFactionResourcesAsCost(em, faction);

            // Building level for advanced-unit gating. Default L1 for buildings
            // that haven't been stamped with BuildingUpgradeState yet.
            // Temples track their level via TempleLevel rather than
            // BuildingUpgradeState — read whichever is present so the
            // Scholar/Acolyte minBuildingLevel: 4 gate fires correctly
            // (spec refinement #5: ritualists train at a fully-leveled Temple).
            int buildingLevel = 1;
            if (em.HasComponent<BuildingUpgradeState>(entity))
            {
                int lv = em.GetComponentData<BuildingUpgradeState>(entity).Level;
                if (lv > buildingLevel) buildingLevel = lv;
            }
            if (em.HasComponent<TempleLevel>(entity))
            {
                int lv = em.GetComponentData<TempleLevel>(entity).Level;
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
                    Veilsteel = unit.cost.Veilsteel,
                } : default;

                string tooltip = BuildTooltip(
                    unit.name,
                    unit.unitClass,
                    cost,
                    available,
                    trainingTime: unit.trainingTime
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

                actions.Add(new ActionButton
                {
                    Id = unit.id,
                    Label = levelLocked
                        ? string.Format(Loc.T("{0}  (Lv {1})"), Loc.T(unit.name), minLv)
                        : Loc.T(unit.name),
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = !levelLocked,
                    CanAfford = !levelLocked && FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }

            return actions;
        }

        /// <summary>
        /// Extract current training state from a building for the progress bar.
        /// </summary>
        private static TrainingInfo GetTrainingInfo(Entity entity, EntityManager em)
        {
            var tInfo = new TrainingInfo();

            if (!em.HasComponent<TrainingState>(entity)) return tInfo;

            var ts = em.GetComponentData<TrainingState>(entity);
            var queue = em.GetBuffer<TrainQueueItem>(entity);

            // Total items in buffer (including currently training)
            tInfo.QueueCapacity = queue.Length;

            if (ts.Busy != 0 && queue.Length > 0)
            {
                string unitId = queue[0].UnitId.ToString();
                tInfo.IsTraining = true;
                tInfo.CurrentUnitId = unitId;

                // Get total training time from TechTreeDB to compute progress
                float totalTime = 1f;
                if (TechCatalog.TryGetUnit(unitId, out var udef))
                    totalTime = udef.trainingTime > 0 ? udef.trainingTime : 1f;

                tInfo.Total = totalTime;
                tInfo.TimeRemaining = ts.Remaining > 0 ? ts.Remaining : 0f;
                tInfo.Progress = totalTime > 0 ? 1f - (tInfo.TimeRemaining / totalTime) : 1f;
            }

            // Build queue display (excludes currently training item)
            if (queue.Length > 0)
            {
                int startIndex = ts.Busy != 0 ? 1 : 0; // skip current if training
                var queueList = new List<string>();
                for (int i = startIndex; i < queue.Length; i++)
                    queueList.Add(queue[i].UnitId.ToString());
                tInfo.Queue = queueList.ToArray();
            }
            else
            {
                tInfo.Queue = System.Array.Empty<string>();
            }

            return tInfo;
        }

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

        /// <summary>Append unit ids to a trains list without mutating the source
        /// array (which may be the SO's own array). Skips duplicates.</summary>
        private static string[] AppendTrains(string[] baseList, params string[] extra)
        {
            var list = new List<string>(baseList ?? System.Array.Empty<string>());
            foreach (var id in extra)
                if (!list.Contains(id)) list.Add(id);
            return list.ToArray();
        }

        // ═══════════════════════════════════════════════════════════════════
        // CHAPEL TRAINING & RESEARCH (from SectConfig, not TechTreeDB)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get training actions for a chapel entity.
        /// Each chapel trains one unique sect unit defined in SectConfig.
        /// </summary>
        private static List<ActionButton> GetChapelTrainingActions(Entity entity, EntityManager em, Faction faction)
        {
            var actions = new List<ActionButton>();
            if (!em.HasComponent<ChapelTag>(entity)) return actions;
            string sectId = em.GetComponentData<ChapelTag>(entity).SectId.ToString();

            // Generic chapel unit (2026-08-11, docs/Design/Sect_Units.md):
            // every sect with a live kit trains its unique unit here —
            // resolved through SectConfig.UnitIdFor + the TechCatalog def,
            // executed by the generic train path (ExecuteTrain at this
            // chapel). Antiquity keeps its bespoke block below (Lorekeeper
            // tooltip + the Reliquary building lever).
            string chapelUnitId = TheWaningBorder.Economy.SectConfig.UnitIdFor(sectId);
            if (chapelUnitId != null
                && sectId != TheWaningBorder.Economy.SectConfig.Antiquity
                && TechCatalog.TryGetUnit(chapelUnitId, out var chapelUnit)
                && chapelUnit != null)
            {
                var ucost = Cost.Of(
                    supplies: chapelUnit.cost?.Supplies ?? 0,
                    iron: chapelUnit.cost?.Iron ?? 0,
                    veilstone: chapelUnit.cost?.Veilstone ?? 0);
                string ability = chapelUnit.abilities != null && chapelUnit.abilities.Length > 0
                    ? chapelUnit.abilities[0] : null;
                actions.Add(new ActionButton
                {
                    Id = chapelUnitId,
                    Label = Loc.T(chapelUnit.name),
                    Tooltip = string.Format(Loc.T("{0} — the sect's unique unit"), Loc.T(chapelUnit.name))
                        + (ability != null ? ".\n" + Loc.T(ability) + "." : ".")
                        + "\n" + Loc.T("Cost: ")
                        + string.Format(Loc.T("{0} Supplies, {1} Iron"), ucost.Supplies, ucost.Iron)
                        + (ucost.Veilstone > 0
                            ? string.Format(Loc.T(", {0} Veilstone"), ucost.Veilstone) : ""),
                    Cost = ucost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, ucost),
                    Icon = null
                });
            }

            // Sect of Antiquity — the Lorekeeper (Unit lever, implemented
            // 2026-07-05). Other sects' unique units surface here as they
            // are implemented.
            if (sectId == TheWaningBorder.Economy.SectConfig.Antiquity)
            {
                var cost = Cost.Of(supplies: 120, iron: 40);
                actions.Add(new ActionButton
                {
                    Id = "Sect_Lorekeeper",
                    Label = Loc.T("Lorekeeper"),
                    Tooltip = Loc.T("Lorekeeper — Antiquity support scholar.\n"
                        + "Reveals stealthed enemies nearby (Lv II: doubled radius; "
                        + "Lv III: far-sight through fog).\n"
                        + "Garrison the Reliquary (stand beside it) to speed up its "
                        + "ability cooldowns.")
                        + "\n" + Loc.T("Cost: ")
                        + string.Format(Loc.T("{0} Supplies, {1} Iron"), cost.Supplies, cost.Iron),
                    Cost = cost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });

                // Build the Reliquary (Building lever) — one per faction,
                // spawned under construction beside the chapel.
                if (!FactionHasReliquary(em, faction))
                {
                    var relCost = Cost.Of(supplies: 300, iron: 120, veilstone: 40);
                    actions.Add(new ActionButton
                    {
                        Id = "Reliquary_Build",
                        Label = Loc.T("Reliquary"),
                        Tooltip = Loc.T("The Reliquary — Antiquity intel hub (one per faction).\n"
                            + "Lv I: Scry (reveal a distant area). Lv II: adds Ability "
                            + "Lockout and a Vision aura. Lv III: cooldowns -30%, "
                            + "garrison effects doubled.\n"
                            + "Garrison a Lorekeeper beside it to recharge abilities faster.")
                            + "\n" + Loc.T("Cost: ")
                            + string.Format(Loc.T("{0} Supplies, {1} Iron, {2} Veilstone"),
                                relCost.Supplies, relCost.Iron, relCost.Veilstone),
                        Cost = relCost,
                        Enabled = true,
                        CanAfford = FactionEconomy.CanAfford(em, faction, relCost),
                        Icon = null
                    });
                }
            }

            return actions;
        }

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
        /// Returns training buttons for ALL adopted sect units (from completed chapel slots).
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

            // HOLY SCHOLAR (2026-08-04 purify flow): Alanthor's well ritualist
            // trains HERE, gated on the Temple reaching max level. The bespoke
            // Temple path bypasses the generic trains[] extractor, so without
            // this block the unit existed but no button ever showed it.
            if (TechCatalog.TryGetUnit("Alanthor_Scholar", out var scholarUnit))
            {
                byte requiredCulture = GetRequiredCultureForUnit("Alanthor_Scholar");
                byte factionCulture = GetFactionCulture(em, faction);
                if (requiredCulture == Cultures.None || requiredCulture == factionCulture)
                {
                    int templeLevel = em.HasComponent<TempleLevel>(entity)
                        ? em.GetComponentData<TempleLevel>(entity).Level : 1;
                    int minLv = scholarUnit.minBuildingLevel < 1 ? 1 : scholarUnit.minBuildingLevel;
                    bool levelLocked = templeLevel < minLv;

                    var sCost = scholarUnit.cost != null ? new Cost
                    {
                        Supplies = scholarUnit.cost.Supplies,
                        Iron = scholarUnit.cost.Iron,
                        Veilstone = scholarUnit.cost.Veilstone,
                        Veilsteel = scholarUnit.cost.Veilsteel,
                    } : default;

                    string sTooltip = Loc.T("Holy Scholar — purifies wells (channels the ritual) and "
                        + "walks a wide cleansing font that burns away curse and blood.") + "\n"
                        + BuildTooltip(scholarUnit.name, scholarUnit.unitClass, sCost, available,
                            trainingTime: scholarUnit.trainingTime);
                    if (levelLocked)
                        sTooltip = string.Format(Loc.T("Requires Temple Level {0}"), minLv) + "\n" + sTooltip;

                    actions.Add(new ActionButton
                    {
                        Id = scholarUnit.id,
                        Label = levelLocked
                            ? string.Format(Loc.T("{0}  (Temple Lv {1})"), Loc.T(scholarUnit.name), minLv)
                            : Loc.T(scholarUnit.name),
                        Tooltip = sTooltip,
                        Cost = sCost,
                        Enabled = !levelLocked,
                        CanAfford = !levelLocked && FactionEconomy.CanAfford(em, faction, sCost),
                        Icon = null
                    });
                }
            }

            // Feraldis CORRUPTOR — the Feraldis answer to the Scholar, and
            // the same story: the Temple path bypasses the generic trains[]
            // extractor, so without a hardcoded block the unit exists in the
            // catalog but no button ever surfaces it. (That is exactly why
            // Feraldis_Iconoclast was untrainable for its whole life.)
            if (TechCatalog.TryGetUnit("Feraldis_Iconoclast", out var corruptorUnit))
            {
                byte reqCulture = GetRequiredCultureForUnit("Feraldis_Iconoclast");
                if (reqCulture == Cultures.None || reqCulture == GetFactionCulture(em, faction))
                {
                    int templeLevel = em.HasComponent<TempleLevel>(entity)
                        ? em.GetComponentData<TempleLevel>(entity).Level : 1;
                    int minLv = corruptorUnit.minBuildingLevel < 1 ? 1 : corruptorUnit.minBuildingLevel;
                    bool locked = templeLevel < minLv;

                    var cCost = corruptorUnit.cost != null ? new Cost
                    {
                        Supplies = corruptorUnit.cost.Supplies,
                        Iron = corruptorUnit.cost.Iron,
                        Veilstone = corruptorUnit.cost.Veilstone,
                        Veilsteel = corruptorUnit.cost.Veilsteel,
                    } : default;

                    string cTooltip = Loc.T("Corruptor — channels on a well to crack it OPEN, "
                        + "leaving it vulnerable to attack for a short window. "
                        + "The curse defends it while it is exposed; break the well "
                        + "before it seals. Destroy every well to win.") + "\n"
                        + BuildTooltip("Corruptor", corruptorUnit.unitClass, cCost, available,
                            trainingTime: corruptorUnit.trainingTime);
                    if (locked)
                        cTooltip = string.Format(Loc.T("Requires Temple Level {0}"), minLv) + "\n" + cTooltip;

                    actions.Add(new ActionButton
                    {
                        Id = corruptorUnit.id,
                        Label = locked
                            ? string.Format(Loc.T("{0}  (Temple Lv {1})"), Loc.T("Corruptor"), minLv)
                            : Loc.T("Corruptor"),
                        Tooltip = cTooltip,
                        Cost = cCost,
                        Enabled = !locked,
                        CanAfford = !locked && FactionEconomy.CanAfford(em, faction, cCost),
                        Icon = null
                    });
                }
            }

            // Adopted-sect unique units (new roster, rollout 2026-07-05):
            // once a sect is adopted its unit trains here at the Temple.
            // Sects without an implemented unit yet contribute nothing —
            // widen SectUnitFor as their kits land.
            if (TheWaningBorder.Economy.FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<SectAdoptionState>(bank))
            {
                var adoption = em.GetComponentData<SectAdoptionState>(bank);
                for (int i = 0; i < TheWaningBorder.Economy.SectConfig.SectCount; i++)
                {
                    string sectId = TheWaningBorder.Economy.SectConfig.IdAt(i);
                    if (!adoption.Get(sectId).IsAdopted) continue;
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
                case TheWaningBorder.Economy.SectConfig.Antiquity:
                    unitId = "Sect_Lorekeeper"; label = "Lorekeeper";
                    cost = Cost.Of(supplies: 120, iron: 40);
                    tooltip = "Lorekeeper — Antiquity support scholar. Reveals stealthed "
                        + "enemies nearby; garrison the Reliquary to speed its cooldowns.";
                    return true;
                case TheWaningBorder.Economy.SectConfig.Renewal:
                    unitId = "Sect_Tinker"; label = "Tinker";
                    cost = Cost.Of(supplies: 100, iron: 20);
                    tooltip = "Tinker — Renewal field engineer. Repairs and raises "
                        + "structures; cannot fight or mine.";
                    return true;
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
