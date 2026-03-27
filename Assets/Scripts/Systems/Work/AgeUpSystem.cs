// File: Assets/Scripts/Systems/Work/AgeUpSystem.cs
// Timer-based age-up system — ticks AgeUpState.Remaining and runs
// completion logic (era set, hall scale, culture effects, RP grant)
// when the timer expires.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Ticks AgeUpState.Remaining on Hall entities each frame.
    /// When Remaining reaches 0, applies all age-up completion effects:
    ///   1. Set FactionProgress.Culture on the Hall
    ///   2. Scale the Hall 1.3x
    ///   3. Set FactionEra to 2 on the faction bank entity
    ///   4. Grant RP if a Temple exists
    ///   5. Alanthor: start GathererHut self-destruct timers
    ///   6. Remove AgeUpState component
    ///
    /// NOTE: Not Burst-compiled — accesses managed FactionColors and Debug.Log.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct AgeUpSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AgeUpState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Collect completed halls (structural changes can't happen during iteration)
            var completed = new NativeList<Entity>(Allocator.Temp);

            foreach (var (ageUp, entity) in SystemAPI
                .Query<RefRW<AgeUpState>>()
                .WithAll<HallTag>()
                .WithEntityAccess())
            {
                ageUp.ValueRW.Remaining -= dt;

                if (ageUp.ValueRO.Remaining <= 0f)
                {
                    completed.Add(entity);
                }
            }

            // Process completed age-ups
            for (int i = 0; i < completed.Length; i++)
            {
                Entity hallEntity = completed[i];
                if (!em.Exists(hallEntity)) continue;
                if (!em.HasComponent<AgeUpState>(hallEntity)) continue;

                var ageUpState = em.GetComponentData<AgeUpState>(hallEntity);
                byte culture = ageUpState.Culture;

                // Determine faction
                Faction faction = Faction.Blue;
                if (em.HasComponent<FactionTag>(hallEntity))
                    faction = em.GetComponentData<FactionTag>(hallEntity).Value;

                // 1. Set FactionProgress.Culture on the Hall
                if (em.HasComponent<FactionProgress>(hallEntity))
                {
                    var progress = em.GetComponentData<FactionProgress>(hallEntity);
                    progress.Culture = culture;
                    em.SetComponentData(hallEntity, progress);
                }

                // 2. Scale the Hall 1.3x
                if (em.HasComponent<LocalTransform>(hallEntity))
                {
                    var lt = em.GetComponentData<LocalTransform>(hallEntity);
                    lt.Scale = 1.3f;
                    em.SetComponentData(hallEntity, lt);
                }

                // 3. Set FactionEra to 2 and grant RP for temple level 1
                if (FactionEconomy.TryGetBank(em, faction, out var bankEntity))
                {
                    if (em.HasComponent<FactionEra>(bankEntity))
                        em.SetComponentData(bankEntity, new FactionEra { Value = 2 });

                    // Grant RP for temple level 1 (2 RP) if a temple exists
                    bool hasTemple = HasFactionTemple(em, faction);
                    if (hasTemple && em.HasComponent<ReligionPoints>(bankEntity))
                    {
                        var rp = em.GetComponentData<ReligionPoints>(bankEntity);
                        rp.Value += TempleLevelConfig.GetRPGranted(1);
                        em.SetComponentData(bankEntity, rp);
                        UnityEngine.Debug.Log($"[AgeUpSystem] {faction} granted {TempleLevelConfig.GetRPGranted(1)} RP for temple");
                    }
                }

                // 4. Alanthor: start 2-minute self-destruct countdown on all faction GathererHuts
                if (culture == Cultures.Alanthor)
                {
                    StartGathererHutSelfDestruct(em, faction);
                }

                // 5. Register culture with FactionColors (idempotent — may already be set by UI popup)
                FactionColors.SetFactionCulture(faction, culture);

                // 6. Rebuild building visuals with culture tone
                if (PresentationSpawnSystem.Instance != null)
                    PresentationSpawnSystem.Instance.RefreshFactionVisuals(faction);

                // 7. Remove AgeUpState — age-up is complete
                em.RemoveComponent<AgeUpState>(hallEntity);

                UnityEngine.Debug.Log($"[AgeUpSystem] {faction} completed age-up to Era 2 — culture: {CultureConfig.GetName(culture)}");
            }

            completed.Dispose();
        }

        /// <summary>
        /// Check if a faction has a Temple of Ridan (completed or under construction).
        /// </summary>
        private static bool HasFactionTemple(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleTag>(),
                ComponentType.ReadOnly<FactionTag>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// When Alanthor is chosen, all existing GathererHuts of this faction
        /// receive a 2-minute self-destruct timer with 80% cost refund.
        /// </summary>
        private static void StartGathererHutSelfDestruct(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<UnderConstruction>(),
                ComponentType.Exclude<SelfDestructTimer>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;

                em.AddComponentData(entities[i], new SelfDestructTimer
                {
                    TimeRemaining = 120f, // 2 minutes
                    RefundPaid = 0
                });
                count++;
            }

            if (count > 0)
                UnityEngine.Debug.Log($"[AgeUpSystem] {count} Gatherer's Hut(s) marked for self-destruct (2 min)");
        }
    }
}
