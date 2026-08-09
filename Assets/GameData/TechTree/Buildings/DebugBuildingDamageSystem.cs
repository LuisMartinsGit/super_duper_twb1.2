// DebugBuildingDamageSystem.cs
// Test-only: drains HP from buildings tagged for the BuildingDamageTest scenario
// so the progressive BuildingDamage shader can be reviewed live.
// Location: Assets/GameData/TechTree/Buildings/DebugBuildingDamageSystem.cs

using Unity.Entities;

/// <summary>
/// Marks a building to be steadily damaged by <c>DebugBuildingDamageSystem</c>.
/// Only the BuildingDamageTest scenario stamps this, so the system is inert in
/// normal play. <see cref="Accumulator"/> carries the fractional HP between
/// frames (Health is integer).
/// </summary>
public struct DebugBuildingDamageTarget : IComponentData
{
    public float Accumulator;
}

namespace TheWaningBorder.Systems.Buildings
{
    /// <summary>
    /// Drains 5% of each tagged building's <b>max</b> HP per second (so every
    /// building reaches 0 in ~20 s regardless of its HP pool). When HP hits 0 the
    /// normal DeathSystem → BuildingEffectSystem collapse takes over. Gated by
    /// <c>RequireForUpdate&lt;DebugBuildingDamageTarget&gt;</c>, so it does nothing
    /// unless the test scenario has stamped that tag.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct DebugBuildingDamageSystem : ISystem
    {
        private const float DrainFractionPerSecond = 0.05f; // 5% of max HP / second

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DebugBuildingDamageTarget>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (health, target) in
                     SystemAPI.Query<RefRW<Health>, RefRW<DebugBuildingDamageTarget>>())
            {
                if (health.ValueRO.Value <= 0) continue; // already dead — let DeathSystem handle it

                float acc = target.ValueRO.Accumulator
                          + health.ValueRO.Max * DrainFractionPerSecond * dt;

                int whole = (int)acc;
                if (whole > 0)
                {
                    acc -= whole;
                    int v = health.ValueRO.Value - whole;
                    health.ValueRW.Value = v < 0 ? 0 : v;
                }
                target.ValueRW.Accumulator = acc;
            }
        }
    }
}
