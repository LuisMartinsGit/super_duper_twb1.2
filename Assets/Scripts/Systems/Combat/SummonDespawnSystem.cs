// SummonDespawnSystem.cs
// Ticks down SummonedUnit.DespawnTimer and expires summoned units
// Location: Assets/Scripts/Systems/Combat/SummonDespawnSystem.cs

using Unity.Entities;
using Unity.Burst;

/// <summary>
/// Ticks SummonedUnit.DespawnTimer each frame. When the timer reaches 0 the
/// summon's Health is set to 0 and DeathSystem destroys it (unit-death
/// contract). Recording our own DestroyEntity here double-destroyed summons
/// that died in combat during their last seconds — two EndSimulation buffers
/// each held a DestroyEntity for the same entity and the second threw
/// "entity does not exist" at playback. Corpses (DeathAnimationState) are
/// excluded so an already-dying summon is left to DeathSystem entirely.
/// Runs in SimulationSystemGroup.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SummonDespawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SummonedUnit>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (summon, health) in SystemAPI
            .Query<RefRW<SummonedUnit>, RefRW<Health>>()
            .WithNone<DeathAnimationState>())
        {
            summon.ValueRW.DespawnTimer -= dt;

            if (summon.ValueRO.DespawnTimer <= 0f)
                health.ValueRW.Value = 0;
        }
    }
}
