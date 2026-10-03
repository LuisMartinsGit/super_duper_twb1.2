using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core;
using TheWaningBorder.Data;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles building repair by worker units.
    ///
    /// Repair workflow:
    /// 1. Player right-clicks damaged building with worker selected
    /// 2. Worker receives RepairOrder component pointing to damaged building
    /// 3. Worker moves to building (within RepairRange)
    /// 4. On arrival, resources are deducted:
    ///    Cost = (missingHP / maxHP) * originalBuildCost * RepairCostMultiplier
    /// 5. Worker repairs at RepairRatePerWorker HP/second
    /// 6. When HP reaches max, RepairOrder is removed
    ///
    /// Multiple workers can repair the same building simultaneously.
    /// Resources are paid once per worker on arrival (proportional to remaining damage).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BuildingConstructionSystem))]
    public partial struct BuildingRepairSystem : ISystem
    {
        private const float RepairRange = 4.0f;
        private const float RepairRatePerWorker = 15.0f; // HP per second per worker
        private const float RepairCostMultiplier = 1.2f;  // 1.2x cost penalty

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RepairOrder>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot all workers with repair orders
            var workers = new NativeList<Entity>(Allocator.Temp);
            var workerPositions = new NativeList<float3>(Allocator.Temp);
            var workerOrders = new NativeList<RepairOrder>(Allocator.Temp);

            foreach (var (transform, order, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<RepairOrder>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                workers.Add(entity);
                workerPositions.Add(transform.ValueRO.Position);
                workerOrders.Add(order.ValueRO);
            }

            // Process each worker
            for (int i = 0; i < workers.Length; i++)
            {
                Entity worker = workers[i];
                float3 bPos = workerPositions[i];
                RepairOrder order = workerOrders[i];
                Entity site = order.Site;

                // Validate building still exists
                if (!em.Exists(site))
                {
                    em.RemoveComponent<RepairOrder>(worker);
                    continue;
                }

                // Check if building is under construction (shouldn't repair, use BuildOrder instead)
                if (em.HasComponent<UnderConstruction>(site))
                {
                    em.RemoveComponent<RepairOrder>(worker);
                    continue;
                }

                // Check if building still needs repair
                if (!em.HasComponent<Health>(site))
                {
                    em.RemoveComponent<RepairOrder>(worker);
                    continue;
                }

                var hp = em.GetComponentData<Health>(site);
                if (hp.Value >= hp.Max)
                {
                    // Fully repaired - clear order
                    em.RemoveComponent<RepairOrder>(worker);

                    // Update guard point
                    if (em.HasComponent<GuardPoint>(worker))
                    {
                        em.SetComponentData(worker, new GuardPoint
                        {
                            Position = bPos,
                            Has = 1
                        });
                    }
                    continue;
                }

                // Get building position. Reach is measured to the footprint edge,
                // not the pivot, so a large building isn't unrepairable from the
                // only ground a worker can actually stand on.
                float3 sitePos = em.GetComponentData<LocalTransform>(site).Position;
                var extent = TargetGeometry.Extent(em, site);
                float dist = extent.SurfaceDistXZ(bPos);

                if (dist > RepairRange)
                {
                    // Walk to the footprint edge, a half-step back so the
                    // destination is walkable ground.
                    float3 approach = extent.ApproachPoint(bPos, RepairRange * 0.5f);
                    approach.y = sitePos.y;

                    if (em.HasComponent<DesiredDestination>(worker))
                    {
                        em.SetComponentData(worker, new DesiredDestination
                        {
                            Position = approach,
                            Has = 1
                        });
                    }
                    else
                    {
                        em.AddComponentData(worker, new DesiredDestination
                        {
                            Position = approach,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In range - plant and face the building
                    TargetGeometry.StopAndFace(em, worker, sitePos, dt);

                    // Pay repair cost on first arrival
                    if (order.CostPaid == 0)
                    {
                        if (!TryPayRepairCost(em, worker, site, hp))
                        {
                            // Can't afford repair - remove order
                            em.RemoveComponent<RepairOrder>(worker);
                            continue;
                        }

                        // Mark cost as paid
                        order.CostPaid = 1;
                        order.StartHP = hp.Value;
                        order.TargetHP = hp.Max;
                        em.SetComponentData(worker, order);
                    }

                    // Repair: add HP over time
                    hp.Value = math.min(hp.Max, hp.Value + (int)math.ceil(RepairRatePerWorker * dt));
                    em.SetComponentData(site, hp);

                    if (hp.Value >= hp.Max)
                    {
                        // Repair complete
                        em.RemoveComponent<RepairOrder>(worker);

                        if (em.HasComponent<GuardPoint>(worker))
                        {
                            em.SetComponentData(worker, new GuardPoint
                            {
                                Position = bPos,
                                Has = 1
                            });
                        }
                    }
                }
            }

            workers.Dispose();
            workerPositions.Dispose();
            workerOrders.Dispose();
        }

        /// <summary>
        /// Calculate and deduct repair cost from faction resources.
        /// Cost = (missingHP / maxHP) * originalBuildCost * 1.2
        /// </summary>
        private static bool TryPayRepairCost(EntityManager em, Entity worker, Entity building, Health hp)
        {
            // Get faction
            if (!em.HasComponent<FactionTag>(worker)) return false;
            var faction = em.GetComponentData<FactionTag>(worker).Value;

            // Get building's TechTree ID to look up original cost
            string buildingId = GetBuildingId(em, building);
            if (buildingId == null) return true; // Unknown building - repair for free

            if (!TechCatalog.IsReady) return true;
            if (!TechCatalog.TryGetBuilding(buildingId, out var def)) return true;
            if (def.cost == null) return true; // No cost defined - repair for free

            // Guard against division by zero (hp.Max should never be 0, but be safe)
            if (hp.Max <= 0) return true;

            // Calculate damage ratio
            float damageRatio = 1f - ((float)hp.Value / hp.Max);
            if (damageRatio <= 0f) return true; // Not damaged

            // Calculate repair cost with 1.2x penalty
            int repairSupplies = (int)math.ceil(def.cost.Supplies * damageRatio * RepairCostMultiplier);
            int repairIron = (int)math.ceil(def.cost.Iron * damageRatio * RepairCostMultiplier);
            int repairVeilstone = (int)math.ceil(def.cost.Veilstone * damageRatio * RepairCostMultiplier);

            var cost = Cost.Of(
                supplies: repairSupplies,
                iron: repairIron,
                veilstone: repairVeilstone
            );

            // Try to spend
            return FactionEconomy.Spend(em, faction, cost);
        }

        /// <summary>
        /// Map entity to its TechTree building ID using tag components.
        /// </summary>
        private static string GetBuildingId(EntityManager em, Entity entity)
        {
            // Delegate to the canonical entity->building-id mapping so repair-cost
            // lookups automatically track new building types (BuildCosts.IdFromEntity
            // is the single source of truth, with broader coverage than this used to).
            return TheWaningBorder.Data.BuildCosts.IdFromEntity(em, entity);
        }

    }
}
