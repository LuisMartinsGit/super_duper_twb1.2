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
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

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

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
