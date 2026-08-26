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
                        // Refund 80% of construction cost. Salvage bonus: any
                        // Veilsteel consumed yields 10% of that as Glow on top
                        // of the standard refund — the spec's "salvaging" lane
                        // for Glow income. (audit follow-up)
                        int glowSalvage = (int)(cost.Veilsteel * 0.10f);
                        var refund = Cost.Of(
                            supplies: (int)(cost.Supplies * RefundMultiplier),
                            iron: (int)(cost.Iron * RefundMultiplier),
                            veilstone: (int)(cost.Veilstone * RefundMultiplier),
                            veilsteel: (int)(cost.Veilsteel * RefundMultiplier),
                            glow: (int)(cost.Glow * RefundMultiplier) + glowSalvage
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
            // Delegate to the canonical entity->building-id mapping. BuildCosts is the
            // single source of truth and explicitly names this system as a reader, so
            // self-destruct refunds now cover every building type it knows.
            return TheWaningBorder.Data.BuildCosts.IdFromEntity(em, entity);
        }
    }
}
