// File: Assets/Scripts/Systems/Economy/BazaarPackSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Handles Bazaar pack/unpack commands.
    ///
    /// Pack: Bazaar building → Wagon unit (proportional HP transfer)
    /// Unpack: Wagon unit → Bazaar building (proportional HP transfer, valid position required)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BazaarPackSystem : ISystem
    {
        /// <summary>Bazaar building size for placement validation.</summary>
        private static readonly int2 BazaarSize = new int2(5, 5);

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================
            // PACK: Bazaar → Wagon
            // =============================================================
            foreach (var (health, transform, faction, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BazaarTag, BazaarPackCommand>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                int bazaarHP = health.ValueRO.Value;
                int bazaarMaxHP = health.ValueRO.Max;
                float3 pos = transform.ValueRO.Position;
                Faction fac = faction.ValueRO.Value;

                // Proportional HP: bazaarHP/bazaarMaxHP = wagonHP/wagonMaxHP
                int wagonHP = (int)((bazaarHP / (float)bazaarMaxHP) * BazaarWagon.MaxHP);
                wagonHP = math.max(1, wagonHP);

                // Create wagon unit at building position
                BazaarWagon.Create(em, pos, fac, wagonHP, bazaarMaxHP);

                // Destroy the building
                ecb.DestroyEntity(entity);

            }

            // =============================================================
            // UNPACK: Wagon → Bazaar
            // =============================================================
            foreach (var (wagonState, health, transform, faction, entity) in SystemAPI
                .Query<RefRO<BazaarWagonState>, RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BazaarWagonTag, BazaarUnpackCommand>()
                .WithEntityAccess())
            {
                float3 pos = transform.ValueRO.Position;
                Faction fac = faction.ValueRO.Value;

                // Validate placement
                if (!BuildCommandHelper.IsValidBuildPosition(em, pos, BazaarSize))
                {
                    // Invalid position — remove command, player must move wagon first
                    ecb.RemoveComponent<BazaarUnpackCommand>(entity);
                    continue;
                }

                int wagonHP = health.ValueRO.Value;
                int originalMaxHP = wagonState.ValueRO.OriginalMaxHP;

                // Proportional HP: wagonHP/wagonMaxHP = bazaarHP/bazaarMaxHP
                int bazaarHP = (int)((wagonHP / (float)BazaarWagon.MaxHP) * originalMaxHP);
                bazaarHP = math.max(1, bazaarHP);

                // Create Bazaar building (via factory, then fix HP and remove UnderConstruction)
                Entity bazaar = BuildingFactory.Create(em, "ThessarasBazaar", pos, fac);

                // Remove UnderConstruction if present (instant deploy)
                if (em.HasComponent<UnderConstruction>(bazaar))
                    em.RemoveComponent<UnderConstruction>(bazaar);

                // Set HP to proportional value
                em.SetComponentData(bazaar, new Health { Value = bazaarHP, Max = originalMaxHP });

                // Destroy wagon
                ecb.DestroyEntity(entity);

            }
        }
    }
}
