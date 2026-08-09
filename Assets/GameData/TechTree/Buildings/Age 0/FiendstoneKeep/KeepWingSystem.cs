// KeepWingSystem.cs
// Fiendstone Keep wing construction + per-wing running effects (choice
// building leveling, design 2026-07-04):
//   * Ticks KeepWingConstruction; on completion fills a KeepWings slot and
//     applies the wing's one-shot effects (Engineers HP, Temple RP, training
//     capability for War/Civic/Temple).
//   * Ticks the Civic/Economic Supplies trickle into the faction bank.
//
// Per-volley Engineers ballistas live in BuildingCombatSystem; the
// Librarians' research effects live in ResearchSystem (speed) and
// EntityExtractors.Research (Hall techs at the Keep); the War/Civic/Temple
// train roster lives in EntityExtractors.Training.
//
// Managed SystemBase — grants RP and touches managed helpers.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class KeepWingSystem : SystemBase
    {
        private float _incomeAcc;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = World.Time.DeltaTime;

            // ── 1. Wing construction ────────────────────────────────────────
            var buildQuery = GetEntityQuery(
                ComponentType.ReadWrite<KeepWingConstruction>(),
                ComponentType.ReadWrite<KeepWings>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var ents = buildQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    var c = em.GetComponentData<KeepWingConstruction>(e);
                    c.Remaining -= dt;
                    if (c.Remaining > 0f)
                    {
                        em.SetComponentData(e, c);
                        continue;
                    }

                    // Complete the wing.
                    var wings = em.GetComponentData<KeepWings>(e);
                    var wing = (KeepWingType)c.Wing;
                    if (wings.Add(wing))
                    {
                        em.SetComponentData(e, wings);
                        ApplyWingCompletion(em, e, wing);
                        TWBLog.Log($"[KeepWing] {NameOfFaction(em, e)} completed {KeepWingConfig.NameOf(wing)}");
                    }
                    em.RemoveComponent<KeepWingConstruction>(e);
                }
            }

            // ── 2. Civic / Economic Supplies trickle (1 s cadence) ──────────
            _incomeAcc += dt;
            if (_incomeAcc < 1f) return;
            float tick = _incomeAcc;
            _incomeAcc = 0f;

            var incomeQuery = GetEntityQuery(
                ComponentType.ReadOnly<KeepWings>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<UnderConstruction>());
            using (var ents = incomeQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var wings = em.GetComponentData<KeepWings>(ents[i]);
                    float perSecond = 0f;
                    if (wings.Has(KeepWingType.Civic)) perSecond += KeepWingConfig.CivicSuppliesPerSecond;
                    if (wings.Has(KeepWingType.Economic)) perSecond += KeepWingConfig.EconomicSuppliesPerSecond;
                    if (perSecond <= 0f) continue;

                    var faction = em.GetComponentData<FactionTag>(ents[i]).Value;
                    int amount = (int)math.round(perSecond * tick);
                    if (amount <= 0) continue;
                    if (FactionEconomy.TryGetBank(em, faction, out var bank))
                    {
                        var res = em.GetComponentData<FactionResources>(bank);
                        res.Supplies += amount;
                        res.Clamp();
                        em.SetComponentData(bank, res);
                    }
                }
            }
        }

        /// <summary>One-shot effects when a wing finishes building.</summary>
        private static void ApplyWingCompletion(EntityManager em, Entity keep, KeepWingType wing)
        {
            switch (wing)
            {
                case KeepWingType.Engineers:
                    if (em.HasComponent<Health>(keep))
                    {
                        var hp = em.GetComponentData<Health>(keep);
                        hp.Max = (int)(hp.Max * KeepWingConfig.EngineersHpMultiplier);
                        hp.Value = (int)(hp.Value * KeepWingConfig.EngineersHpMultiplier);
                        em.SetComponentData(keep, hp);
                    }
                    break;

                case KeepWingType.Temple:
                    if (em.HasComponent<FactionTag>(keep))
                        FactionReligionPointsHelper.Refund(em, em.GetComponentData<FactionTag>(keep).Value, 1);
                    EnsureTrainingCapability(em, keep);
                    break;

                case KeepWingType.War:
                    EnsureTrainingCapability(em, keep);
                    break;

                case KeepWingType.Civic:
                    EnsureTrainingCapability(em, keep);
                    break;

                // Economic / Librarians have no one-shot component changes:
                // their effects are read live (income tick above, research
                // speed in ResearchSystem, Hall techs in the research UI).
            }
        }

        /// <summary>Give the Keep a training queue the first time a training wing completes.</summary>
        private static void EnsureTrainingCapability(EntityManager em, Entity keep)
        {
            if (!em.HasComponent<TrainingState>(keep))
                em.AddComponentData(keep, new TrainingState { Busy = 0, Remaining = 0 });
            if (!em.HasBuffer<TrainQueueItem>(keep))
                em.AddBuffer<TrainQueueItem>(keep);
            if (!em.HasComponent<RallyPoint>(keep) && em.HasComponent<Unity.Transforms.LocalTransform>(keep))
            {
                var pos = em.GetComponentData<Unity.Transforms.LocalTransform>(keep).Position;
                em.AddComponentData(keep, new RallyPoint
                {
                    Position = pos + new Unity.Mathematics.float3(4f, 0, 4f),
                    Has = 1
                });
            }
        }

        private static string NameOfFaction(EntityManager em, Entity e)
            => em.HasComponent<FactionTag>(e) ? em.GetComponentData<FactionTag>(e).Value.ToString() : "?";
    }
}
