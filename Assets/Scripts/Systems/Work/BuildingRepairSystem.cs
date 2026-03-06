// File: Assets/Scripts/Systems/Work/BuildingRepairSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Core;
using TheWaningBorder.Data;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Handles building repair by builder units.
    ///
    /// Repair workflow:
    /// 1. Player right-clicks damaged building with builder selected
    /// 2. Builder receives RepairOrder component pointing to damaged building
    /// 3. Builder moves to building (within RepairRange)
    /// 4. On arrival, resources are deducted:
    ///    Cost = (missingHP / maxHP) * originalBuildCost * RepairCostMultiplier
    /// 5. Builder repairs at RepairRatePerBuilder HP/second
    /// 6. When HP reaches max, RepairOrder is removed
    ///
    /// Multiple builders can repair the same building simultaneously.
    /// Resources are paid once per builder on arrival (proportional to remaining damage).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BuildingConstructionSystem))]
    public partial struct BuildingRepairSystem : ISystem
    {
        private const float RepairRange = 4.0f;
        private const float RepairRatePerBuilder = 15.0f; // HP per second per builder
        private const float RepairCostMultiplier = 1.2f;  // 1.2x cost penalty

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RepairOrder>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Snapshot all builders with repair orders
            var builders = new NativeList<Entity>(Allocator.Temp);
            var builderPositions = new NativeList<float3>(Allocator.Temp);
            var builderOrders = new NativeList<RepairOrder>(Allocator.Temp);

            foreach (var (transform, order, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<RepairOrder>>()
                .WithAll<CanBuild>()
                .WithEntityAccess())
            {
                builders.Add(entity);
                builderPositions.Add(transform.ValueRO.Position);
                builderOrders.Add(order.ValueRO);
            }

            // Process each builder
            for (int i = 0; i < builders.Length; i++)
            {
                Entity builder = builders[i];
                float3 bPos = builderPositions[i];
                RepairOrder order = builderOrders[i];
                Entity site = order.Site;

                // Validate building still exists
                if (!em.Exists(site))
                {
                    em.RemoveComponent<RepairOrder>(builder);
                    continue;
                }

                // Check if building is under construction (shouldn't repair, use BuildOrder instead)
                if (em.HasComponent<UnderConstruction>(site))
                {
                    em.RemoveComponent<RepairOrder>(builder);
                    continue;
                }

                // Check if building still needs repair
                if (!em.HasComponent<Health>(site))
                {
                    em.RemoveComponent<RepairOrder>(builder);
                    continue;
                }

                var hp = em.GetComponentData<Health>(site);
                if (hp.Value >= hp.Max)
                {
                    // Fully repaired - clear order
                    em.RemoveComponent<RepairOrder>(builder);

                    // Update guard point
                    if (em.HasComponent<GuardPoint>(builder))
                    {
                        em.SetComponentData(builder, new GuardPoint
                        {
                            Position = bPos,
                            Has = 1
                        });
                    }
                    continue;
                }

                // Get building position
                float3 sitePos = em.GetComponentData<LocalTransform>(site).Position;
                float dist = DistXZ(bPos, sitePos);

                if (dist > RepairRange)
                {
                    // Move toward building
                    if (em.HasComponent<DesiredDestination>(builder))
                    {
                        em.SetComponentData(builder, new DesiredDestination
                        {
                            Position = sitePos,
                            Has = 1
                        });
                    }
                    else
                    {
                        em.AddComponentData(builder, new DesiredDestination
                        {
                            Position = sitePos,
                            Has = 1
                        });
                    }
                }
                else
                {
                    // In range - stop moving
                    if (em.HasComponent<DesiredDestination>(builder))
                    {
                        em.SetComponentData(builder, new DesiredDestination { Has = 0 });
                    }

                    // Pay repair cost on first arrival
                    if (order.CostPaid == 0)
                    {
                        if (!TryPayRepairCost(em, builder, site, hp))
                        {
                            // Can't afford repair - remove order
                            em.RemoveComponent<RepairOrder>(builder);
                            UnityEngine.Debug.Log("Cannot afford repair - insufficient resources.");
                            continue;
                        }

                        // Mark cost as paid
                        order.CostPaid = 1;
                        order.StartHP = hp.Value;
                        order.TargetHP = hp.Max;
                        em.SetComponentData(builder, order);
                    }

                    // Repair: add HP over time
                    hp.Value = math.min(hp.Max, hp.Value + (int)math.ceil(RepairRatePerBuilder * dt));
                    em.SetComponentData(site, hp);

                    if (hp.Value >= hp.Max)
                    {
                        // Repair complete
                        em.RemoveComponent<RepairOrder>(builder);
                        UnityEngine.Debug.Log($"Building {site.Index} repair complete!");

                        if (em.HasComponent<GuardPoint>(builder))
                        {
                            em.SetComponentData(builder, new GuardPoint
                            {
                                Position = bPos,
                                Has = 1
                            });
                        }
                    }
                }
            }

            builders.Dispose();
            builderPositions.Dispose();
            builderOrders.Dispose();
        }

        /// <summary>
        /// Calculate and deduct repair cost from faction resources.
        /// Cost = (missingHP / maxHP) * originalBuildCost * 1.2
        /// </summary>
        private static bool TryPayRepairCost(EntityManager em, Entity builder, Entity building, Health hp)
        {
            // Get faction
            if (!em.HasComponent<FactionTag>(builder)) return false;
            var faction = em.GetComponentData<FactionTag>(builder).Value;

            // Get building's TechTree ID to look up original cost
            string buildingId = GetBuildingId(em, building);
            if (buildingId == null) return true; // Unknown building - repair for free

            if (TechTreeDB.Instance == null) return true;
            if (!TechTreeDB.Instance.TryGetBuilding(buildingId, out var def)) return true;
            if (def.cost == null) return true; // No cost defined - repair for free

            // Guard against division by zero (hp.Max should never be 0, but be safe)
            if (hp.Max <= 0) return true;

            // Calculate damage ratio
            float damageRatio = 1f - ((float)hp.Value / hp.Max);
            if (damageRatio <= 0f) return true; // Not damaged

            // Calculate repair cost with 1.2x penalty
            int repairSupplies = (int)math.ceil(def.cost.Supplies * damageRatio * RepairCostMultiplier);
            int repairIron = (int)math.ceil(def.cost.Iron * damageRatio * RepairCostMultiplier);
            int repairCrystal = (int)math.ceil(def.cost.Crystal * damageRatio * RepairCostMultiplier);

            var cost = Cost.Of(
                supplies: repairSupplies,
                iron: repairIron,
                crystal: repairCrystal
            );

            // Try to spend
            return FactionEconomy.Spend(em, faction, cost);
        }

        /// <summary>
        /// Map entity to its TechTree building ID using tag components.
        /// </summary>
        private static string GetBuildingId(EntityManager em, Entity entity)
        {
            if (em.HasComponent<HallTag>(entity)) return "Hall";
            if (em.HasComponent<BarracksTag>(entity)) return "Barracks";
            if (em.HasComponent<GathererHutTag>(entity)) return "GatherersHut";
            if (em.HasComponent<HutTag>(entity)) return "Hut";
            if (em.HasComponent<TempleTag>(entity)) return "TempleOfRidan";
            if (em.HasComponent<VaultTag>(entity)) return "VaultOfAlmierra";
            if (em.HasComponent<FiendstoneKeepTag>(entity)) return "FiendstoneKeep";
            if (em.HasComponent<SmelterTag>(entity)) return "Alanthor_Smelter";
            return null;
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}
