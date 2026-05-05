// TempleUpgradeSystem.cs
// Ticks TempleUpgradeState.Remaining each frame. On completion:
// sets TempleLevel, updates FactionEra, grants RP, recalculates sect passives.
// Pattern follows AgeUpSystem.cs.
// Location: Assets/Scripts/Systems/Work/TempleUpgradeSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Ticks TempleUpgradeState.Remaining on Temple entities each frame.
    /// When Remaining reaches 0, applies all upgrade completion effects:
    ///   1. Set TempleLevel to TargetLevel
    ///   2. Set FactionEra on the faction bank
    ///   3. Grant Religion Points
    ///   4. Recalculate sect passive scaling
    ///   5. Remove TempleUpgradeState component
    ///
    /// NOTE: Not Burst-compiled — accesses managed singletons.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TempleUpgradeSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TempleUpgradeState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            // Collect completed temples (structural changes can't happen during iteration)
            var completed = new NativeList<Entity>(Allocator.Temp);

            foreach (var (upgrade, entity) in SystemAPI
                .Query<RefRW<TempleUpgradeState>>()
                .WithAll<TempleTag>()
                .WithEntityAccess())
            {
                upgrade.ValueRW.Remaining -= dt;

                if (upgrade.ValueRO.Remaining <= 0f)
                {
                    completed.Add(entity);
                }
            }

            // Process completed upgrades
            for (int i = 0; i < completed.Length; i++)
            {
                Entity templeEntity = completed[i];
                if (!em.Exists(templeEntity)) continue;
                if (!em.HasComponent<TempleUpgradeState>(templeEntity)) continue;

                var upgradeState = em.GetComponentData<TempleUpgradeState>(templeEntity);
                int nextLevel = upgradeState.TargetLevel;

                // Determine faction
                Faction faction = Faction.Blue;
                if (em.HasComponent<FactionTag>(templeEntity))
                    faction = em.GetComponentData<FactionTag>(templeEntity).Value;

                // 1. Set temple level
                em.SetComponentData(templeEntity, new TempleLevel { Level = nextLevel });

                // 2. Set FactionEra on faction bank + award the per-age RP bonus.
                //    task-063: RP comes from age-up (6/8/10) + Shrine (+1) only.
                //    Temple-level grants were retired with the old sect system.
                int nextEra = TempleLevelConfig.GetEraForLevel(nextLevel);
                if (FactionEconomy.TryGetBank(em, faction, out var bankEntity))
                {
                    if (em.HasComponent<FactionEra>(bankEntity))
                        em.SetComponentData(bankEntity, new FactionEra { Value = nextEra });

                    FactionReligionPointsHelper.AwardAgeUp(em, faction, newAge: nextEra);
                }

                // 3. Remove TempleUpgradeState — upgrade is complete
                em.RemoveComponent<TempleUpgradeState>(templeEntity);

                // Notify player. Look up the RP delta after the award so the
                // toast shows the actual gain (which may be augmented by carryover).
                int rpGain = SectConfig.RpAwardForAge(nextEra);
                TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                    $"Era {nextEra} reached! +{rpGain} Religion Points");
            }

            completed.Dispose();
        }
    }
}
