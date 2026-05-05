// UpgradePilePickupSystem.cs
// Picks up Lv2+ veteran death drops. Each tick:
//   1. Decrement Lifetime; destroy expired piles.
//   2. For each pile, find the nearest UnitTag entity within PickupRadius.
//      The first faction to walk over the pile collects the full drop.
//
// This is the "any player can gather these" half of the unit upgrade
// drop spec. Mirrors the auto-pickup pattern rather than miner-required
// gathering — keeps the UX low-friction for accidental veteran kills.
//
// Audit follow-up.
//
// Location: Assets/Scripts/Systems/Economy/UpgradePilePickupSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct UpgradePilePickupSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UpgradePile>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Snapshot pile entities to avoid mutating the iteration set.
            var pilesToProcess = new NativeList<Entity>(Allocator.Temp);
            foreach (var (pile, entity) in SystemAPI
                .Query<RefRW<UpgradePile>>()
                .WithEntityAccess())
            {
                pile.ValueRW.Lifetime -= dt;
                if (pile.ValueRO.Lifetime <= 0f)
                {
                    em.DestroyEntity(entity);
                    continue;
                }
                pilesToProcess.Add(entity);
            }

            for (int i = 0; i < pilesToProcess.Length; i++)
            {
                var pile = pilesToProcess[i];
                if (!em.Exists(pile)) continue;
                TryPickup(em, pile);
            }

            pilesToProcess.Dispose();
        }

        private static void TryPickup(EntityManager em, Entity pile)
        {
            var pileData = em.GetComponentData<UpgradePile>(pile);
            var pilePos = em.GetComponentData<LocalTransform>(pile).Position;
            float r2 = pileData.PickupRadius * pileData.PickupRadius;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - pilePos.x, dz = p.z - pilePos.z;
                if (dx * dx + dz * dz > r2) continue;

                var faction = em.GetComponentData<FactionTag>(e).Value;
                FactionEconomy.Add(em, faction, pileData.Drop);
                em.DestroyEntity(pile);
                return;
            }
        }
    }
}
