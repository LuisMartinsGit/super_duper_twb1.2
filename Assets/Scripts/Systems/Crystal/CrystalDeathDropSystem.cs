// File: Assets/Scripts/Systems/Crystal/CrystalDeathDropSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Intercepts curse entity deaths (units and buildings with CrystalResourceValue)
    /// before DeathSystem destroys them. Spawns a crystal node (Cadaver) at the
    /// death position worth the entity's BuildCost in gatherable crystal.
    ///
    /// ALL curse entity deaths drop crystal nodes. The only limit is a cap of
    /// 32 crystal nodes alive on the map at once — players can clear them to
    /// allow more to spawn.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct CrystalDeathDropSystem : ISystem
    {
        private const int MaxCrystalNodes = 128;

        /// <summary>Radius for cadavers dropped by main curse nodes (large).</summary>
        private const float MainNodeCadaverRadius = 2.0f;

        /// <summary>Default cadaver radius for regular curse entities.</summary>
        private const float DefaultCadaverRadius = 0.8f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalResourceValue>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var em = state.EntityManager;

            // Count existing crystal nodes (cadavers) on the map
            int activeNodes = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CadaverTag>>())
                activeNodes++;

            foreach (var (health, transform, resourceValue, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<CrystalResourceValue>>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                int lootAmount = resourceValue.ValueRO.BuildCost;
                if (lootAmount <= 0) continue;

                // Respect the cap — skip drop but keep processing deaths
                if (activeNodes >= MaxCrystalNodes) continue;

                var pos = transform.ValueRO.Position;

                // Main curse nodes leave behind larger crystal deposits
                bool isMainNode = em.HasComponent<CrystalMainNodeTag>(entity);
                float radius = isMainNode ? MainNodeCadaverRadius : DefaultCadaverRadius;

                Cadaver.Create(ecb, pos, lootAmount, radius);
                activeNodes++;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
