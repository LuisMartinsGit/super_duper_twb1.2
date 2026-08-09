// File: Assets/Scripts/Systems/Border/BorderIncomeSystem.cs
// Veilstone income system: generates Veilstone resources based on border ground coverage.
// Income is credited to Faction.Border's resource bank every second.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BorderIncomeSystem : ISystem
    {
        private float _timer;
        private const float TickInterval = 1.0f;
        private const float IncomePerAreaUnit = 0.03f;
        private const float TileRadius = 2.0f; // matches BorderSpreadSystem

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BorderMainNodeTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;

            var em = state.EntityManager;

            // Count total border ground tiles
            int totalTiles = 0;
            foreach (var _ in SystemAPI.Query<RefRO<BorderGroundTag>>())
            {
                totalTiles++;
            }

            if (totalTiles == 0) return;

            // Calculate total border area
            float tileArea = math.PI * TileRadius * TileRadius;
            float totalArea = totalTiles * tileArea;

            // Income: 0.1 veilstone per area unit per second
            int income = (int)math.ceil(totalArea * IncomePerAreaUnit);

            if (income <= 0) return;

            // Credit to Faction.Border bank
            FactionEconomy.Add(em, Faction.Border, new Cost { Veilstone = income });
        }
    }
}
