// File: Assets/Scripts/Systems/Research/ResearchSystem.cs
using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Research
{
    /// <summary>
    /// ECS system that processes technology research for buildings.
    ///
    /// Research workflow (mirrors TrainingSystem):
    /// 1. UI adds ResearchQueueItem to building's buffer (cost paid at queue time)
    /// 2. System starts researching first item when building is idle
    /// 3. Timer counts down based on tech's researchTime from TechTreeDB
    /// 4. When complete, marks tech as researched in FactionResearchState
    /// 5. Next queued tech starts automatically
    ///
    /// Works with: any building that has ResearchState + ResearchQueueItem buffer
    /// </summary>
    // NOTE: No [BurstCompile] — uses managed types (TechTreeDB, FactionResearchState, String, Debug.Log)
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ResearchSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ResearchState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var db = TechTreeDB.Instance;
            if (db == null) return;

            var researchState = FactionResearchState.Instance;
            if (researchState == null) return;

            float dt = SystemAPI.Time.DeltaTime;

            foreach (var (rs, entity) in SystemAPI
                         .Query<RefRW<ResearchState>>()
                         .WithNone<UnderConstruction>()
                         .WithEntityAccess())
            {
                var queue = state.EntityManager.GetBuffer<ResearchQueueItem>(entity);

                // Start researching if idle and queue has items
                if (rs.ValueRO.Busy == 0)
                {
                    if (queue.Length == 0) continue;

                    var techId = queue[0].TechId.ToString();

                    // Skip techs already researched (edge case: queued twice)
                    var em = state.EntityManager;
                    var faction = em.GetComponentData<FactionTag>(entity).Value;
                    if (researchState.HasResearched(faction, techId))
                    {
                        queue.RemoveAt(0);
                        continue;
                    }

                    if (!db.TryGetTechnology(techId, out var techDef))
                    {
                        // Unknown tech - remove from queue
                        queue.RemoveAt(0);
                        UnityEngine.Debug.LogWarning($"[ResearchSystem] Unknown tech ID in queue: {techId}");
                        continue;
                    }

                    // Start research
                    float researchTime = techDef.researchTime > 0 ? techDef.researchTime : 30f;

                    // Apply sect research speed multiplier (ResearchSpeed > 1.0 = faster)
                    if (FactionSectState.Instance != null)
                    {
                        var mults = FactionSectState.Instance.GetMultipliers(faction);
                        if (mults.ResearchSpeed > 0f)
                            researchTime /= mults.ResearchSpeed;
                    }

                    rs.ValueRW.Busy = 1;
                    rs.ValueRW.Remaining = researchTime;

                    UnityEngine.Debug.Log($"[ResearchSystem] {faction} started researching: {techDef.name} ({researchTime}s)");
                }
                else
                {
                    // Tick research timer
                    rs.ValueRW.Remaining -= dt;

                    if (rs.ValueRO.Remaining <= 0f && queue.Length > 0)
                    {
                        // Research complete
                        var techId = queue[0].TechId.ToString();
                        var em = state.EntityManager;
                        var faction = em.GetComponentData<FactionTag>(entity).Value;

                        // Mark as researched
                        researchState.CompleteResearch(faction, techId);

                        // Remove from queue and reset state
                        queue.RemoveAt(0);
                        rs.ValueRW.Busy = 0;
                        rs.ValueRW.Remaining = 0f;

                        if (db.TryGetTechnology(techId, out var techDef))
                        {
                            UnityEngine.Debug.Log($"[ResearchSystem] {faction} completed research: {techDef.name}");
                        }
                    }
                }
            }
        }
    }
}
