// File: Assets/Scripts/Systems/Crystal/CrystalDeathDropSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Intercepts curse entity deaths (units and buildings with CrystalResourceValue)
    /// before DeathSystem destroys them. Three paths:
    ///
    /// - Secondary curse-location nodes (<see cref="SecondaryCurseLocationTag"/>):
    ///   grant +1 Religion Point to the killer's faction; no cadaver, no pile.
    ///   These are the nodes spawned by an over-grown resource patch — they don't
    ///   yield crystal, just a single RP per node when destroyed.
    ///
    /// - Curse UNITS (CrystalUnitTag — Crystallings, Veilstingers, Godsplinters):
    ///   their full BuildCost is added to a CursePendingPile at the death position.
    ///   If a pile already exists within Cadaver.MergeRadius, the new value is added
    ///   and the 30 s timer is reset. On expiry, CursePendingPileSystem distributes
    ///   the accumulated value across every crystal patch on the map.
    ///
    /// - Curse BUILDINGS (CrystalResourceValue but no CrystalUnitTag — primary main
    ///   nodes, primary sub-nodes): their full BuildCost still drops immediately as
    ///   a cadaver via Cadaver.CreateOrMerge. Demolishing a primary node should
    ///   remain instantly rewarding.
    ///
    /// Fires once per death: WithNone&lt;DeathAnimationState, BuildingCollapseState,
    /// NodeDormant&gt; excludes entities whose death has already been registered or
    /// which have been frozen by NodeStateDeathInterceptSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct CrystalDeathDropSystem : ISystem
    {
        private const int MaxCrystalNodes = 128;

        /// <summary>Seconds a pending pile waits for additional nearby curse-unit deaths before paying out.</summary>
        public const float PendingPileTimerSeconds = 30f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalResourceValue>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            var unitPilePositions = new NativeList<float3>(Allocator.Temp);
            var unitPileAmounts = new NativeList<int>(Allocator.Temp);
            var buildingDropPositions = new NativeList<float3>(Allocator.Temp);
            var buildingDropAmounts = new NativeList<int>(Allocator.Temp);
            var secondaryKillers = new NativeList<Faction>(Allocator.Temp);

            foreach (var (health, transform, resourceValue, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<CrystalResourceValue>>()
                .WithNone<DeathAnimationState, BuildingCollapseState, NodeDormant>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;

                int lootAmount = resourceValue.ValueRO.BuildCost;
                if (lootAmount <= 0) continue;

                if (em.HasComponent<SecondaryCurseLocationTag>(entity))
                {
                    // Secondary curse node — 1 RP, no crystal yield.
                    Faction killer = em.HasComponent<LastDamagedByFaction>(entity)
                        ? em.GetComponentData<LastDamagedByFaction>(entity).Value
                        : Faction.Curse;
                    secondaryKillers.Add(killer);
                }
                else if (em.HasComponent<CrystalUnitTag>(entity))
                {
                    // Curse unit — full build cost feeds the pending pile.
                    unitPilePositions.Add(transform.ValueRO.Position);
                    unitPileAmounts.Add(lootAmount);
                }
                else
                {
                    // Primary curse building — full build cost drops immediately.
                    buildingDropPositions.Add(transform.ValueRO.Position);
                    buildingDropAmounts.Add(lootAmount);
                }
            }

            for (int i = 0; i < buildingDropPositions.Length; i++)
            {
                Cadaver.CreateOrMerge(em, buildingDropPositions[i], buildingDropAmounts[i], MaxCrystalNodes);
            }

            for (int i = 0; i < unitPilePositions.Length; i++)
            {
                AddToOrCreatePile(em, unitPilePositions[i], unitPileAmounts[i]);
            }

            for (int i = 0; i < secondaryKillers.Length; i++)
            {
                var killer = secondaryKillers[i];
                if (killer == Faction.Curse) continue;  // unattributed kills don't reward RP
                FactionReligionPointsHelper.Refund(em, killer, 1);
                Debug.Log($"[CrystalDeathDropSystem] secondary curse node destroyed by {killer} — +1 RP");
            }

            unitPilePositions.Dispose();
            unitPileAmounts.Dispose();
            buildingDropPositions.Dispose();
            buildingDropAmounts.Dispose();
            secondaryKillers.Dispose();
        }

        private static void AddToOrCreatePile(EntityManager em, float3 position, int amount)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CursePendingPile>(),
                ComponentType.ReadOnly<LocalTransform>());

            using var pileEntities = query.ToEntityArray(Allocator.Temp);
            using var pileTransforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float mergeSqr = Cadaver.MergeRadius * Cadaver.MergeRadius;
            for (int i = 0; i < pileEntities.Length; i++)
            {
                float2 a = new float2(pileTransforms[i].Position.x, pileTransforms[i].Position.z);
                float2 b = new float2(position.x, position.z);
                if (math.distancesq(a, b) <= mergeSqr)
                {
                    var pile = em.GetComponentData<CursePendingPile>(pileEntities[i]);
                    pile.Amount += amount;
                    pile.TimerRemaining = PendingPileTimerSeconds;
                    em.SetComponentData(pileEntities[i], pile);
                    return;
                }
            }

            var entity = em.CreateEntity(
                typeof(LocalTransform),
                typeof(CursePendingPile));
            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            em.SetComponentData(entity, new CursePendingPile
            {
                Amount = amount,
                TimerRemaining = PendingPileTimerSeconds
            });
        }
    }
}
