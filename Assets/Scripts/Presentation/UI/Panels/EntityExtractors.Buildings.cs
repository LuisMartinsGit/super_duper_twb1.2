// EntityExtractors.Buildings.cs
// Building-placement actions (builder palette, icons, culture/era/cap gating)
// plus hut age-up and wall-segment conversion action cells.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI
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

            // Map building IDs to icon filenames where they differ
            string iconName = buildingId switch
            {
                "TempleOfRidan" => "ShrineOfRidan",
                _ => buildingId
            };

            var tex = UnityEngine.Resources.Load<UnityEngine.Texture2D>($"UI/Icons/Buildings/{iconName}");
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

            // Resolve parent segment to derive the gate width label.
            Entity segment = Entity.Null;
            if (em.HasComponent<WallInstanceParent>(entity))
                segment = em.GetComponentData<WallInstanceParent>(entity).Segment;
            int segmentInstanceCount = 0;
            if (em.Exists(segment) && em.HasBuffer<WallInstanceRef>(segment))
                segmentInstanceCount = em.GetBuffer<WallInstanceRef>(segment).Length;
            int gateWidth = segmentInstanceCount > 0 ? System.Math.Min(segmentInstanceCount, 5) : 5;
            bool shortSegment = segmentInstanceCount > 0 && segmentInstanceCount < 5;
            bool segmentConverting = em.Exists(segment) && em.HasComponent<WallSegmentUpgradeState>(segment);

            // Gate cell — segment-level conversion. Drops out while the
            // segment is mid-conversion (no double-charge / double-stack).
            if (!segmentConverting)
            {
                var gateCost = TheWaningBorder.Core.Commands.Types
                    .ConvertSegmentToGateCommandHelper.ConversionCost;
                bool canAffordGate = !em.Equals(default(EntityManager))
                    ? FactionEconomy.CanAfford(em, faction, gateCost)
                    : true;
                string gateLabel = string.Format(Loc.T("Convert to Gate ({0}x)"), gateWidth);
                string gateSubtitle = shortSegment
                    ? string.Format(Loc.T("Short segment — gate will span {0} instances. Groups wider than {0} may not fit."), gateWidth)
                    : Loc.T("3-instance opening. Units can path through.");

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

            // Tower cell — per-instance legacy conversion (unchanged from
            // the IMGUI reference at EntityActionPanel.cs:1641-1660).
            if (TheWaningBorder.Data.BuildCosts.TryGet("Alanthor_WallTower", out var towerCost))
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

            return actions;
        }

        // Buildings the player can place via builder (excludes starting buildings and other-faction variants)
        //
        // task-109: Alanthor wall primitives — only "Alanthor_Wall" (hub) and "Alanthor_Tower"
        //           (standalone watch tower) are placeable. "Alanthor_WallTower" and
        //           "Alanthor_WallGate" are CONVERSION-ONLY (segment selection → Convert
        //           to Tower / Convert to Gate). They MUST NOT appear in this HashSet.
        //           See docs/Design/Age_1_Alanthor.md § Wall System (BFME2 hub-and-segment)
        //           and the static-ctor Debug.Assert guard below.
        private static readonly HashSet<string> BuildableBuildings = new()
        {
            // Choice buildings (ShrineOfRidan / VaultOfAlmierra /
            // FiendstoneKeep) are NOT builder-placeable: they are placed from
            // the top-bar special-building buttons and self-construct
            // (design: Age_0.md § Special buildings).
            "Hut", "GatherersHut", "Barracks", "ArcheryRange", "Mine",
            "TempleOfRidan",
            // Additional Halls — culture-gated (post-age-up only) and capped at
            // 6 per faction. The 6-cap and culture gate are enforced inside
            // GetBuildingActions; the runtime cap fallback lives in
            // BuilderCommandPanel.SpawnSelectedBuilding.
            "Hall",
            "Alanthor_Wall", "Alanthor_Smelter",
            // Runai culture buildings
            "Runai_Outpost", "Runai_TradeHub", "Runai_TradingPost", "ThessarasBazaar", "Runai_SiegeWorkshop",
            // Alanthor culture buildings. Alanthor_PracticeRange retired (it is
            // the LEVELED Archery Range) and Alanthor_Crucible deleted (the
            // Smelter absorbs its veilsteel role) — calculator 2026-08.
            "Alanthor_Tower", "Alanthor_SiegeYard", "Alanthor_RoyalStable",
            "Alanthor_Sawyer",
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
        // builder). Keeping the assertion close to the HashSet declaration makes
        // the contract self-documenting.
        static EntityActionExtractor()
        {
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_WallTower"),
                "task-109: Alanthor_WallTower must remain conversion-only (segment → Convert to Tower). Do not add it to BuildableBuildings.");
            UnityEngine.Debug.Assert(
                !BuildableBuildings.Contains("Alanthor_WallGate"),
                "task-109: Alanthor_WallGate must remain conversion-only (segment → Convert to Gate). Do not add it to BuildableBuildings.");
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

            // Check if faction already has a choice building (Shrine/Vault/Keep)
            string existingChoice = null;
            if (!em.Equals(default(EntityManager)))
                existingChoice = BuildingFactory.GetFactionChoiceBuilding(em, faction);

            // Determine local faction's culture from the Hall entity's FactionProgress
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
            // building loop. The Hall has no per-faction cap any more: it is one
            // per TERRITORY, which is a question about a position and cannot be
            // answered from a button (see the Hall case below). Temple of Ridan
            // caps at 1.
            int templeCount = !em.Equals(default(EntityManager))
                ? BuildingFactory.GetFactionBuildingCount<TempleOfRidanTag>(em, faction) : 0;
            int smelterCount = !em.Equals(default(EntityManager))
                ? BuildingFactory.GetFactionBuildingCount<SmelterTag>(em, faction) : 0;
            const int TempleCap = 1;
            const int SmelterCap = 5;   // Forge: passive veilsteel generator, limit 5 (raised from 1, endgame completeness pass)

            if (TechCatalog.IsReady)
            {
                foreach (var building in TechCatalog.GetAllBuildings())
                {
                    // Only show buildings the player can actually place
                    if (!BuildableBuildings.Contains(building.id)) continue;

                    // Choice building exclusion: if one is built, hide the other two
                    if (BuildingFactory.IsChoiceBuilding(building.id) && existingChoice != null)
                        continue;

                    // Hall: THE claim structure (docs/Design/Regions.md §2).
                    // Always offered — it is how a player takes ground, and the
                    // Hall is an Age 0 building, so expansion is open from the
                    // first minute. The old rules here were both wrong under
                    // that model: hidden until age-up (which would have made
                    // Age 0 unexpandable) and capped at six per faction (which
                    // capped how much of the map anyone could ever hold). The
                    // real limit is one Hall per TERRITORY, enforced at
                    // placement by TerritoryOwnership.HallCapReached — a cap on
                    // a position cannot be answered from a button.
                    

                    // Temple of Ridan: one per faction.
                    if (building.id == "TempleOfRidan" && templeCount >= TempleCap) continue;

                    // Forge: capped at 5 per faction (passive veilsteel generator).
                    if (building.id == "Alanthor_Smelter" && smelterCount >= SmelterCap) continue;

                    // Sect buildings: adopt the sect to unlock it, then 5 max.
                    if (SectBuildingOwner.TryGetValue(building.id, out var owningSect))
                    {
                        if (em.Equals(default(EntityManager))) continue;
                        if (!SectQuery.IsAdopted(em, faction, owningSect)) continue;
                        if (SectBuildingCount(em, building.id, faction)
                            >= TheWaningBorder.Entities.SectBuilding.CapPerFaction) continue;
                    }

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

                    bool canAfford = !em.Equals(default(EntityManager))
                        ? FactionEconomy.CanAfford(em, faction, cost)
                        : true;

                    // Era gating: show button disabled with requirement text instead of hiding
                    bool eraLocked = building.minEra > 0 && building.minEra > factionEra;
                    string requirement = eraLocked
                        ? string.Format(Loc.T("Requires: Era {0}"), building.minEra) : null;

                    string tooltip = BuildTooltip(
                        building.name,
                        building.role,
                        cost,
                        available,
                        requirement: requirement
                    );

                    actions.Add(new ActionButton
                    {
                        Id = building.id,
                        Label = Loc.T(building.name),
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
