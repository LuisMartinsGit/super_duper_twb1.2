// MindControlSystem.cs
// Manages mind-controlled units: ticks duration and reverts faction on expiry
// Location: Assets/Scripts/Systems/Combat/MindControlSystem.cs

using Unity.Entities;
using Unity.Burst;

/// <summary>
/// Queries all MindControlled entities:
/// - Ticks TimeRemaining each frame
/// - When TimeRemaining &lt;= 0: reverts FactionTag.Value to OriginalFaction, removes MindControlled via ECB
/// - Also checks if unit's Health &lt;= 0: if dead, removes MindControlled (DeathSystem handles destruction)
///
/// Runs in SimulationSystemGroup.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct MindControlSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<MindControlled>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        // Fix #225: Singleton ECB instead of local Temp allocator.
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (mc, factionTag, health, entity) in SystemAPI
            .Query<RefRW<MindControlled>, RefRW<FactionTag>, RefRO<Health>>()
            .WithEntityAccess())
        {
            // If unit is dead, just remove the mind control component
            if (health.ValueRO.Value <= 0)
            {
                ecb.RemoveComponent<MindControlled>(entity);
                continue;
            }

            // Tick timer
            mc.ValueRW.TimeRemaining -= dt;

            if (mc.ValueRO.TimeRemaining <= 0f)
            {
                // Revert faction to original
                factionTag.ValueRW.Value = mc.ValueRO.OriginalFaction;

                // Remove MindControlled component
                ecb.RemoveComponent<MindControlled>(entity);
            }
        }

        // ECB plays back automatically at EndSimulation.
    }
}
