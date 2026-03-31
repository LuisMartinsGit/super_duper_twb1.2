// DeathSystem.cs
// Destroys entities that have reached 0 HP and cleans up references
// Location: Assets/Scripts/Systems/Combat/DeathSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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

        /// <summary>Death animation duration in seconds before entity destruction.</summary>
        private const float DeathAnimationDuration = 2.0f;

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            float dt = SystemAPI.Time.DeltaTime;

            // Phase 0: Tick death animation timers and collect expired entities
            var expiredEntities = new NativeList<Entity>(Allocator.Temp);
            foreach (var (deathAnim, entity) in SystemAPI
                         .Query<RefRW<DeathAnimationState>>()
                         .WithEntityAccess())
            {
                deathAnim.ValueRW.Timer -= dt;
                if (deathAnim.ValueRO.Timer <= 0f)
                {
                    expiredEntities.Add(entity);
                }
            }
            for (int i = 0; i < expiredEntities.Length; i++)
                ecb.DestroyEntity(expiredEntities[i]);
            expiredEntities.Dispose();

            // Phase 1: Collect all dead entities (health <= 0, no death animation yet)
            var deadEntities = new NativeList<Entity>(Allocator.Temp);

            foreach (var (health, entity) in SystemAPI
                         .Query<RefRO<Health>>()
                         .WithNone<BattalionLeader, DeathAnimationState>()
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

                // Phase 4: Add death animation delay for units, destroy buildings immediately
                for (int i = 0; i < deadEntities.Length; i++)
                {
                    var dead = deadEntities[i];

                    // Units get a death animation delay; buildings are destroyed immediately
                    bool isBuilding = state.EntityManager.HasComponent<BuildingTag>(dead);
                    if (!isBuilding)
                    {
                        ecb.AddComponent(dead, new DeathAnimationState { Timer = DeathAnimationDuration });

                        // --- Terrain Influence: Feraldis blood accumulation ---
                        // Crystal units (CrystalTag + CrystalUnitTag) do not bleed — they
                        // shatter. Only organic non-crystal units spill blood onto BloodMap.
                        bool isCrystalUnit = state.EntityManager.HasComponent<CrystalTag>(dead);
                        if (!isCrystalUnit && state.EntityManager.HasComponent<LocalTransform>(dead))
                        {
                            float3 pos = state.EntityManager.GetComponentData<LocalTransform>(dead).Position;

                            // Derive splat "amount" from the unit's max HP.
                            // A weak unit (~50 HP) → ~0.25 (small splat).
                            // A strong unit (200+ HP) → 1.0 (large, irregular splat).
                            // Adjust the divisor to match your game's HP ranges.
                            int maxHp = state.EntityManager.HasComponent<Health>(dead)
                                ? state.EntityManager.GetComponentData<Health>(dead).Max
                                : 50;
                            float amount = math.saturate(maxHp / 200f);

                            InfluenceBridge.OnUnitDied(
                                new UnityEngine.Vector3(pos.x, pos.y, pos.z),
                                amount);
                        }
                    }
                    else
                    {
                        ecb.DestroyEntity(dead);
                    }
                }
            }

            deadEntities.Dispose();
        }
    }
}
