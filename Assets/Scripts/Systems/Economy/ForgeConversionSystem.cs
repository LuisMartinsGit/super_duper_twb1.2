// File: Assets/Scripts/Economy/ForgeConversionSystem.cs
// Converts iron + crystal stored in Smelter into veilsteel for the faction.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Every 5 seconds, if a completed Smelter has >= 5 iron and >= 3 crystal
    /// in its ForgeStorage, converts them into 1 veilsteel added to the faction bank.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ForgeConversionSystem : ISystem
    {
        private const float ConversionInterval = 5.0f;
        private const int IronPerConversion = 5;
        private const int CrystalPerConversion = 3;
        private const int VeilsteelPerConversion = 1;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ForgeStorage>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            foreach (var (storage, faction, entity) in SystemAPI
                .Query<RefRW<ForgeStorage>, RefRO<FactionTag>>()
                .WithAll<SmelterTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var forge = ref storage.ValueRW;

                // Tick conversion timer
                forge.ConversionTimer += dt;

                if (forge.ConversionTimer >= ConversionInterval)
                {
                    forge.ConversionTimer -= ConversionInterval;

                    // Check if enough resources for conversion
                    if (forge.Iron >= IronPerConversion && forge.Crystal >= CrystalPerConversion)
                    {
                        // Deduct from forge storage
                        forge.Iron -= IronPerConversion;
                        forge.Crystal -= CrystalPerConversion;

                        // Add veilsteel to faction bank
                        var fac = faction.ValueRO.Value;
                        FactionEconomy.Add(em, fac, Cost.Of(veilsteel: VeilsteelPerConversion));
                    }
                }
            }
        }
    }
}
