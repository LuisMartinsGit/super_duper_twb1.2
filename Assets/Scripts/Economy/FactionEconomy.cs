// FactionEconomy.cs
// Core economy utilities for faction resource management
// Part of: Economy/

using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Economy
{


    // ═══════════════════════════════════════════════════════════════════════
    // FACTION ECONOMY UTILITIES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Static utility class for faction economy operations.
    /// Provides methods to find faction banks, check affordability, and spend resources.
    /// </summary>
    public static class FactionEconomy
    {
        // Cached faction → bank entity mapping. Populated lazily, cleared on world destroy.
        private static readonly Dictionary<Faction, Entity> _bankCache = new Dictionary<Faction, Entity>();
        private static EntityQuery _bankQuery;
        private static bool _bankQueryInitialized;

        /// <summary>
        /// Clear the bank cache. Call when the ECS world is destroyed or reset.
        /// </summary>
        public static void ClearCache()
        {
            _bankCache.Clear();
            _bankQueryInitialized = false;
        }

        /// <summary>
        /// Try to find the resource bank entity for a faction.
        /// Uses a cached dictionary to avoid creating queries every call.
        /// </summary>
        /// <param name="em">EntityManager to query</param>
        /// <param name="fac">Faction to find bank for</param>
        /// <param name="bank">Output bank entity if found</param>
        /// <returns>True if bank was found</returns>
        public static bool TryGetBank(EntityManager em, Faction fac, out Entity bank)
        {
            // Check cache first — validate the entity still exists and has required components
            if (_bankCache.TryGetValue(fac, out bank))
            {
                if (em.Exists(bank) && em.HasComponent<FactionResources>(bank))
                    return true;
                // Cached entity invalid — remove and re-query
                _bankCache.Remove(fac);
            }

            // Initialize query once
            if (!_bankQueryInitialized)
            {
                _bankQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadWrite<FactionResources>()
                );
                _bankQueryInitialized = true;
            }

            using var ents = _bankQuery.ToEntityArray(Allocator.Temp);
            using var tags = _bankQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                // Populate cache for all factions while we iterate
                _bankCache[tags[i].Value] = ents[i];

                if (tags[i].Value == fac)
                {
                    bank = ents[i];
                    return true;
                }
            }

            bank = Entity.Null;
            return false;
        }

        /// <summary>
        /// Check if a faction can afford a cost.
        /// </summary>
        /// <param name="em">EntityManager to query</param>
        /// <param name="fac">Faction to check</param>
        /// <param name="c">Cost to check against</param>
        /// <returns>True if faction has enough resources</returns>
        public static bool CanAfford(EntityManager em, Faction fac, in Cost c)
        {
            if (c.IsZero) return true;
            if (!TryGetBank(em, fac, out var bank)) return false;

            var r = em.GetComponentData<FactionResources>(bank);
            return r.Supplies >= c.Supplies
                && r.Iron >= c.Iron
                && r.Crystal >= c.Crystal
                && r.Veilsteel >= c.Veilsteel
                && r.Glow >= c.Glow;
        }

        /// <summary>
        /// Attempt to spend resources from a faction's bank.
        /// </summary>
        /// <param name="em">EntityManager to modify</param>
        /// <param name="fac">Faction to deduct from</param>
        /// <param name="c">Cost to spend</param>
        /// <returns>True if resources were spent successfully, false if not affordable</returns>
        public static bool Spend(EntityManager em, Faction fac, in Cost c)
        {
            if (c.IsZero) return true;
            if (!TryGetBank(em, fac, out var bank)) return false;

            var r = em.GetComponentData<FactionResources>(bank);
            
            // Check affordability first
            if (r.Supplies < c.Supplies || r.Iron < c.Iron || r.Crystal < c.Crystal ||
                r.Veilsteel < c.Veilsteel || r.Glow < c.Glow)
                return false;

            // Deduct resources
            r.Supplies -= c.Supplies;
            r.Iron -= c.Iron;
            r.Crystal -= c.Crystal;
            r.Veilsteel -= c.Veilsteel;
            r.Glow -= c.Glow;

            em.SetComponentData(bank, r);
            return true;
        }
        
        /// <summary>
        /// Add resources to a faction's bank (e.g., from gathering, income, or refunds).
        /// </summary>
        /// <param name="em">EntityManager to modify</param>
        /// <param name="fac">Faction to credit</param>
        /// <param name="c">Resources to add</param>
        /// <returns>True if resources were added successfully</returns>
        public static bool Add(EntityManager em, Faction fac, in Cost c)
        {
            if (c.IsZero) return true;
            if (!TryGetBank(em, fac, out var bank)) return false;

            var r = em.GetComponentData<FactionResources>(bank);
            
            r.Supplies += c.Supplies;
            r.Iron += c.Iron;
            r.Crystal += c.Crystal;
            r.Veilsteel += c.Veilsteel;
            r.Glow += c.Glow;
            r.Clamp();

            em.SetComponentData(bank, r);
            return true;
        }
        
        /// <summary>
        /// Get current resource amounts for a faction.
        /// </summary>
        /// <param name="em">EntityManager to query</param>
        /// <param name="fac">Faction to query</param>
        /// <param name="resources">Output resources if found</param>
        /// <returns>True if faction bank was found</returns>
        public static bool TryGetResources(EntityManager em, Faction fac, out FactionResources resources)
        {
            if (!TryGetBank(em, fac, out var bank))
            {
                resources = default;
                return false;
            }
            
            resources = em.GetComponentData<FactionResources>(bank);
            return true;
        }
    }
}