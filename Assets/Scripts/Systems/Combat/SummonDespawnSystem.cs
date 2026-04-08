// SummonDespawnSystem.cs
// Ticks down SummonedUnit.DespawnTimer and destroys expired summoned units
// Location: Assets/Scripts/Systems/Combat/SummonDespawnSystem.cs

using Unity.Entities;
using Unity.Burst;

/// <summary>
/// Ticks SummonedUnit.DespawnTimer each frame. When timer reaches 0,
/// the summoned entity is destroyed via EntityCommandBuffer.
/// Runs in SimulationSystemGroup.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SummonDespawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SummonedUnit>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        // Fix #225: Singleton ECB.
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (summon, entity) in SystemAPI
            .Query<RefRW<SummonedUnit>>()
            .WithEntityAccess())
        {
            summon.ValueRW.DespawnTimer -= dt;

            if (summon.ValueRO.DespawnTimer <= 0f)
            {
                ecb.DestroyEntity(entity);
            }
        }

        // ECB plays back automatically at EndSimulation.
    }
}
