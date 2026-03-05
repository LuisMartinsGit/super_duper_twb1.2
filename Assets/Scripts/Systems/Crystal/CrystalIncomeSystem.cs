// File: Assets/Scripts/Systems/Crystal/CrystalIncomeSystem.cs
// Crystal income system: generates Crystal resources based on cursed ground coverage.
// Income is credited to Faction.White's resource bank every second.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Crystal
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystalIncomeSystem : ISystem
    {
        private float _timer;
        private const float TickInterval = 1.0f;
        private const float IncomePerAreaUnit = 0.1f;
        private const float TileRadius = 2.0f; // matches CrystalSpreadSystem

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalMainNodeTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            var em = state.EntityManager;

            // Count total cursed ground tiles
            int totalTiles = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CursedGroundTag>>())
            {
                totalTiles++;
            }

            if (totalTiles == 0) return;

            // Calculate total cursed area
            float tileArea = math.PI * TileRadius * TileRadius;
            float totalArea = totalTiles * tileArea;

            // Income: 0.1 crystal per area unit per second
            int income = (int)math.ceil(totalArea * IncomePerAreaUnit);

            if (income <= 0) return;

            // Credit to Faction.White bank
            FactionEconomy.Add(em, Faction.White, new Cost { Crystal = income });
        }
    }
}
