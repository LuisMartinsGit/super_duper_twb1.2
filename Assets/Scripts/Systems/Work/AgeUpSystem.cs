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

                // 3. Set FactionEra to 2 and award the Age-2 RP bonus.
                //    task-063: RP economy is sect-adoption-driven now (6/8/10
                //    per age + ⌊leftover/2⌋ carryover, plus +1 from Shrine).
                //    Temple existence is no longer a gate on the per-age award.
                if (FactionEconomy.TryGetBank(em, faction, out var bankEntity))
                {
                    if (em.HasComponent<FactionEra>(bankEntity))
                        em.SetComponentData(bankEntity, new FactionEra { Value = 2 });

                    FactionReligionPointsHelper.AwardAgeUp(em, faction, newAge: 2);
                }

                // 4. Per-culture hut transform (design §1.4 "transform, don't replace").
                //    Phase 1 of task-066 removes the old self-destruct paths; Phases 2-3
                //    add the wall-anchor / wagon-burst / raider-spawn behaviors. For now
                //    the huts simply persist across age-up — no auto-destruction.
                TransformGathererHutsForCulture(em, faction, culture);
                TransformHutsForCulture(em, faction, culture);

                // 4b. Runai: instant 200-pop override (Houses don't apply; wagon-burst is task-066 Phase 2).
                if (culture == Cultures.Runai)
                {
                    if (FactionEconomy.TryGetBank(em, faction, out var runaiBank))
                    {
                        if (!em.HasComponent<RunaiPopOverride>(runaiBank))
                            em.AddComponent<RunaiPopOverride>(runaiBank);
                    }
                }

                // 4c. Feraldis: instant 200-pop override (Houses don't contribute pop;
                //     they become raider-spawn buildings per design §5.1).
                if (culture == Cultures.Feraldis)
                {
                    if (FactionEconomy.TryGetBank(em, faction, out var feraldisBank))
                    {
                        if (!em.HasComponent<FeraldisPopOverride>(feraldisBank))
                            em.AddComponent<FeraldisPopOverride>(feraldisBank);
                    }
                }

                // 5. Register culture with FactionColors (idempotent — may already be set by UI popup)
                FactionColors.SetFactionCulture(faction, culture);

                // 6. Rebuild building visuals with culture tone
                if (PresentationSpawnSystem.Instance != null)
                    PresentationSpawnSystem.Instance.RefreshFactionVisuals(faction);

                // 7. Remove AgeUpState — age-up is complete
                em.RemoveComponent<AgeUpState>(hallEntity);

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
        /// Per-culture transform of faction-owned Gatherer's Huts at age-up.
        /// Alanthor → wall-segment anchor (Phase 2).
        /// Runai    → caravan-wagon with income decay (Phase 2).
        /// Feraldis → persists with income + raider-spawn tag (Phase 3).
        /// Phase 1 (task-066): no destruction; behaviors are stubs.
        /// </summary>
        private static void TransformGathererHutsForCulture(EntityManager em, Faction faction, byte culture)
        {
            // Phase 2/3 will dispatch culture-specific transform systems here.
            // For Phase 1 the huts remain unchanged across age-up.
        }

        /// <summary>
        /// Per-culture transform of faction-owned Huts (Houses) at age-up.
        /// Runai    → houses are removed (population comes from RunaiPopOverride).
        /// Alanthor → houses persist (standard pop ladder).
        /// Feraldis → houses become raider-spawn buildings (Phase 3 — task-066).
        /// Phase 1 (task-066): no destruction; behaviors are stubs.
        /// </summary>
        private static void TransformHutsForCulture(EntityManager em, Faction faction, byte culture)
        {
            // Phase 2/3 will dispatch culture-specific transform systems here.
            // For Phase 1 the huts remain unchanged across age-up.
        }
    }
}
