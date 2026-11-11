// FarmSystem.cs
// The Farm's pay (docs/Design/Age_1_Alanthor.md § The Farm, decision 55 (c)):
// every second each finished Farm adds (Workers inside x the Farm SO's
// suppliesPerWorkerPerMinute) sixtieths of a supply to its own carry and
// pays the whole units into its owner's bank as IncomeSource.Farm (its own
// income line; the Vault's full bars raise it like any income). An
// unfinished Farm, or one with nobody inside, pays nothing.
//
// Lockstep: SimCadence clock (epoch-reset, so nothing carries in from the
// menu or a previous match), integer arithmetic, the carry on the Farm
// entity, and Farms paid in entity-index order.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FarmSystem : SystemBase
    {
        /// <summary>The pay clock, seconds (engine-side, as CountingHouseSystem).</summary>
        private const float TickSeconds = 1f;

        private SimCadence.Periodic _tick;
        private readonly List<Entity> _farms = new List<Entity>();

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<FarmTag>()));
        }

        protected override void OnUpdate()
        {
            if (!_tick.Due(SystemAPI.Time.DeltaTime, TickSeconds)) return;
            var em = EntityManager;

            _farms.Clear();
            foreach (var (_, e) in SystemAPI.Query<RefRO<FarmPay>>().WithAll<FarmTag>()
                         .WithNone<UnderConstruction>().WithEntityAccess())
                _farms.Add(e);
            if (_farms.Count == 0) return;
            _farms.Sort((a, b) => a.Index.CompareTo(b.Index));

            for (int i = 0; i < _farms.Count; i++)
            {
                var farm = _farms[i];
                if (!em.Exists(farm) || !em.HasComponent<FactionTag>(farm)) continue;
                if (em.HasComponent<Health>(farm) && em.GetComponentData<Health>(farm).Value <= 0) continue;
                int perMinute = Farm.SuppliesPerMinute(em, farm);
                if (perMinute <= 0) continue;

                var pay = em.GetComponentData<FarmPay>(farm);
                pay.SuppliesSixtieths += perMinute;
                int whole = pay.SuppliesSixtieths / 60;
                pay.SuppliesSixtieths %= 60;
                em.SetComponentData(farm, pay);
                if (whole > 0)
                    FactionEconomy.Add(em, em.GetComponentData<FactionTag>(farm).Value,
                        Cost.Of(supplies: whole), IncomeSource.Farm);
            }
        }
    }
}
