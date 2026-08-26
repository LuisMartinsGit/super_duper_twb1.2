// DeathSystem.cs
// Destroys entities that have reached 0 HP and cleans up references

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
        /// <summary>Building collapse animation duration in seconds before entity destruction.</summary>
        private const float BuildingCollapseDuration = 2.0f;

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            float dt = SystemAPI.Time.DeltaTime;

            // Phase 0a: Tick death animation timers and collect expired entities
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

            // Phase 0b: Tick building collapse timers and collect expired buildings
            foreach (var (collapse, entity) in SystemAPI
                         .Query<RefRW<BuildingCollapseState>>()
                         .WithEntityAccess())
            {
                collapse.ValueRW.Timer -= dt;
                if (collapse.ValueRO.Timer <= 0f)
                {
                    expiredEntities.Add(entity);
                }
            }

            for (int i = 0; i < expiredEntities.Length; i++)
                ecb.DestroyEntity(expiredEntities[i]);
            expiredEntities.Dispose();

            // Phase 1: Collect all dead entities (health <= 0, no death/collapse animation yet)
            var deadEntities = new NativeList<Entity>(Allocator.Temp);

            // Ability: Life Cling clamps HP to its floor instead of dying — applied
            // here (source-agnostic, immediately before the death check) so it works
            // regardless of where the lethal damage came from.
            var lifeClinged = new NativeList<Entity>(Allocator.Temp);
            // Feraldis moment-of-death rules (Berserker last stand, Suicidal
            // detonation). Same reasoning as LifeCling: this pass is the only
            // point guaranteed to run after EVERY damage source in the frame.
            // See FeraldisDeathInterceptor for why a separate
            // [UpdateBefore(DeathSystem)] system was not order-safe.
            var feraldisIntercept = new NativeList<Entity>(Allocator.Temp);
            foreach (var (health, entity) in SystemAPI
                         .Query<RefRO<Health>>()
                         .WithNone<DeathAnimationState, BuildingCollapseState>()
                         .WithNone<NodeDormant>()
                         .WithEntityAccess())
            {
                if (health.ValueRO.Value <= 0)
                {
                    if (state.EntityManager.HasComponent<TheWaningBorder.Abilities.LifeCling>(entity))
                        lifeClinged.Add(entity);
                    else if (FeraldisDeathInterceptor.WantsIntercept(state.EntityManager, entity))
                        feraldisIntercept.Add(entity);
                    else
                        deadEntities.Add(entity);
                }
            }
            // Post-loop: Apply makes structural changes, so it must not run
            // inside the iteration above.
            for (int i = 0; i < feraldisIntercept.Length; i++)
            {
                var e = feraldisIntercept[i];
                if (!FeraldisDeathInterceptor.Apply(state.EntityManager, e))
                    deadEntities.Add(e);
            }
            feraldisIntercept.Dispose();
            for (int i = 0; i < lifeClinged.Length; i++)
            {
                var e = lifeClinged[i];
                var h = state.EntityManager.GetComponentData<Health>(e);
                int floor = state.EntityManager.GetComponentData<TheWaningBorder.Abilities.LifeCling>(e).Floor;
                if (h.Value < floor) { h.Value = floor; state.EntityManager.SetComponentData(e, h); }
            }
            lifeClinged.Dispose();

            if (deadEntities.Length > 0)
            {
                // Build O(1) lookup set from dead entities list
                var deadSet = new NativeHashSet<Entity>(deadEntities.Length, Allocator.Temp);
                for (int i = 0; i < deadEntities.Length; i++)
                    deadSet.Add(deadEntities[i]);

                // Phase 2: Clean up Target references pointing to dead entities.
                // Written directly, NOT via ECB: a deferred SetComponent can land
                // on an entity this same buffer destroys earlier in playback (an
                // expiring corpse that still holds a Target) and throws "entity
                // does not exist", which aborts playback and corrupts the world.
                foreach (var target in SystemAPI.Query<RefRW<Target>>())
                {
                    if (deadSet.Contains(target.ValueRO.Value))
                        target.ValueRW.Value = Entity.Null;
                }

                // Phase 3: Clean up AttackCommand references pointing to dead
                // entities. Corpses are excluded — phase 0 may have recorded
                // their DestroyEntity into this same buffer, and a
                // RemoveComponent after that destroy throws at playback.
                foreach (var (attackCmd, entity) in SystemAPI
                             .Query<RefRO<AttackCommand>>()
                             .WithNone<DeathAnimationState, BuildingCollapseState>()
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

                    // King Lexor respawn tax: record his death so the next one trains slower.
                    if (state.EntityManager.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(dead) &&
                        state.EntityManager.GetComponentData<TheWaningBorder.Abilities.UniqueUnitTag>(dead).Kind == TheWaningBorder.Abilities.UniqueUnitKind.KingLexor &&
                        state.EntityManager.HasComponent<FactionTag>(dead))
                        TheWaningBorder.Abilities.HeroTrainLimit.RecordRespawn(
                            state.EntityManager.GetComponentData<FactionTag>(dead).Value);

                    // Units get a death animation delay; buildings are destroyed immediately
                    bool isBuilding = state.EntityManager.HasComponent<BuildingTag>(dead);
                    if (!isBuilding)
                    {
                        ecb.AddComponent(dead, new DeathAnimationState { Timer = DeathAnimationDuration });

                        // Cancel movement immediately on death so the corpse
                        // doesn't slide while the death animation plays. The
                        // integrator already excludes DeathAnimationState, but
                        // clearing the destination + smoothed direction here
                        // makes the stop instant and leaves no residual intent.
                        if (state.EntityManager.HasComponent<DesiredDestination>(dead))
                        {
                            var dd = state.EntityManager.GetComponentData<DesiredDestination>(dead);
                            dd.Has = 0;
                            ecb.SetComponent(dead, dd);
                        }
                        if (state.EntityManager.HasComponent<SmoothedDirection>(dead))
                            ecb.SetComponent(dead, new SmoothedDirection { Value = float3.zero });

                        // --- Terrain Influence: Feraldis blood accumulation ---
                        // Veilstone units (BorderTag + BorderUnitTag) do not bleed — they
                        // shatter. Only organic non-veilstone units spill blood
                        // onto the blood map (TheWaningBorder.Influence.BloodMap).
                        bool isBorderUnit = state.EntityManager.HasComponent<BorderTag>(dead);
                        // Exposure kills shed no blood (§2.5b loop damping):
                        // the curse must never farm its own blood-spawner —
                        // only real combat deaths feed blood-curse births.
                        bool curseKilled = state.EntityManager.HasComponent<CurseKilledTag>(dead);
                        if (!isBorderUnit && !curseKilled
                            && state.EntityManager.HasComponent<LocalTransform>(dead))
                        {
                            float3 pos = state.EntityManager.GetComponentData<LocalTransform>(dead).Position;

                            // Derive splat "amount" from the unit's max HP.
                            // A weak unit (~50 HP) → ~0.25 (small splat).
                            // A strong unit (200+ HP) → 1.0 (large, irregular splat).
                            int maxHp = state.EntityManager.HasComponent<Health>(dead)
                                ? state.EntityManager.GetComponentData<Health>(dead).Max
                                : 50;
                            float amount = math.saturate(maxHp / 200f);

                            TheWaningBorder.Influence.BloodMap.AddBlood(
                                new UnityEngine.Vector3(pos.x, pos.y, pos.z),
                                amount);
                        }
                    }
                    else
                    {
                        // Buildings get a collapse animation delay (visual handled by BuildingEffectSystem)
                        ecb.AddComponent(dead, new BuildingCollapseState { Timer = BuildingCollapseDuration });
                    }
                }
            }

            deadEntities.Dispose();
        }
    }
}
