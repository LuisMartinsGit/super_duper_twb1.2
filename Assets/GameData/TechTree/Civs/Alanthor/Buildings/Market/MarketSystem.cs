// MarketSystem.cs
// The shared Market prices drift home (docs/Design/Age_1_Alanthor.md § The
// Market, decision 67). Trades themselves are instant — the MarketTrade
// lockstep command, executed by Market.Trade. All this system does is, every
// marketDriftSeconds, close marketDriftPermille of the gap between each shared
// price and its starting price on Market.asset (at least one hundredth), so a
// dump or a buying spree marks the prices for minutes, not the whole match.
//
// Lockstep: SimCadence clock, integer arithmetic, one MarketPrices entity.

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MarketSystem : SystemBase
    {
        private SimCadence.Periodic _tick;

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<MarketPrices>()));
        }

        protected override void OnUpdate()
        {
            if (!_tick.Due(SystemAPI.Time.DeltaTime, Market.DriftSeconds)) return;
            Market.Drift(EntityManager);
        }
    }
}
