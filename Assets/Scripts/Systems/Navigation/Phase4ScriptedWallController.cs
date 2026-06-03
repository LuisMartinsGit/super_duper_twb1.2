// Phase4ScriptedWallController.cs
// task-112 M4 -- auto-mode driver for the Phase4Test scenario.
//
// Drives the place / destroy script described in
// Phase4TestSetup.cs:
//   * At Phase4TestSetup.PlaceWallAtSeconds (5s of sim time) spawns a
//     row of building entities along the centre of the unit corridor.
//     Each entity has BuildingTag + LocalTransform + BuildingSize 1x1.
//     The next BuildingCostStampSystem pass writes them into the cost
//     field, which dirties the centre tiles.
//   * At Phase4TestSetup.DestroyWallAtSeconds (10s of sim time) deletes
//     every spawned entity. Their cells then clear on the next stamp,
//     which dirties the same tiles again.
//
// Runs only when Phase4Test is the active scenario AND the
// Phase4ScriptedWallState singleton is present (set up by
// Phase4TestSetup.SpawnScenarioEntities). For every other game mode
// this system is a no-op (RequireForUpdate gates).
//
// Determinism notes:
//   * dt = SystemAPI.Time.DeltaTime, fixed-step constant.
//   * Wall entities are spawned in z-ascending order so the
//     BuildingCostStampSystem stamps them in a stable order across
//     machines.
//   * Spawning uses EntityManager.CreateEntity directly (managed
//     entry point) -- this system is NOT [BurstCompile]'d.
//
// Location: Assets/Scripts/Systems/Navigation/Phase4ScriptedWallController.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Bootstrap;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M4 auto-mode controller for the Phase4Test scenario.
    /// Spawns + destroys a wall row on a scripted timer to exercise
    /// the dirty-tile -> incremental-rebuild -> cache-invalidation
    /// path of M4 without needing manual editor input.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(CostFieldStampSystem))]
    public partial class Phase4ScriptedWallController : SystemBase
    {
        // Parent GameObject holding all debug-cube visuals for the wall.
        // Lets the tester actually SEE the otherwise-invisible cost-field
        // wall during Phase4Test. Cleared when the wall is destroyed.
        private GameObject _visualRoot;

        protected override void OnCreate()
        {
            RequireForUpdate<Phase4ScriptedWallState>();
        }

        protected override void OnDestroy()
        {
            if (_visualRoot != null)
            {
                Object.Destroy(_visualRoot);
                _visualRoot = null;
            }
        }

        protected override void OnUpdate()
        {
            // Gate on the scenario flag so we don't run during regular
            // skirmish / other scenarios that happen to leave a state
            // entity around (defensive -- shouldn't happen in practice).
            if (GameSettings.Mode != GameMode.Scenario
                || GameSettings.ActiveScenario != ScenarioType.Phase4Test) return;

            float dt = SystemAPI.Time.DeltaTime;

            var em = EntityManager;
            var stateEntity = SystemAPI.GetSingletonEntity<Phase4ScriptedWallState>();
            var state = em.GetComponentData<Phase4ScriptedWallState>(stateEntity);

            state.ElapsedSeconds += dt;

            switch (state.Phase)
            {
                case Phase4ScriptedWallState.PhaseWaitingToPlace:
                    if (state.ElapsedSeconds >= Phase4TestSetup.PlaceWallAtSeconds)
                    {
                        SpawnWallRow(em, stateEntity, ref state);
                        SpawnDebugVisuals();
                        state.Phase = Phase4ScriptedWallState.PhasePlaced;
                    }
                    break;

                case Phase4ScriptedWallState.PhasePlaced:
                    if (state.ElapsedSeconds >= Phase4TestSetup.DestroyWallAtSeconds)
                    {
                        DestroyWallRow(em, stateEntity, ref state);
                        DestroyDebugVisuals();
                        state.Phase = Phase4ScriptedWallState.PhaseDestroyed;
                    }
                    break;

                case Phase4ScriptedWallState.PhaseDestroyed:
                    // Terminal state -- nothing to do.
                    break;
            }

            em.SetComponentData(stateEntity, state);
        }

        // Debug-only cube visuals so the tester can SEE the wall during
        // Phase4Test. The wall would otherwise be invisible -- only the
        // impassable cost-field cells exist, leading to confusion about
        // why units "pile up" mid-march.
        private void SpawnDebugVisuals()
        {
            if (_visualRoot != null) return;
            _visualRoot = new GameObject("Phase4WallDebugVisuals");

            float startZ = Phase4TestSetup.WallCentreZ - (Phase4TestSetup.WallLength * 0.5f);
            int count = Phase4TestSetup.WallLength;
            for (int i = 0; i < count; i++)
            {
                float z = startZ + i + 0.5f;
                float x = Phase4TestSetup.WallCentreX;
                float y = TerrainUtility.GetHeight(x, z);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"WallDebug_{i}";
                cube.transform.SetParent(_visualRoot.transform, worldPositionStays: false);
                cube.transform.position = new Vector3(x, y + 1.0f, z);
                cube.transform.localScale = new Vector3(Phase4TestSetup.WallWidth, 2.0f, 1.0f);
                var rend = cube.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.material.color = new Color(0.8f, 0.2f, 0.2f);
                }
                // Strip the auto-collider -- the nav stack reads the cost
                // field, not Unity physics, so a physical collider would
                // only confuse other systems.
                var col = cube.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
            }
        }

        private void DestroyDebugVisuals()
        {
            if (_visualRoot == null) return;
            Object.Destroy(_visualRoot);
            _visualRoot = null;
        }

        // Spawn WallLength building entities centred on (WallCentreX, _, WallCentreZ).
        // Each entity carries BuildingTag + LocalTransform + BuildingSize so the
        // cost-field stamp picks it up like any other building.
        //
        // CreateEntity is a structural change which invalidates DynamicBuffer
        // type handles -- so we cannot hold a buffer reference across the
        // create loop. Stage all entity creations into a temp array, THEN
        // re-acquire the buffer and append in one pass.
        private static void SpawnWallRow(EntityManager em, Entity stateEntity,
            ref Phase4ScriptedWallState state)
        {
            float startZ = Phase4TestSetup.WallCentreZ - (Phase4TestSetup.WallLength * 0.5f);
            int count = Phase4TestSetup.WallLength;
            var newWalls = new Unity.Collections.NativeArray<Entity>(
                count, Unity.Collections.Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                float z = startZ + i + 0.5f;
                float x = Phase4TestSetup.WallCentreX;
                float y = TerrainUtility.GetHeight(x, z);

                var wall = em.CreateEntity(
                    typeof(BuildingTag),
                    typeof(LocalTransform),
                    typeof(BuildingSize));
                em.SetComponentData(wall, LocalTransform.FromPosition(new float3(x, y, z)));
                em.SetComponentData(wall, new BuildingSize
                {
                    Width = Phase4TestSetup.WallWidth,
                    Height = 1,
                });
                newWalls[i] = wall;
            }

            // Re-acquire AFTER the structural changes above.
            var buffer = em.GetBuffer<Phase4ScriptedWallEntity>(stateEntity);
            for (int i = 0; i < count; i++)
                buffer.Add(new Phase4ScriptedWallEntity { Value = newWalls[i] });
            newWalls.Dispose();

            state.WallEntityCount = count;
        }

        // DestroyEntity is a structural change which invalidates the buffer
        // type handle, so we cannot iterate buffer.Length across the destroy
        // loop. Snapshot the wall entities into a temp array, clear the
        // buffer immediately, THEN do the destroys against the snapshot.
        private static void DestroyWallRow(EntityManager em, Entity stateEntity,
            ref Phase4ScriptedWallState state)
        {
            var buffer = em.GetBuffer<Phase4ScriptedWallEntity>(stateEntity);
            int count = buffer.Length;
            var snapshot = new Unity.Collections.NativeArray<Entity>(
                count, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < count; i++)
                snapshot[i] = buffer[i].Value;
            buffer.Clear();
            // From here on the buffer reference is no longer used. Destroying
            // entities is allowed because we're working from the snapshot.
            for (int i = 0; i < count; i++)
            {
                var e = snapshot[i];
                if (em.Exists(e)) em.DestroyEntity(e);
            }
            snapshot.Dispose();
            state.WallEntityCount = 0;
        }
    }
}
