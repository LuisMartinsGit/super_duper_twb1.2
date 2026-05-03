// File: Assets/Scripts/Systems/Work/ForgeSupplySystem.cs
// Handles miners supplying a Smelter (Forge) with iron and crystal.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Manages the miner → GathererHut/Hall → Smelter supply chain.
    ///
    /// When a miner has a ForgeSupplyOrder:
    ///   Phase 0 (GoingToPickup): Walk to nearest Hall/GathererHut, withdraw 10 iron or crystal
    ///   Phase 1 (DeliveringToForge): Walk to forge, deposit into ForgeStorage
    ///   Loop back to Phase 0.
    ///
    /// Resource type is auto-selected based on what the forge needs more of.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MiningSystem))]
    public partial struct ForgeSupplySystem : ISystem
    {
        private const float PickupRange = 6f;
        private const float DeliveryRange = 6f;
        private const int PickupAmount = 10;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ForgeSupplyOrder>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (order, minerState, transform, faction, entity) in SystemAPI
                .Query<RefRW<ForgeSupplyOrder>, RefRW<MinerState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<MinerTag>()
                .WithEntityAccess())
            {
                ref var supply = ref order.ValueRW;
                ref var miner = ref minerState.ValueRW;
                var pos = transform.ValueRO.Position;
                var fac = faction.ValueRO.Value;

                // --- UserMoveOrder interrupt ---
                if (em.HasComponent<UserMoveOrder>(entity))
                {
                    // Player issued move: clear forge supply, keep load, go idle
                    em.RemoveComponent<ForgeSupplyOrder>(entity);
                    miner.State = MinerWorkState.Idle;
                    miner.AssignedDeposit = Entity.Null;
                    miner.DropoffTarget = Entity.Null;
                    continue;
                }

                // Validate forge still exists
                if (supply.Forge == Entity.Null || !em.Exists(supply.Forge))
                {
                    em.RemoveComponent<ForgeSupplyOrder>(entity);
                    miner.State = MinerWorkState.Idle;
                    continue;
                }

                // Validate forge still has ForgeStorage (not destroyed/changed)
                if (!em.HasComponent<ForgeStorage>(supply.Forge))
                {
                    em.RemoveComponent<ForgeSupplyOrder>(entity);
                    miner.State = MinerWorkState.Idle;
                    continue;
                }

                switch (supply.Phase)
                {
                    case 0: // GoingToPickup
                        ProcessPickupPhase(ref supply, ref miner, em, entity, pos, fac);
                        break;

                    case 1: // DeliveringToForge
                        ProcessDeliveryPhase(ref supply, ref miner, em, entity, pos, fac);
                        break;
                }
            }
        }

        private void ProcessPickupPhase(ref ForgeSupplyOrder supply, ref MinerState miner,
            EntityManager em, Entity entity, float3 pos, Faction fac)
        {
            // Find pickup location (nearest Hall or GathererHut)
            if (miner.DropoffTarget == Entity.Null || !em.Exists(miner.DropoffTarget))
            {
                miner.DropoffTarget = FindNearestDropoff(em, pos, fac);
                if (miner.DropoffTarget == Entity.Null)
                {
                    // No pickup location available
                    return;
                }

                // Move to pickup
                var pickupPos = em.GetComponentData<LocalTransform>(miner.DropoffTarget).Position;
                SetDestination(em, entity, pickupPos);
            }

            // Check if at pickup
            var targetPos = em.GetComponentData<LocalTransform>(miner.DropoffTarget).Position;
            float dist = DistXZ(pos, targetPos);

            if (dist <= PickupRange)
            {
                // Stop moving
                StopMoving(em, entity);

                // Determine which resource the forge needs more
                var forgeStorage = em.GetComponentData<ForgeStorage>(supply.Forge);
                byte resourceType = DetermineNeededResource(forgeStorage);
                supply.ResourceType = resourceType;

                // Try to withdraw from faction bank
                Cost cost = resourceType == 0
                    ? Cost.Of(iron: PickupAmount)
                    : Cost.Of(crystal: PickupAmount);

                if (!FactionEconomy.Spend(em, fac, cost))
                {
                    // Can't afford — wait and retry next frame
                    return;
                }

                // Picked up resources
                miner.CurrentLoad = PickupAmount;
                miner.GatheringResource = resourceType;
                miner.DropoffTarget = Entity.Null; // Clear pickup target
                supply.Phase = 1; // Move to delivery phase

                // Move to forge
                var forgePos = em.GetComponentData<LocalTransform>(supply.Forge).Position;
                SetDestination(em, entity, forgePos);
            }
        }

        private void ProcessDeliveryPhase(ref ForgeSupplyOrder supply, ref MinerState miner,
            EntityManager em, Entity entity, float3 pos, Faction fac)
        {
            // Validate forge still exists
            if (supply.Forge == Entity.Null || !em.Exists(supply.Forge) ||
                !em.HasComponent<ForgeStorage>(supply.Forge))
            {
                // Forge destroyed — keep load, go idle
                em.RemoveComponent<ForgeSupplyOrder>(entity);
                miner.State = MinerWorkState.Idle;
                return;
            }

            var forgePos = em.GetComponentData<LocalTransform>(supply.Forge).Position;
            float dist = DistXZ(pos, forgePos);

            // Keep moving toward forge
            if (dist > DeliveryRange)
            {
                SetDestination(em, entity, forgePos);
                return;
            }

            // At forge — deposit resources
            StopMoving(em, entity);

            var forgeStorage = em.GetComponentData<ForgeStorage>(supply.Forge);
            int deposited = 0;

            if (supply.ResourceType == 0)
            {
                // Iron
                int spaceLeft = forgeStorage.MaxIron - forgeStorage.Iron;
                deposited = math.min(miner.CurrentLoad, spaceLeft);
                forgeStorage.Iron += deposited;
            }
            else
            {
                // Crystal
                int spaceLeft = forgeStorage.MaxCrystal - forgeStorage.Crystal;
                deposited = math.min(miner.CurrentLoad, spaceLeft);
                forgeStorage.Crystal += deposited;
            }

            em.SetComponentData(supply.Forge, forgeStorage);

            // If couldn't deposit all (forge full), return excess to faction bank
            int excess = miner.CurrentLoad - deposited;
            if (excess > 0)
            {
                var refund = supply.ResourceType == 0
                    ? Cost.Of(iron: excess)
                    : Cost.Of(crystal: excess);
                FactionEconomy.Add(em, fac, refund);
            }

            miner.CurrentLoad = 0;
            supply.Phase = 0; // Back to pickup phase
        }

        /// <summary>
        /// Determine which resource the forge needs more of.
        /// Returns 0 for iron, 1 for crystal.
        /// </summary>
        private static byte DetermineNeededResource(ForgeStorage forge)
        {
            // If iron storage is full, pick crystal
            if (forge.Iron >= forge.MaxIron) return 1;
            // If crystal storage is full, pick iron
            if (forge.Crystal >= forge.MaxCrystal) return 0;

            // Pick whichever has lower fill ratio
            float ironRatio = forge.MaxIron > 0 ? (float)forge.Iron / forge.MaxIron : 1f;
            float crystalRatio = forge.MaxCrystal > 0 ? (float)forge.Crystal / forge.MaxCrystal : 1f;

            return ironRatio <= crystalRatio ? (byte)0 : (byte)1;
        }

        /// <summary>
        /// Find nearest completed Hall or GathererHut of the miner's faction.
        /// </summary>
        private static Entity FindNearestDropoff(EntityManager em, float3 pos, Faction fac)
        {
            Entity nearest = Entity.Null;
            float nearestDist = float.MaxValue;

            // Search Halls
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );
            using var halls = hallQuery.ToEntityArray(Allocator.Temp);
            using var hallFactions = hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < halls.Length; i++)
            {
                if (hallFactions[i].Value != fac) continue;
                float dist = DistXZ(pos, hallTransforms[i].Position);
                if (dist < nearestDist) { nearest = halls[i]; nearestDist = dist; }
            }

            // Search GathererHuts
            var hutQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<UnderConstruction>()
            );
            using var huts = hutQuery.ToEntityArray(Allocator.Temp);
            using var hutFactions = hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hutTransforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < huts.Length; i++)
            {
                if (hutFactions[i].Value != fac) continue;
                float dist = DistXZ(pos, hutTransforms[i].Position);
                if (dist < nearestDist) { nearest = huts[i]; nearestDist = dist; }
            }

            return nearest;
        }

        private static void SetDestination(EntityManager em, Entity entity, float3 target)
        {
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Position = target, Has = 1 });
                else
                    em.AddComponentData(entity, new DesiredDestination { Position = target, Has = 1 });
        }

        private static void StopMoving(EntityManager em, Entity entity)
        {
            if (em.HasComponent<DesiredDestination>(entity))
                em.SetComponentData(entity, new DesiredDestination { Has = 0 });
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}
