// File: Assets/Scripts/Entities/Units/UnitFactory.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// Unified factory for creating all unit types.
    /// 
    /// Provides a single entry point for spawning units by ID,
    /// with automatic stat loading from TechTreeDB.
    /// 
    /// Usage:
    ///   Entity unit = UnitFactory.Create(em, "Archer", position, faction);
    /// </summary>
    public static class UnitFactory
    {
        /// <summary>
        /// Create a unit by its ID string.
        /// Automatically loads stats from TechTreeDB if available.
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="unitId">Unit type: "Builder", "Miner", "Swordsman", "Archer", "Scout", "Litharch"</param>
        /// <param name="position">World position to spawn at</param>
        /// <param name="faction">Faction the unit belongs to</param>
        /// <returns>Created entity</returns>
        public static Entity Create(EntityManager em, string unitId, float3 position, Faction faction)
        {
            return unitId switch
            {
                "Builder" => Builder.Create(em, position, faction),
                "Miner" => Miner.Create(em, position, faction),
                "Swordsman" => Swordsman.Create(em, position, faction),
                "Archer" => Archer.Create(em, position, faction),
                "Scout" => Scout.Create(em, position, faction),
                "Litharch" => Litharch.Create(em, position, faction),
                "Berserker" or "Feraldis_Berserker" => Berserker.Create(em, position, faction),
                "Crystalling" => Crystalling.Create(em, position, faction),
                "Veilstinger" => Veilstinger.Create(em, position, faction),
                "Godsplinter" => Godsplinter.Create(em, position, faction),
                _ => CreateDefault(em, unitId, position, faction)
            };
        }

        /// <summary>
        /// Create a unit using EntityCommandBuffer for deferred creation.
        /// Useful when creating units from within system updates.
        /// </summary>
        public static Entity Create(EntityCommandBuffer ecb, string unitId, float3 position, Faction faction)
        {
            return unitId switch
            {
                "Builder" => Builder.Create(ecb, position, faction),
                "Miner" => Miner.Create(ecb, position, faction),
                "Swordsman" => Swordsman.Create(ecb, position, faction),
                "Archer" => Archer.Create(ecb, position, faction),
                "Scout" => Scout.Create(ecb, position, faction),
                "Litharch" => Litharch.Create(ecb, position, faction),
                "Berserker" or "Feraldis_Berserker" => Berserker.Create(ecb, position, faction),
                "Crystalling" => Crystalling.Create(ecb, position, faction),
                "Veilstinger" => Veilstinger.Create(ecb, position, faction),
                "Godsplinter" => Godsplinter.Create(ecb, position, faction),
                _ => CreateDefault(ecb, unitId, position, faction)
            };
        }

        /// <summary>
        /// Get population cost for a unit type.
        /// Delegates to PopulationHelper as the single source of truth.
        /// </summary>
        public static int GetPopulationCost(string unitId)
        {
            return PopulationHelper.GetUnitPopulationCost(unitId);
        }

        /// <summary>
        /// Get the UnitClass for a unit type.
        /// </summary>
        public static UnitClass GetUnitClass(string unitId)
        {
            return unitId switch
            {
                "Builder" => UnitClass.Economy,
                "Miner" => UnitClass.Miner,
                "Swordsman" => UnitClass.Melee,
                "Archer" => UnitClass.Ranged,
                "Scout" => UnitClass.Scout,
                "Litharch" => UnitClass.Support,
                "Berserker" or "Feraldis_Berserker" => UnitClass.Melee,
                "Crystalling" => UnitClass.Melee,
                "Veilstinger" => UnitClass.Ranged,
                "Godsplinter" => UnitClass.Siege,
                _ => UnitClass.Melee
            };
        }

        /// <summary>
        /// Get the PresentationId for a unit type.
        /// </summary>
        public static int GetPresentationId(string unitId)
        {
            return unitId switch
            {
                "Builder" => 200,
                "Swordsman" => 201,
                "Archer" => 202,
                "Miner" => 203,
                "Scout" => 206,
                "Litharch" => 207,
                "Berserker" or "Feraldis_Berserker" => 210,
                "Crystalling" => 320,
                "Veilstinger" => 321,
                "Godsplinter" => 322,
                _ => 201
            };
        }

        /// <summary>
        /// Default unit creation for unknown types.
        /// Falls back to Swordsman stats.
        /// </summary>
        private static Entity CreateDefault(EntityManager em, string unitId, float3 position, Faction faction)
        {
            UnityEngine.Debug.LogWarning($"[UnitFactory] Unknown unit type '{unitId}', creating default melee unit");
            return Swordsman.Create(em, position, faction);
        }

        private static Entity CreateDefault(EntityCommandBuffer ecb, string unitId, float3 position, Faction faction)
        {
            UnityEngine.Debug.LogWarning($"[UnitFactory] Unknown unit type '{unitId}', creating default melee unit");
            return Swordsman.Create(ecb, position, faction);
        }
    }
}