// WellHoldIncomeSystem.cs
// Ongoing veilstone income from HELD wells (Curse & Shardroot canon §2.2):
//   * Converted (Pacified, Runai verb) — the tethered well trickles
//     veilstone to its owner while it lives. Preserve-and-profit.
//   * Cleansed (Purified, Alanthor verb) — the Sanctified Font generates
//     veilstone for its owner. (Influence/build-space projection is a
//     follow-up — noted in canon §9.)
// Destroyed wells yield nothing here — Feraldis' income is the one-time
// shard field left by the shatter (NodeStateDeathInterceptSystem).

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WellHoldIncomeSystem : SystemBase
    {
        private const float TickInterval = 10f;
        private const int PacifiedVeilstonePerTick = 8;   // Runai: best sustained rate
        private const int PurifiedVeilstonePerTick = 6;   // Alanthor: income + (future) influence

        private float _acc;

        protected override void OnCreate()
        {
            RequireForUpdate<BorderNodeState>();
        }

        protected override void OnUpdate()
        {
            _acc += (float)SystemAPI.Time.DeltaTime;
            if (_acc < TickInterval) return;
            _acc -= TickInterval;

            var em = EntityManager;

            foreach (var state in SystemAPI
                .Query<RefRO<BorderNodeState>>()
                .WithAll<BorderMainNodeTag>())
            {
                var s = state.ValueRO;
                if (s.OwnerFaction == Faction.Border) continue;

                int amount = s.State switch
                {
                    NodeState.Converted => PacifiedVeilstonePerTick,
                    NodeState.Cleansed => PurifiedVeilstonePerTick,
                    _ => 0,
                };
                if (amount <= 0) continue;

                FactionEconomy.Add(em, s.OwnerFaction,
                    new Cost { Veilstone = amount });
            }
        }
    }
}
