// DeathSystem.cs
// Destroys entities that have reached 0 HP and cleans up references
// Location: Assets/Scripts/Systems/Combat/DeathSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Destroys entities whose Health has reached 0 or below.
    /// Also cleans up Target and AttackCommand references from other entities
    /// so they don't hold stale references to dead entities.
    ///
    /// Runs after all combat systems so damage is fully resolved first.
    /// Uses EndSimulationEntityCommandBufferSystem so entity destruction is
    /// deferred until after all other systems' ECB commands have played back.
    /// This prevents "entity does not exist" errors from TargetingSystem and
    /// combat systems whose deferred commands reference entities destroyed here.
    ///
    /// Visual cleanup is handled by PresentationSpawnSystem which detects
    /// destroyed entities and removes their GameObjects.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    public partial struct DeathSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Phase 1: Collect all dead entities
            var deadEntities = new NativeList<Entity>(Allocator.Temp);

            foreach (var (health, entity) in SystemAPI
                         .Query<RefRO<Health>>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.Value <= 0)
                {
                    deadEntities.Add(entity);
                }
            }

            if (deadEntities.Length > 0)
            {
                // Phase 2: Clean up Target references pointing to dead entities
                foreach (var (target, entity) in SystemAPI
                             .Query<RefRW<Target>>()
                             .WithEntityAccess())
                {
                    for (int i = 0; i < deadEntities.Length; i++)
                    {
                        if (target.ValueRO.Value == deadEntities[i])
                        {
                            ecb.SetComponent(entity, new Target { Value = Entity.Null });
                            break;
                        }
                    }
                }

                // Phase 3: Clean up AttackCommand references pointing to dead entities
                foreach (var (attackCmd, entity) in SystemAPI
                             .Query<RefRO<AttackCommand>>()
                             .WithEntityAccess())
                {
                    for (int i = 0; i < deadEntities.Length; i++)
                    {
                        if (attackCmd.ValueRO.Target == deadEntities[i])
                        {
                            ecb.RemoveComponent<AttackCommand>(entity);
                            break;
                        }
                    }
                }

                // Phase 4: Destroy dead entities (deferred — plays back last since
                // DeathSystem updates after all combat systems)
                for (int i = 0; i < deadEntities.Length; i++)
                {
                    ecb.DestroyEntity(deadEntities[i]);
                }
            }

            deadEntities.Dispose();
        }
    }
}
