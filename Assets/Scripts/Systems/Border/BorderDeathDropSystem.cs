// File: Assets/Scripts/Systems/Border/BorderDeathDropSystem.cs
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Intercepts border entity deaths (units and buildings with VeilstoneWorth)
    /// before DeathSystem destroys them.
    ///
    /// Secondary border-location nodes (<see cref="SecondaryBorderLocationTag"/>)
    /// grant +1 Religion Point to the killer's faction.
    ///
    /// Other border deaths yield nothing here — veilstone is a fixed map
    /// resource (seeded at map start, mined until gone, exactly like iron);
    /// the old potential-veilstone pool / patch-regrowth economy was removed.
    /// Glow drops are handled by their own systems.
    ///
    /// Fires once per death: WithNone&lt;DeathAnimationState, BuildingCollapseState,
    /// NodeDormant&gt; excludes entities whose death has already been registered or
    /// which have been frozen by NodeStateDeathInterceptSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct BorderDeathDropSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VeilstoneWorth>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            var secondaryKillers = new NativeList<Faction>(Allocator.Temp);

            foreach (var (health, entity) in SystemAPI
                .Query<RefRO<Health>>()
                .WithAll<VeilstoneWorth, SecondaryBorderLocationTag>()
                .WithNone<DeathAnimationState, BuildingCollapseState, NodeDormant>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                // Secondary border node — 1 RP to the killer, no veilstone yield.
                Faction killer = em.HasComponent<LastDamagedByFaction>(entity)
                    ? em.GetComponentData<LastDamagedByFaction>(entity).Value
                    : Faction.Border;
                secondaryKillers.Add(killer);
            }

            for (int i = 0; i < secondaryKillers.Length; i++)
            {
                var killer = secondaryKillers[i];
                if (killer == Faction.Border) continue;  // unattributed kills don't reward RP
                FactionReligionPointsHelper.Refund(em, killer, 1);
                TWBLog.Log($"[BorderDeathDropSystem] secondary border node destroyed by {killer} — +1 RP");
            }

            secondaryKillers.Dispose();
        }
    }
}
