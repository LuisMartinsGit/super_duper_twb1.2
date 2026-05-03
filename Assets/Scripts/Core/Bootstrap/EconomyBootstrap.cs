// EconomyBootstrap.cs
// Initializes faction economy entities (resource banks, population tracking)
// Location: Assets/Scripts/Core/Bootstrap/EconomyBootstrap.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Creates and initializes faction economy entities.
    /// Each faction gets a resource bank entity with:
    /// - FactionResources (Supplies, Iron, Crystal, Veilsteel, Glow)
    /// - FactionPopulation (Current, Max population)
    /// - ResourceTickState (for passive income calculations)
    /// </summary>
    public static class EconomyBootstrap
    {
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Starting supplies for each faction</summary>
        public const int StartingSupplies = 400;

        /// <summary>Starting iron for each faction</summary>
        public const int StartingIron = 150;

        /// <summary>Starting crystal (advanced resource)</summary>
        public const int StartingCrystal = 0;

        /// <summary>Starting veilsteel (advanced resource)</summary>
        public const int StartingVeilsteel = 0;

        /// <summary>Starting glow (advanced resource)</summary>
        public const int StartingGlow = 0;

        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Ensure one resource bank entity per participating faction.
        /// Safe to call multiple times - will skip existing banks.
        /// </summary>
        /// <param name="totalPlayers">Number of factions to initialize</param>
        public static void EnsureFactionBanks(int totalPlayers)
        {
            Unity.Entities.World world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            var em = world.EntityManager;

            for (int i = 0; i < totalPlayers; i++)
            {
                var faction = (Faction)i;

                if (FactionBankExists(em, faction))
                {
                    continue;
                }

                CreateFactionBank(em, faction, world);
            }

        }

        /// <summary>
        /// Create a faction bank with custom starting resources.
        /// Use for special game modes or testing.
        /// </summary>
        public static Entity CreateFactionBank(EntityManager em, Faction faction,
            int supplies, int iron, int crystal = 0, int veilsteel = 0, int glow = 0)
        {
            // In ResetAllFactionBanks and the public CreateFactionBank overload:
            var world = EntityWorld.DefaultGameObjectInjectionWorld;  // Not World.DefaultGameObjectInjectionWorld

            var bank = em.CreateEntity(
                typeof(FactionTag),
                typeof(FactionResources),
                typeof(ResourceTickState),
                typeof(FactionPopulation),
                typeof(FactionEra),
                typeof(ReligionPoints)
            );

            em.SetComponentData(bank, new FactionTag { Value = faction });

            em.SetComponentData(bank, new FactionResources
            {
                Supplies = supplies,
                Iron = iron,
                Crystal = crystal,
                Veilsteel = veilsteel,
                Glow = glow
            });

            em.SetComponentData(bank, new FactionPopulation
            {
                Current = 0,
                Max = 0
            });

            em.SetComponentData(bank, new ResourceTickState
            {
                LastWholeSecond = world != null
                    ? (int)math.floor(world.Time.ElapsedTime)
                    : 0
            });

            em.SetComponentData(bank, new FactionEra { Value = 1 });
            em.SetComponentData(bank, new ReligionPoints { Value = 0 });

            return bank;
        }

        /// <summary>
        /// Get the resource bank entity for a specific faction.
        /// </summary>
        public static bool TryGetFactionBank(EntityManager em, Faction faction, out Entity bank)
        {
            bank = Entity.Null;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionResources>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var tags = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (tags[i].Value == faction)
                {
                    bank = entities[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reset all faction banks to starting resources.
        /// Useful for game restart without reloading scene.
        /// </summary>
        public static void ResetAllFactionBanks(int totalPlayers)
        {
            // In ResetAllFactionBanks and the public CreateFactionBank overload:
            var world = EntityWorld.DefaultGameObjectInjectionWorld;  // Not World.DefaultGameObjectInjectionWorld
            if (world == null) return;

            var em = world.EntityManager;

            for (int i = 0; i < totalPlayers; i++)
            {
                var faction = (Faction)i;

                if (TryGetFactionBank(em, faction, out var bank))
                {
                    bool max = GameSettings.MaxStartingResources;
                    int cap = FactionResources.ResourceCap;

                    em.SetComponentData(bank, new FactionResources
                    {
                        Supplies  = max ? cap : StartingSupplies,
                        Iron      = max ? cap : StartingIron,
                        Crystal   = max ? cap : StartingCrystal,
                        Veilsteel = max ? cap : StartingVeilsteel,
                        Glow      = max ? cap : StartingGlow
                    });

                    em.SetComponentData(bank, new FactionPopulation
                    {
                        Current = 0,
                        Max = 0
                    });

                    // Reset era and religion points
                    if (em.HasComponent<FactionEra>(bank))
                        em.SetComponentData(bank, new FactionEra { Value = 1 });
                        else
                            em.AddComponentData(bank, new FactionEra { Value = 1 });

                    if (em.HasComponent<ReligionPoints>(bank))
                        em.SetComponentData(bank, new ReligionPoints { Value = 0 });
                        else
                            em.AddComponentData(bank, new ReligionPoints { Value = 0 });
                }
            }

        }

        // ═══════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static bool FactionBankExists(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionResources>()
            );

            using var banks = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < banks.Length; i++)
            {
                var tag = em.GetComponentData<FactionTag>(banks[i]);
                if (tag.Value == faction) return true;
            }

            return false;
        }

        private static Entity CreateFactionBank(EntityManager em, Faction faction, EntityWorld world)
        {
            var bank = em.CreateEntity(
                typeof(FactionTag),
                typeof(FactionResources),
                typeof(ResourceTickState),
                typeof(FactionPopulation),
                typeof(FactionEra),
                typeof(ReligionPoints)
            );

            em.SetComponentData(bank, new FactionTag { Value = faction });

            bool max = GameSettings.MaxStartingResources;
            int cap = FactionResources.ResourceCap;

            em.SetComponentData(bank, new FactionResources
            {
                Supplies  = max ? cap : StartingSupplies,
                Iron      = max ? cap : StartingIron,
                Crystal   = max ? cap : StartingCrystal,
                Veilsteel = max ? cap : StartingVeilsteel,
                Glow      = max ? cap : StartingGlow
            });

            em.SetComponentData(bank, new FactionPopulation
            {
                Current = 0,
                Max = 0
            });

            em.SetComponentData(bank, new ResourceTickState
            {
                LastWholeSecond = (int)math.floor(world.Time.ElapsedTime)
            });

            em.SetComponentData(bank, new FactionEra { Value = 1 });
            em.SetComponentData(bank, new ReligionPoints { Value = 0 });

            return bank;
        }

    }
}