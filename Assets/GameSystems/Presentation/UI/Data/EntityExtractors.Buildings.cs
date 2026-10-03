// EntityExtractors.Buildings.cs
// Building-placement actions (worker palette, icons, culture/era/cap gating)
// plus hut age-up and wall-segment conversion action cells.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Data
{
    public static partial class EntityActionExtractor
    {
        // Icon cache: loaded once from Resources/UI/Icons/Buildings/
        private static readonly Dictionary<string, UnityEngine.Texture2D> _buildingIconCache = new();

        /// <summary>
        /// Load a building icon from Resources/UI/Icons/Buildings/.
        /// Maps building IDs to icon filenames where they differ.
        /// Returns null if no icon exists for that building.
        /// </summary>
        private static UnityEngine.Texture2D GetBuildingIcon(string buildingId)
        {
            if (_buildingIconCache.TryGetValue(buildingId, out var cached))
                return cached;

            // The icon file is named after the building id.
            var tex = UnityEngine.Resources.Load<UnityEngine.Texture2D>($"UI/Icons/Buildings/{buildingId}");
            _buildingIconCache[buildingId] = tex; // Cache even null to avoid repeated lookups
            return tex;
        }

        /// <summary>
        /// Build the two action cells surfaced on a Gatherer's Hut with the
        /// age-up choice marker. Both cells share the same canonical cost
        /// (40 supplies + 30 iron) and the same 5-second timer — only the
        /// outcome differs. While mid-conversion (GathererHutConverting
        /// present and the marker stripped) the helper returns an empty
        /// list, so the panel collapses to a progress display only.
        /// (task-109 phase 2)
        /// </summary>
        private static List<ActionButton> GetHutAgeUpChoiceActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();

            // Mid-conversion → no buttons (cannot cancel in v1, per Phase 1
            // canonical design).
            if (em.HasComponent<GathererHutConverting>(entity))
                return actions;

            if (!em.HasComponent<GathererHutAgeUpChoice>(entity))
                return actions;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            var cost = TheWaningBorder.Core.Commands.Types.ConvertHutCommandHelper.ConversionCost;
            bool canAfford = !em.Equals(default(EntityManager))
                ? FactionEconomy.CanAfford(em, faction, cost)
                : true;
            Cost available = GetFactionResourcesAsCost(em, faction);

            actions.Add(new ActionButton
            {
                Id = "ConvertToWallHub",
                Label = Loc.T("Convert to Wall Hub"),
                Tooltip = BuildTooltip(
                    "Convert to Wall Hub",
                    "Replaces the hut with a Wall Hub. Adjacent hubs auto-link into wall segments.",
                    cost,
                    available,
                    trainingTime: TheWaningBorder.Core.Commands.Types.ConvertHutCommandHelper.ConversionDuration
                ),
                Cost = cost,
                Enabled = true,
                CanAfford = canAfford,
                Icon = null,
            });

            actions.Add(new ActionButton
            {
                Id = "ConvertToWatchTower",
                Label = Loc.T("Convert to Watch Tower"),
                Tooltip = BuildTooltip(
                    "Convert to Watch Tower",
                    "Replaces the hut with a stand-alone Alanthor Watch Tower (ranged defense).",
                    cost,
                    available,
                    trainingTime: TheWaningBorder.Core.Commands.Types.ConvertHutCommandHelper.ConversionDuration
                ),
                Cost = cost,
                Enabled = true,
                CanAfford = canAfford,
                Icon = null,
            });

            return actions;
        }

        /// <summary>
        /// Build the action cells surfaced when the player selects a wall
        /// instance. Per task-109 Phase 6 the action panel resolves an
        /// instance click to its parent segment and presents:
        ///   - "Convert to Gate (Nx)" — segment-level 3-instance conversion
        ///     (task-109 Phase 5 path). N is min(instance count, 5); a short
        ///     segment is allowed but the label communicates the shortened
        ///     gate width and the helper surfaces a warning suffix.
        ///   - "Convert to Tower"     — per-instance legacy conversion
        ///     (single-instance WallUpgradeState path; cost from BuildCosts).
        /// Mid-conversion (parent segment carries WallSegmentUpgradeState)
        /// the Gate button drops out — only the Tower stays. (task-109 phase 6)
        /// </summary>
        private static List<ActionButton> BuildSegmentConversionActions(Entity entity, EntityManager em)
        {
            var actions = new List<ActionButton>();
            if (!em.HasComponent<WallInstanceTag>(entity)) return actions;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            Cost available = GetFactionResourcesAsCost(em, faction);

            // A mounted module whose engine was destroyed sells a new one
            // (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet
            // emplacements). First in the list: it is the reason the player
            // clicked an empty emplacement.
            AddReplaceEquipmentAction(actions, em, entity, faction, available);

            // Resolve parent segment to derive the gate width label.
            Entity segment = Entity.Null;
            if (em.HasComponent<WallInstanceParent>(entity))
                segment = em.GetComponentData<WallInstanceParent>(entity).Segment;
            int segmentInstanceCount = 0;
            if (em.Exists(segment) && em.HasBuffer<WallInstanceRef>(segment))
                segmentInstanceCount = em.GetBuffer<WallInstanceRef>(segment).Length;
            int span = AlanthorWall.GateRegionSpan;
            int gateWidth = segmentInstanceCount > 0 ? System.Math.Min(segmentInstanceCount, span) : span;
            bool shortSegment = segmentInstanceCount > 0 && segmentInstanceCount < span;
            bool segmentConverting = em.Exists(segment) && em.HasComponent<WallSegmentUpgradeState>(segment);

            // What may be fitted HERE (docs/Design/Age_1_Alanthor.md § What a
            // module may become): a fitting needs a clear run of untouched
            // modules around it, and a tower needs masonry to stand on.
            int freeRun = AlanthorWall.FreeRunAround(em, entity);
            bool roomForFitting = freeRun >= AlanthorWall.FreeRunForTower;
            bool roomForGate = freeRun >= AlanthorWall.FreeRunForGate;
            byte tier = WallTiers.Of(em, entity);
            bool palisade = AlanthorWall.IsPalisade(em, entity);
            // A palisade is a fence: it converts to a GATE (you have to be
            // able to walk through your own wall) and to a HUB (so a fence can
            // still branch), and to nothing else. On the stone wall the level
            // decides the fittings: towers from Stone, a Ballista from
            // Battlemented, a Trebuchet from Shielded
            // (docs/Design/Age_1_Alanthor.md § The stone wall).
            bool masonry = !palisade && WallTiers.AllowsTowers(tier);

            // Gate cell — segment-level conversion. Drops out while the
            // segment is mid-conversion (no double-charge / double-stack).
            if (!segmentConverting && roomForGate)
            {
                var gateCost = TheWaningBorder.Core.Commands.Types
                    .ConvertSegmentToGateCommandHelper.ConversionCost;
                bool canAffordGate = !em.Equals(default(EntityManager))
                    ? FactionEconomy.CanAfford(em, faction, gateCost)
                    : true;
                string gateLabel = string.Format(Loc.T("Convert to Gate ({0}x)"), gateWidth);
                string gateSubtitle = shortSegment
                    ? string.Format(Loc.T("Short segment — the gatehouse will be built {0} modules wide."), gateWidth)
                    : Loc.T("One gatehouse, three modules wide. The sections beside this one are replaced by it; units path through the middle.");

                actions.Add(new ActionButton
                {
                    Id = "WallSegmentToGate",
                    Label = gateLabel,
                    Tooltip = BuildTooltip(
                        gateLabel,
                        gateSubtitle,
                        gateCost,
                        available,
                        trainingTime: TheWaningBorder.Core.Commands.Types
                            .ConvertSegmentToGateCommandHelper.ConversionDuration
                    ),
                    Cost = gateCost,
                    Enabled = true,
                    CanAfford = canAffordGate,
                    Icon = null,
                });
            }

            // Tower cell — per-instance conversion. A timber palisade cannot
            // carry a tower, and neither can a module boxed in by other
            // fittings.
            if (masonry && roomForFitting
                && TheWaningBorder.Data.BuildCosts.TryGet("Alanthor_WallTower", out var towerCost))
            {
                bool canAffordTower = !em.Equals(default(EntityManager))
                    ? FactionEconomy.CanAfford(em, faction, towerCost)
                    : true;
                actions.Add(new ActionButton
                {
                    Id = "WallInstanceToTower",
                    Label = Loc.T("Convert to Tower"),
                    Tooltip = BuildTooltip(
                        "Convert to Tower",
                        "Reinforces this wall section into a watchtower (ranged defense).",
                        towerCost,
                        available,
                        trainingTime: 10f
                    ),
                    Cost = towerCost,
                    Enabled = true,
                    CanAfford = canAffordTower,
                    Icon = null,
                });
            }

            // Emplacement cells — mount a war engine on this module's crown.
            // The platform and the engine standing on it stay two entities
            // (docs/Design/Age_1_Alanthor.md § Ballista and Trebuchet
            // emplacements); the module itself is still wall.
            if (masonry && roomForFitting && WallTiers.AllowsBallista(tier))
                AddEmplacementAction(actions, em, faction, available,
                    "WallToBallista", "Alanthor_BallistaEmplacement",
                    Loc.T("Mount Ballista"),
                    Loc.T("A bolt thrower on the wall: single targets, heavy against buildings. If the engine is destroyed the platform stays, and a new one can be bought with Replace Equipment."));
            if (masonry && roomForFitting && WallTiers.AllowsTrebuchet(tier))
                AddEmplacementAction(actions, em, faction, available,
                    "WallToTrebuchet", "Alanthor_TrebuchetEmplacement",
                    Loc.T("Mount Trebuchet"),
                    Loc.T("A counterweight engine on the wall: long range, splash, slow. If the engine is destroyed the platform stays, and a new one can be bought with Replace Equipment."));

            // NO PLACEHOLDER CELLS (2026-09-24). The panel used to fill the
            // gap with disabled "No room" / "No tower" cards explaining the
            // absence. A card the player cannot press is not information, it
            // is clutter, and a tooltip nobody hovers is the wrong place for a
            // rule. An empty action panel on a fence is the correct reading.
            // docs/Design/Age_1_Alanthor.md § The three wall levels.

            // Hub cell — the cell becomes a hub and its segment splits there,
            // so a new wall can be drawn off it (T / X junctions). Costs a hub.
            // A hub of the wall's own kind — so not on a palisade its owner
            // can no longer build (Alanthor / Runai after age-up).
            if (AlanthorWall.CanConvertInstanceToHub(em, entity)
                && WallTiers.CanBuild(em, faction, palisade)
                && TheWaningBorder.Data.BuildCosts.TryGet(AlanthorWall.HubIdFor(palisade), out var hubCost))
            {
                bool canAffordHub = !em.Equals(default(EntityManager))
                    ? FactionEconomy.CanAfford(em, faction, hubCost)
                    : true;
                actions.Add(new ActionButton
                {
                    Id = "WallInstanceToHub",
                    Label = Loc.T("Convert to Hub"),
                    Tooltip = BuildTooltip(
                        "Convert to Hub",
                        "Raises a wall hub on this section. New walls can be drawn from it, so the wall can branch.",
                        hubCost,
                        available,
                        trainingTime: 10f
                    ),
                    Cost = hubCost,
                    Enabled = true,
                    CanAfford = canAffordHub,
                    Icon = null,
                });
            }

            return actions;
        }

        /// <summary>One "mount an engine here" cell. Both emplacements are
        /// the same card with a different id, cost and engine.</summary>
        private static void AddEmplacementAction(List<ActionButton> actions, EntityManager em,
            Faction faction, Cost available, string actionId, string buildingId,
            string label, string subtitle)
        {
            if (!TheWaningBorder.Data.BuildCosts.TryGet(buildingId, out var cost)) return;
            bool canAfford = !em.Equals(default(EntityManager))
                ? FactionEconomy.CanAfford(em, faction, cost) : true;
            actions.Add(new ActionButton
            {
                Id = actionId,
                Label = label,
                Tooltip = BuildTooltip(label, subtitle, cost, available, trainingTime: 12f),
                Cost = cost,
                Enabled = true,
                CanAfford = canAfford,
                Icon = null,
            });
        }

        /// <summary>
        /// The Replace Equipment card on an emplacement platform. Offered only
        /// while the engine is gone; while a paid restore runs the card turns
        /// into its countdown (not pressable — the order is already paid).
        /// Price and time are the engine SO's cost / trainingTime, read
        /// through <see cref="EmplacementEquipment"/>, the same numbers the
        /// executor charges.
        /// </summary>
        private static void AddReplaceEquipmentAction(List<ActionButton> actions, EntityManager em,
            Entity platform, Faction faction, Cost available)
        {
            if (em.Equals(default(EntityManager))) return;
            if (!em.HasComponent<EmplacementTag>(platform) || !em.HasComponent<EmplacementCrew>(platform)) return;
            if (!TechCatalog.IsReady) return;
            string engineId = EmplacementEquipment.EngineIdOf(em, platform);
            if (string.IsNullOrEmpty(engineId)) return;

            string label = Loc.T("Replace Equipment");
            if (EmplacementEquipment.IsRestoring(em, platform))
            {
                var crew = em.GetComponentData<EmplacementCrew>(platform);
                int left = (int)System.Math.Ceiling(crew.Restore);
                actions.Add(new ActionButton
                {
                    Id = "ReplaceEquipmentRestoring",
                    Label = string.Format(Loc.T("Restoring ({0}s)"), left),
                    Tooltip = Loc.T("The crew is raising a new engine on this platform."),
                    Enabled = false,
                    CanAfford = true,
                    Icon = null,
                });
                return;
            }
            if (!EmplacementEquipment.IsEmpty(em, platform)) return;

            var cost = EmplacementEquipment.CostOf(engineId);
            float seconds = EmplacementEquipment.SecondsOf(engineId);
            string engineName = TechCatalog.Unit(engineId).name;
            actions.Add(new ActionButton
            {
                Id = "ReplaceEquipment",
                Label = label,
                Tooltip = BuildTooltip(label,
                    string.Format(Loc.T("The {0} on this platform was destroyed. The crew raises a new one when the timer ends; no worker needed."),
                        Loc.T(engineName)),
                    cost, available, trainingTime: seconds),
                Cost = cost,
                Enabled = true,
                CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                Icon = null,
            });
        }

        // Buildings the player can place via worker (excludes starting buildings and other-faction variants)
        //
        // task-109: Alanthor wall primitives — only "Alanthor_Wall" (hub) and "Alanthor_Tower"
        //           (standalone watch tower) are placeable. "Alanthor_WallTower" and
        //           "Alanthor_WallGate" are CONVERSION-ONLY (segment selection → Convert
        //           to Tower / Convert to Gate). They MUST NOT appear in this HashSet.
        //           See docs/Design/Age_1_Alanthor.md § Wall System (BFME2 hub-and-segment)
        //           and the static-ctor Debug.Assert guard below.
        private static readonly HashSet<string> BuildableBuildings = new()
        {
            // Choice buildings (VaultOfAlmierra /
            // FiendstoneKeep) are NOT worker-placeable: they are placed from
            // the top-bar special-building buttons and self-construct
            // (design: Age_0.md § Special buildings).
            "Hut", "GatherersHut", "Barracks", "ArcheryRange",
            // BOTH extractors. The Veilstone Mine was absent from this set
            // and so was never offered, despite having an asset, a factory
            // recipe, a footprint, an upgrade ladder and its own placement
            // refusal message — every part except the one that shows it.
            "Mine", "VeilstoneMine",
            "TempleOfRidan",
            // The FORTRESS (Territory_Claims.md §4): how ground with no
            // resource node is locked. One per territory, enforced at placement.
            "Fortress",
            // The two walls are different buildings (2026-10-02): the
            // Palisade is every culture's in Age 0 and Feraldis's after; the
            // Stone Wall (Alanthor_Wall) is Alanthor's from the age-up.
            "Palisade",
            "Alanthor_Wall",
            // Runai culture buildings
            "Runai_Outpost", "Runai_TradeHub", "Runai_TradingPost", "ThessarasBazaar", "Runai_SiegeWorkshop",
            // Alanthor culture buildings. Alanthor_PracticeRange retired (it is
            // the LEVELED Archery Range).
            "Alanthor_Tower", "Alanthor_SiegeYard", "Alanthor_RoyalStable",
            "Alanthor_TradingOutpost",
            // NO emplacement platforms (2026-09-25): emplacements are
            // WALL-MOUNT ONLY — Mount Ballista / Mount Trebuchet on a masonry
            // curtain module's panel. The free-standing platforms are
            // guarded out by the static-ctor asserts below.
            // Feraldis culture buildings. Hunting Lodge / Logging Station
            // were CUT (2026-08-05 rev.4) — Feraldis huts became Raider
            // Camps, so the gathering-upgrade pair had nothing left to do.
            "Feraldis_Longhouse",
            "Feraldis_Tower", "Feraldis_SiegeYard", "Feraldis_WarTotem", "Feraldis_Pasture",
            // Sect buildings — one per sect, each capped at 5 per faction and
            // only offered once that sect is adopted. Both gates are enforced
            // in GetBuildingActions; CommandRouter.IssuePlaceBuilding is the
            // authoritative cap. docs/Design/Sects.md section 1.
            "Sect_Reliquary", "Sect_MendingHall", "Sect_Stonehold", "Sect_Veilworks",
            "Sect_MusterYard"
        };

        /// <summary>
        /// Sect building id -> the sect that unlocks it. A sect building is
        /// offered only to a faction that has adopted its sect, and never more
        /// than SectBuilding.CapPerFaction times.
        /// </summary>
        private static readonly Dictionary<string, string> SectBuildingOwner = new()
        {
            { "Sect_Reliquary",   SectConfig.Antiquity },
            { "Sect_MendingHall", SectConfig.Renewal },
            { "Sect_Stonehold",   SectConfig.Fortitude },
            { "Sect_Veilworks",   SectConfig.Reclamation },
            { "Sect_MusterYard",  SectConfig.War },
        };

        /// <summary>Current count of a faction's sect buildings, by id.</summary>
        private static int SectBuildingCount(EntityManager em, string buildingId, Faction faction)
        {
            if (em.Equals(default(EntityManager))) return 0;
            switch (buildingId)
            {
                case "Sect_Reliquary":   return BuildingFactory.GetFactionBuildingCount<ReliquaryTag>(em, faction);
                case "Sect_MendingHall": return BuildingFactory.GetFactionBuildingCount<MendingHallTag>(em, faction);
                case "Sect_Stonehold":   return BuildingFactory.GetFactionBuildingCount<StoneholdTag>(em, faction);
                case "Sect_Veilworks":   return BuildingFactory.GetFactionBuildingCount<VeilworksTag>(em, faction);
                case "Sect_MusterYard":  return BuildingFactory.GetFactionBuildingCount<MusterYardTag>(em, faction);
                default: return 0;
            }
        }

        // task-109: defensive boot-time guard. If a future PR accidentally adds
        // "Alanthor_WallTower" or "Alanthor_WallGate" to BuildableBuildings, this
        // static constructor will fire a Debug.Assert at first class touch (which
        // happens during the first build-action extraction on the local player
        // worker). Keeping the assertion close to the HashSet declaration makes
        // the contract self-documenting.
        static EntityActionExtractor()
        {
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_WallTower"),
                "task-109: Alanthor_WallTower must remain conversion-only (segment → Convert to Tower). Do not add it to BuildableBuildings.");
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_WallGate"),
                "task-109: Alanthor_WallGate must remain conversion-only (segment → Convert to Gate). Do not add it to BuildableBuildings.");
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_BallistaEmplacement"),
                "Emplacements are wall-mount only (Mount Ballista on a masonry curtain module). Do not add Alanthor_BallistaEmplacement to BuildableBuildings.");
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_TrebuchetEmplacement"),
                "Emplacements are wall-mount only (Mount Trebuchet on a masonry curtain module). Do not add Alanthor_TrebuchetEmplacement to BuildableBuildings.");
        }

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly Unity.Entities.ComponentType[] HallCultureQueryTypes =
            { typeof(HallTag), typeof(FactionTag), typeof(FactionProgress) };
        private static TheWaningBorder.Core.CachedEntityQuery _hallCultureQuery;

        private static List<ActionButton> GetBuildingActions()
        {
            var actions = new List<ActionButton>();
            var faction = GameSettings.LocalPlayerFaction;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            EntityManager em = (world != null && world.IsCreated) ? world.EntityManager : default;

            // Check if faction already has a landmark (Vault/Keep)
            string existingChoice = null;
            if (!em.Equals(default(EntityManager)))
                existingChoice = BuildingFactory.GetFactionChoiceBuilding(em, faction);

            // Determine local faction's culture from the capital's FactionProgress
            byte factionCulture = Cultures.None;
            if (!em.Equals(default(EntityManager)))
            {
                var hallQuery = _hallCultureQuery.Get(em, HallCultureQueryTypes);
                var hallEntities = hallQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                for (int i = 0; i < hallEntities.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(hallEntities[i]).Value == faction)
                    {
                        factionCulture = em.GetComponentData<FactionProgress>(hallEntities[i]).Culture;
                        break;
                    }
                }
                hallEntities.Dispose();
            }

            // Get faction era for era gating
            int factionEra = !em.Equals(default(EntityManager))
                ? EntityInfoExtractor.GetFactionEra(em, faction)
                : 1;

            // Get current resources for rich tooltip coloring
            Cost available = GetFactionResourcesAsCost(em, faction);

            // Per-faction caps — counted once so we don't re-query inside the
            // building loop. The Fortress has no per-faction cap: it is one
            // per TERRITORY, which is a question about a position and cannot be
            // answered from a button. Temple of Ridan caps at 1.
            int templeCount = !em.Equals(default(EntityManager))
                ? BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(em, faction) : 0;
            const int TempleCap = 1;

            if (TechCatalog.IsReady)
            {
                foreach (var building in TechCatalog.GetAllBuildings())
                {
                    // Only show buildings the player can actually place
                    if (!BuildableBuildings.Contains(building.id)) continue;

                    // ONE MINE BUTTON (2026-09-29): the Veilstone Mine rides the
                    // "Mine" button, which raises whichever the node under the
                    // cursor needs (TerritoryOwnership.ResolveExtractorAt).
                    if (building.id == "VeilstoneMine") continue;

                    // Choice building exclusion: if one is built, hide the other two
                    if (BuildingFactory.IsChoiceBuilding(building.id) && existingChoice != null)
                        continue;

                    // Temple of Ridan: one per faction.
                    if (building.id == "TempleOfRidan" && templeCount >= TempleCap) continue;

                    // Sect buildings: adopt the sect to unlock it, then 5 max.
                    if (SectBuildingOwner.TryGetValue(building.id, out var owningSect))
                    {
                        if (em.Equals(default(EntityManager))) continue;
                        if (!SectQuery.IsAdopted(em, faction, owningSect)) continue;
                        if (SectBuildingCount(em, building.id, faction)
                            >= TheWaningBorder.Entities.SectBuilding.CapPerFaction) continue;
                    }

                    // The Palisade is not culture-prefixed but is still gated:
                    // Alanthor and Runai lose it at age-up, Feraldis keep it
                    // (docs/Design/Age_0.md § Palisade). Same test the
                    // executors apply.
                    if (building.id == AlanthorWall.PalisadeHubId
                        && (em.Equals(default(EntityManager)) || !WallTiers.CanBuild(em, faction, palisade: true)))
                        continue;

                    // Data-driven culture gating: buildings with culture prefix require that culture
                    byte requiredCulture = GetRequiredCulture(building.id);
                    if (requiredCulture != Cultures.None && requiredCulture != factionCulture)
                        continue;

                    // Gatherer's Huts stay buildable for every culture, all game
                    // (directive 2026-07-04: Alanthor huts don't despawn in Age 1
                    // and remain buildable throughout).

                    // Runai cannot build Huts (population is set to 200 on age-up)
                    if (building.id == "Hut" && factionCulture == Cultures.Runai)
                        continue;

                    var cost = building.cost != null ? new Cost
                    {
                        Supplies = building.cost.Supplies,
                        Iron = building.cost.Iron,
                        Veilstone = building.cost.Veilstone
                    } : default;
                    // Show what THIS faction would be charged (the executor's
                    // price): Deep Foundations. Ids the cost table does not carry
                    // keep the catalog figure.
                    if (!em.Equals(default(EntityManager))
                        && TheWaningBorder.Data.BuildCosts.Exists(building.id))
                        cost = TheWaningBorder.Data.BuildCosts.For(em, faction, building.id);

                    bool canAfford = !em.Equals(default(EntityManager))
                        ? FactionEconomy.CanAfford(em, faction, cost)
                        : true;

                    // Era gating: show button disabled with requirement text instead of hiding
                    bool eraLocked = building.minEra > 0 && building.minEra > factionEra;
                    string requirement = eraLocked
                        ? string.Format(Loc.T("Requires: Era {0}"), building.minEra) : null;

                    // The Temple also costs a Religion Point (Religion.md §2),
                    // which is earned by killing the curse.
                    if (building.id == "TempleOfRidan" && !em.Equals(default(EntityManager)))
                    {
                        int rpCost = FactionReligionPointsHelper.Cfg.templeRp;
                        bool rpOk = FactionReligionPointsHelper.CanAfford(em, faction, rpCost);
                        canAfford &= rpOk;
                        requirement = string.Format(Loc.T("Costs {0} Religion Point — earned by killing curse units"), rpCost)
                            + (requirement != null ? "\n" + requirement : "");
                    }

                    // A wall is paid per 3 m module on top of its hubs: say so,
                    // with the module's own price.
                    if (AlanthorWall.IsWallHubId(building.id))
                    {
                        var mod = TheWaningBorder.Core.Commands.CommandRouter.WallModuleCost(
                            building.id == AlanthorWall.PalisadeHubId);
                        string modText = mod.Iron > 0
                            ? string.Format("{0} S + {1} I", mod.Supplies, mod.Iron)
                            : string.Format("{0} S", mod.Supplies);
                        requirement = string.Format(Loc.T("Price shown is per hub. Each 3 m of wall costs {0} more."), modText)
                            + (requirement != null ? "\n" + requirement : "");
                    }

                    // The capital is the Shelter in Age 0 (its SO name) and the
                    // Fortress once the faction has aged up.
                    string buildingName = building.id == "Fortress" && factionCulture != Cultures.None
                        ? Fortress.AgedName : building.name;

                    string tooltip = BuildTooltip(
                        building.id == "Alanthor_Wall"
                            ? WallTiers.DisplayName(WallTiers.LevelFor(em, faction)) : buildingName,
                        building.id == "Mine"
                            ? Loc.T("Built on a resource node — an iron deposit raises an Iron Mine, a " +
                                    "veilstone outcropping a Veilstone Mine. Its first extractor locks " +
                                    "the territory.")
                            : building.role,
                        cost,
                        available,
                        requirement: requirement
                    );

                    // The stone wall is named for the level this faction's wall
                    // stands at: Stone, Battlemented or Shielded
                    // (docs/Design/Age_1_Alanthor.md § The stone wall).
                    string label = building.id == "Alanthor_Wall"
                        ? WallTiers.DisplayName(WallTiers.LevelFor(em, faction))
                        : buildingName;

                    actions.Add(new ActionButton
                    {
                        Id = building.id,
                        Label = Loc.T(label),
                        Tooltip = tooltip,
                        Cost = cost,
                        Enabled = !eraLocked,
                        CanAfford = canAfford && !eraLocked,
                        Icon = GetBuildingIcon(building.id)
                    });
                }
            }

            return actions;
        }

        /// <summary>
        /// Determine the required culture for a building based on its ID prefix.
        /// Buildings with "Alanthor_" prefix require Alanthor culture, etc.
        /// Returns Cultures.None for universal buildings (available to all cultures).
        /// </summary>
        private static byte GetRequiredCulture(string buildingId)
        {
            // The Stone Wall (Alanthor_Wall) is Alanthor's again since
            // 2026-10-02: the timber fence every culture raises in Age 0 is
            // its own building, the Palisade (docs/Design/Age_0.md
            // § Palisade), which carries its own gate above.
            if (buildingId.StartsWith("Alanthor_")) return Cultures.Alanthor;
            if (buildingId.StartsWith("Feraldis_")) return Cultures.Feraldis;
            if (buildingId.StartsWith("Runai_")) return Cultures.Runai;
            // FiendstoneKeep is a choice building (like Temple/Vault) — available to all cultures
            if (buildingId == "FiendstoneKeep") return Cultures.None;
            // ThessarasBazaar is a Runai building (doesn't use Runai_ prefix)
            if (buildingId == "ThessarasBazaar") return Cultures.Runai;
            // The Mine is Feraldis-only and Age 1 (2026-08-13; it was briefly
            // specced universal + Age 0). The id has no culture prefix and is
            // NOT going to get one — renaming it would ripple through the
            // factory recipe table, BuildingSizeConfig, BuildCosts,
            // CommandRouter build times, BuildCommandPannel's BuildType map,
            // the name resolver and the Feraldis AI. The era half of the gate
            // is data: Mine.asset minEra = 1.
            // Canon: docs/Design/Age_1_Feraldis.md § Mine.
            // The Mine is UNIVERSAL as of the territory economy
            // (docs/Design/Regions.md §4): "territories containing Iron or
            // Veilstone produce a trickle -- a mine built on the deposit
            // harvests more" is the rule for every culture, not a Feraldis
            // perk. It stays in the Feraldis folder and keeps its unprefixed
            // id for the reasons CLAUDE.md gives (renaming ripples through the
            // recipe table, sizes, costs, build times and the name resolver).
            if (buildingId == "Mine") return Cultures.None;
            return Cultures.None; // universal
        }
    }
}
