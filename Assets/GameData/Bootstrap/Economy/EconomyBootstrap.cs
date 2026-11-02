// EconomyBootstrap.cs
// Initializes faction economy entities (resource banks, population tracking)

using Unity.Entities;
using TheWaningBorder.Core;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Creates and initializes faction economy entities.
    /// Each faction gets a resource bank entity with:
    /// - FactionResources (Supplies, Iron, Veilstone, Veilsteel)
    /// - FactionPopulation (Current, Max population)
    /// - ResourceTickState (for passive income calculations)
    /// </summary>
    public static class EconomyBootstrap
    {
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        // The starting bank and religion points per lobby level are DATA:
        // EconomyBootstrap.asset (EconomyBootstrapConfig), read through
        // StartFor(). The four Starting* constants that lived here (400 /
        // 150 / 0 / 0, plus a hard-wired 100,000 for MAX) are gone
        // (2026-10-16).

        /// <summary>The starting bank + RP for the lobby's level.</summary>
        public static EconomyBootstrapConfig.StartLevel StartFor()
            => EconomyBootstrapConfig.I.For(GameSettings.StartingResources);

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
            EntityWorld world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            var em = world.EntityManager;

            for (int i = 0; i < totalPlayers; i++)
            {
                var faction = (Faction)i;

                // The bank lookup every economy caller uses (cached, world-
                // aware) — this file used to walk the same query by hand.
                if (FactionEconomy.TryGetBank(em, faction, out _))
                {
                    continue;
                }

                CreateFactionBank(em, faction, world);
            }

        }

        // ═══════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static Entity CreateFactionBank(EntityManager em, Faction faction, EntityWorld world)
        {
            var bank = em.CreateEntity(
                typeof(FactionTag),
                typeof(FactionResources),
                typeof(ResourceTickState),
                typeof(FactionPopulation),
                typeof(FactionEra),
                typeof(FactionReligionPoints),  // task-063
                typeof(SectAdoptionState),      // task-063
                typeof(FactionEquipmentTier),   // spec §4 — faction-wide equipment research
                typeof(GodPowerState)           // spec §6.2 + refinement #6
            );

            em.SetComponentData(bank, new FactionTag { Value = faction });

            var start = StartFor();

            em.SetComponentData(bank, new FactionResources
            {
                Supplies  = start.supplies,
                Iron      = start.iron,
                Veilstone   = start.veilstone,
                Veilsteel = start.veilsteel
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
            em.SetComponentData(bank, new FactionReligionPoints { Balance = start.religionPoints, CurrentAge = 1 });
            em.SetComponentData(bank, default(SectAdoptionState));
            em.SetComponentData(bank, default(FactionEquipmentTier));  // all classes start at Base
            em.SetComponentData(bank, new GodPowerState
            {
                BaseCooldown = TheWaningBorder.Core.Config.BorderConstants.GodPowerBaseCooldown,
                CooldownRemaining = TheWaningBorder.Core.Config.BorderConstants.GodPowerBaseCooldown,
                CastCount = 0,
            });

            return bank;
        }

    }
}
