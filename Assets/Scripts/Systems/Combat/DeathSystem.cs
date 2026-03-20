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
                         .WithNone<BattalionLeader>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.Value <= 0)
                {
                    deadEntities.Add(entity);
                }
            }

            // Phase 1.5: Remove dead members from battalion buffers
            int originalDeadCount = deadEntities.Length;
            for (int i = 0; i < originalDeadCount; i++)
            {
                var dead = deadEntities[i];
                if (!state.EntityManager.HasComponent<BattalionMemberData>(dead)) continue;

                var memberData = state.EntityManager.GetComponentData<BattalionMemberData>(dead);
                var leader = memberData.Leader;
                if (leader == Entity.Null || !state.EntityManager.Exists(leader)) continue;
                if (!state.EntityManager.HasBuffer<BattalionMember>(leader)) continue;

                var buffer = state.EntityManager.GetBuffer<BattalionMember>(leader);
                for (int j = buffer.Length - 1; j >= 0; j--)
                {
                    if (buffer[j].Value == dead) { buffer.RemoveAt(j); break; }
                }

                // If battalion is now empty, kill the leader too
                if (buffer.Length == 0)
                {
                    deadEntities.Add(leader);
                }
            }

            if (deadEntities.Length > 0)
            {
                // Build O(1) lookup set from dead entities list
                var deadSet = new NativeHashSet<Entity>(deadEntities.Length, Allocator.Temp);
                for (int i = 0; i < deadEntities.Length; i++)
                    deadSet.Add(deadEntities[i]);

                // Phase 2: Clean up Target references pointing to dead entities
                foreach (var (target, entity) in SystemAPI
                             .Query<RefRW<Target>>()
                             .WithEntityAccess())
                {
                    if (deadSet.Contains(target.ValueRO.Value))
                    {
                        ecb.SetComponent(entity, new Target { Value = Entity.Null });
                    }
                }

                // Phase 3: Clean up AttackCommand references pointing to dead entities
                foreach (var (attackCmd, entity) in SystemAPI
                             .Query<RefRO<AttackCommand>>()
                             .WithEntityAccess())
                {
                    if (deadSet.Contains(attackCmd.ValueRO.Target))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                }

                deadSet.Dispose();

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
