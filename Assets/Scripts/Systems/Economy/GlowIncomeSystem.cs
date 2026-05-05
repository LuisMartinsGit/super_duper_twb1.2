// GlowIncomeSystem.cs
// Credits Glow to the killer faction when an Elite crystal unit dies
// (Veilstinger / Godsplinter). Mirrors PillageSystem's death-event hook
// — runs before DeathSystem with WithNone<DeathAnimationState> so each
// kill pays exactly once.
//
// "Elite" identification: any CrystalUnitTag entity that also carries
// VeilstingerState or GodsplinterState. Cadaver-spawned crystal mobs
// don't count — they have CrystalUnitTag but neither named state.
//
// Salvage + craft Glow paths are out of scope here; this covers the
// kill-reward lane only.
//
// Audit fix #2.
//
// Location: Assets/Scripts/Systems/Economy/GlowIncomeSystem.cs

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct GlowIncomeSystem : ISystem
    {
        // Reward per kill.  Phase-5 polish may differentiate Veilstinger vs
        // Godsplinter, but for now both pay the same modest amount.
        private const int VeilstingerGlow = 2;
        private const int GodsplinterGlow = 5;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (health, lastDamager, faction, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastDamagedByFaction>, RefRO<FactionTag>>()
                .WithAll<CrystalUnitTag>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Faction killer = lastDamager.ValueRO.Value;
                if (faction.ValueRO.Value == killer) continue; // shouldn't happen

                int glow;
                if (em.HasComponent<GodsplinterState>(entity)) glow = GodsplinterGlow;
                else if (em.HasComponent<VeilstingerState>(entity)) glow = VeilstingerGlow;
                else continue; // not Elite

                FactionEconomy.Add(em, killer, Cost.Of(glow: glow));
            }
        }
    }
}
