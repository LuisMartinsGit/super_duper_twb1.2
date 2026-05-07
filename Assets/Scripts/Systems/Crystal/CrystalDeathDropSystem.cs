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
    /// Fires once per death: WithNone&lt;DeathAnimationState, BuildingCollapseState&gt;
    /// excludes entities whose death has already been registered, so the 2-second
    /// death animation no longer spawns a fresh cadaver every frame.
    ///
    /// Drops route through Cadaver.CreateOrMerge — adjacent drops within
    /// Cadaver.MergeRadius coalesce into a single node carrying the summed crystal.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct CrystalDeathDropSystem : ISystem
    {
        private const int MaxCrystalNodes = 128;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalResourceValue>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            var dropPositions = new NativeList<float3>(Allocator.Temp);
            var dropAmounts = new NativeList<int>(Allocator.Temp);

            foreach (var (health, transform, resourceValue, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<CrystalResourceValue>>()
                .WithNone<DeathAnimationState, BuildingCollapseState>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                int lootAmount = resourceValue.ValueRO.BuildCost;
                if (lootAmount <= 0) continue;

                // Crystal units drop only 10% of their build cost — discourages
                // farming the curse. Buildings (no CrystalUnitTag) still drop
                // their full cost so demolishing a node is worth the effort.
                if (em.HasComponent<CrystalUnitTag>(entity))
                {
                    lootAmount = math.max(1, lootAmount / 10);
                }

                dropPositions.Add(transform.ValueRO.Position);
                dropAmounts.Add(lootAmount);
            }

            for (int i = 0; i < dropPositions.Length; i++)
            {
                Cadaver.CreateOrMerge(em, dropPositions[i], dropAmounts[i], MaxCrystalNodes);
            }

            dropPositions.Dispose();
            dropAmounts.Dispose();
        }
    }
}
