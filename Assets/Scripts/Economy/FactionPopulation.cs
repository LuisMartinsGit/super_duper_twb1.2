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
        
        /// <summary>Hard cap on population - cannot exceed this value. 300
        /// since 2026-10-01 (was 200), alongside doubled House population.</summary>
        public const int AbsoluteMax = 300;
        
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
        // Fix #206: cache the query so per-frame UI calls don't allocate
        // native memory every time.
        private static EntityQuery _cachedQuery;
        private static EntityManager _queryOwner;

        /// <summary>Clear the cached query on world reset.</summary>
        public static void ClearCache()
        {
            _cachedQuery = default;
            _queryOwner = default;
            System.Array.Clear(_entityOf, 0, _entityOf.Length);
        }

        public static bool TryGetFactionPopulation(Faction faction, out int current, out int max)
        {
            current = 0;
            max = 0;

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return false;

            var em = world.EntityManager;

            if (!_queryOwner.Equals(em))
            {
                System.Array.Clear(_entityOf, 0, _entityOf.Length);
                _cachedQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionPopulation>()
                );
                _queryOwner = em;
            }

            // Entity remembered, data read fresh (see FactionResourcesHelper).
            int slot = (int)faction;
            if (slot >= 0 && slot < _entityOf.Length)
            {
                var known = _entityOf[slot];
                if (known != Entity.Null && em.Exists(known)
                    && em.HasComponent<FactionPopulation>(known) && em.HasComponent<FactionTag>(known)
                    && em.GetComponentData<FactionTag>(known).Value == faction)
                {
                    var pop = em.GetComponentData<FactionPopulation>(known);
                    current = pop.Current;
                    max = pop.Max;
                    return true;
                }
            }

            using var entities = _cachedQuery.ToEntityArray(Allocator.Temp);
            using var tags = _cachedQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value == faction)
                {
                    if (slot >= 0 && slot < _entityOf.Length) _entityOf[slot] = entities[i];
                    var pop = em.GetComponentData<FactionPopulation>(entities[i]);
                    current = pop.Current;
                    max = pop.Max;
                    return true;
                }
            }

            return false;
        }

        private static readonly Entity[] _entityOf = new Entity[16];

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
        /// The population a unit of this id occupies: its SO's
        /// <c>populationCost</c>, the same number its factory stamps on the
        /// PopulationCost component. This was an id switch until 2026-10-03
        /// (unification item 34) — a second table that disagreed with the
        /// factories on three units (Sentinel, Acolyte, Warbreaker) and still
        /// called the Alanthor Catapult a retired Ballista alias.
        /// </summary>
        public static int GetUnitPopulationCost(string unitId)
            => TechCatalog.Unit(unitId).populationCost;

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