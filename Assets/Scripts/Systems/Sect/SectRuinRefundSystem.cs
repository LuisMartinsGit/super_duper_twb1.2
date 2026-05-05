// SectRuinRefundSystem.cs
// Implements Ruin's Lv I "Profane Hands" passive (the 12%-refund-on-destruction
// half). Mirrors PillageSystem: ticks before DeathSystem, scans entities at
// Health <= 0 that haven't entered the death-animation pipeline yet, and if
// the killer faction has Ruin adopted AND the dead entity is an enemy
// building, credits the killer with 12% of the building's build cost.
//
// The +25% damage-vs-buildings half lives in CombatDamageHelper.ApplyBonusDamageOnHit.
//
// No-stack note: design rule #3 says Renewal/Ruin/Reclamation refunds don't
// stack. Renewal Lv I refunds 12% of cost on UNIT death (deferred to a later
// phase — see SectRenewalAutoRepairSystem.cs); Ruin Lv I refunds 12% on
// BUILDING death. They never compete on the same entity, so no per-event
// guard component is needed at Lv I. If Phase 4 introduces a sect that also
// pays out on building destruction, a RefundConsumed marker should be added
// here BEFORE the second sect is wired up.
//
// task-063 phase 2d.
//
// Location: Assets/Scripts/Systems/Sect/SectRuinRefundSystem.cs

using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Data;
using TheWaningBorder.Systems.Combat;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectRuinRefundSystem : ISystem
    {
        // Per-level refund fraction.
        private static float RefundFractionFor(byte level) => level switch
        {
            2 => 0.18f,
            3 => 0.25f,
            _ => 0.12f,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (health, lastDamager, faction, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LastDamagedByFaction>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<DeathAnimationState, BuildingCollapseState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                Faction killerFaction = lastDamager.ValueRO.Value;
                Faction victimFaction = faction.ValueRO.Value;

                // Skip own-faction kills (denies refund-farming demolitions).
                if (killerFaction == victimFaction) continue;

                byte level = SectQuery.LevelOf(em, killerFaction,
                    SectConfig.Ruin, SectLeverKind.Passive);
                if (level == 0) continue;

                string buildingId = BuildCosts.IdFromEntity(em, entity);
                if (buildingId == null) continue;
                if (!BuildCosts.TryGet(buildingId, out var cost)) continue;

                float frac = RefundFractionFor(level);
                var refund = Cost.Of(
                    supplies:  (int)(cost.Supplies  * frac),
                    iron:      (int)(cost.Iron      * frac),
                    crystal:   (int)(cost.Crystal   * frac),
                    veilsteel: (int)(cost.Veilsteel * frac),
                    glow:      (int)(cost.Glow      * frac)
                );

                FactionEconomy.Add(em, killerFaction, refund);
            }
        }
    }
}
