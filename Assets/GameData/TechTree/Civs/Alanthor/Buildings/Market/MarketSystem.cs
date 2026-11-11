// MarketSystem.cs
// The Market's purchases (docs/Design/Age_1_Alanthor.md § The Market,
// decision 55 (d)). Every second each finished Market adds its iron and
// veilstone rates (a minute) to its carries in sixtieths and buys the whole
// units due: it SPENDS supplies at the Market SO's fixed price
// (SpendCategory.Trade) and credits the resource as IncomeSource.Market (its
// own income line; not raised by the Vault, not scaled by an AI cheat — it
// is a purchase). A resource the bank cannot pay for that second is not
// bought, and its due units are dropped rather than owed. Iron is bought
// before veilstone.
//
// Lockstep: SimCadence clock, integer arithmetic, the carries on the Market
// entity, Markets in entity-index order.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MarketSystem : SystemBase
    {
        /// <summary>The purchase clock, seconds (engine-side).</summary>
        private const float TickSeconds = 1f;

        private SimCadence.Periodic _tick;
        private readonly List<Entity> _markets = new List<Entity>();

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<MarketTag>()));
        }

        protected override void OnUpdate()
        {
            if (!_tick.Due(SystemAPI.Time.DeltaTime, TickSeconds)) return;
            var em = EntityManager;

            _markets.Clear();
            foreach (var (_, e) in SystemAPI.Query<RefRO<MarketRates>>().WithAll<MarketTag>()
                         .WithNone<UnderConstruction>().WithEntityAccess())
                _markets.Add(e);
            if (_markets.Count == 0) return;
            _markets.Sort((a, b) => a.Index.CompareTo(b.Index));

            int perIron = Market.SuppliesPerIron;
            int perVeil = Market.SuppliesPerVeilstone;
            for (int i = 0; i < _markets.Count; i++)
            {
                var m = _markets[i];
                if (!em.Exists(m) || !em.HasComponent<FactionTag>(m)) continue;
                if (em.HasComponent<Health>(m) && em.GetComponentData<Health>(m).Value <= 0) continue;
                var r = em.GetComponentData<MarketRates>(m);
                if (r.IronPerMinute <= 0 && r.VeilstonePerMinute <= 0) continue;
                var faction = em.GetComponentData<FactionTag>(m).Value;

                r.IronSixtieths += r.IronPerMinute;
                int iron = r.IronSixtieths / 60;
                r.IronSixtieths %= 60;

                r.VeilstoneSixtieths += r.VeilstonePerMinute;
                int veil = r.VeilstoneSixtieths / 60;
                r.VeilstoneSixtieths %= 60;

                em.SetComponentData(m, r);

                if (iron > 0) Buy(em, faction, Cost.Of(iron: iron), iron * perIron);
                if (veil > 0) Buy(em, faction, Cost.Of(veilstone: veil), veil * perVeil);
            }
        }

        /// <summary>Pay <paramref name="supplies"/> and take <paramref name="goods"/>,
        /// or neither when the bank cannot pay.</summary>
        private static void Buy(EntityManager em, Faction faction, in Cost goods, int supplies)
        {
            if (supplies > 0 && !FactionEconomy.Spend(em, faction, Cost.Of(supplies: supplies), SpendCategory.Trade))
                return;
            FactionEconomy.Add(em, faction, goods, IncomeSource.Market);
        }
    }
}
