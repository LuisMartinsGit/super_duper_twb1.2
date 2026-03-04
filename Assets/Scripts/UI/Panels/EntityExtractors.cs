// EntityExtractors.cs
// Helper classes to extract UI display info from ECS entities
// Location: Assets/Scripts/UI/Common/EntityExtractors.cs

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.AI;

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
                IronPerMinute = 0
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

            // Type and name
            if (em.HasComponent<BuildingTag>(entity))
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
                    info.ResourceMax = 10;
                    info.ResourceTypeName = "Crystal";
                    info.Description = cadState.Depleted == 1 ? "Depleted" : "Harvestable crystal";
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
    }

    /// <summary>
    /// Extracts action information from entities for EntityActionPanel.
    /// </summary>
    public static class EntityActionExtractor
    {
        public static EntityActionInfo GetActionInfo(Entity entity, EntityManager em)
        {
            var info = new EntityActionInfo
            {
                Type = ActionType.None,
                Actions = new List<ActionButton>()
            };

            if (!em.Exists(entity)) return info;

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

            // Check if this is a training building (any building with a TrainingState)
            if (em.HasComponent<BuildingTag>(entity) && em.HasComponent<TrainingState>(entity))
            {
                var trainingActions = GetTrainingActions(entity, em);
                if (trainingActions.Count > 0)
                {
                    info.Type = ActionType.UnitTraining;
                    info.Actions = trainingActions;
                    info.TrainingState = GetTrainingInfo(entity, em);
                    return info;
                }
            }

            return info;
        }

        // Buildings the player can place via builder (excludes starting buildings and other-faction variants)
        private static readonly HashSet<string> BuildableBuildings = new()
        {
            "Hut", "GatherersHut", "Barracks", "TempleOfRidan", "VaultOfAlmierra", "FiendstoneKeep",
            "Alanthor_Wall", "Alanthor_Smelter"
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

            if (TechTreeDB.Instance != null)
            {
                foreach (var building in TechTreeDB.Instance.GetAllBuildings())
                {
                    // Only show buildings the player can actually place
                    if (!BuildableBuildings.Contains(building.id)) continue;

                    // Choice building exclusion: if one is built, hide the other two
                    if (BuildingFactory.IsChoiceBuilding(building.id) && existingChoice != null)
                        continue;

                    // Culture-gated building restrictions
                    // Alanthor cannot build Gatherer's Huts (they use walls for income)
                    if (building.id == "GatherersHut" && factionCulture == Cultures.Alanthor)
                        continue;
                    // Only Alanthor can build walls
                    if (building.id == "Alanthor_Wall" && factionCulture != Cultures.Alanthor)
                        continue;
                    // Only Alanthor can build Smelters
                    if (building.id == "Alanthor_Smelter" && factionCulture != Cultures.Alanthor)
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

                    actions.Add(new ActionButton
                    {
                        Id = building.id,
                        Label = building.name,
                        Tooltip = building.role ?? "",
                        Cost = cost,
                        Enabled = true,
                        CanAfford = canAfford,
                        Icon = null
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

            // Identify building type and look up its definition
            string buildingId = GetBuildingId(entity, em);
            if (buildingId == null || TechTreeDB.Instance == null) return actions;

            if (!TechTreeDB.Instance.TryGetBuilding(buildingId, out var buildingDef)) return actions;
            if (buildingDef.trains == null || buildingDef.trains.Length == 0) return actions;

            // Only show units this building can train (from its "trains" array)
            foreach (var unitId in buildingDef.trains)
            {
                if (!TechTreeDB.Instance.TryGetUnit(unitId, out var unit)) continue;

                var cost = unit.cost != null ? new Cost
                {
                    Supplies = unit.cost.Supplies,
                    Iron = unit.cost.Iron,
                    Crystal = unit.cost.Crystal
                } : default;

                actions.Add(new ActionButton
                {
                    Id = unit.id,
                    Label = unit.name,
                    Tooltip = unit.unitClass ?? "",
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

            // Build queue display
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
        /// Map entity to its TechTree building ID using tag components.
        /// </summary>
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
            return null;
        }
    }
}