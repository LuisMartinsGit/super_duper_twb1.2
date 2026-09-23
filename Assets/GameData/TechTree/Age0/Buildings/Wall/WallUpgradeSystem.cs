// WallUpgradeSystem.cs
// Ticks WallUpgradeState timers and applies tower/gate components on completion.
//
// Two loops:
//   1. Per-instance WallUpgradeState. UpgradeType 1 (Tower) converts the
//      cell in place; 3 (Hub) replaces the cell with a hub and splits its
//      segment there (AlanthorWall.ConvertInstanceToHub) so walls can
//      branch; 4 / 5 mount a ballista / trebuchet emplacement on the
//      module's crown (AlanthorWall.MountEmplacement); 2 (single-instance
//      Gate) is kept for backward compatibility with pre-task-109 saves and
//      the IMGUI reference panel (EntityActionPanel.cs:1641-1681).
//   2. Per-segment WallSegmentUpgradeState. The player's "Convert to Gate"
//      command attaches this to the segment entity; on completion the
//      focus module BECOMES the gate and the modules beside it are
//      DESTROYED (AlanthorWall.MakeGate) - a gate is ONE structure three
//      modules wide, not a run of tagged cells
//      (docs/Design/Age_1_Alanthor.md § The gate is one structure).

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

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
            // Hub conversions are structural (a hub entity, a split segment)
            // and run after the query loop closes and the ECB has played.
            var toHub = new NativeList<Entity>(4, Allocator.Temp);
            // Gate conversions are structural too (the flanking modules are
            // destroyed), so they are collected here and run after playback.
            var pendingGates = new NativeList<Entity>(2, Allocator.Temp);
            var gateMembers = new System.Collections.Generic.List<NativeArray<Entity>>();
            // Emplacement mounts, likewise structural.
            var toMount = new NativeList<Entity>(2, Allocator.Temp);
            var toMountKind = new NativeList<int>(2, Allocator.Temp);

            // ── Loop 1: per-instance upgrades (Tower; Hub; legacy single-instance Gate) ──
            foreach (var (upgrade, health, presId, entity) in SystemAPI
                         .Query<RefRW<WallUpgradeState>, RefRW<Health>, RefRW<PresentationId>>()
                         .WithAll<WallInstanceTag>()
                         .WithEntityAccess())
            {
                upgrade.ValueRW.Remaining -= dt;
                if (upgrade.ValueRW.Remaining > 0f) continue;

                // Upgrade complete
                if (upgrade.ValueRO.UpgradeType == 3)
                {
                    ecb.RemoveComponent<WallUpgradeState>(entity);
                    toHub.Add(entity);
                    continue;
                }
                if (upgrade.ValueRO.UpgradeType == 4 || upgrade.ValueRO.UpgradeType == 5)
                {
                    // Mounting an engine is structural (the crew raises a
                    // second entity on it), so it runs after playback.
                    ecb.RemoveComponent<WallUpgradeState>(entity);
                    toMount.Add(entity);
                    toMountKind.Add(upgrade.ValueRO.UpgradeType);
                    continue;
                }
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

                    // HP from the TOWER's own SO, scaled to the wall's level.
                    // It was a literal 500 while Tower/WallTower.asset said
                    // 900 — a designer editing the asset changed nothing.
                    var towerDef = TechCatalog.Building(
                        TheWaningBorder.Entities.AlanthorWall.TowerId);
                    int towerHp = TheWaningBorder.Entities.WallTiers.ScaleHp(
                        towerDef.hp, TheWaningBorder.Entities.WallTiers.Of(em, entity));
                    health.ValueRW.Max = towerHp;
                    health.ValueRW.Value = towerHp;

                    // Change visual
                    presId.ValueRW.Id = TheWaningBorder.Entities.AlanthorWall.TowerPresentationID;
                }
                else if (upgrade.ValueRO.UpgradeType == 2)
                {
                    // Legacy single-instance Gate (pre-task-109 / IMGUI reference path).
                    // The new segment-level path runs through Loop 2 below.
                    ecb.AddComponent<WallGateTag>(entity);
                    ecb.AddComponent(entity, new WallGateState { IsOpen = 0, RecheckTimer = 0f });
                    ecb.AddComponent(entity, new WallGateLock { Sealed = 0 });
                    var legacySpan = new WallGateSpan
                    {
                        Metres = TheWaningBorder.Entities.AlanthorWall.InstanceSpacing,
                    };
                    ecb.AddComponent(entity, legacySpan);

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

                        // The focus module becomes the gate; its
                        // neighbours in the window are swallowed by the
                        // gatehouse's own masonry and destroyed. Deferred
                        // to AFTER the ECB playback below: MakeGate is
                        // structural and must not run inside this loop's
                        // snapshot.
                        int leaderIdx = members.Length / 2;
                        Entity leader = members[leaderIdx];
                        if (seg.FocusInstance != Entity.Null)
                            for (int i = 0; i < members.Length; i++)
                                if (members[i] == seg.FocusInstance) { leader = members[i]; break; }

                        pendingGates.Add(leader);
                        gateMembers.Add(new NativeArray<Entity>(members.AsArray(), Allocator.Temp));

                        // Remove the segment-level timer; conversion done.
                        ecb.RemoveComponent<WallSegmentUpgradeState>(segment);

                        // Also clear any stale WallSegmentFocus marker — the
                        // segment is now a gate region, no further focus state
                        // is meaningful until a future re-conversion.
                        if (em.HasComponent<WallSegmentFocus>(segment))
                            ecb.RemoveComponent<WallSegmentFocus>(segment);

                    }
                    finally
                    {
                        members.Dispose();
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();

            for (int i = 0; i < toHub.Length; i++)
                TheWaningBorder.Entities.AlanthorWall.ConvertInstanceToHub(em, toHub[i]);
            toHub.Dispose();

            for (int i = 0; i < toMount.Length; i++)
            {
                var cell = toMount[i];
                if (!em.Exists(cell)) continue;
                bool trebuchet = toMountKind[i] == 5;
                TheWaningBorder.Entities.AlanthorWall.MountEmplacement(em, cell, trebuchet);
                var mountSpawn = PresentationSpawnSystem.Instance;
                if (mountSpawn != null) mountSpawn.ForceRespawn(cell);
            }
            toMount.Dispose();
            toMountKind.Dispose();

            for (int i = 0; i < pendingGates.Length; i++)
            {
                var window = gateMembers[i];
                var list = new System.Collections.Generic.List<Entity>(window.Length);
                for (int k = 0; k < window.Length; k++) list.Add(window[k]);
                window.Dispose();
                TheWaningBorder.Entities.AlanthorWall.MakeGate(em, pendingGates[i], list);
                var spawn = PresentationSpawnSystem.Instance;
                if (spawn != null && em.Exists(pendingGates[i])) spawn.ForceRespawn(pendingGates[i]);
            }
            pendingGates.Dispose();
        }
    }
}
