// WallUpgradeSystem.cs
// Ticks WallUpgradeState timers and applies tower/gate components on completion.
// Location: Assets/Scripts/Systems/Buildings/WallUpgradeSystem.cs
//
// Two loops:
//   1. Per-instance WallUpgradeState — legacy path. UpgradeType 1 (Tower) is
//      the live use; UpgradeType 2 (single-instance Gate) is kept for
//      backward compatibility with pre-task-109 saves and the IMGUI
//      reference panel (EntityActionPanel.cs:1641-1681).
//   2. Per-segment WallSegmentUpgradeState — task-109 Phase 5 path. The
//      player's "Convert to Gate (5×)" command attaches this to the
//      segment entity; on completion we tag the centre-5 instances with
//      WallGateRegionTag + WallGateGroup + WallGateTag + WallGateState.

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

            // ── Loop 1: per-instance upgrades (Tower; legacy single-instance Gate) ──
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
                    // Legacy single-instance Gate (pre-task-109 / IMGUI reference path).
                    // The new segment-level path runs through Loop 2 below.
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

            // ── Loop 2: per-segment Gate conversion (task-109 Phase 5) ──
            // The segment entity carries WallSegmentUpgradeState. On completion
            // we tag the centre-5 instances of the segment (or all instances
            // if the segment has < 5 — cap-at-segment-length per R5).
            //
            // Snapshot-then-mutate: PickGateRegionInstances reads the
            // WallInstanceRef buffer; we materialise the list to a Temp
            // allocation BEFORE issuing any structural change, then close
            // the iteration scope, then run the structural changes via
            // both ECB (component-add) and PresentationSpawnSystem (visual
            // respawn).
            using (var pendingSegments = new NativeList<Entity>(8, Allocator.Temp))
            {
                foreach (var (segUp, entity) in SystemAPI
                             .Query<RefRW<WallSegmentUpgradeState>>()
                             .WithAll<WallSegmentTag>()
                             .WithEntityAccess())
                {
                    segUp.ValueRW.Remaining -= dt;
                    if (segUp.ValueRW.Remaining > 0f) continue;

                    pendingSegments.Add(entity);
                }

                for (int s = 0; s < pendingSegments.Length; s++)
                {
                    Entity segment = pendingSegments[s];
                    if (!em.Exists(segment)) continue;
                    if (!em.HasComponent<WallSegmentUpgradeState>(segment)) continue;

                    var seg = em.GetComponentData<WallSegmentUpgradeState>(segment);

                    // Resolve the 5-instance window (or shorter on short segments).
                    var members = TheWaningBorder.Entities.AlanthorWall
                        .PickGateRegionInstances(em, segment, seg.FocusInstance, Allocator.Temp);

                    try
                    {
                        if (members.Length == 0)
                        {
                            // Segment has no live instances — clean up the timer
                            // and bail. Should never happen in practice because
                            // segments cascade-die with their last instance.
                            ecb.RemoveComponent<WallSegmentUpgradeState>(segment);
                            continue;
                        }

                        // Centre instance acts as the group leader. For short
                        // segments (< 5) we clamp to whatever is available so
                        // 1- and 2-instance segments still get a valid leader.
                        int leaderIdx = members.Length / 2;
                        Entity leader = members[leaderIdx];

                        for (int i = 0; i < members.Length; i++)
                        {
                            Entity inst = members[i];
                            if (!em.Exists(inst)) continue;

                            // Add the gate-region marker triad.
                            if (!em.HasComponent<WallGateTag>(inst))
                                ecb.AddComponent<WallGateTag>(inst);
                            if (!em.HasComponent<WallGateRegionTag>(inst))
                                ecb.AddComponent<WallGateRegionTag>(inst);
                            ecb.AddComponent(inst, new WallGateGroup { Leader = leader });
                            if (!em.HasComponent<WallGateState>(inst))
                                ecb.AddComponent(inst, new WallGateState { IsOpen = 0, RecheckTimer = 0f });

                            // Swap visual to gate presentation. Defer the
                            // actual respawn to AFTER the ECB playback so the
                            // PresentationSpawnSystem reads the new id.
                            if (em.HasComponent<PresentationId>(inst))
                            {
                                ecb.SetComponent(inst, new PresentationId
                                {
                                    Id = TheWaningBorder.Entities.AlanthorWall.GatePresentationID
                                });
                            }
                        }

                        // Remove the segment-level timer; conversion done.
                        ecb.RemoveComponent<WallSegmentUpgradeState>(segment);

                        // Also clear any stale WallSegmentFocus marker — the
                        // segment is now a gate region, no further focus state
                        // is meaningful until a future re-conversion.
                        if (em.HasComponent<WallSegmentFocus>(segment))
                            ecb.RemoveComponent<WallSegmentFocus>(segment);

                        // Force visual respawn for each instance in the region.
                        var spawnSys = PresentationSpawnSystem.Instance;
                        if (spawnSys != null)
                        {
                            for (int i = 0; i < members.Length; i++)
                                spawnSys.ForceRespawn(members[i]);
                        }
                    }
                    finally
                    {
                        members.Dispose();
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
