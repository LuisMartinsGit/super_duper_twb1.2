// EntityExtractors.cs
// Helper classes to extract UI display info from ECS entities
// Location: Assets/Scripts/UI/Common/EntityExtractors.cs

using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI
{
    /// <summary>
    /// Extracts display information from entities for EntityInfoPanel.
    /// </summary>
    public static class EntityInfoExtractor
    {
        public static EntityDisplayInfo GetDisplayInfo(Entity entity, EntityManager em)
        {
            var info = new EntityDisplayInfo
            {
                Name = "Unknown",
                Type = "Entity",
                Description = "",
                Portrait = null,
                CurrentHealth = 0,
                MaxHealth = 0,
                Faction = "Neutral",
                HasCombatStats = false,
                Attack = 0,
                Defense = 0,
                Speed = 0,
                HasResourceGeneration = false,
                SuppliesPerMinute = 0,
                IronPerMinute = 0,
                CrystalPerMinute = 0,
                VeilsteelPerMinute = 0,
                GlowPerMinute = 0
            };

            if (!em.Exists(entity)) return info;

            // Faction
            if (em.HasComponent<FactionTag>(entity))
                info.Faction = em.GetComponentData<FactionTag>(entity).Value.ToString();

            // Health
            if (em.HasComponent<Health>(entity))
            {
                var health = em.GetComponentData<Health>(entity);
                info.CurrentHealth = (int)health.Value;
                info.MaxHealth = (int)health.Max;
            }

            // Combat stats
            if (em.HasComponent<Damage>(entity))
            {
                info.HasCombatStats = true;
                info.Attack = (int)em.GetComponentData<Damage>(entity).Value;
            }
            if (em.HasComponent<Defense>(entity))
            {
                info.HasCombatStats = true;
                var def = em.GetComponentData<Defense>(entity);
                info.Defense = (int)def.Melee; // or average of all defense types
            }
            if (em.HasComponent<MoveSpeed>(entity))
            {
                info.Speed = em.GetComponentData<MoveSpeed>(entity).Value;
            }

            // Resource generation
            if (em.HasComponent<SuppliesIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.SuppliesPerMinute = em.GetComponentData<SuppliesIncome>(entity).PerMinute;
            }
            if (em.HasComponent<IronIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.IronPerMinute = em.GetComponentData<IronIncome>(entity).PerMinute;
            }
            if (em.HasComponent<CrystalIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.CrystalPerMinute = em.GetComponentData<CrystalIncome>(entity).PerMinute;
            }
            if (em.HasComponent<VeilsteelIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.VeilsteelPerMinute = em.GetComponentData<VeilsteelIncome>(entity).PerMinute;
            }
            if (em.HasComponent<GlowIncome>(entity))
            {
                info.HasResourceGeneration = true;
                info.GlowPerMinute = em.GetComponentData<GlowIncome>(entity).PerMinute;
            }

            // Type and name — check battalion leader first
            if (em.HasComponent<BattalionLeader>(entity))
            {
                var bl = em.GetComponentData<BattalionLeader>(entity);
                info.Type = "Battalion";
                info.Name = bl.UnitId.ToString() + " Battalion";

                // Aggregate HP from living members
                if (em.HasBuffer<BattalionMember>(entity))
                {
                    var buf = em.GetBuffer<BattalionMember>(entity);
                    int memberCount = 0, totalHP = 0, totalMaxHP = 0;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (!em.Exists(buf[i].Value)) continue;
                        memberCount++;
                        if (em.HasComponent<Health>(buf[i].Value))
                        {
                            var mhp = em.GetComponentData<Health>(buf[i].Value);
                            totalHP += mhp.Value;
                            totalMaxHP += mhp.Max;
                        }
                    }
                    info.CurrentHealth = totalHP;
                    info.MaxHealth = totalMaxHP;
                    info.Description = $"{memberCount}/{bl.Columns * bl.Rows} soldiers";
                }
            }
            else if (em.HasComponent<CrystalMainNodeTag>(entity))
            {
                info.Type = "Crystal Hive";
                info.Name = "Crystal Main Node";
                if (em.HasComponent<CrystalNodeLevel>(entity))
                {
                    int level = em.GetComponentData<CrystalNodeLevel>(entity).Value;
                    string threat = level switch { 1 => "Low Threat", 2 => "Moderate Threat", _ => "High Threat" };
                    info.Description = $"Level {level} — {threat}";
                }
                if (em.HasComponent<CrystalNode>(entity) && em.HasComponent<CrystalSpreadState>(entity))
                {
                    var cn = em.GetComponentData<CrystalNode>(entity);
                    var ss = em.GetComponentData<CrystalSpreadState>(entity);
                    int pct = cn.SpreadRadius > 0 ? (int)(ss.CurrentRingRadius / cn.SpreadRadius * 100f) : 0;
                    info.Description += $"\nSpread: {pct}%";
                }
            }
            else if (em.HasComponent<BuildingTag>(entity))
            {
                info.Type = "Building";
                info.Name = GetBuildingName(entity, em);
            }
            else if (em.HasComponent<UnitTag>(entity))
            {
                info.Type = "Unit";
                info.Name = GetUnitName(entity, em);
            }
            else if (em.HasComponent<IronMineTag>(entity))
            {
                info.Type = "Resource";
                info.Name = "Iron Deposit";
                info.HasResourceInfo = true;
                if (em.HasComponent<IronDepositState>(entity))
                {
                    var depState = em.GetComponentData<IronDepositState>(entity);
                    info.ResourceRemaining = depState.RemainingIron;
                    info.ResourceMax = 500;
                    info.ResourceTypeName = "Iron";
                    info.Description = depState.Depleted == 1 ? "Depleted" : "Active iron deposit";
                }
            }
            else if (em.HasComponent<CadaverTag>(entity))
            {
                info.Type = "Resource";
                info.Name = "Crystal Node";
                info.HasResourceInfo = true;
                if (em.HasComponent<CadaverState>(entity))
                {
                    var cadState = em.GetComponentData<CadaverState>(entity);
                    info.ResourceRemaining = cadState.RemainingCrystal;
                    info.ResourceMax = cadState.MaxCrystal > 0 ? cadState.MaxCrystal : cadState.RemainingCrystal;
                    info.ResourceTypeName = "Crystal";
                    info.Description = cadState.Depleted == 1 ? "Depleted" : "Harvestable crystal";
                }
            }

            // Temple level and era info
            if (em.HasComponent<TempleTag>(entity) && em.HasComponent<TempleLevel>(entity))
            {
                var templeLevel = em.GetComponentData<TempleLevel>(entity);
                int era = TempleLevelConfig.GetEraForLevel(templeLevel.Level);
                string levelStr = templeLevel.Level >= TempleLevelConfig.MaxLevel
                    ? $"Level {templeLevel.Level} (Max)"
                    : $"Level {templeLevel.Level}";
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Temple {levelStr} | Era {era}";

                // Show faction RP
                if (em.HasComponent<FactionTag>(entity))
                {
                    var faction = em.GetComponentData<FactionTag>(entity).Value;
                    int rp = GetFactionReligionPoints(em, faction);
                    if (rp > 0)
                        info.Description += $"\nReligion Points: {rp}";
                }
            }

            // Forge storage info
            if (em.HasComponent<ForgeStorage>(entity))
            {
                var forge = em.GetComponentData<ForgeStorage>(entity);
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Iron: {forge.Iron}/{forge.MaxIron}  Crystal: {forge.Crystal}/{forge.MaxCrystal}";
            }

            // Self-destruct timer
            if (em.HasComponent<SelfDestructTimer>(entity))
            {
                var timer = em.GetComponentData<SelfDestructTimer>(entity);
                int minutes = (int)(timer.TimeRemaining / 60f);
                int seconds = (int)(timer.TimeRemaining % 60f);
                info.Description += (info.Description.Length > 0 ? "\n" : "")
                    + $"Self-destructing in {minutes}m {seconds:D2}s";
            }

            // Miner info
            if (em.HasComponent<MinerTag>(entity) && em.HasComponent<MinerState>(entity))
            {
                var miner = em.GetComponentData<MinerState>(entity);
                info.HasMinerInfo = true;
                info.MinerCurrentLoad = miner.CurrentLoad;

                if (miner.GatheringResource == 1)
                {
                    info.MinerResourceType = "Crystal";
                    info.MinerMaxCarry = 10;
                    info.MinerExtractionRate = "1 crystal / 1.5s";
                }
                else
                {
                    info.MinerResourceType = "Iron";
                    info.MinerMaxCarry = 10;
                    info.MinerExtractionRate = "10 iron / 2s";
                }

                info.MinerState = miner.State switch
                {
                    MinerWorkState.Idle => "Idle",
                    MinerWorkState.MovingToDeposit => "Moving to resource",
                    MinerWorkState.Gathering => "Gathering",
                    MinerWorkState.ReturningToBase => "Delivering",
                    _ => "Unknown"
                };
            }

            return info;
        }

        private static string GetBuildingName(Entity entity, EntityManager em)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<GathererHutTag>(entity)) return "Gatherer's Hut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<DepotTag>(entity)) return "Depot";
            if (em.HasComponent<WorkshopTag>(entity)) return "Workshop";
            if (em.HasComponent<TempleTag>(entity)) return "Shrine of Ridan";
            if (em.HasComponent<VaultTag>(entity)) return "Vault of Almiérra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "Fiendstone Keep";
            if (em.HasComponent<SmelterTag>(entity)) return "Smelter";
            if (em.HasComponent<WallHubTag>(entity)) return "Wall Hub";
            if (em.HasComponent<WallSegmentTag>(entity)) return "Wall";
            // Runai culture buildings
            if (em.HasComponent<OutpostTag>(entity)) return "Runai Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Trade Hub";
            if (em.HasComponent<TradingPostTag>(entity))
            {
                int num = em.HasComponent<TradingPostData>(entity) ? em.GetComponentData<TradingPostData>(entity).PostNumber : 0;
                return num > 0 ? $"Trading Post #{num}" : "Trading Post";
            }
            if (em.HasComponent<BazaarTag>(entity)) return "Bazaar";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Siege Workshop";
            // Alanthor culture buildings
            if (em.HasComponent<WatchTowerTag>(entity)) return "Watch Tower";
            if (em.HasComponent<GarrisonTag>(entity)) return "Garrison";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Royal Stable";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Siege Yard";
            // Feraldis culture buildings
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Hunting Lodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Logging Station";
            if (em.HasComponent<LonghouseTag>(entity)) return "Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Totem Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Siege Yard";
            return "Building";
        }

        private static string GetUnitName(Entity entity, EntityManager em)
        {
            if (em.HasComponent<CanBuild>(entity)) return "Builder";
            if (em.HasComponent<MinerTag>(entity)) return "Miner";
            if (em.HasComponent<ArcherTag>(entity)) return "Archer";
            if (em.HasComponent<BerserkerTag>(entity)) return "Berserker";

            // Fall back to UnitTag.Class for units without specific tags
            if (em.HasComponent<UnitTag>(entity))
            {
                var unitTag = em.GetComponentData<UnitTag>(entity);
                return unitTag.Class switch
                {
                    UnitClass.Melee => "Swordsman",
                    UnitClass.Ranged => "Archer",
                    UnitClass.Scout => "Scout",
                    UnitClass.Support => "Litharch",
                    UnitClass.Siege => "Siege Unit",
                    UnitClass.Economy => "Builder",
                    UnitClass.Miner => "Miner",
                    _ => "Unit"
                };
            }

            return "Unit";
        }

        /// <summary>
        /// Get the current era for a faction from its bank entity.
        /// Returns 1 if not found.
        /// </summary>
        public static int GetFactionEra(EntityManager em, Faction faction)
        {
            if (Unity.Entities.World.DefaultGameObjectInjectionWorld == null || !Unity.Entities.World.DefaultGameObjectInjectionWorld.IsCreated)
                return 1;
            if (em.Equals(default(EntityManager)))
                return 1;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionEra>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var tags = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var eras = query.ToComponentDataArray<FactionEra>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                    return eras[i].Value;
            }

            return 1;
        }

        /// <summary>
        /// Get the current religion points for a faction.
        /// Returns 0 if not found.
        /// </summary>
        public static int GetFactionReligionPoints(EntityManager em, Faction faction)
        {
            if (Unity.Entities.World.DefaultGameObjectInjectionWorld == null || !Unity.Entities.World.DefaultGameObjectInjectionWorld.IsCreated)
                return 0;
            if (em.Equals(default(EntityManager)))
                return 0;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ReligionPoints>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var tags = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var rps = query.ToComponentDataArray<ReligionPoints>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                    return rps[i].Value;
            }

            return 0;
        }
    }

    /// <summary>
    /// Extracts action information from entities for EntityActionPanel.
    /// </summary>
    public static class EntityActionExtractor
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
                "TempleOfRidan" => "ShrineOfAhridan",
                _ => buildingId
            };

            var tex = UnityEngine.Resources.Load<UnityEngine.Texture2D>($"UI/Icons/Buildings/{iconName}");
            _buildingIconCache[buildingId] = tex; // Cache even null to avoid repeated lookups
            return tex;
        }

        public static EntityActionInfo GetActionInfo(Entity entity, EntityManager em)
        {
            var info = new EntityActionInfo
            {
                Type = ActionType.None,
                Actions = new List<ActionButton>()
            };

            if (!em.Exists(entity)) return info;

            // Check if this is a battalion entity (leader or member) — show stance panel
            if (em.HasComponent<BattalionLeader>(entity))
            {
                info.Type = ActionType.BattalionStance;
                return info;
            }
            if (em.HasComponent<BattalionMemberData>(entity))
            {
                var memberData = em.GetComponentData<BattalionMemberData>(entity);
                if (em.Exists(memberData.Leader) && em.HasComponent<BattalionStanceData>(memberData.Leader))
                {
                    info.Type = ActionType.BattalionStance;
                    return info;
                }
            }

            // Check if this is a builder (can place buildings)
            if (em.HasComponent<CanBuild>(entity))
            {
                info.Type = ActionType.BuildingPlacement;
                info.Actions = GetBuildingActions();
                return info;
            }

            // Check if this is a vault
            if (em.HasComponent<VaultTag>(entity) && em.HasComponent<VaultStorage>(entity))
            {
                info.Type = ActionType.VaultManagement;
                return info;
            }

            // Check if this is a temple (training + level-up)
            if (em.HasComponent<TempleTag>(entity) && em.HasComponent<TempleLevel>(entity)
                && em.HasComponent<TrainingState>(entity))
            {
                info.Type = ActionType.TempleUpgrade;
                info.Actions = GetTrainingActions(entity, em);
                info.TrainingState = GetTrainingInfo(entity, em);
                return info;
            }

            // Check if this is a training building (any building with a TrainingState)
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<TrainingState>(entity))
            {
                var trainingActions = GetTrainingActions(entity, em);
                bool hasResearch = em.HasComponent<ResearchState>(entity);

                if (trainingActions.Count > 0)
                {
                    // Building can train and possibly research
                    info.Type = hasResearch ? ActionType.UnitTrainingAndResearch : ActionType.UnitTraining;
                    info.Actions = trainingActions;
                    info.TrainingState = GetTrainingInfo(entity, em);

                    if (hasResearch)
                        info.ResearchState = GetResearchInfo(entity, em);

                    return info;
                }
            }

            // Check if this is a research-only building
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<ResearchState>(entity))
            {
                info.Type = ActionType.UnitTrainingAndResearch;
                info.Actions = new List<ActionButton>();
                info.ResearchState = GetResearchInfo(entity, em);
                return info;
            }

            return info;
        }

        /// <summary>
        /// Get the current faction resources as a Cost for rich tooltip formatting.
        /// </summary>
        private static Cost GetFactionResourcesAsCost(EntityManager em, Faction faction)
        {
            if (em.Equals(default(EntityManager))) return default;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return default;
            return new Cost
            {
                Supplies = res.Supplies,
                Iron = res.Iron,
                Crystal = res.Crystal,
                Veilsteel = res.Veilsteel,
                Glow = res.Glow
            };
        }

        /// <summary>
        /// Build a rich-text tooltip for an action button.
        /// Shows name, cost (color-coded), training time, and any requirement lines in red.
        /// </summary>
        private static string BuildTooltip(string name, string subtitle, Cost cost, Cost available, float trainingTime = 0f, string requirement = null)
        {
            var sb = new System.Text.StringBuilder(128);
            sb.Append($"<b>{name}</b>");
            if (!string.IsNullOrEmpty(subtitle))
                sb.Append($"  <color=#b0a890>({subtitle})</color>");

            // Cost line
            sb.Append("\nCost: ");
            sb.Append(UIHelpers.FormatCostRich(cost, available));

            // Training/build time
            if (trainingTime > 0f)
                sb.Append($"\nTime: {trainingTime:F0}s");

            // Requirement (shown in red)
            if (!string.IsNullOrEmpty(requirement))
                sb.Append($"\n<color=#ff5555>{requirement}</color>");

            return sb.ToString();
        }

        // Buildings the player can place via builder (excludes starting buildings and other-faction variants)
        private static readonly HashSet<string> BuildableBuildings = new()
        {
            "Hut", "GatherersHut", "Barracks", "TempleOfRidan", "VaultOfAlmierra", "FiendstoneKeep",
            "Alanthor_Wall", "Alanthor_Smelter",
            // Runai culture buildings
            "Runai_Outpost", "Runai_TradeHub", "Runai_TradingPost", "ThessarasBazaar", "Runai_SiegeWorkshop",
            // Alanthor culture buildings
            "Alanthor_Tower", "Alanthor_Garrison", "Alanthor_Stable", "Alanthor_SiegeYard",
            // Feraldis culture buildings
            "Feraldis_HuntingLodge", "Feraldis_LoggingStation", "Feraldis_Longhouse",
            "Feraldis_Tower", "Feraldis_SiegeYard"
        };

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
                var hallQuery = em.CreateEntityQuery(typeof(HallTag), typeof(FactionTag), typeof(FactionProgress));
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

            if (TechTreeDB.Instance != null)
            {
                foreach (var building in TechTreeDB.Instance.GetAllBuildings())
                {
                    // Only show buildings the player can actually place
                    if (!BuildableBuildings.Contains(building.id)) continue;

                    // Choice building exclusion: if one is built, hide the other two
                    if (BuildingFactory.IsChoiceBuilding(building.id) && existingChoice != null)
                        continue;

                    // Data-driven culture gating: buildings with culture prefix require that culture
                    byte requiredCulture = GetRequiredCulture(building.id);
                    if (requiredCulture != Cultures.None && requiredCulture != factionCulture)
                        continue;

                    // Alanthor cannot build Gatherer's Huts (they use walls for income)
                    if (building.id == "GatherersHut" && factionCulture == Cultures.Alanthor)
                        continue;

                    var cost = building.cost != null ? new Cost
                    {
                        Supplies = building.cost.Supplies,
                        Iron = building.cost.Iron,
                        Crystal = building.cost.Crystal
                    } : default;

                    bool canAfford = !em.Equals(default(EntityManager))
                        ? FactionEconomy.CanAfford(em, faction, cost)
                        : true;

                    // Era gating: show button disabled with requirement text instead of hiding
                    bool eraLocked = building.minEra > 0 && building.minEra > factionEra;
                    string requirement = eraLocked ? $"Requires: Era {building.minEra}" : null;

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
                        Label = building.name,
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

        private static List<ActionButton> GetTrainingActions(Entity entity, EntityManager em)
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
            if (buildingId == null || TechTreeDB.Instance == null) return actions;

            if (!TechTreeDB.Instance.TryGetBuilding(buildingId, out var buildingDef)) return actions;
            if (buildingDef.trains == null || buildingDef.trains.Length == 0) return actions;

            // Determine faction culture from the building's faction -> Hall -> FactionProgress
            byte factionCulture = Cultures.None;
            if (em.HasComponent<FactionTag>(entity))
            {
                var buildingFaction = em.GetComponentData<FactionTag>(entity).Value;
                var hallQuery = em.CreateEntityQuery(typeof(HallTag), typeof(FactionTag), typeof(FactionProgress));
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

            // Only show units this building can train (from its "trains" array)
            foreach (var unitId in buildingDef.trains)
            {
                if (!TechTreeDB.Instance.TryGetUnit(unitId, out var unit)) continue;

                // Culture gating: skip units that require a different culture
                byte requiredCulture = GetRequiredCultureForUnit(unitId);
                if (requiredCulture != Cultures.None && requiredCulture != factionCulture)
                    continue;

                var cost = unit.cost != null ? new Cost
                {
                    Supplies = unit.cost.Supplies,
                    Iron = unit.cost.Iron,
                    Crystal = unit.cost.Crystal
                } : default;

                string tooltip = BuildTooltip(
                    unit.name,
                    unit.unitClass,
                    cost,
                    available,
                    trainingTime: unit.trainingTime
                );

                actions.Add(new ActionButton
                {
                    Id = unit.id,
                    Label = unit.name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, cost),
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
                if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit(unitId, out var udef))
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
        public static TheWaningBorder.Core.Cost GetUnitCost(string unitId)
        {
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit(unitId, out var udef) && udef.cost != null)
            {
                return new TheWaningBorder.Core.Cost
                {
                    Supplies = udef.cost.Supplies,
                    Iron = udef.cost.Iron,
                    Crystal = udef.cost.Crystal,
                    Veilsteel = udef.cost.Veilsteel,
                    Glow = udef.cost.Glow
                };
            }
            return default;
        }

        /// <summary>
        /// Get research action buttons for a building.
        /// Returns buttons for techs this building can research, with affordability and prerequisite checks.
        /// </summary>
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
            if (buildingId == null || TechTreeDB.Instance == null) return actions;

            if (!TechTreeDB.Instance.TryGetBuilding(buildingId, out var buildingDef)) return actions;
            if (buildingDef.research == null || buildingDef.research.Length == 0) return actions;

            var researchState = TheWaningBorder.Economy.FactionResearchState.Instance;
            Cost available = GetFactionResourcesAsCost(em, faction);

            foreach (var techId in buildingDef.research)
            {
                if (!TechTreeDB.Instance.TryGetTechnology(techId, out var tech)) continue;

                // Skip Research_Era2 — age-up is handled by DrawAgeUpSection + CultureChoicePopup
                if (techId == "Research_Era2") continue;

                // Skip already-researched techs
                bool alreadyResearched = researchState != null && researchState.HasResearched(faction, techId);
                if (alreadyResearched) continue;

                var cost = tech.cost != null ? new Cost
                {
                    Supplies = tech.cost.Supplies,
                    Iron = tech.cost.Iron,
                    Crystal = tech.cost.Crystal,
                    Veilsteel = tech.cost.Veilsteel,
                    Glow = tech.cost.Glow
                } : default;

                bool canAfford = FactionEconomy.CanAfford(em, faction, cost);
                bool meetsPrereqs = researchState == null || researchState.MeetsPrerequisites(faction, tech.prerequisites);

                string requirement = null;
                if (!meetsPrereqs && tech.prerequisites != null)
                    requirement = $"Requires: {string.Join(", ", tech.prerequisites)}";

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
                    Label = tech.name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = meetsPrereqs,
                    CanAfford = canAfford && meetsPrereqs,
                    Icon = null
                });
            }

            return actions;
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
                if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetTechnology(techId, out var techDef))
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
            return Cultures.None; // universal
        }

        /// <summary>
        /// Determine the required culture for a unit based on its ID prefix.
        /// Units with "Alanthor_" prefix require Alanthor culture, etc.
        /// Returns Cultures.None for universal units (available to all cultures).
        /// </summary>
        private static byte GetRequiredCultureForUnit(string unitId)
        {
            if (unitId.StartsWith("Alanthor_")) return Cultures.Alanthor;
            if (unitId.StartsWith("Feraldis_")) return Cultures.Feraldis;
            if (unitId.StartsWith("Runai_")) return Cultures.Runai;
            return Cultures.None; // universal
        }

        /// <summary>
        /// Map entity to its TechTree building ID using tag components.
        /// </summary>
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

            var chapelTag = em.GetComponentData<ChapelTag>(entity);
            string sectId = chapelTag.SectId.ToString();
            string unitId = SectConfig.GetSectUnitId(sectId);
            if (string.IsNullOrEmpty(unitId)) return actions;

            Cost available = GetFactionResourcesAsCost(em, faction);

            // Look up the unit in TechTreeDB for cost/stats
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetUnit(unitId, out var unit))
            {
                var cost = unit.cost != null ? new Cost
                {
                    Supplies = unit.cost.Supplies,
                    Iron = unit.cost.Iron,
                    Crystal = unit.cost.Crystal
                } : default;

                string tooltip = BuildTooltip(unit.name, unit.unitClass, cost, available, trainingTime: unit.trainingTime);

                actions.Add(new ActionButton
                {
                    Id = unit.id,
                    Label = unit.name,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }
            else
            {
                // Fallback if unit not in TechTreeDB — show with placeholder cost
                string displayName = SectConfig.GetDisplayName(sectId) + " Unit";
                var fallbackCost = Cost.Of(supplies: 100, iron: 50);
                actions.Add(new ActionButton
                {
                    Id = unitId,
                    Label = displayName,
                    Tooltip = BuildTooltip(displayName, "Sect unit", fallbackCost, available),
                    Cost = fallbackCost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, fallbackCost),
                    Icon = null
                });
            }

            return actions;
        }

        /// <summary>
        /// Get research actions for a chapel entity.
        /// Each chapel researches one unique sect technology defined in SectConfig.
        /// </summary>
        private static List<ActionButton> GetChapelResearchActions(Entity entity, EntityManager em, Faction faction)
        {
            var actions = new List<ActionButton>();
            if (!em.HasComponent<ChapelTag>(entity)) return actions;

            var chapelTag = em.GetComponentData<ChapelTag>(entity);
            string sectId = chapelTag.SectId.ToString();
            string techId = SectConfig.GetTechId(sectId);
            if (string.IsNullOrEmpty(techId)) return actions;

            // Skip if already researched
            var researchState = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (researchState != null && researchState.HasResearched(faction, techId))
                return actions;

            Cost available = GetFactionResourcesAsCost(em, faction);

            // Look up the tech in TechTreeDB for cost
            if (TechTreeDB.Instance != null && TechTreeDB.Instance.TryGetTechnology(techId, out var tech))
            {
                var cost = tech.cost != null ? new Cost
                {
                    Supplies = tech.cost.Supplies,
                    Iron = tech.cost.Iron,
                    Crystal = tech.cost.Crystal,
                    Veilsteel = tech.cost.Veilsteel,
                    Glow = tech.cost.Glow
                } : default;

                string techName = tech.name ?? SectConfig.GetTechDisplayName(sectId);
                string tooltip = BuildTooltip(
                    techName,
                    SectConfig.GetTechDescription(sectId),
                    cost,
                    available,
                    trainingTime: tech.researchTime
                );

                actions.Add(new ActionButton
                {
                    Id = tech.id,
                    Label = techName,
                    Tooltip = tooltip,
                    Cost = cost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, cost),
                    Icon = null
                });
            }
            else
            {
                // Fallback if tech not in TechTreeDB
                string techName = SectConfig.GetTechDisplayName(sectId);
                string techDesc = SectConfig.GetTechDescription(sectId);
                var fallbackCost = Cost.Of(supplies: 150, iron: 75, crystal: 50);

                actions.Add(new ActionButton
                {
                    Id = techId,
                    Label = techName,
                    Tooltip = BuildTooltip(techName, techDesc, fallbackCost, available),
                    Cost = fallbackCost,
                    Enabled = true,
                    CanAfford = FactionEconomy.CanAfford(em, faction, fallbackCost),
                    Icon = null
                });
            }

            return actions;
        }

        private static string GetBuildingId(Entity entity, EntityManager em)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<GathererHutTag>(entity)) return "GatherersHut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<TempleTag>(entity)) return "TempleOfRidan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";
            if (em.HasComponent<SmelterTag>(entity)) return "Alanthor_Smelter";
            // Runai culture buildings
            if (em.HasComponent<OutpostTag>(entity)) return "Runai_Outpost";
            if (em.HasComponent<TradeHubTag>(entity)) return "Runai_TradeHub";
            if (em.HasComponent<TradingPostTag>(entity)) return "Runai_TradingPost";
            if (em.HasComponent<BazaarTag>(entity)) return "ThessarasBazaar";
            if (em.HasComponent<SiegeWorkshopTag>(entity)) return "Runai_SiegeWorkshop";
            // Alanthor culture buildings
            if (em.HasComponent<WatchTowerTag>(entity)) return "Alanthor_Tower";
            if (em.HasComponent<GarrisonTag>(entity)) return "Alanthor_Garrison";
            if (em.HasComponent<RoyalStableTag>(entity)) return "Alanthor_Stable";
            if (em.HasComponent<SiegeYardTag>(entity)) return "Alanthor_SiegeYard";
            // Feraldis culture buildings
            if (em.HasComponent<HuntingLodgeTag>(entity)) return "Feraldis_HuntingLodge";
            if (em.HasComponent<LoggingStationTag>(entity)) return "Feraldis_LoggingStation";
            if (em.HasComponent<LonghouseTag>(entity)) return "Feraldis_Longhouse";
            if (em.HasComponent<TotemTowerTag>(entity)) return "Feraldis_Tower";
            if (em.HasComponent<FerSiegeYardTag>(entity)) return "Feraldis_SiegeYard";
            // Sect chapels — dynamic building ID based on sect
            if (em.HasComponent<ChapelTag>(entity))
            {
                var chapelTag = em.GetComponentData<ChapelTag>(entity);
                return "Chapel_" + chapelTag.SectId.ToString();
            }
            return null;
        }
    }
}