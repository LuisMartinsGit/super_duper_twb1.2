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

                // 2. Set FactionEra on faction bank
                int nextEra = TempleLevelConfig.GetEraForLevel(nextLevel);
                if (FactionEconomy.TryGetBank(em, faction, out var bankEntity))
                {
                    if (em.HasComponent<FactionEra>(bankEntity))
                        em.SetComponentData(bankEntity, new FactionEra { Value = nextEra });

                    // 3. Grant Religion Points
                    int rpGrant = TempleLevelConfig.GetRPGranted(nextLevel);
                    if (em.HasComponent<ReligionPoints>(bankEntity))
                    {
                        var rp = em.GetComponentData<ReligionPoints>(bankEntity);
                        rp.Value += rpGrant;
                        em.SetComponentData(bankEntity, rp);
                    }

                }

                // 4. Recalculate sect passive scaling
                SectEffectSystem.Instance?.RecalculateAllPassives(faction);

                // 5. Remove TempleUpgradeState — upgrade is complete
                em.RemoveComponent<TempleUpgradeState>(templeEntity);

                // Notify player
                TheWaningBorder.UI.HUD.PlayerNotificationSystem.Notify(
                    $"Era {nextEra} reached! +{TempleLevelConfig.GetRPGranted(nextLevel)} Religion Points");
            }

            completed.Dispose();
        }
    }
}
