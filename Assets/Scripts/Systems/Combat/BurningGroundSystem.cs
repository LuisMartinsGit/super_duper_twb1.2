// BurningGroundSystem.cs
// Applies damage-over-time from BurningGround entities to nearby non-friendly units
// Location: Assets/Scripts/Systems/Combat/BurningGroundSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;

/// <summary>
/// Queries all BurningGround entities. Every 1 second (throttled):
/// - Finds all non-friendly units within BurningGround.Radius of each tile
/// - Deals BurningGround.DPS damage
/// - Decrements TimeRemaining. When &lt;= 0, destroys tile via ECB.
///
/// Follows the pattern of CursedGroundDamageSystem for the damage ticking approach.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct BurningGroundSystem : ISystem
{
    /// <summary>Interval between damage ticks in seconds.</summary>
    private const float DamageTickInterval = 1f;

    private float _tickTimer;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BurningGround>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _tickTimer = 0f;
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        _tickTimer += dt;

        // Fix #225: use the frame-scoped Singleton ECB so structural changes
        // (DestroyEntity below) play back at EndSimulation in a predictable
        // order, instead of immediately via a local ECB mid-frame.
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Always tick down BurningGround timers
        foreach (var (burning, entity) in SystemAPI
            .Query<RefRW<BurningGround>>()
            .WithEntityAccess())
        {
            burning.ValueRW.TimeRemaining -= dt;

            if (burning.ValueRO.TimeRemaining <= 0f)
            {
                ecb.DestroyEntity(entity);
            }
        }

        // Only apply damage on tick intervals
        if (_tickTimer >= DamageTickInterval)
        {
            _tickTimer -= DamageTickInterval;

            // Collect all active burning ground positions and data
            var groundPositions = new NativeList<float3>(Allocator.Temp);
            var groundDPS = new NativeList<float>(Allocator.Temp);
            var groundRadii = new NativeList<float>(Allocator.Temp);
            var groundFactions = new NativeList<Faction>(Allocator.Temp);

            foreach (var (burning, transform, factionTag) in SystemAPI
                .Query<RefRO<BurningGround>, RefRO<LocalTransform>, RefRO<FactionTag>>())
            {
                if (burning.ValueRO.TimeRemaining <= 0f) continue; // Skip expired tiles

                groundPositions.Add(transform.ValueRO.Position);
                groundDPS.Add(burning.ValueRO.DPS);
                groundRadii.Add(burning.ValueRO.Radius);
                groundFactions.Add(factionTag.ValueRO.Value);
            }

            if (groundPositions.Length > 0)
            {
                // Apply damage to non-friendly units standing on burning ground
                foreach (var (health, unitTransform, unitFaction, entity) in SystemAPI
                    .Query<RefRW<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                    .WithAll<UnitTag>()
                    .WithNone<Invulnerable>() // (task-062 C-4) — DoT honors LockdownVault
                    .WithEntityAccess())
                {
                    float3 unitPos = unitTransform.ValueRO.Position;

                    for (int i = 0; i < groundPositions.Length; i++)
                    {
                        // Don't damage friendly units
                        if (unitFaction.ValueRO.Value == groundFactions[i]) continue;

                        float dist = math.distance(
                            new float2(unitPos.x, unitPos.z),
                            new float2(groundPositions[i].x, groundPositions[i].z));

                        if (dist <= groundRadii[i])
                        {
                            int damage = math.max(1, (int)(groundDPS[i] * DamageTickInterval));
                            ref var hp = ref health.ValueRW;
                            hp.Value = math.max(0, hp.Value - damage);
                            break; // Only take damage from one tile per tick
                        }
                    }
                }
            }

            groundPositions.Dispose();
            groundDPS.Dispose();
            groundRadii.Dispose();
            groundFactions.Dispose();
        }
        // ECB is played back automatically by EndSimulationEntityCommandBufferSystem.
    }
}
