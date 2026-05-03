// WallUpgradeSystem.cs
// Ticks WallUpgradeState timers and applies tower/gate components on completion.
// Location: Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs

using Unity.Entities;
using Unity.Collections;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WallUpgradeSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (upgrade, health, presId, entity) in SystemAPI
                         .Query<RefRW<WallUpgradeState>, RefRW<Health>, RefRW<PresentationId>>()
                         .WithAll<WallInstanceTag>()
                         .WithEntityAccess())
            {
                upgrade.ValueRW.Remaining -= dt;
                if (upgrade.ValueRW.Remaining > 0f) continue;

                // Upgrade complete
                if (upgrade.ValueRO.UpgradeType == 1)
                {
                    // Tower upgrade
                    ecb.AddComponent<WallTowerTag>(entity);
                    ecb.AddComponent(entity, new BuildingRangedAttack
                    {
                        Range = 16f,
                        Damage = 12,
                        Cooldown = 2.5f,
                        Timer = 0f,
                        MaxTargets = 1
                    });
                    ecb.AddComponent(entity, new DamageTypeData { Value = DamageType.Ranged });

                    // Boost HP
                    health.ValueRW.Max = 500;
                    health.ValueRW.Value = 500;

                    // Change visual
                    presId.ValueRW.Id = TheWaningBorder.Entities.AlanthorWall.TowerPresentationID;
                }
                else if (upgrade.ValueRO.UpgradeType == 2)
                {
                    // Gate upgrade
                    ecb.AddComponent<WallGateTag>(entity);
                    ecb.AddComponent(entity, new WallGateState { IsOpen = 0, RecheckTimer = 0f });

                    // Change visual
                    presId.ValueRW.Id = TheWaningBorder.Entities.AlanthorWall.GatePresentationID;
                }

                ecb.RemoveComponent<WallUpgradeState>(entity);

                // Force visual respawn
                var spawnSys = PresentationSpawnSystem.Instance;
                if (spawnSys != null) spawnSys.ForceRespawn(entity);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
