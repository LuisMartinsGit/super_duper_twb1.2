// File: Assets/Scripts/Systems/Work/SelfDestructSystem.cs
// Countdown timer for building self-destruction with resource refund.
// Used when Alanthor is chosen: GathererHuts get a 2-minute self-destruct.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Data;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Ticks down SelfDestructTimer on buildings.
    /// When timer expires, refunds 80% of original build cost and destroys the entity.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SelfDestructSystem : ISystem
    {
        private const float RefundMultiplier = 0.80f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SelfDestructTimer>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot entities with timers (can't destroy during iteration).
            // Fix #237: the old code also allocated a NativeList<float> named
            // 'timers' that was never written to or read. Removed.
            var toProcess = new NativeList<Entity>(Allocator.Temp);

            foreach (var (timer, entity) in SystemAPI
                .Query<RefRW<SelfDestructTimer>>()
                .WithEntityAccess())
            {
                timer.ValueRW.TimeRemaining -= dt;

                if (timer.ValueRO.TimeRemaining <= 0f)
                {
                    toProcess.Add(entity);
                }
            }

            // Process expired timers
            for (int i = 0; i < toProcess.Length; i++)
            {
                Entity entity = toProcess[i];
                if (!em.Exists(entity)) continue;

                // Get faction for refund
                if (em.HasComponent<FactionTag>(entity))
                {
                    var faction = em.GetComponentData<FactionTag>(entity).Value;

                    // Determine building type and look up cost
                    string buildingId = GetBuildingId(em, entity);
                    if (buildingId != null && BuildCosts.TryGet(buildingId, out var cost))
                    {
                        // Refund 80% of construction cost
                        var refund = Cost.Of(
                            supplies: (int)(cost.Supplies * RefundMultiplier),
                            iron: (int)(cost.Iron * RefundMultiplier),
                            crystal: (int)(cost.Crystal * RefundMultiplier),
                            veilsteel: (int)(cost.Veilsteel * RefundMultiplier),
                            glow: (int)(cost.Glow * RefundMultiplier)
                        );

                        FactionEconomy.Add(em, faction, refund);
                    }
                }

                // Destroy the entity
                em.DestroyEntity(entity);
            }

            toProcess.Dispose();
        }

        /// <summary>
        /// Map entity to its building cost ID using tag components.
        /// </summary>
        private static string GetBuildingId(EntityManager em, Entity entity)
        {
            if (em.HasComponent<GathererHutTag>(entity)) return "GatherersHut";
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<TempleTag>(entity)) return "TempleOfRidan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";
            if (em.HasComponent<SmelterTag>(entity)) return "Alanthor_Smelter";
            return null;
        }
    }
}
