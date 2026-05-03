// PillageSystem.cs
// Grants resources to Feraldis factions when they kill enemy units or destroy buildings
// Location: Assets/Scripts/Systems/Economy/PillageSystem.cs

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Feraldis battle economy: pillage rewards on kills.
    ///
    /// Runs BEFORE DeathSystem so it can inspect dead entities before destruction.
    /// When an entity dies (Health &lt;= 0) and the killer faction has Feraldis culture:
    ///   - Enemy non-military unit killed (Damage &lt;= 5): +15 Supplies, +1 Iron
    ///   - Enemy military unit killed (Damage &gt; 5): +5 Supplies
    ///   - Enemy building destroyed: +50 Supplies, +5 Iron
    ///
    /// Uses LastDamagedByFaction to identify the killer faction.
    /// Only triggers if the killer faction has Feraldis culture (via FactionColors).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct PillageSystem : ISystem
    {
        // Pillage rewards
        private const int UnitKillSupplies_NonMilitary = 15;
        private const int UnitKillIron_NonMilitary = 1;
        private const int UnitKillSupplies_Military = 5;
        private const int BuildingKillSupplies = 50;
        private const int BuildingKillIron = 5;

        // Military threshold: units with Damage > this are considered military
        private const int MilitaryDamageThreshold = 5;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Process all dead entities that have kill credit tracking.
            //
            // Earlier this query had no death-marker exclusion, and the body
            // only filtered by `Health.Value <= 0`. DeathSystem keeps dead
            // entities alive for ~2s during the death animation / collapse,
            // so this loop hit the same dead entity for ~120 frames at 60fps
            // and called FactionEconomy.Add on EACH frame — a single worker
            // kill paid ~1800 supplies + 120 iron instead of 15 + 1, a
            // building kill paid 6000 supplies + 600 iron instead of 50 + 5.
            // The `WithNone` excludes dead entities still in the animation
            // pipeline so each kill pays exactly once. (task-056 F-1)
            foreach (var (health, lastDamager, faction, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastDamagedByFaction>, RefRO<FactionTag>>()
                .WithNone<DeathAnimationState, BuildingCollapseState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // Get killer faction
                Faction killerFaction = lastDamager.ValueRO.Value;

                // Only grant pillage if killer is Feraldis culture
                byte killerCulture = FactionColors.GetFactionCulture(killerFaction);
                if (killerCulture != Cultures.Feraldis) continue;

                // Don't grant pillage for killing your own faction's entities
                if (faction.ValueRO.Value == killerFaction) continue;

                // Determine reward based on entity type
                if (em.HasComponent<BuildingTag>(entity))
                {
                    // Building destroyed: +50 Supplies, +5 Iron
                    FactionEconomy.Add(em, killerFaction,
                        Cost.Of(supplies: BuildingKillSupplies, iron: BuildingKillIron));
                }
                else if (em.HasComponent<UnitTag>(entity))
                {
                    // Unit killed: check if military or non-military
                    int damage = 0;
                    if (em.HasComponent<Damage>(entity))
                    {
                        damage = em.GetComponentData<Damage>(entity).Value;
                    }

                    if (damage > MilitaryDamageThreshold)
                    {
                        // Military unit: +5 Supplies
                        FactionEconomy.Add(em, killerFaction,
                            Cost.Of(supplies: UnitKillSupplies_Military));
                    }
                    else
                    {
                        // Non-military unit (worker/miner): +15 Supplies, +1 Iron
                        FactionEconomy.Add(em, killerFaction,
                            Cost.Of(supplies: UnitKillSupplies_NonMilitary,
                                    iron: UnitKillIron_NonMilitary));
                    }
                }
            }
        }
    }
}
