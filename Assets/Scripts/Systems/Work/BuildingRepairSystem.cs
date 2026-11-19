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
    /// 3. Worker walks out to its own spot just beyond the footprint's edge and
    ///    repairs only within reach of that edge, never from inside it
    ///    (BuilderStand, BuildingConstructionSystem.asset - the same rule as
    ///    construction, docs/Design/Age_0.md § Worker)
    /// 4. In range, it restores its CanBuild.RepairRate HP/second (Worker.asset
    ///    repairSpeed), shared by the crew on the building (CrewShare)
    /// 5. Each HP is paid as it goes in: repairCostShare (BuildingRepairSystem.asset)
    ///    of the building's price per max HP; with no funds the repair stops
    /// 6. When HP reaches max, RepairOrder is removed
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BuildingConstructionSystem))]
    public partial struct BuildingRepairSystem : ISystem
    {

        private EntityQuery _activeOrders;
        private EntityQuery _footprints;

        public void OnCreate(ref SystemState state)
        {
            _footprints = BuilderStand.FootprintQuery(ref state);
            // RepairOrder is enableable and pre-added DISABLED on every Worker
            // (2026-10-15); RequireForUpdate ignores enable bits, so the
            // IsEmpty test below restores the old "only while an order is
            // live" gate.
            state.RequireForUpdate<RepairOrder>();
            _activeOrders = state.GetEntityQuery(ComponentType.ReadOnly<RepairOrder>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_activeOrders.IsEmpty) return;   // see OnCreate
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

            var cfg = BuildingRepairSystemConfig.I;
            var standCfg = BuildingConstructionSystemConfig.I;
            if (cfg == null || standCfg == null) { workers.Dispose(); workerPositions.Dispose(); workerOrders.Dispose(); return; }

            // The crew on each building this tick (CrewShare, as construction),
            // and every spot already held round it (BuilderStand).
            var crew = new NativeHashMap<Entity, int>(16, Allocator.Temp);
            var claimed = new NativeHashSet<int4>(16, Allocator.Temp);
            for (int i = 0; i < workers.Length; i++)
            {
                Entity s0 = workerOrders[i].Site;
                if (!em.Exists(s0) || em.HasComponent<UnderConstruction>(s0) || !em.HasComponent<Health>(s0)) continue;
                BuilderStand.Claim(claimed, s0, workerOrders[i].Stand);
                if (!BuilderStand.InReach(TargetGeometry.Extent(em, s0), workerPositions[i],
                        BuilderStand.RadiusOf(em, workers[i]), standCfg)) continue;
                crew.TryGetValue(s0, out int c0);
                crew[s0] = c0 + 1;
            }
            var footprints = new BuilderStand.Footprints(_footprints);

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
                    TransientState.Clear<RepairOrder>(em, worker);
                    continue;
                }

                // Check if building is under construction (shouldn't repair, use BuildOrder instead)
                if (em.HasComponent<UnderConstruction>(site))
                {
                    TransientState.Clear<RepairOrder>(em, worker);
                    continue;
                }

                // Check if building still needs repair
                if (!em.HasComponent<Health>(site))
                {
                    TransientState.Clear<RepairOrder>(em, worker);
                    continue;
                }

                var hp = em.GetComponentData<Health>(site);
                if (hp.Value >= hp.Max)
                {
                    // Fully repaired - clear order
                    TransientState.Clear<RepairOrder>(em, worker);

                    // Update guard point
                    if (TransientState.Active<GuardPoint>(em, worker))
                    {
                        em.SetComponentData(worker, new GuardPoint
                        {
                            Position = bPos,
                            Has = 1
                        });
                    }
                    continue;
                }

                // Reach is measured to the footprint edge, not the pivot, and
                // the repairer must stand OUTSIDE it, at its own latched spot
                // (BuilderStand, as construction).
                float3 sitePos = em.GetComponentData<LocalTransform>(site).Position;
                var extent = TargetGeometry.Extent(em, site);
                var step = BuilderStand.Tick(em, worker, bPos, site, extent, ref order.Stand,
                    claimed, ref footprints, standCfg, dt);
                if (!BuilderStand.SameStand(order.Stand, workerOrders[i].Stand))
                    em.SetComponentData(worker, order);

                if (step == BuilderStand.Step.GiveUp)
                {
                    BuilderStand.StandDown<RepairOrder>(em, worker, bPos);
                    continue;
                }
                if (step == BuilderStand.Step.Walk) continue;
                else
                {
                    // In range - plant and face the building
                    TargetGeometry.StopAndFace(em, worker, sitePos, dt);

                    // The repairer's own rate, from its unit SO (Worker.asset
                    // repairSpeed, on CanBuild), shared by the crew on this
                    // building as in construction (CrewShare: n repairers
                    // restore (n + 2) / 3 of one's HP). The fraction of an HP
                    // is carried to the next tick: rounding each tick UP gave
                    // 1 HP a tick at 30 Hz, double the rate the SO states.
                    float rate = em.HasComponent<CanBuild>(worker)
                        ? em.GetComponentData<CanBuild>(worker).RepairRate : 0f;
                    crew.TryGetValue(site, out int crewSize);
                    order.Carry += rate * BuildingConstructionSystem.CrewShare(crewSize) * dt;
                    int add = math.min((int)order.Carry, hp.Max - hp.Value);

                    // PAID AS THE HP GOES IN (2026-11-17): each repaired HP costs
                    // repairCostShare of the building's price over its max HP -
                    // whoever restores it, so a crew pays once, not once a head.
                    // No funds: the repair stops (developer: "Make sure repairs
                    // stop if there are no funds").
                    if (add > 0 && !PayForHp(em, worker, site, hp.Max, add, ref order))
                    {
                        TransientState.Clear<RepairOrder>(em, worker);
                        if (em.HasComponent<FactionTag>(worker)
                            && em.GetComponentData<FactionTag>(worker).Value == GameSettings.LocalPlayerFaction)
                            SimSignals.Notify(TheWaningBorder.Core.Localization.Loc.T("Repair stopped - not enough resources"));
                        continue;
                    }
                    order.Carry -= add;
                    em.SetComponentData(worker, order);
                    hp.Value = math.min(hp.Max, hp.Value + add);
                    em.SetComponentData(site, hp);

                    if (hp.Value >= hp.Max)
                    {
                        // Repair complete
                        TransientState.Clear<RepairOrder>(em, worker);

                        if (TransientState.Active<GuardPoint>(em, worker))
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
            crew.Dispose();
            claimed.Dispose();
            footprints.Dispose();
        }

        /// <summary>
        /// Pay for <paramref name="hpAdded"/> repaired HP: repairCostShare of
        /// the building's price per max HP, with the fractions of a resource
        /// carried on the order. False - and nothing spent - when the faction
        /// cannot pay.
        /// </summary>
        private static bool PayForHp(EntityManager em, Entity worker, Entity building, int hpMax,
            int hpAdded, ref RepairOrder order)
        {
            if (!em.HasComponent<FactionTag>(worker)) return false;
            var faction = em.GetComponentData<FactionTag>(worker).Value;
            var cfg = BuildingRepairSystemConfig.I;
            if (cfg == null || hpMax <= 0) return true;
            string buildingId = GetBuildingId(em, building);
            if (buildingId == null || !TechCatalog.IsReady
                || !TechCatalog.TryGetBuilding(buildingId, out var def) || def.cost == null)
                return true;   // no price on record: the repair is free

            float perHp = cfg.repairCostShare / hpMax;
            float cs = order.CostCarryS + def.cost.Supplies * perHp * hpAdded;
            float ci = order.CostCarryI + def.cost.Iron * perHp * hpAdded;
            float cv = order.CostCarryV + def.cost.Veilstone * perHp * hpAdded;
            var cost = Cost.Of(supplies: (int)cs, iron: (int)ci, veilstone: (int)cv);
            if (cost.Supplies > 0 || cost.Iron > 0 || cost.Veilstone > 0)
            {
                if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
                if (!FactionEconomy.Spend(em, faction, cost, TheWaningBorder.Economy.SpendCategory.Repair))
                    return false;
            }
            order.CostCarryS = cs - (int)cs;
            order.CostCarryI = ci - (int)ci;
            order.CostCarryV = cv - (int)cv;
            return true;
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
