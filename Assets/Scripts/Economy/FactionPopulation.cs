// FactionPopulation.cs
// Population tracking components and helper utilities
// Part of: Economy/

using Unity.Entities;
using Unity.Collections;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Economy
{
    // ═══════════════════════════════════════════════════════════════════════
    // FACTION POPULATION COMPONENT
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Tracks population capacity and usage for a faction.
    /// Attached to the faction's "bank" entity alongside FactionResources.
    /// 
    /// Current = sum of all living units with PopulationCost
    /// Max = sum of all completed buildings with PopulationProvider (capped at AbsoluteMax)
    /// </summary>
    public struct FactionPopulation : IComponentData
    {
        /// <summary>How many population slots are currently used by units</summary>
        public int Current;
        
        /// <summary>Maximum population available from buildings (capped at AbsoluteMax)</summary>
        public int Max;
        
        /// <summary>Hard cap on population - cannot exceed this value (200)</summary>
        public const int AbsoluteMax = 200;
        
        // ==================== Helpers ====================
        
        /// <summary>
        /// Returns true if population is at maximum capacity.
        /// </summary>
        public bool IsFull => Current >= Max;
        
        /// <summary>
        /// Returns true if population cap has been reached (200).
        /// </summary>
        public bool IsAtCap => Max >= AbsoluteMax;
        
        /// <summary>
        /// Returns available population slots.
        /// </summary>
        public int Available => Max - Current;
        
        /// <summary>
        /// Check if there's room for a unit with the specified population cost.
        /// </summary>
        public bool HasCapacityFor(int populationCost)
        {
            return (Current + populationCost) <= Max;
        }
        
        /// <summary>
        /// Get a formatted display string (e.g., "15/50" or "200/200 (MAX)")
        /// </summary>
        public string GetDisplayString()
        {
            if (Max >= AbsoluteMax)
                return $"{Current}/{Max} (MAX)";
            return $"{Current}/{Max}";
        }
        
        public override string ToString()
        {
            return $"Pop: {Current}/{Max}";
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // POPULATION PROVIDER COMPONENT
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Attached to buildings that provide population capacity.
    /// Only counted when the building is complete (no UnderConstruction component).
    /// 
    /// Examples:
    /// - Hall: 20 population
    /// - Hut: 10 population
    /// </summary>
    public struct PopulationProvider : IComponentData
    {
        /// <summary>How much population capacity this building provides when completed</summary>
        public int Amount;
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // POPULATION COST COMPONENT
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Attached to units that consume population slots.
    /// Most basic units consume 1 slot, larger units may consume more.
    /// </summary>
    public struct PopulationCost : IComponentData
    {
        /// <summary>How many population slots this unit consumes</summary>
        public int Amount;
        
        /// <summary>
        /// Standard population cost for most units.
        /// </summary>
        public static PopulationCost Standard => new PopulationCost { Amount = 1 };
        
        /// <summary>
        /// Create a population cost with specified amount.
        /// </summary>
        public static PopulationCost Of(int amount) => new PopulationCost { Amount = amount };
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // POPULATION HELPER UTILITIES
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Static utility class for population queries and operations.
    /// </summary>
    public static class PopulationHelper
    {
        /// <summary>
        /// Get the population stats for a specific faction.
        /// </summary>
        /// <param name="faction">Faction to query</param>
        /// <param name="current">Current population used</param>
        /// <param name="max">Maximum population available</param>
        /// <returns>True if faction population data was found</returns>
        public static bool TryGetFactionPopulation(Faction faction, out int current, out int max)
        {
            current = 0;
            max = 0;
            
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return false;

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionPopulation>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var tags = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var populations = query.ToComponentDataArray<FactionPopulation>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                {
                    current = populations[i].Current;
                    max = populations[i].Max;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a faction has enough population capacity to create a unit.
        /// </summary>
        /// <param name="faction">Faction to check</param>
        /// <param name="requiredPopulation">Population cost of the unit</param>
        /// <returns>True if faction has capacity</returns>
        public static bool HasPopulationCapacity(Faction faction, int requiredPopulation)
        {
            if (TryGetFactionPopulation(faction, out int current, out int max))
            {
                return (current + requiredPopulation) <= max;
            }
            return false;
        }

        /// <summary>
        /// Get the population cost for a unit type by ID.
        /// Override this with TechTreeDB lookup in the future.
        /// </summary>
        /// <param name="unitId">Unit type ID</param>
        /// <returns>Population cost for the unit</returns>
        public static int GetUnitPopulationCost(string unitId)
        {
            return unitId switch
            {
                // Basic units - 1 population each
                "Builder" => 1,
                "Miner" => 1,
                "Scout" => 1,
                "Archer" => 1,
                "Swordsman" => 1,
                "Litharch" => 1,
                
                // Feraldis units
                "Feraldis_Berserker" => 1,
                "Feraldis_Hunter" => 1,
                "Feraldis_WarboarRider" => 2,
                "Feraldis_SiegeRam" => 3,
                
                // Alanthor units
                "Alanthor_Sentinel" => 1,
                "Alanthor_Crossbowman" => 1,
                "Alanthor_Cataphract" => 2,
                "Alanthor_Ballista" => 3,
                
                // Default for unknown units
                _ => 1
            };
        }

        /// <summary>
        /// Get the population provided by a building type.
        /// Override this with TechTreeDB lookup in the future.
        /// </summary>
        /// <param name="buildingId">Building type ID</param>
        /// <returns>Population capacity provided</returns>
        public static int GetBuildingPopulationProvided(string buildingId)
        {
            return buildingId switch
            {
                "Hall" => 20,
                "Hut" => 10,
                "FiendstoneKeep" => 25,
                "KingsCourt" => 30,
                "Feraldis_Longhouse" => 15,
                _ => 0  // Most buildings don't provide population
            };
        }

        /// <summary>
        /// Check if a faction is at the absolute population cap (200).
        /// </summary>
        public static bool IsAtPopulationCap(Faction faction)
        {
            if (TryGetFactionPopulation(faction, out _, out int max))
            {
                return max >= FactionPopulation.AbsoluteMax;
            }
            return false;
        }

        /// <summary>
        /// Get a formatted string for population display in UI.
        /// Example: "15/50" or "150/200 (MAX)"
        /// </summary>
        public static string GetPopulationDisplayString(Faction faction)
        {
            if (TryGetFactionPopulation(faction, out int current, out int max))
            {
                if (max >= FactionPopulation.AbsoluteMax)
                {
                    return $"{current}/{max} (MAX)";
                }
                return $"{current}/{max}";
            }
            return "0/0";
        }
    }
}